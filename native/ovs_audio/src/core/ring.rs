//! Package 173: the speaker's ring of interleaved stereo frames. One producer (the client's playback thread) writes,
//! one consumer (the device's render thread) reads; neither ever waits for the other or allocates. What does not fit
//! is discarded, as `BufferedWaveProvider.DiscardOnBufferOverflow` did; what is missing is played as silence.

use std::sync::atomic::{AtomicU32, AtomicU64, Ordering};

/// Samples per frame: interleaved stereo.
pub const CHANNELS: usize = 2;

/// The longest silence between two written frames that still counts as an underrun (a gap inside the audio); a longer
/// one is a pause of the producer (nothing to play). 100 ms at 48 kHz.
// ponytail: a fixed window guesses "gap" against "pause"; the producer could flag its pauses if the guess misleads
pub const UNDERRUN_WINDOW_FRAMES: u64 = 4_800;

/// A single-producer, single-consumer ring of stereo `f32` frames.
pub struct Ring {
    samples: Box<[AtomicU32]>, // f32 bits, CHANNELS per frame
    capacity: u64,             // in frames
    written: AtomicU64,        // frames written since the start
    read: AtomicU64,           // frames read since the start
    gap: AtomicU64,            // silent frames read since the ring last ran empty, capped at the window plus one
    underruns: AtomicU64,      // silent frames inside the audio, counted when the next frame was written
}

impl Ring {
    /// A ring that holds `capacity` frames.
    pub fn new(capacity: usize) -> Ring {
        Ring {
            samples: (0..capacity * CHANNELS).map(|_| AtomicU32::new(0)).collect(),
            capacity: capacity as u64,
            written: AtomicU64::new(0),
            read: AtomicU64::new(0),
            gap: AtomicU64::new(0),
            underruns: AtomicU64::new(0),
        }
    }

    /// Producer: appends the frames of `samples` (interleaved stereo); returns how many fitted, the rest is discarded.
    pub fn write(&self, samples: &[f32]) -> usize {
        let frames = (samples.len() / CHANNELS) as u64;
        let written = self.written.load(Ordering::Relaxed);
        let free = self.capacity - (written - self.read.load(Ordering::Acquire));
        let count = frames.min(free);
        for frame in 0..count {
            let slot = (((written + frame) % self.capacity) as usize) * CHANNELS;
            for channel in 0..CHANNELS {
                self.samples[slot + channel].store(samples[frame as usize * CHANNELS + channel].to_bits(), Ordering::Relaxed);
            }
        }
        self.written.store(written + count, Ordering::Release);
        if count > 0 {
            let gap = self.gap.swap(0, Ordering::Relaxed);
            if gap <= UNDERRUN_WINDOW_FRAMES {
                self.underruns.fetch_add(gap, Ordering::Relaxed);
            }
        }
        count as usize
    }

    /// Consumer: fills `out` (interleaved stereo) with the oldest frames, and with silence where none are left.
    pub fn read(&self, out: &mut [f32]) {
        let frames = (out.len() / CHANNELS) as u64;
        let read = self.read.load(Ordering::Relaxed);
        let written = self.written.load(Ordering::Acquire);
        let count = frames.min(written - read);
        for frame in 0..count {
            let slot = (((read + frame) % self.capacity) as usize) * CHANNELS;
            for channel in 0..CHANNELS {
                out[frame as usize * CHANNELS + channel] = f32::from_bits(self.samples[slot + channel].load(Ordering::Relaxed));
            }
        }
        self.read.store(read + count, Ordering::Release);
        out[(count as usize) * CHANNELS..].fill(0.0);
        let short = frames - count;
        if short > 0 && written > 0 {
            let gap = self.gap.load(Ordering::Relaxed);
            self.gap.store((gap + short).min(UNDERRUN_WINDOW_FRAMES + 1), Ordering::Relaxed);
        }
    }

    /// Frames waiting to be played.
    pub fn buffered(&self) -> u64 {
        self.written.load(Ordering::Acquire) - self.read.load(Ordering::Acquire)
    }

    /// Silent frames played inside the audio so far: the counted gaps and the one going on, if it is short enough.
    pub fn underrun_frames(&self) -> u64 {
        let gap = self.gap.load(Ordering::Relaxed);
        self.underruns.load(Ordering::Relaxed) + if gap <= UNDERRUN_WINDOW_FRAMES { gap } else { 0 }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn frames(values: &[f32]) -> Vec<f32> {
        values.iter().flat_map(|value| [*value, -*value]).collect()
    }

    #[test]
    fn ring_write_then_read_returns_frames_in_order() {
        let ring = Ring::new(8);
        ring.write(&frames(&[1.0, 2.0, 3.0]));
        let mut out = [9.0; 6];

        ring.read(&mut out);

        assert_eq!(out.to_vec(), frames(&[1.0, 2.0, 3.0]));
        assert_eq!(ring.buffered(), 0);
    }

    #[test]
    fn ring_full_discards_overflow_and_counts_nothing_missing() {
        let ring = Ring::new(2);

        let fitted = ring.write(&frames(&[1.0, 2.0, 3.0]));
        let mut out = [0.0; 4];
        ring.read(&mut out);

        assert_eq!(fitted, 2);
        assert_eq!(out.to_vec(), frames(&[1.0, 2.0]));
        assert_eq!(ring.underrun_frames(), 0);
    }

    #[test]
    fn ring_empty_read_fills_silence_and_counts_underrun() {
        let ring = Ring::new(8);
        ring.write(&frames(&[1.0, 2.0]));
        let mut out = [9.0; 10];

        ring.read(&mut out);

        assert_eq!(out.to_vec(), frames(&[1.0, 2.0, 0.0, 0.0, 0.0]));
        assert_eq!(ring.underrun_frames(), 3);
        ring.write(&frames(&[4.0]));
        assert_eq!(ring.underrun_frames(), 3);
    }

    #[test]
    fn ring_long_pause_or_start_not_counted_as_underrun() {
        let ring = Ring::new(8);
        let mut out = vec![0.0; 2 * 100];
        ring.read(&mut out); // before the first frame: the stream starts, nothing missing yet
        ring.write(&frames(&[1.0]));
        for _ in 0..60 {
            ring.read(&mut out); // 6000 frames: a pause, longer than the window
        }

        ring.write(&frames(&[2.0]));

        assert_eq!(ring.underrun_frames(), 0);
    }

    #[test]
    fn ring_concurrent_producer_consumer_keeps_order() {
        let ring = std::sync::Arc::new(Ring::new(4_800));
        let total = 1_000_000u32;
        let producer = {
            let ring = ring.clone();
            std::thread::spawn(move || {
                let mut next = 1u32;
                while next <= total {
                    let chunk: Vec<f32> = (next..(next + 480).min(total + 1)).flat_map(|value| [value as f32, value as f32]).collect();
                    next += ring.write(&chunk) as u32;
                    std::thread::yield_now();
                }
            })
        };
        let mut expected = 1u32;
        let mut out = vec![0.0; 2 * 512];
        while expected <= total {
            ring.read(&mut out);
            for frame in out.chunks(2).filter(|frame| frame[0] != 0.0) {
                assert_eq!(frame[0], expected as f32);
                assert_eq!(frame[1], expected as f32);
                expected += 1;
            }
        }
        producer.join().expect("producer");
    }
}

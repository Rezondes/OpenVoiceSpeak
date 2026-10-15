//! Package 175: one speaker's jitter buffer, ported from the managed `JitterBuffer` unchanged in its rules (Packages
//! 124, 133, 134, 136, 142): it reorders the Opus frames, hides losses (FEC from the next packet, the copy of Package 136,
//! else concealment), rebuffers after a talk pause and counts what happens to the frames. The frames live in fixed
//! slots, so pushing and pulling allocate nothing. Not thread-safe: its owner serializes the calls.

use super::decoder::{may_carry_fec, Decoder, STEREO_FRAME_SAMPLES};

/// The least cushion before playback starts, in frames: 40 ms.
pub const START_DELAY: i32 = 2;
/// The most frames held; caps the latency at 200 ms.
pub const MAX_FRAMES: usize = 10;
/// One frame's length in ms.
pub const FRAME_MS: f64 = 20.0;
/// Underruns before a talk pause is assumed and the buffer rebuffers.
const MAX_UNDERRUNS: i32 = 2;
/// Package 134: the lateness fades with a half-life of about 230 frames.
const LATE_DECAY: f64 = 0.997;
/// `LossWindow.FreshAfter`: after a silence of a second the frame numbers may have moved on.
const FRESH_AFTER_MS: f64 = 1_000.0;
/// `LossWindow.MaxGap`: a bigger jump in the frame numbers starts afresh.
const MAX_GAP: u32 = 50;
/// The largest packet kept; a larger one is cut (Opus frames of 20 ms stay far below it).
pub const MAX_PACKET: usize = 1_500;

/// A few packets by frame number in fixed slots.
struct PacketMap {
    seqs: [u32; MAX_FRAMES + 1],
    lengths: [usize; MAX_FRAMES + 1],
    used: [bool; MAX_FRAMES + 1],
    data: Box<[[u8; MAX_PACKET]; MAX_FRAMES + 1]>,
}

impl PacketMap {
    fn new() -> PacketMap {
        PacketMap {
            seqs: [0; MAX_FRAMES + 1],
            lengths: [0; MAX_FRAMES + 1],
            used: [false; MAX_FRAMES + 1],
            data: Box::new([[0; MAX_PACKET]; MAX_FRAMES + 1]),
        }
    }

    fn len(&self) -> usize {
        self.used.iter().filter(|used| **used).count()
    }

    fn slot(&self, seq: u32) -> Option<usize> {
        (0..self.used.len()).find(|&slot| self.used[slot] && self.seqs[slot] == seq)
    }

    fn contains(&self, seq: u32) -> bool {
        self.slot(seq).is_some()
    }

    fn get(&self, seq: u32) -> Option<&[u8]> {
        self.slot(seq).map(|slot| &self.data[slot][..self.lengths[slot]])
    }

    fn oldest(&self) -> Option<u32> {
        (0..self.used.len()).filter(|&slot| self.used[slot]).map(|slot| self.seqs[slot]).min()
    }

    /// Stores (or replaces) `seq`; when every slot is taken the oldest makes room.
    fn insert(&mut self, seq: u32, packet: &[u8]) {
        let slot = match self.slot(seq).or_else(|| self.used.iter().position(|used| !used)) {
            Some(slot) => slot,
            None => {
                let oldest = self.oldest().unwrap_or(seq);
                self.slot(oldest).unwrap_or(0)
            }
        };
        let length = packet.len().min(MAX_PACKET);
        self.data[slot][..length].copy_from_slice(&packet[..length]);
        self.seqs[slot] = seq;
        self.lengths[slot] = length;
        self.used[slot] = true;
    }

    /// Removes `seq`, copying its packet into `out`; its length, or None.
    fn take(&mut self, seq: u32, out: &mut [u8; MAX_PACKET]) -> Option<usize> {
        let slot = self.slot(seq)?;
        let length = self.lengths[slot];
        out[..length].copy_from_slice(&self.data[slot][..length]);
        self.used[slot] = false;
        Some(length)
    }

    fn remove(&mut self, seq: u32) -> bool {
        match self.slot(seq) {
            Some(slot) => {
                self.used[slot] = false;
                true
            }
            None => false,
        }
    }

    fn clear(&mut self) {
        self.used = [false; MAX_FRAMES + 1];
    }

    fn remove_below(&mut self, seq: u32) {
        for slot in 0..self.used.len() {
            if self.used[slot] && self.seqs[slot] < seq {
                self.used[slot] = false;
            }
        }
    }
}

/// What the buffer counts and measures (Package 133), as the client reads it.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct JitterStats {
    pub received: i32,
    pub lost: i32,
    pub rebuilt: i32,
    pub concealed: i32,
    pub recovered: i32,
    pub cushion: i32,
    pub delay_ms: i32,
    pub buffered: i32,
    pub jitter_ms: f64,
    pub late_ms: f64,
    pub via_link: u8,
    pub busy: u8,
}

/// One speaker's jitter buffer and decoder.
pub struct JitterBuffer {
    frames: PacketMap,
    copies: PacketMap, // Package 136: frames still missing, as copied into the packet after them
    decoder: Decoder,
    scratch: Box<[u8; MAX_PACKET]>,
    started: bool,
    playing: bool,
    next: u32,
    waited: i32,
    underruns: i32,
    last_transit: f64,
    last_arrival: f64,
    resumed: bool,
    max_transit: f64,
    stats: JitterStats,
}

impl Default for JitterBuffer {
    fn default() -> JitterBuffer {
        JitterBuffer::new()
    }
}

impl JitterBuffer {
    /// An empty buffer.
    pub fn new() -> JitterBuffer {
        JitterBuffer {
            frames: PacketMap::new(),
            copies: PacketMap::new(),
            decoder: Decoder::new(),
            scratch: Box::new([0; MAX_PACKET]),
            started: false,
            playing: false,
            next: 0,
            waited: 0,
            underruns: 0,
            last_transit: f64::NAN,
            last_arrival: f64::NEG_INFINITY,
            resumed: false,
            max_transit: f64::NAN,
            stats: JitterStats::default(),
        }
    }

    /// Package 176: back to empty for another speaker, keeping its memory (the mixer's pool reuses it).
    pub fn reset(&mut self) {
        self.frames.clear();
        self.copies.clear();
        self.decoder.reset();
        self.started = false;
        self.playing = false;
        self.next = 0;
        self.waited = 0;
        self.underruns = 0;
        self.last_transit = f64::NAN;
        self.last_arrival = f64::NEG_INFINITY;
        self.resumed = false;
        self.max_transit = f64::NAN;
        self.stats = JitterStats::default();
    }

    /// Package 134 (A143): frames a talk spurt waits for, from the measured jitter or lateness.
    pub fn cushion(&self) -> i32 {
        let wanted = ((2.0 * self.stats.jitter_ms).max(self.stats.late_ms) / FRAME_MS).ceil() as i32 + 1;
        wanted.clamp(START_DELAY, MAX_FRAMES as i32)
    }

    /// The counts and measures now.
    pub fn stats(&self) -> JitterStats {
        let buffered = self.frames.len() as i32;
        JitterStats {
            cushion: self.cushion(),
            delay_ms: buffered * FRAME_MS as i32,
            buffered,
            busy: u8::from(self.playing || buffered > 0),
            ..self.stats
        }
    }

    /// Takes frame `seq` (and the `copy` of the frame before it, Package 136), arrived at `arrival_ms` on any clock.
    pub fn push(&mut self, seq: u32, opus: &[u8], via_link: bool, arrival_ms: f64, copy: Option<&[u8]>) {
        self.stats.via_link = u8::from(via_link);
        if self.frames.contains(seq) {
            return; // a duplicate is neither counted nor stored again
        }
        self.stats.received += 1;
        let transit = arrival_ms - FRAME_MS * f64::from(seq);
        let idle = !self.playing && self.frames.len() == 0;
        let close = arrival_ms - self.last_arrival <= FRESH_AFTER_MS;
        if !close {
            self.stats.jitter_ms /= 2.0;
            self.stats.late_ms /= 2.0;
        }
        let step = if self.last_transit.is_nan() { f64::NAN } else { (transit - self.last_transit).abs() };
        if self.playing && step < MAX_FRAMES as f64 * FRAME_MS {
            self.stats.jitter_ms += (step - self.stats.jitter_ms) / 16.0;
        }
        self.last_transit = transit;
        let late = self.max_transit - transit;
        self.stats.late_ms *= LATE_DECAY;
        if !self.started || idle || !(late >= 0.0 && late < MAX_FRAMES as f64 * FRAME_MS) {
            self.max_transit = transit;
        } else {
            self.stats.late_ms = self.stats.late_ms.max(late);
            self.max_transit -= 1.0;
        }
        if idle {
            self.resumed = close;
        }
        self.last_arrival = arrival_ms;
        if self.started && seq < self.next {
            return; // too late, already played or concealed
        }
        self.frames.insert(seq, opus);
        self.copies.remove(seq);
        if let Some(copy) = copy {
            if seq > 0 && !(self.started && seq - 1 < self.next) && !self.frames.contains(seq - 1) {
                self.copies.insert(seq - 1, copy);
            }
        }
        let mut dropped = false;
        while self.frames.len() > MAX_FRAMES {
            let Some(oldest) = self.frames.oldest() else { break };
            self.frames.remove(oldest);
            self.stats.lost += 1;
            if self.started && oldest >= self.next {
                self.next = oldest + 1;
            }
            dropped = true;
        }
        if dropped {
            self.copies.remove_below(self.next);
        }
    }

    /// The next 20 ms of interleaved stereo, or None while buffering or idle.
    pub fn pull(&mut self) -> Option<&[f32; STEREO_FRAME_SAMPLES]> {
        if !self.playing {
            let count = self.frames.len() as i32;
            if count == 0 {
                return None;
            }
            let cushion = self.cushion();
            if count < cushion {
                self.waited += 1;
                if self.waited < cushion {
                    return None;
                }
            }
            let mut first = self.frames.oldest().unwrap_or(0);
            if first > 0 && self.copies.contains(first - 1) && (!self.started || first > self.next) {
                first -= 1;
            }
            if self.started && self.resumed && first > self.next && first - self.next <= MAX_GAP {
                self.stats.lost += (first - self.next) as i32;
            }
            self.next = if self.started { self.next.max(first) } else { first };
            self.started = true;
            self.playing = true;
            self.waited = 0;
            self.underruns = 0;
            self.copies.remove_below(self.next);
        }

        let taken = match self.frames.take(self.next, &mut self.scratch) {
            Some(length) => Some(length),
            None => self.copies.take(self.next, &mut self.scratch).inspect(|_| self.stats.recovered += 1),
        };
        if let Some(length) = taken {
            self.stats.concealed += self.underruns;
            self.underruns = 0;
            self.next += 1;
            return Some(self.decoder.decode(&self.scratch[..length]));
        }
        if self.frames.len() > 0 {
            self.stats.lost += 1;
            self.next += 1;
            let following = self.frames.get(self.next).or_else(|| self.copies.get(self.next));
            if let Some(following) = following.filter(|packet| may_carry_fec(packet)) {
                let length = following.len();
                self.scratch[..length].copy_from_slice(following);
                self.stats.rebuilt += 1;
                return Some(self.decoder.decode_lost(&self.scratch[..length]));
            }
            self.stats.concealed += 1;
            return Some(self.decoder.decode(&[]));
        }
        self.underruns += 1;
        if self.underruns > MAX_UNDERRUNS {
            self.playing = false;
            return None;
        }
        Some(self.decoder.decode(&[]))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const PACKET: [u8; 3] = [0x08, 0x00, 0x00]; // a SILK packet header, decoded or concealed alike

    #[test]
    fn pull_below_start_delay_returns_none() {
        let mut buffer = JitterBuffer::new();
        buffer.push(0, &PACKET, false, 0.0, None);

        assert!(buffer.pull().is_none());
    }

    #[test]
    fn buffer_overflow_capped_at_10() {
        let mut buffer = JitterBuffer::new();
        for seq in 0..15 {
            buffer.push(seq, &PACKET, false, f64::from(seq) * 20.0, None);
        }

        assert_eq!(buffer.stats().buffered, 10);
        assert_eq!(buffer.stats().lost, 5);
    }

    #[test]
    fn push_duplicate_neither_counted_nor_stored() {
        let mut buffer = JitterBuffer::new();
        buffer.push(3, &PACKET, false, 0.0, None);
        buffer.push(3, &PACKET, true, 1.0, None);

        assert_eq!((buffer.stats().received, buffer.stats().buffered, buffer.stats().via_link), (1, 1, 1));
    }

    #[test]
    fn talk_pause_rebuffers_after_two_underruns() {
        let mut buffer = JitterBuffer::new();
        buffer.push(0, &PACKET, false, 0.0, None);
        buffer.push(1, &PACKET, false, 20.0, None);
        assert!(buffer.pull().is_some());
        assert!(buffer.pull().is_some());

        let concealing = (buffer.pull().is_some(), buffer.pull().is_some(), buffer.pull().is_some());

        assert_eq!(concealing, (true, true, false));
        assert_eq!(buffer.stats().busy, 0);
    }
}

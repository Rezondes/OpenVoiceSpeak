//! Package 173: what a speaker plays out, rendered on the device's thread: the frames the client wrote into the ring
//! and, Package 174, the sounds on top. Nothing here allocates in steady state or blocks: the sound mixer is locked
//! only by the render thread, so its lock never waits.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;

use super::ring::{Ring, CHANNELS};
use super::sounds::{sounds, SoundMixer, SoundStarter};

/// The ring holds one second, like the managed `BufferedWaveProvider` before it.
pub const RING_FRAMES: usize = 48_000;

/// The speaker's audio source.
pub struct Playout {
    /// The frames the client writes.
    pub ring: Ring,
    starter: SoundStarter,
    mixer: Mutex<SoundMixer>,
    played: AtomicU64, // device frames rendered so far
}

impl Playout {
    /// An empty playout with a ring of [`RING_FRAMES`].
    pub fn new() -> Playout {
        let (starter, mixer) = sounds();
        Playout { ring: Ring::new(RING_FRAMES), starter, mixer: Mutex::new(mixer), played: AtomicU64::new(0) }
    }

    /// Device thread: fills `out` (interleaved stereo) with the next frames and the sounds on top.
    pub fn render(&self, out: &mut [f32]) {
        self.ring.read(out);
        let first = self.played.load(Ordering::Relaxed);
        if let Ok(mut mixer) = self.mixer.try_lock() {
            mixer.mix(out, first);
        }
        self.played.store(first + (out.len() / CHANNELS) as u64, Ordering::Release);
    }

    /// Client thread: starts loaded sound `id` with the next frame the client writes, `offset` samples in.
    pub fn play_sound(&self, id: u32, gain: f32, offset: u64, restart: bool) -> bool {
        let start = self.played.load(Ordering::Acquire) + self.ring.buffered();
        self.starter.play(id, gain, start, offset, restart)
    }

    /// Client thread: stops every sound.
    pub fn stop_sounds(&self) {
        self.starter.stop_all();
    }

    /// Sounds playing or waiting to start (as the render thread last saw them).
    pub fn sounds_playing(&self) -> usize {
        self.mixer.try_lock().map_or(0, |mixer| mixer.playing())
    }
}

impl Default for Playout {
    fn default() -> Playout {
        Playout::new()
    }
}

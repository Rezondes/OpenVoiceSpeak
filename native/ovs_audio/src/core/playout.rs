//! Package 173: what a speaker plays out, rendered on the device's thread: for now the frames the client wrote into
//! the ring (Package 174 adds the sounds on top). Nothing here allocates or blocks.

use super::ring::Ring;

/// The ring holds one second, like the managed `BufferedWaveProvider` before it.
pub const RING_FRAMES: usize = 48_000;

/// The speaker's audio source.
pub struct Playout {
    /// The frames the client writes.
    pub ring: Ring,
}

impl Playout {
    /// An empty playout with a ring of [`RING_FRAMES`].
    pub fn new() -> Playout {
        Playout { ring: Ring::new(RING_FRAMES) }
    }

    /// Device thread: fills `out` (interleaved stereo) with the next frames.
    pub fn render(&self, out: &mut [f32]) {
        self.ring.read(out);
    }
}

impl Default for Playout {
    fn default() -> Playout {
        Playout::new()
    }
}

//! Package 176: the soft limiter of Package 50 (A52), ported from the managed `SoftLimiter`: keeps the boosted mix
//! below the threshold without cutting the waveform flat. A loud frame is scaled down as a whole at once; afterwards
//! the gain climbs back by `RELEASE_PER_FRAME` per frame, ramped sample by sample so it never clicks. Below the
//! threshold the signal is left exactly as it is.

/// The level above which the gain goes down.
pub const THRESHOLD: f32 = 0.95;
/// How fast the gain comes back per frame: from half gain back to full in 200 ms.
pub const RELEASE_PER_FRAME: f32 = 0.05;

/// The limiter's state: the gain applied now.
pub struct Limiter {
    gain: f32,
}

impl Default for Limiter {
    fn default() -> Limiter {
        Limiter { gain: 1.0 }
    }
}

impl Limiter {
    /// The gain applied now.
    pub fn gain(&self) -> f32 {
        self.gain
    }

    /// Lowers `frame` where it would go above the threshold.
    pub fn process(&mut self, frame: &mut [f32]) {
        let peak = frame.iter().fold(0.0f32, |peak, sample| peak.max(sample.abs()));
        let allowed = if peak > THRESHOLD { THRESHOLD / peak } else { 1.0 };
        if allowed <= self.gain {
            self.gain = allowed; // attack at once, the frame's peak lands exactly on the threshold
            if self.gain < 1.0 {
                for sample in frame.iter_mut() {
                    *sample *= self.gain;
                }
            }
            return;
        }
        let (start, end) = (self.gain, allowed.min(self.gain + RELEASE_PER_FRAME));
        self.gain = end;
        if start >= 1.0 {
            return;
        }
        let length = frame.len() as f32;
        for (index, sample) in frame.iter_mut().enumerate() {
            *sample *= start + (end - start) * (index as f32 + 1.0) / length;
        }
    }
}

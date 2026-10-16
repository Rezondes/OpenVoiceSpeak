//! Package 179 (A183): the Opus encoder on libopus. The client sets it up as `VoiceEncoder` did with Concentus
//! (complexity 5, DTX off, FEC in voice mode, the music settings of Package 142) through Opus' own requests.

use audiopus_sys::{opus_encode_float, opus_encoder_create, opus_encoder_ctl, opus_encoder_destroy, OpusEncoder, OPUS_APPLICATION_AUDIO, OPUS_APPLICATION_VOIP};

/// An Opus encoder at 48 kHz.
pub struct Encoder {
    state: *mut OpusEncoder,
    channels: usize,
}

// SAFETY: the encoder state is used by one thread at a time (its owner)
unsafe impl Send for Encoder {}

impl Encoder {
    /// A new encoder for `channels` (1 or 2) in voice (VOIP) or music (AUDIO) mode.
    pub fn new(channels: usize, music: bool) -> Encoder {
        let mut error = 0;
        let application = if music { OPUS_APPLICATION_AUDIO } else { OPUS_APPLICATION_VOIP };
        // SAFETY: valid rate, channel count and application; the error is written before return
        let state = unsafe { opus_encoder_create(48_000, channels as i32, application, &mut error) };
        assert!(!state.is_null() && error == 0, "opus_encoder_create failed: {error}");
        Encoder { state, channels }
    }

    /// Sets a value by Opus' request number (an `OPUS_SET_*` constant); Opus' error code, 0 on success.
    pub fn set(&mut self, request: i32, value: i32) -> i32 {
        // SAFETY: the state is live; the SET requests take one int
        unsafe { opus_encoder_ctl(self.state, request, value) }
    }

    /// Reads a value by Opus' request number (an `OPUS_GET_*` constant).
    pub fn get(&mut self, request: i32) -> i32 {
        let mut value = 0i32;
        // SAFETY: the state is live; the GET requests take a pointer to one int
        unsafe { opus_encoder_ctl(self.state, request, &mut value as *mut i32) };
        value
    }

    /// Encodes 20 ms (`frame` interleaved when stereo) into `out`; the packet's length, or a negative Opus error.
    pub fn encode(&mut self, frame: &[f32], out: &mut [u8]) -> i32 {
        let samples = (frame.len() / self.channels) as i32;
        // SAFETY: the state is live; `frame` holds `samples` frames and `out` its length in bytes
        unsafe { opus_encode_float(self.state, frame.as_ptr(), samples, out.as_mut_ptr(), out.len() as i32) }
    }
}

impl Drop for Encoder {
    fn drop(&mut self) {
        // SAFETY: created in new, destroyed once
        unsafe { opus_encoder_destroy(self.state) };
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::decoder::Decoder;

    const SET_BITRATE: i32 = 4002;
    const GET_BITRATE: i32 = 4003;

    #[test]
    fn encode_decode_sine_keeps_energy() {
        let mut encoder = Encoder::new(1, false);
        encoder.set(SET_BITRATE, 32_000);
        let mut decoder = Decoder::new();
        let mut packet = [0u8; 1_275];
        let mut energy_in = 0.0f64;
        let mut energy_out = 0.0f64;
        for count in 0..50 {
            let frame: Vec<f32> = (0..960).map(|index| 0.3 * ((count * 960 + index) as f32 * 0.05).sin()).collect();
            let length = encoder.encode(&frame, &mut packet);
            assert!(length > 0);
            let decoded = decoder.decode(&packet[..length as usize]);
            if count >= 10 {
                energy_in += frame.iter().map(|sample| f64::from(*sample) * f64::from(*sample)).sum::<f64>();
                energy_out += decoded.iter().step_by(2).map(|sample| f64::from(*sample) * f64::from(*sample)).sum::<f64>();
            }
        }

        assert!((energy_out / energy_in - 1.0).abs() < 0.2, "{energy_out} against {energy_in}");
        assert_eq!(encoder.get(GET_BITRATE), 32_000);
    }
}

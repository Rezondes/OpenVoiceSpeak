//! Package 175 (A183): one speaker's Opus decoder on libopus, always to interleaved stereo (Package 142: a mono packet
//! comes out the same on both sides). An empty or a 1-byte packet (DTX in other senders) and a broken one are
//! concealed (PLC), as `VoiceDecoder` did; nothing here allocates after creation.

use audiopus_sys::{opus_decode_float, opus_decoder_create, opus_decoder_destroy, OpusDecoder};

/// Samples per channel of one 20 ms frame at 48 kHz.
pub const FRAME_SAMPLES: usize = 960;

/// Interleaved stereo samples of one frame.
pub const STEREO_FRAME_SAMPLES: usize = 2 * FRAME_SAMPLES;

/// Package 142: only the speech coder's packets (SILK or hybrid, configurations 0 to 15 of the table of contents, RFC
/// 6716 3.1) can carry FEC; a music coder packet never does.
pub fn may_carry_fec(packet: &[u8]) -> bool {
    packet.len() > 1 && packet[0] >> 3 < 16
}

/// A libopus decoder at 48 kHz stereo with its output frame.
pub struct Decoder {
    state: *mut OpusDecoder,
    pcm: [f32; STEREO_FRAME_SAMPLES],
}

// SAFETY: the decoder state is used by one thread at a time (its owner)
unsafe impl Send for Decoder {}

impl Decoder {
    /// A new decoder.
    pub fn new() -> Decoder {
        let mut error = 0;
        // SAFETY: valid rate and channel count; the error is written before return
        let state = unsafe { opus_decoder_create(48_000, 2, &mut error) };
        assert!(!state.is_null() && error == 0, "opus_decoder_create failed: {error}");
        Decoder { state, pcm: [0.0; STEREO_FRAME_SAMPLES] }
    }

    /// Decodes `packet` into the frame; an empty, 1-byte or broken packet is concealed.
    pub fn decode(&mut self, packet: &[u8]) -> &[f32; STEREO_FRAME_SAMPLES] {
        self.run(packet, false)
    }

    /// Rebuilds the lost frame before `next_packet` from the FEC copy it carries.
    pub fn decode_lost(&mut self, next_packet: &[u8]) -> &[f32; STEREO_FRAME_SAMPLES] {
        self.run(next_packet, true)
    }

    fn run(&mut self, packet: &[u8], fec: bool) -> &[f32; STEREO_FRAME_SAMPLES] {
        let packet = if packet.len() <= 1 { &[][..] } else { packet };
        let data = if packet.is_empty() { std::ptr::null() } else { packet.as_ptr() };
        let fec = i32::from(fec && !packet.is_empty());
        // SAFETY: the state is live, `pcm` holds a full frame of stereo samples
        let decoded = unsafe { opus_decode_float(self.state, data, packet.len() as i32, self.pcm.as_mut_ptr(), FRAME_SAMPLES as i32, fec) };
        if decoded < 0 {
            // SAFETY: as above; a null packet asks for concealment
            unsafe { opus_decode_float(self.state, std::ptr::null(), 0, self.pcm.as_mut_ptr(), FRAME_SAMPLES as i32, 0) };
        }
        &self.pcm
    }
}

impl Default for Decoder {
    fn default() -> Decoder {
        Decoder::new()
    }
}

impl Drop for Decoder {
    fn drop(&mut self) {
        // SAFETY: created in new, destroyed once
        unsafe { opus_decoder_destroy(self.state) };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decode_one_byte_packet_concealed() {
        let mut decoder = Decoder::new();

        let frame = decoder.decode(&[0x08]);

        assert_eq!(frame.len(), STEREO_FRAME_SAMPLES);
    }

    #[test]
    fn broken_packet_concealed_not_panicking() {
        let mut decoder = Decoder::new();

        let frame = decoder.decode(&[0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        assert!(frame.iter().all(|sample| sample.is_finite()));
    }

    #[test]
    fn may_carry_fec_only_speech_coder_packets() {
        assert!(may_carry_fec(&[15 << 3, 0]));
        assert!(!may_carry_fec(&[16 << 3, 0]));
        assert!(!may_carry_fec(&[0]));
    }
}

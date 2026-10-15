//! Package 175: the receive exports: a speaker's jitter buffer (with its decoder) and a bare decoder, both handed to
//! the client as opaque pointers. The client serializes the calls on one buffer (its mixer lock).

use std::ptr::null_mut;

use crate::core::decoder::{Decoder, STEREO_FRAME_SAMPLES};
use crate::core::jitter::{JitterBuffer, JitterStats};
use crate::guard;

/// # Safety
/// `data` holds `length` bytes, or is null.
unsafe fn bytes<'a>(data: *const u8, length: u32) -> &'a [u8] {
    if data.is_null() || length == 0 {
        &[]
    } else {
        // SAFETY: the caller's guarantee
        unsafe { std::slice::from_raw_parts(data, length as usize) }
    }
}

/// A new, empty jitter buffer.
#[no_mangle]
pub extern "C" fn ovs_audio_jitter_create() -> *mut JitterBuffer {
    guard(null_mut(), || Box::into_raw(Box::new(JitterBuffer::new())))
}

/// Frees a jitter buffer.
///
/// # Safety
/// `buffer` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_jitter_free(buffer: *mut JitterBuffer) {
    if !buffer.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(buffer) }));
    }
}

/// Takes frame `seq` with the copy of the frame before it (`copy` null when none came along).
///
/// # Safety
/// `buffer` is live; `opus` holds `length` bytes and `copy` `copy_length` bytes, or is null.
#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn ovs_audio_jitter_push(
    buffer: *mut JitterBuffer,
    seq: u32,
    opus: *const u8,
    length: u32,
    via_link: u8,
    arrival_ms: f64,
    copy: *const u8,
    copy_length: u32,
) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe {
            let copy = if copy.is_null() { None } else { Some(bytes(copy, copy_length)) };
            (&mut *buffer).push(seq, bytes(opus, length), via_link != 0, arrival_ms, copy);
        }
    });
}

/// Writes the next 20 ms of interleaved stereo into `out` and returns 1, or returns 0 while buffering or idle.
///
/// # Safety
/// `buffer` is live; `out` holds a stereo frame (1920 floats).
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_jitter_pull(buffer: *mut JitterBuffer, out: *mut f32) -> u8 {
    guard(0, || {
        // SAFETY: the caller's guarantee
        unsafe {
            match (&mut *buffer).pull() {
                Some(frame) => {
                    std::ptr::copy_nonoverlapping(frame.as_ptr(), out, STEREO_FRAME_SAMPLES);
                    1
                }
                None => 0,
            }
        }
    })
}

/// Writes the buffer's counts and measures into `out`.
///
/// # Safety
/// `buffer` is live; `out` is writable.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_jitter_stats(buffer: *const JitterBuffer, out: *mut JitterStats) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { *out = (&*buffer).stats() });
}

/// A new Opus decoder (48 kHz, stereo out).
#[no_mangle]
pub extern "C" fn ovs_audio_decoder_create() -> *mut Decoder {
    guard(null_mut(), || Box::into_raw(Box::new(Decoder::new())))
}

/// Frees a decoder.
///
/// # Safety
/// `decoder` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_decoder_free(decoder: *mut Decoder) {
    if !decoder.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(decoder) }));
    }
}

/// Decodes `packet` (or, with `fec` 1, rebuilds the frame before it from its FEC) into `out`; an empty, 1-byte or
/// broken packet is concealed.
///
/// # Safety
/// `decoder` is live; `packet` holds `length` bytes or is null; `out` holds a stereo frame (1920 floats).
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_decoder_decode(decoder: *mut Decoder, packet: *const u8, length: u32, fec: u8, out: *mut f32) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe {
            let decoder = &mut *decoder;
            let packet = bytes(packet, length);
            let frame = if fec != 0 { decoder.decode_lost(packet) } else { decoder.decode(packet) };
            std::ptr::copy_nonoverlapping(frame.as_ptr(), out, STEREO_FRAME_SAMPLES);
        }
    });
}

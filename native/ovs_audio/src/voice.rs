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

/// Package 176: what the client reads of the mixer, laid out for C# (`NativeMixerSnapshot`).
#[repr(C)]
pub struct MixerSnapshot {
    pub stats: JitterStats,
    pub mixes: u64,
    pub level_db: f32,
    pub active_count: u32,
    pub speaker_count: u32,
    pub busy: u8,
    pub active_ids: [u32; crate::core::mixer::POOL],
    pub active_links: [u8; crate::core::mixer::POOL],
    pub speaker_ids: [u32; crate::core::mixer::POOL],
    pub speaker_serials: [u64; crate::core::mixer::POOL],
}

/// Package 176: a new voice mixer with its pool of speakers; shared with the speakers it is attached to.
#[no_mangle]
pub extern "C" fn ovs_audio_mixer_create() -> *const crate::core::mixer::Mixer {
    guard(std::ptr::null(), || std::sync::Arc::into_raw(std::sync::Arc::new(crate::core::mixer::Mixer::new())))
}

/// Package 176: lets go of the client's reference; a speaker still attached keeps the mixer alive.
///
/// # Safety
/// `mixer` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_free(mixer: *const crate::core::mixer::Mixer) {
    if !mixer.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { std::sync::Arc::from_raw(mixer) }));
    }
}

/// Package 176: a received packet for the mixer (`copy` null when none came along); any thread.
///
/// # Safety
/// `mixer` is live; `opus` holds `length` bytes and `copy` `copy_length` bytes, or is null.
#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn ovs_audio_mixer_push(
    mixer: *const crate::core::mixer::Mixer,
    speaker: u32,
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
            let data = bytes(opus, length);
            let copied = if copy.is_null() { None } else { Some(bytes(copy, copy_length)) };
            let max = crate::core::jitter::MAX_PACKET;
            let mut packet = crate::core::mixer::Packet {
                speaker,
                seq,
                via_link: via_link != 0,
                arrival_ms,
                length: data.len().min(max),
                data: [0; crate::core::jitter::MAX_PACKET],
                copy_length: copied.map(|copied| copied.len().min(max)),
                copy: [0; crate::core::jitter::MAX_PACKET],
            };
            packet.data[..packet.length].copy_from_slice(&data[..packet.length]);
            if let (Some(copied), Some(copy_length)) = (copied, packet.copy_length) {
                packet.copy[..copy_length].copy_from_slice(&copied[..copy_length]);
            }
            (&*mixer).push(packet);
        }
    });
}

/// Package 176: mixes one whole 20 ms frame into `out` (the client's own clock without a device, and the tests).
///
/// # Safety
/// `mixer` is live; `out` holds a stereo frame (1920 floats).
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_mix(mixer: *const crate::core::mixer::Mixer, out: *mut f32) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe { (&*mixer).mix_once(&mut *(out as *mut [f32; STEREO_FRAME_SAMPLES])) };
    });
}

/// Package 176: the mixer's last snapshot into `out`.
///
/// # Safety
/// `mixer` is live; `out` is writable.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_snapshot(mixer: *const crate::core::mixer::Mixer, out: *mut MixerSnapshot) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe {
            let snapshot = (&*mixer).snapshot();
            let out = &mut *out;
            out.stats = snapshot.stats;
            out.mixes = snapshot.mixes;
            out.level_db = snapshot.level_db;
            out.active_count = snapshot.active_count as u32;
            out.speaker_count = snapshot.speaker_count as u32;
            out.busy = u8::from(snapshot.busy);
            for (index, (id, via_link)) in snapshot.active.iter().enumerate() {
                out.active_ids[index] = *id;
                out.active_links[index] = u8::from(*via_link);
            }
            for (index, (id, serial)) in snapshot.speakers.iter().enumerate() {
                out.speaker_ids[index] = *id;
                out.speaker_serials[index] = *serial;
            }
        }
    });
}

/// Package 176: each listed speaker's final gain and the gain of everyone else.
///
/// # Safety
/// `mixer` is live; `ids` and `gains` hold `count` values each, or are null with `count` 0.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_set_gains(mixer: *const crate::core::mixer::Mixer, ids: *const u32, gains: *const f32, count: u32, others: f32) {
    guard((), || {
        let count = (count as usize).min(crate::core::mixer::POOL);
        let mut table = [(0u32, 1f32); crate::core::mixer::POOL];
        for (index, entry) in table.iter_mut().take(count).enumerate() {
            // SAFETY: the caller's guarantee
            *entry = unsafe { (*ids.add(index), *gains.add(index)) };
        }
        // SAFETY: the caller's guarantee
        unsafe { (&*mixer).set_gains(&table[..count], others) };
    });
}

/// Package 176: drops one speaker's buffer.
///
/// # Safety
/// `mixer` is live.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_remove(mixer: *const crate::core::mixer::Mixer, id: u32) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*mixer).remove(id) });
}

/// Package 176: drops every speaker's buffer.
///
/// # Safety
/// `mixer` is live.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_clear(mixer: *const crate::core::mixer::Mixer) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*mixer).clear() });
}

/// Package 176: deafened and the self test.
///
/// # Safety
/// `mixer` is live.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_set_flags(mixer: *const crate::core::mixer::Mixer, deafened: u8, self_test: u8) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*mixer).set_flags(deafened != 0, self_test != 0) });
}

/// Package 176: takes up to `capacity` mono samples of the far end the speaker played; returns how many.
///
/// # Safety
/// `mixer` is live; `out` holds `capacity` floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_read_far_end(mixer: *const crate::core::mixer::Mixer, out: *mut f32, capacity: u32) -> u32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*mixer).read_far_end(std::slice::from_raw_parts_mut(out, capacity as usize)) as u32 })
}

/// Package 176: a new soft limiter.
#[no_mangle]
pub extern "C" fn ovs_audio_limiter_create() -> *mut crate::core::limiter::Limiter {
    guard(null_mut(), || Box::into_raw(Box::default()))
}

/// Package 176: frees a limiter.
///
/// # Safety
/// `limiter` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_limiter_free(limiter: *mut crate::core::limiter::Limiter) {
    if !limiter.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(limiter) }));
    }
}

/// Package 176: limits `frame` in place and returns the gain applied now.
///
/// # Safety
/// `limiter` is live; `frame` holds `length` floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_limiter_process(limiter: *mut crate::core::limiter::Limiter, frame: *mut f32, length: u32) -> f32 {
    guard(1.0, || {
        // SAFETY: the caller's guarantee
        unsafe {
            let limiter = &mut *limiter;
            limiter.process(std::slice::from_raw_parts_mut(frame, length as usize));
            limiter.gain()
        }
    })
}

/// Package 176: takes the waiting packets and commands and refreshes the snapshot without mixing.
///
/// # Safety
/// `mixer` is live.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mixer_settle(mixer: *const crate::core::mixer::Mixer) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*mixer).settle() });
}

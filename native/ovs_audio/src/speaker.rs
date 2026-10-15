//! Package 173: the speaker exports. A speaker is a [`Playout`] and the device stream that plays it; the client writes
//! frames into its ring and polls its state (A181: nothing calls back into .NET). The fake speaker has no device: a test
//! renders it by hand and sets its error.

use std::ptr::null_mut;
use std::sync::atomic::{AtomicI32, Ordering};
use std::sync::Arc;

use crate::core::playout::Playout;
use crate::core::ring::CHANNELS;
use crate::device::RenderStream;
use crate::{guard, set_last_error};

/// An open speaker, handed to the client as an opaque pointer.
pub struct Speaker {
    playout: Arc<Playout>,
    stream: Option<Box<dyn RenderStream>>,
    fake_error: AtomicI32,
}

impl Speaker {
    fn new(playout: Arc<Playout>, stream: Option<Box<dyn RenderStream>>) -> *mut Speaker {
        Box::into_raw(Box::new(Speaker { playout, stream, fake_error: AtomicI32::new(0) }))
    }

    fn error(&self) -> i32 {
        match self.fake_error.load(Ordering::Acquire) {
            0 => self.stream.as_ref().map_or(0, |stream| stream.error()),
            error => error,
        }
    }
}

/// # Safety
/// `id` is null or a NUL-terminated UTF-16 string.
unsafe fn read_id(id: *const u16) -> Option<Vec<u16>> {
    if id.is_null() {
        return None;
    }
    let mut length = 0;
    // SAFETY: the caller guarantees the terminator
    unsafe {
        while *id.add(length) != 0 {
            length += 1;
        }
        Some(std::slice::from_raw_parts(id, length).to_vec())
    }
}

#[cfg(windows)]
fn open_stream(id: Option<Vec<u16>>, playout: Arc<Playout>) -> Result<Box<dyn RenderStream>, (i32, String)> {
    crate::device::wasapi::WasapiRender::open(id, playout).map(|stream| Box::new(stream) as Box<dyn RenderStream>)
}

#[cfg(not(windows))]
fn open_stream(_id: Option<Vec<u16>>, _playout: Arc<Playout>) -> Result<Box<dyn RenderStream>, (i32, String)> {
    Err((-1, "no device layer on this platform yet (A189)".into()))
}

/// Opens the speaker `id` (an MMDevice id, NUL-terminated UTF-16) or, with `id` null, the default one and starts it.
/// Null when it cannot open: `out_error` gets the HRESULT, the last error its text.
///
/// # Safety
/// `id` is null or NUL-terminated UTF-16; `out_error` is null or writable.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_open(id: *const u16, out_error: *mut i32) -> *mut Speaker {
    guard(null_mut(), || {
        let playout = Arc::new(Playout::new());
        // SAFETY: the caller's guarantee
        match open_stream(unsafe { read_id(id) }, playout.clone()) {
            Ok(stream) => Speaker::new(playout, Some(stream)),
            Err((code, text)) => {
                if !out_error.is_null() {
                    // SAFETY: the caller's guarantee
                    unsafe { *out_error = code };
                }
                set_last_error(text);
                null_mut()
            }
        }
    })
}

/// A speaker without a device, for tests: rendered by [`ovs_audio_speaker_fake_render`].
#[no_mangle]
pub extern "C" fn ovs_audio_speaker_open_fake() -> *mut Speaker {
    guard(null_mut(), || Speaker::new(Arc::new(Playout::new()), None))
}

/// Stops the device and frees the speaker.
///
/// # Safety
/// `speaker` came from an open and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_close(speaker: *mut Speaker) {
    if !speaker.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(speaker) }));
    }
}

/// Queues `frames` interleaved stereo frames; returns how many fitted (the rest is discarded).
///
/// # Safety
/// `speaker` is open; `samples` holds `frames` * 2 floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_write(speaker: *const Speaker, samples: *const f32, frames: u32) -> u32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*speaker).playout.ring.write(std::slice::from_raw_parts(samples, frames as usize * CHANNELS)) as u32 })
}

/// Frames waiting to be played.
///
/// # Safety
/// `speaker` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_buffered_frames(speaker: *const Speaker) -> u32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*speaker).playout.ring.buffered() as u32 })
}

/// The HRESULT that ended the device stream, 0 while it plays.
///
/// # Safety
/// `speaker` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_error(speaker: *const Speaker) -> i32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*speaker).error() })
}

/// Silent frames played inside the audio because none were written in time.
///
/// # Safety
/// `speaker` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_underrun_frames(speaker: *const Speaker) -> u64 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*speaker).playout.ring.underrun_frames() })
}

/// The MMCSS task index of the render thread, 0 when it is not registered (or a fake).
///
/// # Safety
/// `speaker` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_mmcss_task(speaker: *const Speaker) -> u32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*speaker).stream.as_ref().map_or(0, |stream| stream.mmcss_task()) })
}

/// Fake speaker: renders `frames` frames into `out` as the device would.
///
/// # Safety
/// `speaker` is open; `out` holds `frames` * 2 floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_fake_render(speaker: *const Speaker, out: *mut f32, frames: u32) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*speaker).playout.render(std::slice::from_raw_parts_mut(out, frames as usize * CHANNELS)) })
}

/// Fake speaker: ends its stream with `error`, as a device that went away.
///
/// # Safety
/// `speaker` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_speaker_fake_fail(speaker: *const Speaker, error: i32) {
    // SAFETY: the caller's guarantee
    guard((), || unsafe { (&*speaker).fake_error.store(error, Ordering::Release) })
}

//! Package 177: the microphone exports. A microphone is a native capture stream that fills a ring of one second and
//! signals an event after each round; the client's capture thread waits on that event (A181: nothing calls back into
//! .NET) and reads the frames. The fake microphone has no device: a test writes into it by hand.

use std::ptr::null_mut;
use std::sync::atomic::{AtomicI32, Ordering};
use std::sync::Arc;

use crate::core::ring::Ring;
use crate::device::wasapi::{Event, WasapiCapture};
use crate::{guard, set_last_error};

/// An open microphone, handed to the client as an opaque pointer.
pub struct Microphone {
    ring: Arc<Ring>,
    ready: Arc<Event>,
    channels: u32,
    stream: Option<WasapiCapture>,
    fake_error: AtomicI32,
}

/// # Safety
/// `id` is null or NUL-terminated UTF-16.
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

/// Opens the microphone `id` (NUL-terminated UTF-16 MMDevice id) or, with `id` null, the default one and starts it.
/// Null when it cannot open: `out_error` gets the HRESULT, the last error its text.
///
/// # Safety
/// `id` is null or NUL-terminated UTF-16; `out_error` is null or writable.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_open(id: *const u16, out_error: *mut i32) -> *mut Microphone {
    guard(null_mut(), || {
        let fail = |(code, text): (i32, String)| {
            if !out_error.is_null() {
                // SAFETY: the caller's guarantee
                unsafe { *out_error = code };
            }
            set_last_error(text);
            null_mut()
        };
        let ready = match Event::new(false) {
            Ok(ready) => Arc::new(ready),
            Err(error) => return fail(error),
        };
        // SAFETY: the caller's guarantee
        match WasapiCapture::open(unsafe { read_id(id) }, ready.clone()) {
            Ok((stream, (ring, channels))) => {
                Box::into_raw(Box::new(Microphone { ring, ready, channels, stream: Some(stream), fake_error: AtomicI32::new(0) }))
            }
            Err(error) => fail(error),
        }
    })
}

/// A microphone without a device, for tests: written by [`ovs_audio_mic_fake_write`].
#[no_mangle]
pub extern "C" fn ovs_audio_mic_open_fake(channels: u32) -> *mut Microphone {
    guard(null_mut(), || match Event::new(false) {
        Ok(ready) => Box::into_raw(Box::new(Microphone {
            ring: Arc::new(Ring::with_channels(48_000, channels.max(1) as usize)),
            ready: Arc::new(ready),
            channels: channels.max(1),
            stream: None,
            fake_error: AtomicI32::new(0),
        })),
        Err((_, text)) => {
            set_last_error(text);
            null_mut()
        }
    })
}

/// Stops the device and frees the microphone.
///
/// # Safety
/// `microphone` came from an open and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_close(microphone: *mut Microphone) {
    if !microphone.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(microphone) }));
    }
}

/// The samples per frame the microphone delivers (the device's channel count).
///
/// # Safety
/// `microphone` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_channels(microphone: *const Microphone) -> u32 {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*microphone).channels })
}

/// The event signalled after each round of the device (a HANDLE, owned by the microphone).
///
/// # Safety
/// `microphone` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_event(microphone: *const Microphone) -> isize {
    // SAFETY: the caller's guarantee
    guard(0, || unsafe { (&*microphone).ready.0 .0 as isize })
}

/// Takes up to `capacity` samples (whole frames of interleaved channels) into `out`; returns how many.
///
/// # Safety
/// `microphone` is open; `out` holds `capacity` floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_read(microphone: *const Microphone, out: *mut f32, capacity: u32) -> u32 {
    guard(0, || {
        // SAFETY: the caller's guarantee
        unsafe {
            let microphone = &*microphone;
            let channels = microphone.channels as usize;
            let frames = (microphone.ring.buffered() as usize).min(capacity as usize / channels);
            microphone.ring.read(std::slice::from_raw_parts_mut(out, frames * channels));
            (frames * channels) as u32
        }
    })
}

/// The HRESULT that ended the capture stream, 0 while it runs.
///
/// # Safety
/// `microphone` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_error(microphone: *const Microphone) -> i32 {
    guard(0, || {
        // SAFETY: the caller's guarantee
        let microphone = unsafe { &*microphone };
        match microphone.fake_error.load(Ordering::Acquire) {
            0 => microphone.stream.as_ref().map_or(0, WasapiCapture::error),
            error => error,
        }
    })
}

/// Fake microphone: queues `count` samples as the device would, and signals the event.
///
/// # Safety
/// `microphone` is open; `samples` holds `count` floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_fake_write(microphone: *const Microphone, samples: *const f32, count: u32) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe {
            let microphone = &*microphone;
            microphone.ring.write(std::slice::from_raw_parts(samples, count as usize));
            microphone.ready.signal();
        }
    });
}

/// Fake microphone: ends its stream with `error`, as a device that went away.
///
/// # Safety
/// `microphone` is open.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_mic_fake_fail(microphone: *const Microphone, error: i32) {
    guard((), || {
        // SAFETY: the caller's guarantee
        unsafe {
            let microphone = &*microphone;
            microphone.fake_error.store(error, Ordering::Release);
            microphone.ready.signal();
        }
    });
}

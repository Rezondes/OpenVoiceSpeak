//! OpenVoiceSpeak's native audio engine (Package 172). The client loads it as `ovs_audio.dll` and calls the exports
//! below (C ABI, every name prefixed `ovs_audio_`, A180). `core` is platform neutral and testable without a device
//! (A177); the device layers come with the streams (Package 173 on). No export lets a panic cross into C#: each runs
//! its body through [`guard`].

// MSVC's link.exe reports the import library it writes for the cdylib ("Creating library ..."); that note is no problem
#![allow(linker_messages)]

pub mod core;
pub mod device;
#[cfg(windows)]
pub mod microphone;
pub mod speaker;
pub mod voice;

use std::any::Any;
use std::cell::RefCell;
use std::panic::{catch_unwind, AssertUnwindSafe};

thread_local! {
    /// The last error of an export on this thread, read by [`ovs_audio_last_error`] right after the failing call.
    static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) };
}

/// Runs an export's body; a panic returns `failed` and leaves its message as the thread's last error. The body is taken
/// as unwind safe: after a panic the export reports the failure and the client stops using what it touched.
pub fn guard<T>(failed: T, body: impl FnOnce() -> T) -> T {
    match catch_unwind(AssertUnwindSafe(body)) {
        Ok(value) => value,
        Err(panic) => {
            set_last_error(format!("panic: {}", panic_text(panic.as_ref())));
            failed
        }
    }
}

/// Keeps `text` as the calling thread's last error.
pub fn set_last_error(text: String) {
    LAST_ERROR.with(|last| *last.borrow_mut() = text);
}

fn panic_text(panic: &(dyn Any + Send)) -> &str {
    if let Some(text) = panic.downcast_ref::<&str>() {
        text
    } else if let Some(text) = panic.downcast_ref::<String>() {
        text
    } else {
        "unknown panic"
    }
}

/// The ABI version the client checks on load (A180).
#[no_mangle]
pub extern "C" fn ovs_audio_abi_version() -> u32 {
    core::ABI_VERSION
}

/// Copies the calling thread's last error as UTF-8 into `out` (at most `capacity` bytes, no terminator) and returns its
/// whole length in bytes, so a caller with too small a buffer can ask again.
///
/// # Safety
/// `out` must be valid for `capacity` bytes of writing, or null with `capacity` 0.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_last_error(out: *mut u8, capacity: usize) -> usize {
    guard(0, || {
        LAST_ERROR.with(|last| {
            let text = last.borrow();
            let count = text.len().min(capacity);
            if count > 0 {
                // SAFETY: the caller guarantees `out` holds `capacity` bytes, and `count` is at most that
                unsafe { std::ptr::copy_nonoverlapping(text.as_ptr(), out, count) };
            }
            text.len()
        })
    })
}

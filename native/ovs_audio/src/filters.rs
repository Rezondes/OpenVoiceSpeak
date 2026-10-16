//! Package 178: the capture filters of the client in this one library (decision 4): WebRTC's echo canceller (AEC3,
//! linked statically with the feature "aec", A185) behind the C API of Package 140, and RNNoise through nnnoiseless
//! (A184) behind the API of rnnoise.dll, so the client's `EchoCanceller` and `NoiseSuppressor` keep their rules.

use std::ffi::c_void;
use std::ptr::null_mut;

use crate::guard;
#[cfg(not(feature = "aec"))]
use crate::set_last_error;

#[cfg(feature = "aec")]
extern "C" {
    fn ovs_aec_create() -> *mut c_void;
    fn ovs_aec_set_delay(handle: *mut c_void, delay_ms: i32);
    fn ovs_aec_render(handle: *mut c_void, frame: *const f32) -> i32;
    fn ovs_aec_capture(handle: *mut c_void, frame: *mut f32) -> i32;
    fn ovs_aec_destroy(handle: *mut c_void);
}

/// A new echo canceller (48 kHz mono, 10 ms frames); null when this build has none or it cannot be made.
#[no_mangle]
pub extern "C" fn ovs_audio_aec_create() -> *mut c_void {
    guard(null_mut(), || {
        #[cfg(feature = "aec")]
        {
            // SAFETY: the shim catches every C++ exception
            unsafe { ovs_aec_create() }
        }
        #[cfg(not(feature = "aec"))]
        {
            set_last_error("this build of ovs_audio has no echo canceller (feature \"aec\")".into());
            null_mut()
        }
    })
}

/// The delay hint from a played frame to its echo, in ms.
///
/// # Safety
/// `handle` came from create or is null.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_aec_set_delay(handle: *mut c_void, delay_ms: i32) {
    #[cfg(feature = "aec")]
    // SAFETY: the caller's guarantee; the shim ignores null
    guard((), || unsafe { ovs_aec_set_delay(handle, delay_ms) });
    #[cfg(not(feature = "aec"))]
    let _ = (handle, delay_ms);
}

/// The far end: 480 samples about to be played; 0 on success.
///
/// # Safety
/// `handle` came from create or is null; `frame` holds 480 floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_aec_render(handle: *mut c_void, frame: *const f32) -> i32 {
    #[cfg(feature = "aec")]
    // SAFETY: the caller's guarantee
    return guard(-1, || unsafe { ovs_aec_render(handle, frame) });
    #[cfg(not(feature = "aec"))]
    {
        let _ = (handle, frame);
        -1
    }
}

/// The near end: 480 captured samples, the echo removed in place; 0 on success.
///
/// # Safety
/// `handle` came from create or is null; `frame` holds 480 floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_aec_capture(handle: *mut c_void, frame: *mut f32) -> i32 {
    #[cfg(feature = "aec")]
    // SAFETY: the caller's guarantee
    return guard(-1, || unsafe { ovs_aec_capture(handle, frame) });
    #[cfg(not(feature = "aec"))]
    {
        let _ = (handle, frame);
        -1
    }
}

/// Frees an echo canceller (null does nothing).
///
/// # Safety
/// `handle` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_aec_destroy(handle: *mut c_void) {
    #[cfg(feature = "aec")]
    // SAFETY: the caller's guarantee
    guard((), || unsafe { ovs_aec_destroy(handle) });
    #[cfg(not(feature = "aec"))]
    let _ = handle;
}

/// RNNoise's frame: 10 ms at 48 kHz.
const NOISE_FRAME: usize = nnnoiseless::DenoiseState::FRAME_SIZE;

/// A new noise suppressor (`model` is ignored: the default model is built in), as `rnnoise_create`.
#[no_mangle]
pub extern "C" fn ovs_audio_noise_create(_model: *mut c_void) -> *mut nnnoiseless::DenoiseState<'static> {
    guard(null_mut(), || Box::into_raw(nnnoiseless::DenoiseState::new()))
}

/// Denoises 480 samples in the 16-bit range from `input` into `output` (may be the same buffer) and returns the
/// probability that they are speech, as `rnnoise_process_frame`.
///
/// # Safety
/// `state` came from create; `input` and `output` hold 480 floats.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_noise_process_frame(state: *mut nnnoiseless::DenoiseState<'static>, output: *mut f32, input: *const f32) -> f32 {
    guard(0.0, || {
        let mut copy = [0.0f32; NOISE_FRAME];
        // SAFETY: the caller's guarantee; the input is copied first, as the two may be one buffer
        unsafe {
            copy.copy_from_slice(std::slice::from_raw_parts(input, NOISE_FRAME));
            (&mut *state).process_frame(std::slice::from_raw_parts_mut(output, NOISE_FRAME), &copy)
        }
    })
}

/// Frees a noise suppressor, as `rnnoise_destroy`.
///
/// # Safety
/// `state` came from create and is not used afterwards.
#[no_mangle]
pub unsafe extern "C" fn ovs_audio_noise_destroy(state: *mut nnnoiseless::DenoiseState<'static>) {
    if !state.is_null() {
        // SAFETY: the caller's guarantee
        guard((), || drop(unsafe { Box::from_raw(state) }));
    }
}

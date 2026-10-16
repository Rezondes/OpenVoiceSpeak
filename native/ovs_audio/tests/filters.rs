//! Package 178: the filter exports as the client calls them.

use ovs_audio::filters::{ovs_audio_noise_create, ovs_audio_noise_destroy, ovs_audio_noise_process_frame};

#[test]
fn noise_in_place_returns_probability_and_finite_output() {
    let state = ovs_audio_noise_create(std::ptr::null_mut());
    let mut frame: Vec<f32> = (0..480).map(|index| ((index * 7919) % 2000) as f32 - 1000.0).collect();

    // SAFETY: the state is live until destroyed below; the frame holds 480 floats and may be both input and output
    let probability = unsafe {
        let pointer = frame.as_mut_ptr();
        let probability = ovs_audio_noise_process_frame(state, pointer, pointer);
        ovs_audio_noise_destroy(state);
        probability
    };

    assert!((0.0..=1.0).contains(&probability));
    assert!(frame.iter().all(|sample| sample.is_finite()));
}

#[cfg(not(feature = "aec"))]
#[test]
fn aec_without_feature_creates_nothing() {
    assert!(ovs_audio::filters::ovs_audio_aec_create().is_null());
}

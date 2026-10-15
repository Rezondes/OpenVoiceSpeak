//! Package 177: the microphone: the fake one written by hand, and the real default device (only read, never played).

use std::ptr::null;
use std::time::{Duration, Instant};

use ovs_audio::microphone::{
    ovs_audio_mic_channels, ovs_audio_mic_close, ovs_audio_mic_error, ovs_audio_mic_fake_fail, ovs_audio_mic_fake_write, ovs_audio_mic_open,
    ovs_audio_mic_open_fake, ovs_audio_mic_read,
};

#[test]
fn reader_paused_200ms_no_frame_lost() {
    let microphone = ovs_audio_mic_open_fake(2);
    let written: Vec<f32> = (0..2 * 9_600).map(|index| index as f32).collect(); // 200 ms of stereo while nobody reads

    // SAFETY: the microphone is open until closed below; the buffers hold what is passed
    unsafe {
        for chunk in written.chunks(2 * 480) {
            ovs_audio_mic_fake_write(microphone, chunk.as_ptr(), chunk.len() as u32);
        }
        let mut read = vec![0.0f32; written.len() + 100];
        let count = ovs_audio_mic_read(microphone, read.as_mut_ptr(), read.len() as u32) as usize;

        assert_eq!(ovs_audio_mic_channels(microphone), 2);
        assert_eq!(&read[..count], &written[..]);
        ovs_audio_mic_close(microphone);
    }
}

#[test]
fn read_takes_whole_frames_only() {
    let microphone = ovs_audio_mic_open_fake(2);

    // SAFETY: as above
    unsafe {
        ovs_audio_mic_fake_write(microphone, [1.0f32, 2.0, 3.0, 4.0].as_ptr(), 4);
        let mut read = [0.0f32; 3];
        let count = ovs_audio_mic_read(microphone, read.as_mut_ptr(), 3);

        assert_eq!(count, 2);
        assert_eq!(&read[..2], &[1.0, 2.0]);
        ovs_audio_mic_close(microphone);
    }
}

#[test]
fn fake_fail_keeps_the_code() {
    let microphone = ovs_audio_mic_open_fake(1);

    // SAFETY: as above
    unsafe {
        ovs_audio_mic_fake_fail(microphone, 0x8889_0004_u32 as i32);
        assert_eq!(ovs_audio_mic_error(microphone), 0x8889_0004_u32 as i32);
        ovs_audio_mic_close(microphone);
    }
}

#[test]
fn default_device_delivers_48k_and_closes_quickly() {
    let mut code = 0;
    // SAFETY: a null id opens the default capture device
    let microphone = unsafe { ovs_audio_mic_open(null(), &mut code) };
    if microphone.is_null() {
        eprintln!("no default capture device (HRESULT {code:#x}): skipped");
        return;
    }
    let started = Instant::now();
    let mut samples = 0usize;
    let mut buffer = vec![0.0f32; 48_000];
    // SAFETY: the microphone is open until closed below
    unsafe {
        let channels = ovs_audio_mic_channels(microphone) as usize;
        while started.elapsed() < Duration::from_millis(500) {
            samples += ovs_audio_mic_read(microphone, buffer.as_mut_ptr(), buffer.len() as u32) as usize;
            std::thread::yield_now();
        }
        let frames = samples / channels;
        assert_eq!(ovs_audio_mic_error(microphone), 0);
        // about 500 ms at 48 kHz, whatever rate the device runs at (Windows converts it)
        assert!((14_400..=33_600).contains(&frames), "{frames} frames in 500 ms");
        let closing = Instant::now();
        ovs_audio_mic_close(microphone);
        assert!(closing.elapsed() < Duration::from_millis(200));
    }
}

//! Package 173: the speaker output: steady-state allocation, the fake speaker's error, and the real WASAPI stream.
//! The real stream only ever plays silence here (nothing is written to it), so the test is never audible.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;
use std::ptr::null;
use std::time::{Duration, Instant};

use ovs_audio::core::playout::Playout;
use ovs_audio::speaker::{
    ovs_audio_speaker_close, ovs_audio_speaker_error, ovs_audio_speaker_fake_fail, ovs_audio_speaker_mmcss_task, ovs_audio_speaker_open,
    ovs_audio_speaker_open_fake,
};

/// Counts the allocations of the calling thread (A182).
struct CountingAllocator;

thread_local! {
    static ALLOCATIONS: Cell<u64> = const { Cell::new(0) };
}

// SAFETY: forwards to the system allocator and only counts
unsafe impl GlobalAlloc for CountingAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let _ = ALLOCATIONS.try_with(|count| count.set(count.get() + 1));
        // SAFETY: the caller's layout
        unsafe { System.alloc(layout) }
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        // SAFETY: allocated above with this layout
        unsafe { System.dealloc(pointer, layout) }
    }
}

#[global_allocator]
static ALLOCATOR: CountingAllocator = CountingAllocator;

fn allocations() -> u64 {
    ALLOCATIONS.with(Cell::get)
}

#[test]
fn fake_device_render_steady_state_allocates_nothing() {
    let playout = Playout::new();
    let frame = vec![0.25f32; 2 * 480];
    let mut out = vec![0f32; 2 * 480];
    playout.ring.write(&frame);
    playout.render(&mut out);
    let before = allocations();

    for _ in 0..10_000 {
        playout.ring.write(&frame);
        playout.render(&mut out);
    }

    assert_eq!(allocations() - before, 0);
}

#[test]
fn fake_device_error_ends_stream_and_keeps_code() {
    let speaker = ovs_audio_speaker_open_fake();
    let invalidated = 0x8889_0004_u32 as i32; // AUDCLNT_E_DEVICE_INVALIDATED

    // SAFETY: the speaker is open until closed below
    unsafe {
        assert_eq!(ovs_audio_speaker_error(speaker), 0);
        ovs_audio_speaker_fake_fail(speaker, invalidated);
        assert_eq!(ovs_audio_speaker_error(speaker), invalidated);
        ovs_audio_speaker_close(speaker);
    }
}

#[test]
fn close_while_rendering_returns_and_joins() {
    let mut code = 0;
    // SAFETY: a null id opens the default device
    let speaker = unsafe { ovs_audio_speaker_open(null(), &mut code) };
    if speaker.is_null() {
        eprintln!("no default render device (HRESULT {code:#x}): skipped");
        return;
    }
    let rendering = Instant::now();
    while rendering.elapsed() < Duration::from_millis(100) {
        std::thread::yield_now(); // let the device ask for a few periods of silence
    }

    // SAFETY: the speaker is open until closed here
    let (task, error) = unsafe { (ovs_audio_speaker_mmcss_task(speaker), ovs_audio_speaker_error(speaker)) };
    let closing = Instant::now();
    // SAFETY: as above
    unsafe { ovs_audio_speaker_close(speaker) };

    assert!(closing.elapsed() < Duration::from_millis(200), "close took {:?}", closing.elapsed());
    assert_eq!(error, 0);
    assert_ne!(task, 0, "the render thread is not registered with MMCSS");
}

#[test]
fn sounds_steady_state_allocate_nothing() {
    ovs_audio::core::sounds::load(7_001, &[0.1; 4_800]);
    let playout = Playout::new();
    let frame = vec![0.25f32; 2 * 480];
    let mut out = vec![0f32; 2 * 480];
    playout.play_sound(7_001, 0.5, 0, false);
    playout.ring.write(&frame);
    playout.render(&mut out);
    let before = allocations();

    for turn in 0..10_000 {
        if turn % 20 == 0 {
            playout.play_sound(7_001, 0.5, 0, turn % 40 == 0);
        }
        playout.ring.write(&frame);
        playout.render(&mut out);
    }

    assert_eq!(allocations() - before, 0);
}

#[test]
fn twenty_speakers_steady_state_allocate_nothing() {
    let packet = [0x08u8, 0x00, 0x00];
    let mut buffers: Vec<ovs_audio::core::jitter::JitterBuffer> = (0..20).map(|_| ovs_audio::core::jitter::JitterBuffer::new()).collect();
    for (speaker, buffer) in buffers.iter_mut().enumerate() {
        for seq in 0..3 {
            buffer.push(seq, &packet, false, f64::from(seq) * 20.0 + speaker as f64, None);
        }
        let _ = buffer.pull();
    }
    let before = allocations();

    for seq in 3..503u32 {
        for buffer in &mut buffers {
            buffer.push(seq, &packet, false, f64::from(seq) * 20.0, Some(&packet));
            let _ = buffer.pull();
        }
    }

    assert_eq!(allocations() - before, 0);
}

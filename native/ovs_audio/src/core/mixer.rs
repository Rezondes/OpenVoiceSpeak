//! Package 176: the voice mixer, ported from the managed `VoiceMixer`: one jitter buffer per speaker, summed into 20 ms
//! frames with each speaker's gain (the client computes it with `VolumeCurve` and the voice boost), limited, silenced
//! while deafened (except the own voice in the self test). It runs where the device asks for audio, on the render
//! thread, so the voices play on while managed code stands. Packets and settings come in through a bounded channel, so
//! the render thread never waits for a producer; the speakers' buffers come from a pool made once, so a new speaker
//! allocates nothing. What the client reads (who speaks, the level, the counts) is copied into a snapshot the render
//! thread only updates when no reader holds it.

use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TrySendError};
use std::sync::Mutex;

use super::decoder::STEREO_FRAME_SAMPLES;
use super::jitter::{JitterBuffer, JitterStats, MAX_PACKET};
use super::limiter::Limiter;
use super::ring::{Ring, CHANNELS};

/// Speakers the mixer can hold at once; a packet of a further one is dropped.
pub const POOL: usize = 64;
/// Package 124: a speaker keeps its buffer over talk pauses and loses it after a minute without a word.
pub const IDLE_TICKS_UNTIL_REMOVED: i32 = 3_000;
/// Packets and commands waiting for the mixer.
const QUEUE: usize = 256;
/// The echo canceller's far end holds a second of mono.
const FAR_END_FRAMES: usize = 48_000;
/// The echo canceller takes the far end in blocks of 10 ms.
const FAR_END_BLOCK: usize = 480;

/// One received packet, carried by value so nothing is allocated for it.
pub struct Packet {
    pub speaker: u32,
    pub seq: u32,
    pub via_link: bool,
    pub arrival_ms: f64,
    pub length: usize,
    pub data: [u8; MAX_PACKET],
    pub copy_length: Option<usize>,
    pub copy: [u8; MAX_PACKET],
}

// the packet travels by value: a box would be freed on the render thread (A182)
#[allow(clippy::large_enum_variant)]
enum Command {
    Packet(Packet),
    Gains { table: [(u32, f32); POOL], count: usize, others: f32 },
    Remove(u32),
    Clear,
}

/// One speaker of the pool.
struct Slot {
    id: Option<u32>,
    serial: u64,
    idle_ticks: i32,
    buffer: JitterBuffer,
}

/// What the client reads.
#[derive(Clone, Copy)]
pub struct Snapshot {
    pub active: [(u32, bool); POOL],
    pub active_count: usize,
    pub speakers: [(u32, u64); POOL],
    pub speaker_count: usize,
    pub level_db: f32,
    pub stats: JitterStats,
    pub busy: bool,
    pub mixes: u64,
}

impl Default for Snapshot {
    fn default() -> Snapshot {
        Snapshot {
            active: [(0, false); POOL],
            active_count: 0,
            speakers: [(0, 0); POOL],
            speaker_count: 0,
            level_db: -120.0,
            stats: JitterStats::default(),
            busy: false,
            mixes: 0,
        }
    }
}

struct State {
    commands: Receiver<Command>,
    slots: Vec<Slot>,
    gains: [(u32, f32); POOL],
    gain_count: usize,
    other_gain: f32,
    limiter: Limiter,
    frame: [f32; STEREO_FRAME_SAMPLES],
    used: usize, // samples of `frame` already handed out
    retired: JitterStats,
    serials: u64,
    snapshot: Snapshot,
}

/// The voice mixer.
pub struct Mixer {
    commands: SyncSender<Command>,
    pending: AtomicUsize,
    deafened: AtomicBool,
    self_test: AtomicBool,
    state: Mutex<State>,
    published: Mutex<Snapshot>,
    far_end: Ring,
    dropped: AtomicU64,
}

/// The id of the own voice in the self test.
pub const SELF_TEST_SPEAKER: u32 = 0;

/// Package 133: the counts of two buffers added, the worse jitter and delay.
fn add(sum: JitterStats, other: JitterStats) -> JitterStats {
    JitterStats {
        received: sum.received + other.received,
        lost: sum.lost + other.lost,
        rebuilt: sum.rebuilt + other.rebuilt,
        concealed: sum.concealed + other.concealed,
        recovered: sum.recovered + other.recovered,
        jitter_ms: sum.jitter_ms.max(other.jitter_ms),
        delay_ms: sum.delay_ms.max(other.delay_ms),
        ..JitterStats::default()
    }
}

/// The level in dB of the louder side of an interleaved stereo frame (`VoiceActivityDetector.LevelDb`).
pub fn level_db(frame: &[f32]) -> f32 {
    if frame.is_empty() {
        return -120.0;
    }
    let mut loudest = 0.0f64;
    for channel in 0..CHANNELS {
        let sum: f64 = frame.iter().skip(channel).step_by(CHANNELS).map(|sample| f64::from(*sample) * f64::from(*sample)).sum();
        loudest = loudest.max(sum);
    }
    let rms = (loudest / (frame.len() / CHANNELS) as f64).sqrt();
    if rms <= 1e-6 {
        -120.0
    } else {
        (20.0 * rms.log10()) as f32
    }
}

impl State {
    fn retire(&mut self, slot: usize) {
        let stats = self.slots[slot].buffer.stats();
        self.retired = add(self.retired, JitterStats { jitter_ms: 0.0, delay_ms: 0, ..stats }); // a gone speaker's jitter is not shown
        self.slots[slot].buffer.reset();
        self.slots[slot].id = None;
    }

    fn take(&mut self, command: Command) {
        match command {
            Command::Packet(packet) => {
                let found = self.slots.iter().position(|slot| slot.id == Some(packet.speaker));
                let slot = match found.or_else(|| self.slots.iter().position(|slot| slot.id.is_none())) {
                    Some(slot) => slot,
                    None => return,
                };
                if found.is_none() {
                    self.serials += 1;
                    let entry = &mut self.slots[slot];
                    entry.id = Some(packet.speaker);
                    entry.serial = self.serials;
                    entry.idle_ticks = 0;
                }
                let copy = packet.copy_length.map(|length| &packet.copy[..length]);
                self.slots[slot].buffer.push(packet.seq, &packet.data[..packet.length], packet.via_link, packet.arrival_ms, copy);
            }
            Command::Gains { table, count, others } => {
                self.gains = table;
                self.gain_count = count;
                self.other_gain = others;
            }
            Command::Remove(id) => {
                if let Some(slot) = self.slots.iter().position(|slot| slot.id == Some(id)) {
                    self.retire(slot);
                }
            }
            Command::Clear => {
                for slot in 0..self.slots.len() {
                    if self.slots[slot].id.is_some() {
                        self.retire(slot);
                    }
                }
            }
        }
    }

    fn gain_of(&self, id: u32) -> f32 {
        self.gains[..self.gain_count].iter().find(|(speaker, _)| *speaker == id).map_or(self.other_gain, |(_, gain)| *gain)
    }

    /// Mixes the next 20 ms into `frame` and fills the snapshot.
    fn mix(&mut self, silenced: bool, pending: bool) {
        self.frame.fill(0.0);
        self.snapshot.active_count = 0;
        for slot in 0..self.slots.len() {
            let Some(id) = self.slots[slot].id else { continue };
            let gain = self.gain_of(id);
            let pulled = match self.slots[slot].buffer.pull() {
                Some(pcm) => {
                    for (mixed, sample) in self.frame.iter_mut().zip(pcm.iter()) {
                        *mixed += sample * gain;
                    }
                    true
                }
                None => false,
            };
            if pulled {
                self.slots[slot].idle_ticks = 0;
                let via_link = self.slots[slot].buffer.stats().via_link != 0;
                self.snapshot.active[self.snapshot.active_count] = (id, via_link);
                self.snapshot.active_count += 1;
            } else {
                self.slots[slot].idle_ticks += 1;
                if self.slots[slot].idle_ticks >= IDLE_TICKS_UNTIL_REMOVED && self.slots[slot].buffer.stats().buffered == 0 {
                    self.retire(slot);
                }
            }
        }
        self.limiter.process(&mut self.frame);
        if silenced {
            self.frame.fill(0.0);
        }
        self.snapshot.level_db = level_db(&self.frame);
        self.snapshot.mixes += 1;
        self.summarize(pending);
    }

    /// The speakers, the counts and whether anything is left to play, into the snapshot.
    fn summarize(&mut self, pending: bool) {
        let mut stats = self.retired;
        let mut busy = pending;
        self.snapshot.speaker_count = 0;
        for slot in &self.slots {
            if let Some(id) = slot.id {
                let current = slot.buffer.stats();
                stats = add(stats, current);
                busy |= current.busy != 0;
                self.snapshot.speakers[self.snapshot.speaker_count] = (id, slot.serial);
                self.snapshot.speaker_count += 1;
            }
        }
        self.snapshot.stats = stats;
        self.snapshot.busy = busy;
    }
}

impl Default for Mixer {
    fn default() -> Mixer {
        Mixer::new()
    }
}

impl Mixer {
    /// A mixer with its pool of speakers made now.
    pub fn new() -> Mixer {
        let (commands, received) = sync_channel(QUEUE);
        let slots = (0..POOL).map(|_| Slot { id: None, serial: 0, idle_ticks: 0, buffer: JitterBuffer::new() }).collect();
        Mixer {
            commands,
            pending: AtomicUsize::new(0),
            deafened: AtomicBool::new(false),
            self_test: AtomicBool::new(false),
            state: Mutex::new(State {
                commands: received,
                slots,
                gains: [(0, 1.0); POOL],
                gain_count: 0,
                other_gain: 1.0,
                limiter: Limiter::default(),
                frame: [0.0; STEREO_FRAME_SAMPLES],
                used: STEREO_FRAME_SAMPLES,
                retired: JitterStats::default(),
                serials: 0,
                snapshot: Snapshot::default(),
            }),
            published: Mutex::new(Snapshot::default()),
            far_end: Ring::with_channels(FAR_END_FRAMES, 1),
            dropped: AtomicU64::new(0),
        }
    }

    fn send(&self, command: Command) -> bool {
        match self.commands.try_send(command) {
            Ok(()) => true,
            Err(TrySendError::Full(_) | TrySendError::Disconnected(_)) => {
                self.dropped.fetch_add(1, Ordering::Relaxed);
                false
            }
        }
    }

    /// Any thread: a received packet; dropped when the queue is full.
    pub fn push(&self, packet: Packet) {
        self.pending.fetch_add(1, Ordering::AcqRel);
        if !self.send(Command::Packet(packet)) {
            self.pending.fetch_sub(1, Ordering::AcqRel);
        }
    }

    /// The gain of each listed speaker and of everyone else (the client's final factors).
    pub fn set_gains(&self, gains: &[(u32, f32)], others: f32) {
        let mut table = [(0, 1.0); POOL];
        let count = gains.len().min(POOL);
        table[..count].copy_from_slice(&gains[..count]);
        self.send(Command::Gains { table, count, others });
    }

    /// Drops one speaker's buffer.
    pub fn remove(&self, id: u32) {
        self.send(Command::Remove(id));
    }

    /// Drops every speaker's buffer.
    pub fn clear(&self) {
        self.send(Command::Clear);
    }

    /// Deafened and the self test (the own voice plays in the self test also while deafened).
    pub fn set_flags(&self, deafened: bool, self_test: bool) {
        self.deafened.store(deafened, Ordering::Release);
        self.self_test.store(self_test, Ordering::Release);
    }

    /// Takes the waiting commands; whether packets are still pending after them.
    fn drain(&self, state: &mut State) -> bool {
        let mut drained = 0;
        while let Ok(command) = state.commands.try_recv() {
            if matches!(command, Command::Packet(_)) {
                drained += 1;
            }
            state.take(command);
        }
        self.pending.fetch_sub(drained, Ordering::AcqRel) - drained > 0
    }

    fn mix_next(&self, state: &mut State) {
        let pending = self.drain(state);
        let silenced = self.deafened.load(Ordering::Acquire) && !self.self_test.load(Ordering::Acquire);
        state.mix(silenced, pending);
        if let Ok(mut published) = self.published.try_lock() {
            *published = state.snapshot;
        }
    }

    /// Render thread: fills `out` (interleaved stereo) with the mix, 20 ms frames cut to what the device asks.
    pub fn render(&self, out: &mut [f32]) {
        let Ok(mut state) = self.state.lock() else { return };
        let mut written = 0;
        while written < out.len() {
            if state.used == STEREO_FRAME_SAMPLES {
                self.mix_next(&mut state);
                state.used = 0;
            }
            let count = (out.len() - written).min(STEREO_FRAME_SAMPLES - state.used);
            out[written..written + count].copy_from_slice(&state.frame[state.used..state.used + count]);
            state.used += count;
            written += count;
        }
    }

    /// One whole frame, for the client's own clock (no device) and the tests; the snapshot is updated at once.
    pub fn mix_once(&self, out: &mut [f32; STEREO_FRAME_SAMPLES]) {
        let Ok(mut state) = self.state.lock() else { return };
        self.mix_next(&mut state);
        state.used = STEREO_FRAME_SAMPLES;
        out.copy_from_slice(&state.frame);
        if let Ok(mut published) = self.published.lock() {
            *published = state.snapshot;
        }
    }

    /// Client thread: takes the waiting commands and refreshes the speakers and counts without mixing (for the
    /// rare reads between frames: the connection details, a member change, the tests).
    pub fn settle(&self) {
        let Ok(mut state) = self.state.lock() else { return };
        let pending = self.drain(&mut state);
        state.summarize(pending);
        if let Ok(mut published) = self.published.lock() {
            *published = state.snapshot;
        }
    }

    /// The last snapshot.
    pub fn snapshot(&self) -> Snapshot {
        let published = self.published.lock().map(|snapshot| *snapshot).unwrap_or_default();
        Snapshot { busy: published.busy || self.pending.load(Ordering::Acquire) > 0, ..published }
    }

    /// Render thread: the final output (voices and sounds) as the echo canceller's far end, downmixed to mono.
    pub fn write_far_end(&self, out: &[f32]) {
        let mut mono = [0.0f32; 480];
        for chunk in out.chunks(mono.len() * CHANNELS) {
            let frames = chunk.len() / CHANNELS;
            for (index, sample) in mono.iter_mut().take(frames).enumerate() {
                *sample = (chunk[index * CHANNELS] + chunk[index * CHANNELS + 1]) * 0.5;
            }
            self.far_end.write(&mono[..frames]);
        }
    }

    /// Capture thread: takes up to `out.len()` far-end samples in whole 10 ms blocks (what the echo canceller takes);
    /// returns how many.
    pub fn read_far_end(&self, out: &mut [f32]) -> usize {
        let count = (self.far_end.buffered() as usize).min(out.len()) / FAR_END_BLOCK * FAR_END_BLOCK;
        self.far_end.read(&mut out[..count]);
        count
    }

    /// Packets dropped because the queue was full or every slot taken.
    pub fn dropped(&self) -> u64 {
        self.dropped.load(Ordering::Relaxed)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn packet(speaker: u32, seq: u32) -> Packet {
        let mut packet = Packet {
            speaker,
            seq,
            via_link: speaker == 8,
            arrival_ms: f64::from(seq) * 20.0,
            length: 3,
            data: [0; MAX_PACKET],
            copy_length: None,
            copy: [0; MAX_PACKET],
        };
        packet.data[..3].copy_from_slice(&[0x08, 0x00, 0x00]);
        packet
    }

    #[test]
    fn active_speakers_report_via_link() {
        let mixer = Mixer::new();
        for seq in 0..3 {
            mixer.push(packet(7, seq));
            mixer.push(packet(8, seq));
        }
        let mut out = [0.0; STEREO_FRAME_SAMPLES];

        mixer.mix_once(&mut out);

        let snapshot = mixer.snapshot();
        assert_eq!(&snapshot.active[..snapshot.active_count], &[(7, false), (8, true)]);
    }

    #[test]
    fn render_cuts_frames_to_what_the_device_asks() {
        let mixer = Mixer::new();
        let mut out = vec![0.0; 2 * 700];

        mixer.render(&mut out);
        mixer.render(&mut out);

        assert_eq!(mixer.snapshot().mixes, 2); // 1400 frames: the first 960-frame mix and part of the second
    }

    #[test]
    fn remove_and_clear_retire_speakers_and_keep_their_counts() {
        let mixer = Mixer::new();
        mixer.push(packet(7, 0));
        mixer.push(packet(8, 0));
        let mut out = [0.0; STEREO_FRAME_SAMPLES];
        mixer.mix_once(&mut out);

        mixer.remove(7);
        mixer.mix_once(&mut out);
        assert_eq!(mixer.snapshot().speaker_count, 1);
        mixer.clear();
        mixer.mix_once(&mut out);

        let snapshot = mixer.snapshot();
        assert_eq!((snapshot.speaker_count, snapshot.stats.received), (0, 2));
    }

    #[test]
    fn far_end_equals_output_downmixed() {
        let mixer = Mixer::new();
        let out: Vec<f32> = (0..2 * 600).map(|index| if index % 2 == 0 { 0.5 } else { 0.25 }).collect();

        mixer.write_far_end(&out);
        let mut mono = vec![0.0; 700];
        let count = mixer.read_far_end(&mut mono);

        assert_eq!(count, 480); // whole 10 ms blocks; the rest waits
        assert!(mono[..480].iter().all(|sample| (*sample - 0.375).abs() < 1e-6));
    }

    #[test]
    fn level_db_takes_the_louder_side() {
        let frame: Vec<f32> = (0..1920).map(|index| if index % 2 == 0 { 0.5 } else { 0.0 }).collect();

        assert!((level_db(&frame) - 20.0 * 0.5f32.log10()).abs() < 1e-3);
        assert_eq!(level_db(&[0.0; 4]), -120.0);
    }

    #[test]
    fn deafened_silences_voices_unless_self_test() {
        let mixer = Mixer::new();
        let mut out = [0.0; STEREO_FRAME_SAMPLES];
        mixer.set_flags(true, false);
        for seq in 0..3 {
            mixer.push(packet(SELF_TEST_SPEAKER, seq));
        }
        mixer.mix_once(&mut out);

        assert!(out.iter().all(|sample| *sample == 0.0));
        assert_eq!(mixer.snapshot().active_count, 1); // the buffer still plays on, only unheard
    }
}

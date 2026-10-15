//! Package 174: the notification sounds, mixed on the render thread on top of the ring's frames. The samples live in
//! the library once loaded (the bank), so a sound that has started plays to its end whatever the client's threads do.
//! A sound starts at a device frame: the frames played so far plus what the ring held when it was started, which is
//! where the next frame the client writes will play, so the echo canceller's far end in the client stays aligned.

use std::collections::HashMap;
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TrySendError};
use std::sync::{Arc, Mutex, OnceLock};

use super::ring::CHANNELS;

/// At most this many sounds play at once; a further one replaces the oldest.
pub const MAX_PLAYING: usize = 16;

/// Commands waiting for the render thread; the client hands over at most a few per frame.
const QUEUE: usize = 64;

/// The mono samples of every loaded sound, by the client's id, process-wide.
fn bank() -> &'static Mutex<HashMap<u32, Arc<[f32]>>> {
    static BANK: OnceLock<Mutex<HashMap<u32, Arc<[f32]>>>> = OnceLock::new();
    BANK.get_or_init(|| Mutex::new(HashMap::new()))
}

/// Loads (or replaces) the samples of sound `id`; client thread.
pub fn load(id: u32, samples: &[f32]) {
    bank().lock().unwrap_or_else(|poisoned| poisoned.into_inner()).insert(id, samples.into());
}

/// The samples of sound `id`, if loaded.
pub fn samples(id: u32) -> Option<Arc<[f32]>> {
    bank().lock().unwrap_or_else(|poisoned| poisoned.into_inner()).get(&id).cloned()
}

enum Command {
    Play(Sound),
    StopAll,
}

struct Sound {
    id: u32,
    samples: Arc<[f32]>,
    gain: f32,
    start: u64,  // the device frame its first sample plays at
    offset: u64, // samples already played elsewhere (a speaker that reopened mid-sound)
    restart: bool,
}

/// The client's side: starts and stops sounds (one client thread at a time, the playback thread).
pub struct SoundStarter {
    commands: SyncSender<Command>,
}

/// The render thread's side: the sounds playing.
pub struct SoundMixer {
    commands: Receiver<Command>,
    playing: Vec<Sound>,
}

/// A connected pair of starter and mixer.
pub fn sounds() -> (SoundStarter, SoundMixer) {
    let (commands, received) = sync_channel(QUEUE);
    (SoundStarter { commands }, SoundMixer { commands: received, playing: Vec::with_capacity(MAX_PLAYING) })
}

impl SoundStarter {
    /// Starts sound `id` at `gain` on device frame `start`, `offset` samples in; with `restart` the same sound playing
    /// stops first (Package 159). False when the sound is not loaded or the queue is full.
    pub fn play(&self, id: u32, gain: f32, start: u64, offset: u64, restart: bool) -> bool {
        let Some(samples) = samples(id) else { return false };
        let sound = Sound { id, samples, gain, start, offset, restart };
        !matches!(self.commands.try_send(Command::Play(sound)), Err(TrySendError::Full(_) | TrySendError::Disconnected(_)))
    }

    /// Stops every sound.
    pub fn stop_all(&self) {
        let _ = self.commands.try_send(Command::StopAll);
    }
}

impl SoundMixer {
    /// Render thread: takes the new commands and adds the sounds' samples for device frames `first`.. into `out`
    /// (interleaved stereo; a mono sound on both sides), clamped to -1..1 where a sound played.
    pub fn mix(&mut self, out: &mut [f32], first: u64) {
        while let Ok(command) = self.commands.try_recv() {
            match command {
                Command::StopAll => self.playing.clear(),
                Command::Play(sound) => {
                    if sound.restart {
                        self.playing.retain(|playing| playing.id != sound.id);
                    }
                    if self.playing.len() == MAX_PLAYING {
                        self.playing.remove(0);
                    }
                    self.playing.push(sound);
                }
            }
        }
        if self.playing.is_empty() {
            return;
        }
        let frames = (out.len() / CHANNELS) as u64;
        for sound in &self.playing {
            for frame in 0..frames {
                let at = first + frame;
                if at < sound.start {
                    continue;
                }
                let index = at - sound.start + sound.offset;
                if index >= sound.samples.len() as u64 {
                    break;
                }
                let sample = sound.samples[index as usize] * sound.gain;
                for channel in 0..CHANNELS {
                    out[frame as usize * CHANNELS + channel] += sample;
                }
            }
        }
        for sample in out.iter_mut() {
            *sample = sample.clamp(-1.0, 1.0);
        }
        let end = first + frames;
        self.playing.retain(|sound| end < sound.start || end - sound.start + sound.offset < sound.samples.len() as u64);
    }

    /// Sounds still playing (or waiting for their start).
    pub fn playing(&self) -> usize {
        self.playing.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn mixed(mixer: &mut SoundMixer, frames: usize, first: u64) -> Vec<f32> {
        let mut out = vec![0.0; frames * CHANNELS];
        mixer.mix(&mut out, first);
        out
    }

    #[test]
    fn play_mixes_on_top_of_ring_frames_with_gain() {
        load(9001, &[0.5, 0.25]);
        let (starter, mut mixer) = sounds();
        assert!(starter.play(9001, 0.5, 0, 0, false));
        let mut out = vec![0.1; 3 * CHANNELS];

        mixer.mix(&mut out, 0);

        assert_eq!(out, vec![0.35, 0.35, 0.225, 0.225, 0.1, 0.1]);
        assert_eq!(mixer.playing(), 0);
    }

    #[test]
    fn two_sounds_overlap_and_are_clamped() {
        load(9002, &[0.8, 0.8]);
        let (starter, mut mixer) = sounds();
        starter.play(9002, 1.0, 0, 0, false);
        starter.play(9002, 1.0, 0, 0, false);

        let out = mixed(&mut mixer, 2, 0);

        assert_eq!(out, vec![1.0; 4]);
    }

    #[test]
    fn producer_paused_sound_plays_on_without_gap() {
        let tone: Vec<f32> = (0..24_000).map(|index| (index % 100) as f32 / 1_000.0 + 0.001).collect(); // 500 ms
        load(9003, &tone);
        let playout = crate::core::playout::Playout::new();
        playout.ring.write(&vec![0.0; 960 * CHANNELS]);
        assert!(playout.play_sound(9003, 1.0, 0, false));
        let mut played = Vec::new();
        let mut out = vec![0.0; 480 * CHANNELS];

        for _ in 0..60 {
            playout.render(&mut out); // the client writes nothing more: the ring runs empty after 20 ms
            played.extend(out.chunks(CHANNELS).map(|frame| frame[0]));
        }

        let heard: Vec<f32> = played.into_iter().filter(|sample| *sample != 0.0).collect();
        assert_eq!(heard, tone);
    }

    #[test]
    fn restart_same_id_stops_the_running_one() {
        load(9004, &[0.5; 4]);
        let (starter, mut mixer) = sounds();
        starter.play(9004, 1.0, 0, 0, false);
        mixed(&mut mixer, 2, 0);

        starter.play(9004, 1.0, 2, 0, true);
        let out = mixed(&mut mixer, 2, 2);

        assert_eq!(out, vec![0.5; 4]);
        assert_eq!(mixer.playing(), 1);
    }

    #[test]
    fn stop_all_ends_every_sound() {
        load(9005, &[0.5; 100]);
        let (starter, mut mixer) = sounds();
        starter.play(9005, 1.0, 0, 0, false);
        mixed(&mut mixer, 2, 0);

        starter.stop_all();

        assert_eq!(mixed(&mut mixer, 2, 2), vec![0.0; 4]);
        assert_eq!(mixer.playing(), 0);
    }

    #[test]
    fn sound_starts_at_the_frame_written_next() {
        load(9006, &[0.5]);
        let playout = crate::core::playout::Playout::new();
        playout.ring.write(&[0.1; 3 * CHANNELS]);
        playout.play_sound(9006, 1.0, 0, false);
        playout.ring.write(&[0.2; CHANNELS]);
        let mut out = vec![0.0; 4 * CHANNELS];

        playout.render(&mut out);

        assert_eq!(out, vec![0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.7, 0.7]);
    }

    #[test]
    fn offset_starts_inside_the_sound() {
        load(9007, &[0.1, 0.2, 0.3]);
        let (starter, mut mixer) = sounds();
        starter.play(9007, 1.0, 0, 2, false);

        assert_eq!(mixed(&mut mixer, 2, 0), vec![0.3, 0.3, 0.0, 0.0]);
    }

    #[test]
    fn unloaded_sound_not_played() {
        let (starter, mut mixer) = sounds();

        assert!(!starter.play(9999_9999, 1.0, 0, 0, false));
        assert_eq!(mixed(&mut mixer, 1, 0), vec![0.0; 2]);
    }
}

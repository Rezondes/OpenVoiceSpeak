//! Package 173 (A177): the WASAPI render stream. Shared mode, 48 kHz float stereo with `AUTOCONVERTPCM`, so Windows
//! converts to the device's own rate; event driven on its own thread, registered with MMCSS "Pro Audio". All COM work
//! happens on that thread; the opener waits only for its first answer.

use std::sync::atomic::{AtomicI32, AtomicU32, Ordering};
use std::sync::{mpsc, Arc};
use std::thread::JoinHandle;

use windows::core::{w, Result, PCWSTR};
use windows::Win32::Foundation::{CloseHandle, E_FAIL, HANDLE, WAIT_OBJECT_0};
use windows::Win32::Media::Audio::{
    eConsole, eRender, IAudioClient, IAudioRenderClient, IMMDeviceEnumerator, MMDeviceEnumerator, AUDCLNT_SHAREMODE_SHARED,
    AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, WAVEFORMATEX,
    WAVEFORMATEXTENSIBLE, WAVEFORMATEXTENSIBLE_0,
};
use windows::Win32::Media::KernelStreaming::{SPEAKER_FRONT_LEFT, SPEAKER_FRONT_RIGHT, WAVE_FORMAT_EXTENSIBLE};
use windows::Win32::Media::Multimedia::KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
use windows::Win32::System::Com::{CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_ALL, COINIT_MULTITHREADED};
use windows::Win32::System::Threading::{
    AvRevertMmThreadCharacteristics, AvSetMmThreadCharacteristicsW, CreateEventW, SetEvent, WaitForMultipleObjects, INFINITE,
};

use super::RenderStream;
use crate::core::playout::Playout;
use crate::core::ring::CHANNELS;

/// The rate the client mixes at; Windows converts it to the device's.
pub const SAMPLE_RATE: u32 = 48_000;

/// The device's own buffer, `WasapiHardware.SpeakerBufferMs` (30 ms), in units of 100 ns.
const BUFFER_HNS: i64 = 30 * 10_000;

/// Why opening failed: the HRESULT and its text.
pub type OpenError = (i32, String);

/// An event handle, closed when dropped; shared with the stream's thread.
pub struct Event(pub HANDLE);

impl Event {
    /// A new auto-reset (`manual` false) or manual-reset event, not signalled.
    pub fn new(manual: bool) -> std::result::Result<Event, OpenError> {
        // SAFETY: plain event creation, the handle is owned by the result
        unsafe { CreateEventW(None, manual, false, PCWSTR::null()) }.map(Event).map_err(describe)
    }

    /// Wakes whoever waits on it.
    pub fn signal(&self) {
        // SAFETY: the handle lives as long as self
        unsafe {
            let _ = SetEvent(self.0);
        }
    }
}

// SAFETY: an event handle may be waited on and signalled from any thread
unsafe impl Send for Event {}
// SAFETY: as above
unsafe impl Sync for Event {}

impl Drop for Event {
    fn drop(&mut self) {
        // SAFETY: the handle came from CreateEventW and is closed once
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

struct Shared {
    error: AtomicI32,
    mmcss: AtomicU32,
}

/// A running WASAPI render stream.
pub struct WasapiRender {
    shared: Arc<Shared>,
    stop: Arc<Event>,
    thread: Option<JoinHandle<()>>,
}

impl WasapiRender {
    /// Opens the render device `id` (an MMDevice id in UTF-16, no terminator), or the default device of the console role
    /// when `id` is `None` (as `WasapiOut` did), and starts playing `playout` on it.
    pub fn open(id: Option<Vec<u16>>, playout: Arc<Playout>) -> std::result::Result<WasapiRender, OpenError> {
        // SAFETY: plain event creation, the handle is owned by Event
        let stop = Arc::new(Event(unsafe { CreateEventW(None, true, false, PCWSTR::null()) }.map_err(describe)?));
        let shared = Arc::new(Shared { error: AtomicI32::new(0), mmcss: AtomicU32::new(0) });
        let (ready, answer) = mpsc::channel();
        let thread = {
            let (shared, stop) = (shared.clone(), stop.clone());
            std::thread::Builder::new()
                .name("ovs_audio render".into())
                .spawn(move || run(id, &playout, &shared, &stop, &ready))
                .map_err(|error| (E_FAIL.0, error.to_string()))?
        };
        let opened = answer.recv().unwrap_or_else(|_| Err((E_FAIL.0, "the render thread ended before it answered".into())));
        match opened {
            Ok(()) => Ok(WasapiRender { shared, stop, thread: Some(thread) }),
            Err(error) => {
                let _ = thread.join();
                Err(error)
            }
        }
    }
}

impl RenderStream for WasapiRender {
    fn error(&self) -> i32 {
        self.shared.error.load(Ordering::Acquire)
    }

    fn mmcss_task(&self) -> u32 {
        self.shared.mmcss.load(Ordering::Acquire)
    }
}

impl Drop for WasapiRender {
    fn drop(&mut self) {
        // SAFETY: the stop event lives as long as self
        unsafe {
            let _ = SetEvent(self.stop.0);
        }
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn describe(error: windows::core::Error) -> OpenError {
    (error.code().0, error.message())
}

/// The render thread: opens, answers the opener, pumps until stopped or failed, cleans up.
fn run(id: Option<Vec<u16>>, playout: &Playout, shared: &Shared, stop: &Event, ready: &mpsc::Sender<std::result::Result<(), OpenError>>) {
    // SAFETY: COM for this thread only, undone below
    let com = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
    // SAFETY: COM is initialized on this thread
    match unsafe { Stream::open(id.as_deref()) } {
        Err(error) => {
            let _ = ready.send(Err(describe(error)));
        }
        Ok(stream) => {
            let mut task_index = 0u32;
            // SAFETY: registers this thread; reverted below
            let task = unsafe { AvSetMmThreadCharacteristicsW(w!("Pro Audio"), &mut task_index) };
            if task.is_ok() {
                shared.mmcss.store(task_index, Ordering::Release);
            }
            // SAFETY: the stream was opened on this thread
            match unsafe { stream.start(playout) } {
                Err(error) => {
                    let _ = ready.send(Err(describe(error)));
                }
                Ok(()) => {
                    let _ = ready.send(Ok(()));
                    // SAFETY: as above
                    if let Err(error) = unsafe { stream.pump(playout, stop) } {
                        shared.error.store(error.code().0, Ordering::Release);
                    }
                    // SAFETY: as above
                    unsafe {
                        let _ = stream.client.Stop();
                    }
                }
            }
            if let Ok(task) = task {
                // SAFETY: the handle AvSetMmThreadCharacteristicsW returned on this thread
                unsafe {
                    let _ = AvRevertMmThreadCharacteristics(task);
                }
            }
        }
    }
    if com.is_ok() {
        // SAFETY: balances the successful CoInitializeEx; every COM object of this thread is released by now
        unsafe { CoUninitialize() };
    }
}

struct Stream {
    client: IAudioClient,
    render: IAudioRenderClient,
    event: Event,
    frames: u32,
}

impl Stream {
    /// # Safety
    /// COM must be initialized on the calling thread.
    unsafe fn open(id: Option<&[u16]>) -> Result<Stream> {
        // SAFETY: the caller initialized COM; every pointer passed lives through its call
        unsafe {
            let enumerator: IMMDeviceEnumerator = CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
            let device = match id {
                Some(id) => {
                    let terminated: Vec<u16> = id.iter().copied().chain([0]).collect();
                    enumerator.GetDevice(PCWSTR(terminated.as_ptr()))?
                }
                None => enumerator.GetDefaultAudioEndpoint(eRender, eConsole)?,
            };
            let client: IAudioClient = device.Activate(CLSCTX_ALL, None)?;
            let format = WAVEFORMATEXTENSIBLE {
                Format: WAVEFORMATEX {
                    wFormatTag: WAVE_FORMAT_EXTENSIBLE as u16,
                    nChannels: CHANNELS as u16,
                    nSamplesPerSec: SAMPLE_RATE,
                    nAvgBytesPerSec: SAMPLE_RATE * 8,
                    nBlockAlign: 8,
                    wBitsPerSample: 32,
                    cbSize: 22,
                },
                Samples: WAVEFORMATEXTENSIBLE_0 { wValidBitsPerSample: 32 },
                dwChannelMask: SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
                SubFormat: KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
            };
            let flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            client.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, BUFFER_HNS, 0, &format as *const _ as *const WAVEFORMATEX, None)?;
            let event = Event(CreateEventW(None, false, false, PCWSTR::null())?);
            client.SetEventHandle(event.0)?;
            let frames = client.GetBufferSize()?;
            let render: IAudioRenderClient = client.GetService()?;
            Ok(Stream { client, render, event, frames })
        }
    }

    /// Hands the device as many frames as it has room for.
    unsafe fn fill(&self, playout: &Playout) -> Result<()> {
        // SAFETY: the buffer WASAPI returns holds `free` frames of CHANNELS floats until ReleaseBuffer
        unsafe {
            let free = self.frames - self.client.GetCurrentPadding()?;
            if free == 0 {
                return Ok(());
            }
            let data = self.render.GetBuffer(free)? as *mut f32;
            playout.render(std::slice::from_raw_parts_mut(data, free as usize * CHANNELS));
            self.render.ReleaseBuffer(free, 0)
        }
    }

    /// Fills the buffer once (so the device starts with audio, not a gap) and starts the device.
    unsafe fn start(&self, playout: &Playout) -> Result<()> {
        // SAFETY: as in fill
        unsafe {
            self.fill(playout)?;
            self.client.Start()
        }
    }

    /// Refills on every device event until `stop` is signalled; returns the error that ended it.
    unsafe fn pump(&self, playout: &Playout, stop: &Event) -> Result<()> {
        loop {
            // SAFETY: both handles live through the wait
            let woke = unsafe { WaitForMultipleObjects(&[stop.0, self.event.0], false, INFINITE) };
            if woke == WAIT_OBJECT_0 {
                return Ok(());
            }
            // SAFETY: as in fill
            unsafe { self.fill(playout)? };
        }
    }
}

/// Package 177: the microphone's own buffer in WASAPI, `CapturePipeline.MicBufferMs` (20 ms), in units of 100 ns.
const CAPTURE_BUFFER_HNS: i64 = 20 * 10_000;

/// Package 177: what an opened capture stream hands back: the ring it fills and its channel count.
pub type Opened = (Arc<crate::core::ring::Ring>, u32);

/// Package 177: a running WASAPI capture stream. It writes what the device delivers (48 kHz float, the device's own
/// channel count, converted by Windows) into a ring of one second and signals `ready` after each round, so the client
/// can be late by up to a second without losing anything.
pub struct WasapiCapture {
    shared: Arc<Shared>,
    stop: Arc<Event>,
    thread: Option<JoinHandle<()>>,
}

impl WasapiCapture {
    /// Opens the capture device `id` (MMDevice id, no terminator) or the console default, and starts it.
    pub fn open(id: Option<Vec<u16>>, ready: Arc<Event>) -> std::result::Result<(WasapiCapture, Opened), OpenError> {
        let stop = Arc::new(Event::new(true)?);
        let shared = Arc::new(Shared { error: AtomicI32::new(0), mmcss: AtomicU32::new(0) });
        let (answer, answered) = mpsc::channel();
        let thread = {
            let (shared, stop) = (shared.clone(), stop.clone());
            std::thread::Builder::new()
                .name("ovs_audio capture".into())
                .spawn(move || capture(id, &shared, &stop, &ready, &answer))
                .map_err(|error| (E_FAIL.0, error.to_string()))?
        };
        match answered.recv().unwrap_or_else(|_| Err((E_FAIL.0, "the capture thread ended before it answered".into()))) {
            Ok(opened) => Ok((WasapiCapture { shared, stop, thread: Some(thread) }, opened)),
            Err(error) => {
                let _ = thread.join();
                Err(error)
            }
        }
    }

    /// The HRESULT that ended the stream, 0 while it runs.
    pub fn error(&self) -> i32 {
        self.shared.error.load(Ordering::Acquire)
    }

    /// The MMCSS task index of the capture thread, 0 when it is not registered.
    pub fn mmcss_task(&self) -> u32 {
        self.shared.mmcss.load(Ordering::Acquire)
    }
}

impl Drop for WasapiCapture {
    fn drop(&mut self) {
        self.stop.signal();
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn capture(id: Option<Vec<u16>>, shared: &Shared, stop: &Event, ready: &Event, answer: &mpsc::Sender<std::result::Result<Opened, OpenError>>) {
    // SAFETY: COM for this thread only, undone below
    let com = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
    // SAFETY: COM is initialized on this thread
    match unsafe { CaptureStream::open(id.as_deref()) } {
        Err(error) => {
            let _ = answer.send(Err(describe(error)));
        }
        Ok(stream) => {
            let mut task_index = 0u32;
            // SAFETY: registers this thread; reverted below
            let task = unsafe { AvSetMmThreadCharacteristicsW(w!("Pro Audio"), &mut task_index) };
            if task.is_ok() {
                shared.mmcss.store(task_index, Ordering::Release);
            }
            // SAFETY: the stream was opened on this thread
            match unsafe { stream.client.Start() } {
                Err(error) => {
                    let _ = answer.send(Err(describe(error)));
                }
                Ok(()) => {
                    let _ = answer.send(Ok((stream.ring.clone(), stream.channels)));
                    // SAFETY: as above
                    if let Err(error) = unsafe { stream.pump(stop, ready) } {
                        shared.error.store(error.code().0, Ordering::Release);
                        ready.signal(); // the client looks at once and finds the error
                    }
                    // SAFETY: as above
                    unsafe {
                        let _ = stream.client.Stop();
                    }
                }
            }
            if let Ok(task) = task {
                // SAFETY: the handle AvSetMmThreadCharacteristicsW returned on this thread
                unsafe {
                    let _ = AvRevertMmThreadCharacteristics(task);
                }
            }
        }
    }
    if com.is_ok() {
        // SAFETY: balances the successful CoInitializeEx; every COM object of this thread is released by now
        unsafe { CoUninitialize() };
    }
}

struct CaptureStream {
    client: IAudioClient,
    capture: windows::Win32::Media::Audio::IAudioCaptureClient,
    event: Event,
    ring: Arc<crate::core::ring::Ring>,
    channels: u32,
}

impl CaptureStream {
    /// # Safety
    /// COM must be initialized on the calling thread.
    unsafe fn open(id: Option<&[u16]>) -> Result<CaptureStream> {
        // SAFETY: the caller initialized COM; every pointer passed lives through its call
        unsafe {
            let enumerator: IMMDeviceEnumerator = CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
            let device = match id {
                Some(id) => {
                    let terminated: Vec<u16> = id.iter().copied().chain([0]).collect();
                    enumerator.GetDevice(PCWSTR(terminated.as_ptr()))?
                }
                None => enumerator.GetDefaultAudioEndpoint(windows::Win32::Media::Audio::eCapture, eConsole)?,
            };
            let client: IAudioClient = device.Activate(CLSCTX_ALL, None)?;
            let mix = client.GetMixFormat()?;
            let channels = u32::from((*mix).nChannels).max(1);
            windows::Win32::System::Com::CoTaskMemFree(Some(mix as *const _));
            let format = WAVEFORMATEX {
                wFormatTag: 3, // WAVE_FORMAT_IEEE_FLOAT
                nChannels: channels as u16,
                nSamplesPerSec: SAMPLE_RATE,
                nAvgBytesPerSec: SAMPLE_RATE * 4 * channels,
                nBlockAlign: (4 * channels) as u16,
                wBitsPerSample: 32,
                cbSize: 0,
            };
            let flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            client.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, CAPTURE_BUFFER_HNS, 0, &format, None)?;
            let event = Event(CreateEventW(None, false, false, PCWSTR::null())?);
            client.SetEventHandle(event.0)?;
            let capture = client.GetService()?;
            let ring = Arc::new(crate::core::ring::Ring::with_channels(SAMPLE_RATE as usize, channels as usize));
            Ok(CaptureStream { client, capture, event, ring, channels })
        }
    }

    /// Moves every packet the device has into the ring on each device event until `stop` is signalled.
    unsafe fn pump(&self, stop: &Event, ready: &Event) -> Result<()> {
        const SILENT: u32 = 2; // AUDCLNT_BUFFERFLAGS_SILENT
        let silence = [0.0f32; 960];
        loop {
            // SAFETY: both handles live through the wait
            let woke = unsafe { WaitForMultipleObjects(&[stop.0, self.event.0], false, INFINITE) };
            if woke == WAIT_OBJECT_0 {
                return Ok(());
            }
            // SAFETY: the buffer WASAPI returns holds `frames` frames of `channels` floats until ReleaseBuffer
            unsafe {
                while self.capture.GetNextPacketSize()? > 0 {
                    let (mut data, mut frames, mut flags) = (std::ptr::null_mut(), 0u32, 0u32);
                    self.capture.GetBuffer(&mut data, &mut frames, &mut flags, None, None)?;
                    let samples = frames as usize * self.channels as usize;
                    if flags & SILENT != 0 || data.is_null() {
                        for chunk in (0..samples).step_by(silence.len()) {
                            self.ring.write(&silence[..(samples - chunk).min(silence.len())]);
                        }
                    } else {
                        self.ring.write(std::slice::from_raw_parts(data as *const f32, samples));
                    }
                    self.capture.ReleaseBuffer(frames)?;
                }
            }
            ready.signal();
        }
    }
}

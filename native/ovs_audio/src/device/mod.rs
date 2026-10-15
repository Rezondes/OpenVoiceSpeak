//! Package 173 (A177): the device layer, one module per platform behind [`RenderStream`]; later platforms add theirs
//! (A189). A stream plays a [`crate::core::playout::Playout`] on its own thread until it is dropped.

#[cfg(windows)]
pub mod wasapi;

/// An open render stream; dropping it stops the device and joins its thread.
pub trait RenderStream: Send {
    /// The platform error code (on Windows the HRESULT) that ended the stream, 0 while it runs.
    fn error(&self) -> i32;

    /// The MMCSS task index of the render thread, 0 when it is not registered.
    fn mmcss_task(&self) -> u32;
}

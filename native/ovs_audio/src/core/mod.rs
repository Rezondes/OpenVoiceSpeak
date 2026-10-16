//! The platform-neutral core (A177): no OS call in here, so everything is testable by `cargo test` without a device
//! and moves unchanged to other platforms (A189). `tests/core_has_no_os.rs` checks the sources.

pub mod decoder;
pub mod encoder;
pub mod jitter;
pub mod limiter;
pub mod mixer;
pub mod playout;
pub mod ring;
pub mod sounds;

/// The ABI version of the exports; the client refuses a library with another one (A180).
pub const ABI_VERSION: u32 = 1;

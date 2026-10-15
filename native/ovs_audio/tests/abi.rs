//! Package 172: the exports' contract with the client.

use ovs_audio::{guard, ovs_audio_abi_version, ovs_audio_last_error};

fn last_error() -> String {
    let mut buffer = [0u8; 256];
    // SAFETY: the buffer holds 256 bytes
    let length = unsafe { ovs_audio_last_error(buffer.as_mut_ptr(), buffer.len()) };
    String::from_utf8_lossy(&buffer[..length.min(buffer.len())]).into_owned()
}

#[test]
fn abi_version_is_one() {
    assert_eq!(ovs_audio_abi_version(), 1);
}

#[test]
fn export_panic_returns_error_and_names_it() {
    let result = guard(-1, || -> i32 { panic!("boom in an export") });

    assert_eq!(result, -1);
    assert!(last_error().contains("boom in an export"), "{}", last_error());
}

#[test]
fn last_error_small_buffer_returns_whole_length() {
    let _ = guard(0, || -> i32 { panic!("a long enough message") });
    let mut buffer = [0u8; 4];

    // SAFETY: the buffer holds 4 bytes
    let length = unsafe { ovs_audio_last_error(buffer.as_mut_ptr(), buffer.len()) };

    assert_eq!(length, "panic: a long enough message".len());
    assert_eq!(&buffer, b"pani");
}

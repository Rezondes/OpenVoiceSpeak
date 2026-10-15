//! Package 172 (A177): the core stays platform neutral.

use std::fs;
use std::path::Path;

fn sources(folder: &Path, found: &mut Vec<(String, String)>) {
    for entry in fs::read_dir(folder).expect("core folder") {
        let path = entry.expect("entry").path();
        if path.is_dir() {
            sources(&path, found);
        } else if path.extension().is_some_and(|extension| extension == "rs") {
            found.push((path.display().to_string(), fs::read_to_string(&path).expect("source")));
        }
    }
}

#[test]
fn core_sources_use_no_os_api() {
    let mut found = Vec::new();
    sources(&Path::new(env!("CARGO_MANIFEST_DIR")).join("src").join("core"), &mut found);

    assert!(!found.is_empty());
    for (path, text) in found {
        for forbidden in ["use windows", "windows::", "std::os", "extern \"system\""] {
            assert!(!text.contains(forbidden), "{path} uses {forbidden}");
        }
    }
}

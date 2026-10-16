//! Package 178 (A185): with the feature "aec", compiles the AEC3 shim and links WebRTC's audio processing and abseil
//! statically, from the MSVC build `build.ps1` makes in the folder named by OVS_AEC_BUILD (src and b inside).

use std::path::Path;

fn main() {
    println!("cargo:rerun-if-changed=shim/aec.cpp");
    println!("cargo:rerun-if-env-changed=OVS_AEC_BUILD");
    if std::env::var_os("CARGO_FEATURE_AEC").is_none() {
        return;
    }
    let work = std::env::var("OVS_AEC_BUILD").expect("OVS_AEC_BUILD names the folder build.ps1 builds webrtc-audio-processing in");
    let source = Path::new(&work).join("src");
    let build = Path::new(&work).join("b");
    let abseil = "abseil-cpp-20240722.0";
    cc::Build::new()
        .cpp(true)
        .file("shim/aec.cpp")
        .include(&source)
        .include(source.join("webrtc"))
        .include(source.join("subprojects").join(abseil))
        .flag("/std:c++20")
        .flag("/EHsc")
        .define("WEBRTC_WIN", None)
        .define("_WIN32", None)
        .define("NOMINMAX", None)
        .define("_USE_MATH_DEFINES", None)
        .define("NDEBUG", None)
        .compile("ovs_aec_shim");
    let processing = build.join("webrtc").join("modules").join("audio_processing");
    println!("cargo:rustc-link-search=native={}", processing.display());
    println!("cargo:rustc-link-lib=static:+verbatim=libwebrtc-audio-processing-2.a");
    let abseil_build = build.join("subprojects").join(abseil);
    println!("cargo:rustc-link-search=native={}", abseil_build.display());
    let mut libraries: Vec<String> = std::fs::read_dir(&abseil_build)
        .expect("the abseil build")
        .filter_map(|entry| entry.ok().map(|entry| entry.file_name().to_string_lossy().into_owned()))
        .filter(|name| name.starts_with("libabsl_") && name.ends_with(".a"))
        .collect();
    libraries.sort();
    for library in libraries {
        println!("cargo:rustc-link-lib=static:+verbatim={library}");
    }
    println!("cargo:rustc-link-lib=winmm");
}

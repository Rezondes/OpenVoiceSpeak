// Package 140: C wrapper around WebRTC's audio processing module (AEC3 only) for P/Invoke.
// 48 kHz mono float frames of 10 ms (480 samples) in the range [-1, 1].
// Render and capture may run on different threads; the module locks them itself.
// No C++ exception crosses into .NET: create catches everything, the rest are noexcept; a null handle does nothing.

#include <new>

#include <webrtc/modules/audio_processing/include/audio_processing.h>

#define OVS_AEC_API extern "C" __declspec(dllexport)

namespace {
constexpr int kRate = 48000;
constexpr int kNullHandle = -1;

struct OvsAec {
  rtc::scoped_refptr<webrtc::AudioProcessing> apm;
  webrtc::StreamConfig stream{kRate, 1};
};
}  // namespace

// Returns null on failure.
OVS_AEC_API void* ovs_aec_create() noexcept {
  try {
    auto* aec = new (std::nothrow) OvsAec();
    if (!aec) return nullptr;
    aec->apm = webrtc::AudioProcessingBuilder().Create();
    if (!aec->apm) {
      delete aec;
      return nullptr;
    }
    webrtc::AudioProcessing::Config config;
    config.echo_canceller.enabled = true;
    config.echo_canceller.mobile_mode = false;
    // no forced high-pass: it took 1.4 dB off a deep voice without any echo (Package 140, AC3)
    config.echo_canceller.enforce_high_pass_filtering = false;
    aec->apm->ApplyConfig(config);
    return aec;
  } catch (...) {
    return nullptr;
  }
}

// The delay between a render frame and its echo in the capture, in ms (a hint, AEC3 adapts).
OVS_AEC_API void ovs_aec_set_delay(void* handle, int delay_ms) noexcept {
  if (!handle) return;
  static_cast<OvsAec*>(handle)->apm->set_stream_delay_ms(delay_ms);
}

// The far end: 480 samples about to be played. Returns 0 on success.
OVS_AEC_API int ovs_aec_render(void* handle, const float* frame) noexcept {
  if (!handle || !frame) return kNullHandle;
  auto* aec = static_cast<OvsAec*>(handle);
  const float* channels[1] = {frame};
  return aec->apm->AnalyzeReverseStream(channels, aec->stream);
}

// The near end: 480 captured samples, echo removed in place. Returns 0 on success.
OVS_AEC_API int ovs_aec_capture(void* handle, float* frame) noexcept {
  if (!handle || !frame) return kNullHandle;
  auto* aec = static_cast<OvsAec*>(handle);
  float* channels[1] = {frame};
  return aec->apm->ProcessStream(channels, aec->stream, aec->stream, channels);
}

OVS_AEC_API void ovs_aec_destroy(void* handle) noexcept {
  delete static_cast<OvsAec*>(handle);  // null is fine
}

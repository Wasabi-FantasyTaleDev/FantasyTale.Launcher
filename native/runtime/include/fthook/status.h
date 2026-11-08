#pragma once
#include "engine.h"

namespace fthook {

/// Writes a bounded, credential-free schema 1 report beside the host DLL, using atomic replacement.
/// Transient replacement contention is retried for at most 200 ms; permanent errors fail.
/// Callers serialize publication. No file is written for legacy sessions; failures return false.
bool publish_status(const std::wstring& folder, const Session& session, const char* state,
    const char* error, const std::vector<PluginReport>& reports, bool bootstrap_complete = false);

} // namespace fthook

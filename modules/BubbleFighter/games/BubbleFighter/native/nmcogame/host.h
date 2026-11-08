#pragma once
// Process-wide state of the nmcogame.dll host: session, messenger mode, hook worker.
#include "messenger.h"

#include <windows.h>

#include <string>

namespace bubblefighter::host {

/// How messenger calls are answered (docs/decisions D1).
enum class Mode { StandIn, Forward, Failed };

/// Export slots, in the order of kExportNames.
enum Export : int {
    CallNMFunc, CallNMFunc2, MemoryFree, SetLocale, SetLocaleAndRegion, SetPatchOption,
    SetUseFriendModuleOption, SetUseNGMOption, SetVersionFileUrl, SetVersionFileUrlA, SetVersionFileUrlW, ExportCount
};

/// Every messenger entry point seen across Bubble Fighter client builds.
extern const char* const kExportNames[ExportCount];

/// Records this DLL's module handle; called from DllMain.
void set_module(HMODULE module);
/// Loads session.toml (once) and opens the log. Safe to call from any thread after DllMain.
void ensure_initialized();
/// The selected messenger mode (ensure_initialized() first).
Mode mode();
/// The identity used for the passport.
nmco::Identity& identity();
/// Starts the hook worker thread; called from DllMain.
void start_worker();
/// Refuses authentication until a managed launch's required plugins have initialized.
bool await_authentication_ready();
/// Copies the immutable schema-two launch identity only after required plugins are ready; no fallback identity.
bool managed_credentials(std::string& account, std::string& ticket);
/// Latches a host failure and publishes it without credentials.
void fail_host();
/// Loads the forwarding DLL outside the loader lock; false is a host failure.
bool initialize_forward();

} // namespace bubblefighter::host

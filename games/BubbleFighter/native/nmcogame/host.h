#pragma once
// Process-wide state of the nmcogame.dll host: session, messenger mode, hook worker.
#include "friends_client.h"
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
/// The friend list answered over the session's [messenger] service, or null when the session has none.
nmco::FriendsClient* friends();
/// Starts the hook worker thread; called from DllMain.
void start_worker();
/// Refuses authentication until a managed launch's required plugins have initialized.
bool await_authentication_ready();
/// Copies the immutable schema-two launch identity only after required plugins are ready; no fallback identity.
bool managed_credentials(std::string& account, std::string& ticket, bool* name_login = nullptr);
/// Latches a host failure and publishes it without credentials.
void fail_host();
/// Loads the forwarding DLL outside the loader lock; false is a host failure.
bool initialize_forward();
/// Copies opaque launch metadata using the version-one size-query ABI; returns zero when absent.
int32_t launch_metadata(const char* capability, char* buffer, uint32_t capacity);
/// Copies the locked session's correlation ID; never exposes a login ticket or account.
int32_t launch_id(char* buffer, uint32_t capacity);
/// Queues one verified bootstrap completion report without performing file I/O on the game thread.
int32_t report_bootstrap_complete();

} // namespace bubblefighter::host

#pragma once
#include "session.h"

namespace fthook {
/// Serves versioned bounded requests over this launch's local named pipe until the launcher disconnects.
/// Run on a dedicated worker outside DllMain; peers are checked against session.launcher_pid.
void serve_control(const Session& session, const std::wstring& folder);
}

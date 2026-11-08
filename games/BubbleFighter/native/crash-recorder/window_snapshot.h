#pragma once
#include <windows.h>

// DestroyWindow also closes normal startup/splash windows; destruction alone does not establish process exit.
inline bool requests_close_snapshot(UINT message, WPARAM wparam) noexcept {
    return message == WM_CLOSE || message == WM_QUIT || (message == WM_ENDSESSION && wparam != FALSE);
}

#include <windows.h>
#include <dbghelp.h>

// An old same-named library rejects modern dump requests even when the caller changes search flags.
extern "C" BOOL WINAPI LegacyMiniDumpWriteDump(HANDLE, DWORD, HANDLE, MINIDUMP_TYPE,
    PMINIDUMP_EXCEPTION_INFORMATION, PMINIDUMP_USER_STREAM_INFORMATION, PMINIDUMP_CALLBACK_INFORMATION) {
    SetLastError(ERROR_INVALID_PARAMETER);
    return FALSE;
}

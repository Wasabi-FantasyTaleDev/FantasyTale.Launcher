#include "host.h"

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        bubblefighter::host::set_module(module);
        // No file or library work here (loader lock); the worker initializes once DllMain returns.
        bubblefighter::host::start_worker();
    }
    return TRUE;
}

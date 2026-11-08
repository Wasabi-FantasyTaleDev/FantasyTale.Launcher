#include <windows.h>

#pragma comment(linker, "/export:igwInitialize=nmcogame.FT_IGW_Initialize_v1")
#pragma comment(linker, "/export:igwGetModule=nmcogame.FT_IGW_GetModule_v1")
#pragma comment(linker, "/export:igwTerminal=nmcogame.FT_IGW_Terminal_v1")

// Export forwarding keeps authentication in the existing host: one session, worker and control channel.
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }

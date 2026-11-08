#include <windows.h>

namespace {
using Method = void(*)();
struct Client { const Method* methods; };
// CN legacy callers pass the object explicitly; shutdown uses one argument rather than three.
int __stdcall accept(Client*, void*, void*) { return 1; }
int __stdcall stop(Client*) { return 0; }
const Method methods[] = { reinterpret_cast<Method>(&accept), reinterpret_cast<Method>(&accept),
    reinterpret_cast<Method>(&accept), reinterpret_cast<Method>(&stop) };
Client client{methods};
}

extern "C" void* __stdcall gp_create(void*, void*) { return &client; }
extern "C" int __stdcall gp_release() { return 0; }
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }

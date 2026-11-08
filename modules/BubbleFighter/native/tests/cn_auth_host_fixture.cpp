#include <windows.h>
#include <array>
#include <cstring>
#pragma comment(linker, "/export:FT_IGW_Initialize_v1=_initialize@8")
#pragma comment(linker, "/export:FT_IGW_GetModule_v1=_get_module@8")
#pragma comment(linker, "/export:FT_IGW_Terminal_v1=_terminal")
#pragma comment(linker, "/export:FixtureLoginCalls=_fixture_login_calls")
namespace {
using Method = void(*)();
using Callback = int (__stdcall*)(int, const void*, int, int);
struct Object { const Method* methods; int marker; };
struct Record { DWORD unknown0; const char* account; const char* ticket; DWORD unknown12; };
int mode{};
bool delivered{};
int login_calls{};
int __stdcall idle(Object* object) { return object->marker == 712 ? 20 : -20; }
int __stdcall set(Object* object, void* value) { return object->marker == 712 && value == reinterpret_cast<void*>(123) ? 21 : -21; }
int __stdcall login(Object* object, Callback callback, int a, int b) {
    ++login_calls;
    if (object->marker != 712 || a != 45 || b != 67) return -30;
    if (mode == 3) return 0;
    if (mode == 1 || delivered) { callback(7, nullptr, a, b); return static_cast<int>(E_FAIL); }
    delivered = true;
    std::array<char,76> ticket{};
    std::array<char,8> account{"fixture"};
    strcpy_s(ticket.data(), ticket.size(), mode == 4 ? "bff-ticket:bad"
        : "bff-ticket:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    const Record record{0,account.data(),ticket.data(),0};
    callback(0, &record, a, b);
    if (mode == 2) callback(0, &record, a, b);
    SecureZeroMemory(ticket.data(),ticket.size());
    SecureZeroMemory(account.data(),account.size());
    return 0;
}
const auto methods = [] {
    std::array<Method,20> result{};
    result[2] = reinterpret_cast<Method>(&idle); result[3] = reinterpret_cast<Method>(&login);
    result[10] = result[14] = result[16] = reinterpret_cast<Method>(&set);
    result[15] = reinterpret_cast<Method>(&idle); return result;
}();
Object object{methods.data(),712};
}
extern "C" int __stdcall initialize(unsigned version, const void* options) {
    if (version != 512 || !options) return static_cast<int>(E_INVALIDARG);
    mode = *static_cast<const int*>(options); delivered = false; login_calls = 0;
    return mode == 9 ? static_cast<int>(E_FAIL) : 0;
}
extern "C" int __stdcall get_module(const void* id, void** output) {
    if (!output) return static_cast<int>(E_POINTER);
    const auto first = *static_cast<const BYTE*>(id);
    if (first == 0x84 || first == 0x6c) { *output = &object; return 0; }
    *output = const_cast<void*>(id); return 42;
}
extern "C" int __cdecl terminal() { return 17; }
extern "C" int __cdecl fixture_login_calls() { return login_calls; }
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }

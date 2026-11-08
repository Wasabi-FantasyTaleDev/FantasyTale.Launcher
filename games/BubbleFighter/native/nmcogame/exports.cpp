// Exported messenger entry points. Each export is a naked stub that asks resolve_export()
// for the target (the original library's function in forward mode, the stand-in otherwise)
// and jumps to it with the caller's stack untouched, so argument lists the stand-in does not
// know are forwarded exactly. All NMCO exports are __cdecl: the caller cleans the stack.
#include "host.h"
#include "nm_codes.h"
#include "diagnostic_log.h"

#include <fthook/log.h>

#include <cstdio>
#include <mutex>

using namespace bubblefighter;
using namespace bubblefighter::host;

namespace {
HMODULE g_original = nullptr;
std::once_flag g_original_once;
void* g_targets[ExportCount]{};
std::once_flag g_target_once[ExportCount];

extern "C" BOOL __cdecl standin_SetLocale(UINT32 locale) {
    fthook::runtime_log().linef("SetLocale %u", locale);
    return TRUE;
}

extern "C" BOOL __cdecl standin_SetLocaleAndRegion(UINT32 locale, UINT32 region) {
    fthook::runtime_log().linef("SetLocaleAndRegion %u %u", locale, region);
    return TRUE;
}

extern "C" BOOL __cdecl standin_SetPatchOption(BOOL option) {
    fthook::runtime_log().linef("SetPatchOption %d", option);
    return TRUE;
}

extern "C" void __cdecl standin_MemoryFree(void* memory) {
    if (memory != nullptr) HeapFree(GetProcessHeap(), 0, memory);
}

BOOL answer_call(const char* name, UINT32 code, BYTE* request, UINT32 flags, BYTE** reply, UINT32* reply_size) {
    std::vector<uint8_t> object, body;
    std::string note;
    const bool readable = nmco::open_request(request, object);
    nmco::FriendsClient* list = friends();
    const bool handled = readable && (list && nmco::FriendsClient::owns(code) ? list->handle(code, object, body, note)
        : nmco::answer(code, object, identity(), body, note));
    const char* function = nmco::function_name(code);
    diagnostic::record(diagnostic::NmcoCall, code, static_cast<unsigned>(object.size()),
        readable ? (handled ? diagnostic::Decoded : diagnostic::Unhandled) : diagnostic::Unreadable);
    fthook::runtime_log().linef("%s code=0x%04X (%s) flags=%u request=%u %s %s", name, code, function ? function : "unknown id", flags,
        static_cast<unsigned>(object.size()), handled ? "ok" : (readable ? "unhandled" : "unreadable"), note.c_str());
    if (!handled || reply == nullptr || reply_size == nullptr) return FALSE;
    const std::vector<uint8_t> envelope = nmco::build_reply(body);
    // The caller passes its own buffer and capacity (kr-812 sub_91DE10: 0x100000); a larger
    // reply uses our heap, which the caller releases through NMCO_MemoryFree.
    if (*reply == nullptr || *reply_size < envelope.size()) {
        auto* owned = static_cast<BYTE*>(HeapAlloc(GetProcessHeap(), 0, envelope.size()));
        if (owned == nullptr) return FALSE;
        *reply = owned;
    }
    std::memcpy(*reply, envelope.data(), envelope.size());
    *reply_size = static_cast<UINT32>(envelope.size());
    return TRUE;
}

extern "C" BOOL __cdecl standin_CallNMFunc2(UINT32 code, BYTE* request, UINT32 flags, BYTE** reply, UINT32* reply_size) {
    return answer_call("CallNMFunc2", code, request, flags, reply, reply_size);
}

// The older interface is the same call without flags: cn-324 sub_7BF130 passes the code, the
// same request envelope, its own reply buffer and a 0x100000 capacity.
extern "C" BOOL __cdecl standin_CallNMFunc(UINT32 code, BYTE* request, BYTE** reply, UINT32* reply_size) {
    return answer_call("CallNMFunc", code, request, 0, reply, reply_size);
}

// Option setters whose argument lists are not needed: accept and succeed.
extern "C" BOOL __cdecl standin_Accept() { return TRUE; }

void* standin_target(int index) {
    switch (index) {
    case CallNMFunc: return reinterpret_cast<void*>(&standin_CallNMFunc);
    case CallNMFunc2: return reinterpret_cast<void*>(&standin_CallNMFunc2);
    case MemoryFree: return reinterpret_cast<void*>(&standin_MemoryFree);
    case SetLocale: return reinterpret_cast<void*>(&standin_SetLocale);
    case SetLocaleAndRegion: return reinterpret_cast<void*>(&standin_SetLocaleAndRegion);
    case SetPatchOption: return reinterpret_cast<void*>(&standin_SetPatchOption);
    default: return reinterpret_cast<void*>(&standin_Accept);
    }
}

extern "C" BOOL __cdecl refused_call() { return FALSE; }

void resolve(int index) {
    ensure_initialized();
    void* target = nullptr;
    if (mode() == Mode::Forward) {
        initialize_forward();
        if (g_original) target = reinterpret_cast<void*>(GetProcAddress(g_original, kExportNames[index]));
        if (!target) { fthook::runtime_log().linef("forward: %s missing in the original; call refused", kExportNames[index]); fail_host(); }
    }
    g_targets[index] = target ? target : mode() == Mode::StandIn ? standin_target(index) : reinterpret_cast<void*>(&refused_call);
}
}

bool bubblefighter::host::initialize_forward() {
    std::call_once(g_original_once, [] {
            wchar_t path[MAX_PATH]{};
            HMODULE self = nullptr;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&standin_target), &self);
            const DWORD n = GetModuleFileNameW(self, path, MAX_PATH);
            std::wstring file(path, n);
            file = file.substr(0, file.find_last_of(L"\\/") + 1) + L"nmcogame.nexon.dll";
            g_original = LoadLibraryExW(file.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!g_original) fthook::runtime_log().linef("forward: cannot load nmcogame.nexon.dll (error %lu)", GetLastError());
    });
    return g_original != nullptr;
}

extern "C" void* __cdecl resolve_export(int index) {
    if ((index == CallNMFunc || index == CallNMFunc2) && !await_authentication_ready()) return reinterpret_cast<void*>(&refused_call);
    std::call_once(g_target_once[index], resolve, index);
    return g_targets[index];
}

#define FT_EXPORT(name, index)                                  \
    extern "C" __declspec(naked) void ft_##name() {             \
        __asm push index                                        \
        __asm call resolve_export                               \
        __asm add esp, 4                                        \
        __asm jmp eax                                           \
    }

FT_EXPORT(NMCO_CallNMFunc, 0)
FT_EXPORT(NMCO_CallNMFunc2, 1)
FT_EXPORT(NMCO_MemoryFree, 2)
FT_EXPORT(NMCO_SetLocale, 3)
FT_EXPORT(NMCO_SetLocaleAndRegion, 4)
FT_EXPORT(NMCO_SetPatchOption, 5)
FT_EXPORT(NMCO_SetUseFriendModuleOption, 6)
FT_EXPORT(NMCO_SetUseNGMOption, 7)
FT_EXPORT(NMCO_SetVersionFileUrl, 8)
FT_EXPORT(NMCO_SetVersionFileUrlA, 9)
FT_EXPORT(NMCO_SetVersionFileUrlW, 10)

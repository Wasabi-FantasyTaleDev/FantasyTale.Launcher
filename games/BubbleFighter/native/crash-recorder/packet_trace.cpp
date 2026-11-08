#include <winsock2.h>
#include "packet_trace.h"
#include <windows.h>
#include <algorithm>
#include <cstring>

namespace {
using Receive = int(WSAAPI*)(SOCKET, char*, int, int);
using ReceiveFrom = int(WSAAPI*)(SOCKET, char*, int, int, sockaddr*, int*);
Receive receive = nullptr;
ReceiveFrom receive_from = nullptr;
HANDLE file = INVALID_HANDLE_VALUE;
SRWLOCK lock = SRWLOCK_INIT;
uint64_t written = 0, maximum = 0;

void record(SOCKET socket, const char* buffer, int length, uint32_t flags) {
    if (length <= 0) return;
    const DWORD error = GetLastError();
    AcquireSRWLockExclusive(&lock);
    const uint32_t captured = static_cast<uint32_t>(std::min(length, 65536));
    // Fixed little-endian header: tick, socket, wire length, saved length, flags.
    uint64_t tick = GetTickCount64();
    uint32_t header[6] = {static_cast<uint32_t>(tick), static_cast<uint32_t>(tick >> 32),
        static_cast<uint32_t>(socket), static_cast<uint32_t>(length), captured, flags};
    if (file != INVALID_HANDLE_VALUE && sizeof(header) + captured <= maximum - written) {
        DWORD count = 0;
        const bool ok = WriteFile(file, header, sizeof(header), &count, nullptr) && count == sizeof(header)
            && WriteFile(file, buffer, captured, &count, nullptr) && count == captured && FlushFileBuffers(file);
        written += sizeof(header) + captured;
        if (!ok) { CloseHandle(file); file = INVALID_HANDLE_VALUE; }
    }
    ReleaseSRWLockExclusive(&lock);
    SetLastError(error);
}
int WSAAPI traced_recv(SOCKET socket, char* buffer, int length, int flags) {
    const int result = receive(socket, buffer, length, flags);
    record(socket, buffer, result, static_cast<uint32_t>(flags)); return result;
}
int WSAAPI traced_recvfrom(SOCKET socket, char* buffer, int length, int flags, sockaddr* address, int* size) {
    const int result = receive_from(socket, buffer, length, flags, address, size);
    record(socket, buffer, result, static_cast<uint32_t>(flags)); return result;
}
}

bool start_packet_trace(const fthook_host* host, const std::wstring& path, unsigned maximum_mib) {
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 nt{};
    if (host->read(base, &dos, sizeof(dos)) != FTHOOK_OK || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0
        || host->read(base + static_cast<uint32_t>(dos.e_lfanew), &nt, sizeof(nt)) != FTHOOK_OK || nt.Signature != IMAGE_NT_SIGNATURE) return false;
    const auto imports = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    unsigned hooks = 0;
    for (unsigned i = 0; imports.VirtualAddress && i < 256 && i < imports.Size / sizeof(IMAGE_IMPORT_DESCRIPTOR); ++i) {
        IMAGE_IMPORT_DESCRIPTOR entry{};
        if (host->read(base + imports.VirtualAddress + i * sizeof(entry), &entry, sizeof(entry)) != FTHOOK_OK) return false;
        if (!entry.Name) break;
        char library[32]{};
        if (host->read(base + entry.Name, library, sizeof(library) - 1) != FTHOOK_OK) return false;
        if (_stricmp(library, "ws2_32.dll") && _stricmp(library, "wsock32.dll")) continue;
        const auto socket_library = GetModuleHandleA(library);
        if (!entry.OriginalFirstThunk) continue;
        for (unsigned j = 0; j < 4096; ++j) {
            uint32_t name = 0, original = 0;
            if (host->read(base + entry.OriginalFirstThunk + j * 4, &name, 4) != FTHOOK_OK || !name) break;
            const auto slot = base + entry.FirstThunk + j * 4;
            if (host->read(slot, &original, 4) != FTHOOK_OK || !original) return false;
            char symbol[32]{};
            if (name & IMAGE_ORDINAL_FLAG32) {
                // Resolve ordinal imports by the loaded system export address, not by assumed ordinal numbers.
                if (original == reinterpret_cast<uint32_t>(GetProcAddress(socket_library, "recv"))) std::memcpy(symbol, "recv", 5);
                else if (original == reinterpret_cast<uint32_t>(GetProcAddress(socket_library, "recvfrom"))) std::memcpy(symbol, "recvfrom", 9);
                else continue;
            }
            else if (host->read(base + name + 2, symbol, sizeof(symbol) - 1) != FTHOOK_OK) return false;
            if (std::strcmp(symbol, "recv") && std::strcmp(symbol, "recvfrom")) continue;
            uint32_t replacement = 0;
            if (!std::strcmp(symbol, "recv")) {
                if (receive && receive != reinterpret_cast<Receive>(original)) return false;
                receive = reinterpret_cast<Receive>(original); replacement = reinterpret_cast<uint32_t>(&traced_recv);
            } else {
                if (receive_from && receive_from != reinterpret_cast<ReceiveFrom>(original)) return false;
                receive_from = reinterpret_cast<ReceiveFrom>(original); replacement = reinterpret_cast<uint32_t>(&traced_recvfrom);
            }
            if (host->stage_import(host->context, slot, original, replacement) != FTHOOK_OK) return false;
            ++hooks;
        }
    }
    host->log(host->context, hooks ? "receive tracing: synchronous main-image imports; no outbound traffic is recorded"
        : "receive tracing unavailable: no supported synchronous receive imports; asynchronous and dynamic imports are not intercepted");
    if (!hooks) return true;
    maximum = static_cast<uint64_t>(maximum_mib) * 1024 * 1024;
    file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    const char header[8] = {'F','T','N','E','T',1,0,0}; DWORD count = 0;
    written = sizeof(header);
    return WriteFile(file, header, sizeof(header), &count, nullptr) && count == sizeof(header) && FlushFileBuffers(file);
}

void stop_packet_trace() {
    AcquireSRWLockExclusive(&lock);
    if (file != INVALID_HANDLE_VALUE) { CloseHandle(file); file = INVALID_HANDLE_VALUE; }
    ReleaseSRWLockExclusive(&lock);
}

#include "fthook/memory.h"
#include "fthook/image.h"

#include <windows.h>

#include <cstring>
#include <algorithm>

namespace fthook {
namespace {
bool copy_guarded(void* destination, const void* source, size_t size) noexcept {
    __try {
        std::memcpy(destination, source, size);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool is_executable(DWORD protect) noexcept {
    const DWORD access = protect & 0xFF;
    return access == PAGE_EXECUTE_READ || access == PAGE_EXECUTE_READWRITE || access == PAGE_EXECUTE_WRITECOPY;
}
}

bool matches_at(uintptr_t address, const Pattern& expected) noexcept {
    if (!address || expected.size() == 0 || expected.size() > kMaxPatchBytes) return false;
    uint8_t current[kMaxPatchBytes];
    return read_memory(address, current, expected.size()) && expected.matches(current);
}

bool is_import_slot(uintptr_t address) noexcept {
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 nt{};
    if (address % 4 || !read_memory(base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0
        || !read_memory(base + static_cast<uint32_t>(dos.e_lfanew), &nt, sizeof(nt)) || nt.Signature != IMAGE_NT_SIGNATURE) return false;
    const auto iat = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT];
    if (iat.Size < 4 || !iat.VirtualAddress || iat.VirtualAddress > nt.OptionalHeader.SizeOfImage
        || iat.Size > nt.OptionalHeader.SizeOfImage - iat.VirtualAddress) return false;
    MEMORY_BASIC_INFORMATION page{};
    return address >= base + iat.VirtualAddress && address - (base + iat.VirtualAddress) <= iat.Size - 4
        && VirtualQuery(reinterpret_cast<const void*>(address), &page, sizeof(page)) && page.State == MEM_COMMIT
        && page.Type == MEM_IMAGE && reinterpret_cast<uintptr_t>(page.AllocationBase) == base && !(page.Protect & (PAGE_GUARD | PAGE_NOACCESS));
}

WriteResult write_import(uintptr_t address, const Pattern& expected, const std::vector<uint8_t>& replacement) {
    if (!is_import_slot(address) || expected.size() != 4 || expected.mask.size() != 4 || replacement.size() != 4
        || std::any_of(expected.mask.begin(), expected.mask.end(), [](uint8_t mask) { return mask != 255; })) return WriteResult::Failed;
    LONG original = 0, changed = 0;
    std::memcpy(&original, expected.bytes.data(), 4); std::memcpy(&changed, replacement.data(), 4);
    auto* slot = reinterpret_cast<volatile LONG*>(address);
    DWORD previous = 0, ignored = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), 4, PAGE_READWRITE, &previous)) return WriteResult::Failed;
    const auto found = InterlockedCompareExchange(slot, changed, original);
    const bool restored = VirtualProtect(reinterpret_cast<void*>(address), 4, previous, &ignored) != FALSE;
    if (!restored) {
        // Only undo our exchange, never a replacement that predated this attempt.
        const auto undone = found == original ? InterlockedCompareExchange(slot, original, changed) : found;
        const bool bytes_restored = found != original || undone == changed || undone == original;
        const bool protection_restored = VirtualProtect(reinterpret_cast<void*>(address), 4, previous, &ignored) != FALSE;
        return bytes_restored && protection_restored ? WriteResult::Failed : WriteResult::RollbackFailed;
    }
    return found == original ? WriteResult::Installed : found == changed ? WriteResult::AlreadyPresent : WriteResult::Mismatch;
}

WriteResult write_code(uintptr_t address, const Pattern& expected, const std::vector<uint8_t>& replacement) {
    const size_t size = replacement.size();
    if (!address || size == 0 || size != expected.size() || size > kMaxPatchBytes || address > UINTPTR_MAX - size)
        return WriteResult::Failed;
    SYSTEM_INFO system{};
    GetSystemInfo(&system);
    if (address / system.dwPageSize != (address + size - 1) / system.dwPageSize) return WriteResult::Failed;
    MEMORY_BASIC_INFORMATION page{};
    if (!VirtualQuery(reinterpret_cast<const void*>(address), &page, sizeof(page)) || page.State != MEM_COMMIT
        || (page.Protect & (PAGE_GUARD | PAGE_NOACCESS)) || !is_executable(page.Protect))
        return WriteResult::Failed;

    uint8_t original[kMaxPatchBytes];
    if (!read_memory(address, original, size)) return WriteResult::Failed;
    if (std::memcmp(original, replacement.data(), size) == 0) return WriteResult::AlreadyPresent;
    if (!expected.matches(original)) return WriteResult::Mismatch;

    auto* destination = reinterpret_cast<void*>(address);
    DWORD previous = 0, ignored = 0;
    if (!VirtualProtect(destination, size, PAGE_EXECUTE_READWRITE, &previous)) return WriteResult::Failed;
    const bool copied = copy_guarded(destination, replacement.data(), size);
    const bool flushed = copied && FlushInstructionCache(GetCurrentProcess(), destination, size) != FALSE;
    uint8_t readback[kMaxPatchBytes];
    const bool verified = copied && read_memory(address, readback, size) && std::memcmp(readback, replacement.data(), size) == 0;
    const bool restored = VirtualProtect(destination, size, previous, &ignored) != FALSE;
    if (flushed && verified && restored) return WriteResult::Installed;

    bool bytes_restored = false, cache_restored = false;
    if (VirtualProtect(destination, size, PAGE_EXECUTE_READWRITE, &ignored)) {
        const bool undone = copy_guarded(destination, original, size);
        cache_restored = FlushInstructionCache(GetCurrentProcess(), destination, size) != FALSE;
        bytes_restored = undone && read_memory(address, readback, size) && std::memcmp(readback, original, size) == 0;
    }
    const bool protection_restored = VirtualProtect(destination, size, previous, &ignored) != FALSE;
    return bytes_restored && cache_restored && protection_restored ? WriteResult::Failed : WriteResult::RollbackFailed;
}

const char* to_string(WriteResult result) noexcept {
    switch (result) {
    case WriteResult::Installed: return "installed";
    case WriteResult::AlreadyPresent: return "already-present";
    case WriteResult::Mismatch: return "signature-mismatch";
    case WriteResult::Failed: return "failed";
    case WriteResult::RollbackFailed: return "rollback-failed";
    }
    return "failed";
}

} // namespace fthook

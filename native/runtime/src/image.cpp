#include "fthook/image.h"

#include <windows.h>

#include <cstring>

namespace fthook {
namespace {
bool is_readable_code(const MEMORY_BASIC_INFORMATION& page) noexcept {
    if (page.State != MEM_COMMIT || (page.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
    const DWORD access = page.Protect & 0xFF;
    return access == PAGE_EXECUTE_READ || access == PAGE_EXECUTE_READWRITE || access == PAGE_EXECUTE_WRITECOPY;
}

// Plain function without C++ objects so structured exception handling is allowed.
size_t scan_range(const uint8_t* start, size_t size, const uint8_t* bytes, const uint8_t* mask, size_t length,
    size_t limit, size_t found, uintptr_t* first) noexcept {
    __try {
        for (size_t i = 0; i + length <= size && found < limit; ++i) {
            if ((start[i] & mask[0]) != bytes[0]) continue;
            size_t k = 1;
            while (k < length && (start[i + k] & mask[k]) == bytes[k]) ++k;
            if (k != length) continue;
            if (found == 0) *first = reinterpret_cast<uintptr_t>(start + i);
            ++found;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
    return found;
}
}

bool read_memory(uintptr_t address, void* out, size_t size) noexcept {
    __try {
        std::memcpy(out, reinterpret_cast<const void*>(address), size);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

std::optional<ImageInfo> describe_image(uintptr_t base) {
    IMAGE_DOS_HEADER dos{};
    if (!base || !read_memory(base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE) return std::nullopt;
    IMAGE_NT_HEADERS32 nt{};
    if (!read_memory(base + static_cast<uintptr_t>(dos.e_lfanew), &nt, sizeof(nt)) || nt.Signature != IMAGE_NT_SIGNATURE
        || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386)
        return std::nullopt;
    ImageInfo info;
    info.base = base;
    info.pe_timestamp = nt.FileHeader.TimeDateStamp;
    info.image_size = nt.OptionalHeader.SizeOfImage;
    // Page protection, not section flags: packers mark sections arbitrarily and change
    // protection when they finish unpacking.
    uintptr_t cursor = base;
    const uintptr_t end = base + info.image_size;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION page{};
        if (!VirtualQuery(reinterpret_cast<const void*>(cursor), &page, sizeof(page)) || page.RegionSize == 0) break;
        const uintptr_t region_start = reinterpret_cast<uintptr_t>(page.BaseAddress);
        uintptr_t region_end = region_start + page.RegionSize;
        if (region_end > end) region_end = end;
        if (is_readable_code(page)) {
            const uintptr_t start = region_start < base ? base : region_start;
            if (!info.code.empty() && info.code.back().address + info.code.back().size == start)
                info.code.back().size += region_end - start;
            else
                info.code.push_back({start, region_end - start});
        }
        cursor = region_end;
    }
    return info;
}

std::optional<ImageInfo> describe_main_image() {
    return describe_image(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));
}

ScanResult scan(const std::vector<Region>& regions, const Pattern& pattern, size_t limit) {
    ScanResult result;
    if (pattern.size() == 0 || limit == 0) return result;
    for (const Region& region : regions) {
        if (result.count >= limit) break;
        uintptr_t first = 0;
        const size_t before = result.count;
        result.count = scan_range(reinterpret_cast<const uint8_t*>(region.address), region.size, pattern.bytes.data(),
            pattern.mask.data(), pattern.size(), limit, result.count, &first);
        if (before == 0 && result.count > 0) result.first = first;
    }
    return result;
}

} // namespace fthook

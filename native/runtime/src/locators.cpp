// Locators built into the runtime. Each finds a site whose bytes alone are not distinctive
// enough for a pattern, using anchors that survive recompilation across client builds.
#include "fthook/engine.h"

#include <windows.h>

#include <cstring>
#include <string_view>

namespace fthook {
namespace {

// ---- fthook.config-md5-verdict ------------------------------------------------------------
// The config-table verifiers report "MismatchFighterConfig", call a shared MD5 comparer and
// branch on its verdict (`call rel32; test al, al; jne rel32`; kr-1256 0xC954BC, kr-710
// 0xC94AB0). Inside the comparer the four-dword comparison ends in a byte-identical verdict
// block whose `xor al, al` is the mismatch exit (kr-1256 sub_956BC0, kr-710 sub_9564A0).
constexpr uint8_t kMd5InitLow[] = {0x01, 0x23, 0x45, 0x67};
constexpr uint8_t kVerdictShape[] = {
    0x3B, 0x02, 0x75, 0x0F, 0x83, 0xC1, 0x04, 0x83, 0xC2, 0x04, 0x83, 0xEE, 0x04,
    0x73, 0xEF, 0xB0, 0x01, 0xEB, 0x02, 0x32, 0xC0, 0x88, 0x45, 0xEF, 0x84, 0xC0};
constexpr size_t kVerdictOffset = 19;

bool find_wide(const uint8_t* image, size_t size, std::wstring_view text, size_t& out) {
    const size_t needle = text.size() * 2;
    for (size_t i = 0; i + needle <= size; i += 2)
        if (image[i] == static_cast<uint8_t>(text[0]) && std::memcmp(image + i, text.data(), needle) == 0) { out = i; return true; }
    return false;
}

// The closest `push ebp; mov ebp, esp` at or before `ref` whose predecessor ends a function.
bool function_start(const uint8_t* image, size_t ref, size_t& out) {
    const size_t floor = ref > 0x800 ? ref - 0x800 : 3;
    for (size_t p = ref; p > floor; --p) {
        if (image[p] != 0x55 || image[p + 1] != 0x8B || image[p + 2] != 0xEC) continue;
        if (image[p - 1] == 0xCC || image[p - 1] == 0xC3 || image[p - 3] == 0xC2) { out = p; return true; }
    }
    return false;
}

bool contains(const uint8_t* image, size_t size, size_t at, size_t window, const uint8_t* needle, size_t length) {
    if (at >= size) return false;
    if (window > size - at) window = size - at;
    for (size_t i = 0; i + length <= window; ++i)
        if (std::memcmp(image + at + i, needle, length) == 0) return true;
    return false;
}

// Returns 0 on success, otherwise a reason code (see reason_text).
int locate_md5_verdict(const uint8_t* image, uintptr_t base, size_t size, uintptr_t& out) {
    size_t report = 0;
    if (!find_wide(image, size, L"MismatchFighterConfig", report)) return 1;
    const uintptr_t report_va = base + report;
    size_t comparer = 0;
    for (size_t i = 0; i + 5 <= size; ++i) {
        if (image[i] != 0x68 || std::memcmp(image + i + 1, &report_va, 4) != 0) continue;
        size_t start = 0;
        if (!function_start(image, i, start)) continue;
        const size_t window = size - start > 0x800 ? 0x800 : size - start;
        for (size_t k = 0; k + 9 <= window; ++k) {
            const uint8_t* at = image + start + k;
            if (at[0] != 0xE8 || at[5] != 0x84 || at[6] != 0xC0 || at[7] != 0x0F || at[8] != 0x85) continue;
            int32_t rel = 0;
            std::memcpy(&rel, at + 1, 4);
            const int64_t callee = static_cast<int64_t>(start + k + 5) + rel;
            if (callee < 0 || static_cast<size_t>(callee) >= size
                || !contains(image, size, static_cast<size_t>(callee), 0x400, kMd5InitLow, sizeof(kMd5InitLow)))
                continue;
            if (comparer && comparer != static_cast<size_t>(callee)) return 2;
            comparer = static_cast<size_t>(callee);
        }
    }
    if (!comparer) return 3;
    const size_t window = size - comparer > 0x800 ? 0x800 : size - comparer;
    size_t verdict = 0;
    for (size_t k = 0; k + sizeof(kVerdictShape) <= window; ++k) {
        if (std::memcmp(image + comparer + k, kVerdictShape, sizeof(kVerdictShape)) != 0) continue;
        if (verdict) return 4;
        verdict = comparer + k + kVerdictOffset;
    }
    if (!verdict) return 4;
    out = base + verdict;
    return 0;
}

int guarded_md5_verdict(const uint8_t* image, uintptr_t base, size_t size, uintptr_t* out) {
    __try {
        return locate_md5_verdict(image, base, size, *out);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 5;
    }
}

const char* reason_text(int code) {
    switch (code) {
    case 1: return "anchor string MismatchFighterConfig not found";
    case 2: return "verifiers call more than one MD5 comparer";
    case 3: return "no verifier calls an MD5 comparer";
    case 4: return "verdict block missing or not unique";
    case 6: return "image is not fully mapped yet";
    default: return "image read failed";
    }
}

// The whole image must be committed and free of guard pages before it is read linearly,
// so the scan never trips a packer's guard pages.
bool fully_readable(const ImageInfo& image) {
    uintptr_t cursor = image.base;
    const uintptr_t end = image.base + image.image_size;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION page{};
        if (!VirtualQuery(reinterpret_cast<const void*>(cursor), &page, sizeof(page)) || page.RegionSize == 0) return false;
        if (page.State != MEM_COMMIT || (page.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        cursor = reinterpret_cast<uintptr_t>(page.BaseAddress) + page.RegionSize;
    }
    return true;
}
}

void register_builtin_locators(LocatorRegistry& registry) {
    registry.add("fthook.config-md5-verdict", [](const ImageInfo& image, std::string& error) -> std::optional<uintptr_t> {
        if (!fully_readable(image)) return error = reason_text(6), std::nullopt;
        uintptr_t site = 0;
        const int code = guarded_md5_verdict(reinterpret_cast<const uint8_t*>(image.base), image.base, image.image_size, &site);
        if (code != 0) return error = reason_text(code), std::nullopt;
        return site;
    });
}

} // namespace fthook

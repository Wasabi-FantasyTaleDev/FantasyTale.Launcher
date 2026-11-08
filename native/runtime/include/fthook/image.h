#pragma once
// The loaded main module as the patch engine sees it.
#include "pattern.h"

#include <cstdint>
#include <optional>
#include <vector>

namespace fthook {

/// A readable executable address range.
struct Region {
    uintptr_t address = 0;
    size_t size = 0;
};

/// Identity and code ranges of a loaded image.
struct ImageInfo {
    uintptr_t base = 0;
    uint32_t pe_timestamp = 0;      ///< IMAGE_FILE_HEADER::TimeDateStamp.
    uint32_t image_size = 0;        ///< IMAGE_OPTIONAL_HEADER::SizeOfImage.
    std::vector<Region> code;       ///< Committed, readable, executable pages inside the image.
};

/// Reads the PE headers at `base` and collects the image's executable pages by querying
/// page protection, not section flags, because packers rewrite section headers.
/// Returns nullopt when the headers are not a valid 32-bit PE image.
std::optional<ImageInfo> describe_image(uintptr_t base);

/// The process's main executable.
std::optional<ImageInfo> describe_main_image();

/// Outcome of a pattern search.
struct ScanResult {
    size_t count = 0;         ///< Matches found, capped at the search's limit.
    uintptr_t first = 0;      ///< Lowest matching address, 0 when count is 0.
};

/// Searches `regions` for `pattern`, stopping after `limit` matches. Pages that become
/// unreadable during the search are skipped instead of faulting.
ScanResult scan(const std::vector<Region>& regions, const Pattern& pattern, size_t limit);

/// Reads `size` bytes at `address` into `out` without faulting. Returns false when any
/// byte is unreadable.
bool read_memory(uintptr_t address, void* out, size_t size) noexcept;

} // namespace fthook

#pragma once
// Verified code writes.
#include "pattern.h"

#include <cstdint>
#include <vector>

namespace fthook {

/// True only for an aligned four-byte import address slot in the current executable's PE IAT directory.
bool is_import_slot(uintptr_t address) noexcept;

/// Largest single write the runtime performs.
inline constexpr size_t kMaxPatchBytes = 64;

/// Result of a single verified write.
enum class WriteResult {
    Installed,          ///< The bytes were written and read back.
    AlreadyPresent,     ///< The site already held the replacement.
    Mismatch,           ///< The site did not match the expected bytes; nothing was written.
    Failed,             ///< No write occurred, or original bytes and protection were restored.
    RollbackFailed,     ///< Original bytes, cache coherence or protection could not be restored.
};

/// Atomically exchanges a verified IAT pointer, preserving page protection and rejecting changed originals.
/// Incomplete byte/protection recovery returns RollbackFailed; preexisting replacements are never undone.
WriteResult write_import(uintptr_t address, const Pattern& expected, const std::vector<uint8_t>& replacement);

/// True when the bytes at `address` match `expected`. Never faults.
bool matches_at(uintptr_t address, const Pattern& expected) noexcept;

/// Writes `replacement` at `address` after checking `expected`. The range must lie within
/// one committed executable page. The write is read back, the instruction cache is
/// flushed and the original protection restored; on any failure the original bytes are
/// put back and verified. Incomplete recovery returns RollbackFailed.
WriteResult write_code(uintptr_t address, const Pattern& expected, const std::vector<uint8_t>& replacement);

/// Stable lower-case name of a result, used in log lines.
const char* to_string(WriteResult result) noexcept;

} // namespace fthook

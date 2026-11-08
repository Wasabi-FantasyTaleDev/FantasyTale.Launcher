#pragma once
#include <windows.h>
#include <cstdint>
#include <filesystem>

namespace cn_native {
// A native provider request can be new, redundant after native Entry, or outside the verified continuation.
enum class Continuation { None, Deliver, AlreadySubmitted };
// Called outside the loader lock. Only supported, fingerprinted images may receive native data updates.
bool prepare(uintptr_t image, const std::filesystem::path& component_folder);
// Only a matched passport profile needs the provider callback wrapper; optional captions do not.
bool adapts_passport() noexcept;
// Copies an already-authorized provider ticket through the selected client's own string implementation.
bool seed_passport(const char* marked_ticket);
// Classifies and consumes one verified provider continuation for a connected, selected replacement login.
Continuation begin_connection_resume(const char* marked_ticket) noexcept;
// Stops the optional UI timer and releases provider state without changing executable memory.
bool shutdown();
}

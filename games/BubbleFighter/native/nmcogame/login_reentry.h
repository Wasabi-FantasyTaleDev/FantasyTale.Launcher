#pragma once
#include <windows.h>

#include <array>
#include <atomic>
#include <cstdint>
#include <optional>

namespace bubblefighter::login {

/// Audited native login state; it contains no account or ticket data.
struct RedirectLoginSnapshot {
    uintptr_t connection{};
    bool selected{}, connected{}, fresh_identity{};
};

/// Reads only the original CN2012 callback and connection layout; other images cannot authorize reentry.
template<class Reader>
std::optional<RedirectLoginSnapshot> inspect_redirect_login(uintptr_t base, uintptr_t callback, uintptr_t caller, Reader read) {
    if (base != 0x400000 || callback != base + 0x1852D0 || caller != base + 0x18464A) return std::nullopt;
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS32 pe{};
    if (!read(base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE
        || dos.e_lfanew < 0 || dos.e_lfanew > 0x100000
        || !read(base + static_cast<uintptr_t>(dos.e_lfanew), &pe, sizeof(pe))
        || pe.Signature != IMAGE_NT_SIGNATURE || pe.FileHeader.Machine != IMAGE_FILE_MACHINE_I386
        || pe.FileHeader.TimeDateStamp != 0x505DD60C || pe.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC
        || pe.OptionalHeader.ImageBase != base
        || (pe.OptionalHeader.SizeOfImage != 0xA28000 && pe.OptionalHeader.SizeOfImage != 0xA2B000)) return std::nullopt;
    // cn-324 sub_5852D0 consumes the provider record; cn-324 sub_584580 requests it again on a recreated LoginStage.
    constexpr std::array<BYTE, 16> expected = {0x64,0xA1,0,0,0,0,0x6A,0xFF,0x68,0xBA,0x65,0xA0,0,0x50,0x64,0x89};
    std::array<BYTE, expected.size()> actual{};
    if (!read(callback, actual.data(), actual.size()) || actual != expected) return std::nullopt;
    // cn-324 sub_584580 calls slot 3 at 584648, then sets the new stage's delivery latch at 58464A.
    constexpr std::array<BYTE, 24> invocation = {
        0x8B,0x10,0x6A,0,0x6A,0,0x68,0xD0,0x52,0x58,0,0x50,0x8B,0x42,0x0C,0xFF,0xD0,
        0xC6,0x86,0x4B,0x01,0,0,0x01};
    std::array<BYTE, invocation.size()> invocation_actual{};
    if (!read(caller - 17, invocation_actual.data(), invocation_actual.size()) || invocation_actual != invocation) return std::nullopt;
    uintptr_t app{}, connection{}, profile{}, stage{}, stage_vtable{};
    BYTE selected{}, connected{}, stage_delivered{};
    uint32_t identity{};
    // cn-324 sub_705980 promotes C0FDB4; cn-324 sub_7001F0 uses profile+12 to distinguish fresh native login.
    if (!read(base + 0x80EC98, &app, sizeof(app)) || !app
        || !read(base + 0x80FDB4, &connection, sizeof(connection)) || !connection
        || !read(base + 0x80EA90, &profile, sizeof(profile)) || !profile
        // cn-324 sub_44B4C0 returns MainApp+60; cn-324 sub_583EF0 installs LoginStage's primary vtable and resets +331.
        || !read(app + 60, &stage, sizeof(stage)) || !stage
        || !read(stage, &stage_vtable, sizeof(stage_vtable)) || stage_vtable != base + 0x6E9ABC
        || !read(stage + 331, &stage_delivered, sizeof(stage_delivered)) || stage_delivered != 0
        || !read(app + 270, &selected, sizeof(selected))
        || !read(connection + 100, &connected, sizeof(connected))
        || !read(profile + 12, &identity, sizeof(identity))) return std::nullopt;
    return RedirectLoginSnapshot{connection, selected == 1, connected == 1, identity == 0};
}

/// Permits one native reentry on a different selected connection after the original callback delivery.
class RedirectLoginDelivery {
public:
    /// Remembers only the first delivery's verified native connection; unknown images retain one-use behavior.
    void remember_initial(const std::optional<RedirectLoginSnapshot>& snapshot) {
        initial_connection_.store(snapshot ? snapshot->connection : 0);
    }

    /// Claims the single redirect delivery without allowing same-connection retries or concurrent replay.
    bool claim_redirect(const std::optional<RedirectLoginSnapshot>& snapshot) {
        const auto initial = initial_connection_.load();
        return initial && snapshot && snapshot->connection && snapshot->connection != initial
            && snapshot->selected && snapshot->connected && snapshot->fresh_identity
            && !redirect_delivered_.exchange(true);
    }

private:
    std::atomic<uintptr_t> initial_connection_{};
    std::atomic<bool> redirect_delivered_{};
};

} // namespace bubblefighter::login

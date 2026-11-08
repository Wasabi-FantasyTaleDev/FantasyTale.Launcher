#pragma once
#include "check.h"
#include <login_reentry.h>

#include <cstring>
#include <map>
#include <thread>
#include <vector>

namespace {
struct LoginReentryFixture {
    static constexpr uintptr_t base = 0x400000, callback = 0x5852D0, caller = 0x58464A;
    static constexpr uintptr_t app = 0x10000000, connection = 0x20000000, profile = 0x30000000;
    static constexpr uintptr_t stage = 0x40000000;
    std::map<uintptr_t, std::vector<BYTE>> memory;

    template<class T> void put(uintptr_t address, const T& value) {
        const auto begin = reinterpret_cast<const BYTE*>(&value);
        memory[address] = std::vector<BYTE>(begin, begin + sizeof(value));
    }

    LoginReentryFixture() {
        IMAGE_DOS_HEADER dos{}; dos.e_magic = IMAGE_DOS_SIGNATURE; dos.e_lfanew = 0x100;
        IMAGE_NT_HEADERS32 pe{};
        pe.Signature = IMAGE_NT_SIGNATURE; pe.FileHeader.Machine = IMAGE_FILE_MACHINE_I386;
        pe.FileHeader.TimeDateStamp = 0x505DD60C;
        pe.OptionalHeader.Magic = IMAGE_NT_OPTIONAL_HDR32_MAGIC;
        pe.OptionalHeader.ImageBase = base; pe.OptionalHeader.SizeOfImage = 0xA2B000;
        put(base, dos); put(base + 0x100, pe);
        put(callback, std::array<BYTE, 16>{0x64,0xA1,0,0,0,0,0x6A,0xFF,0x68,0xBA,0x65,0xA0,0,0x50,0x64,0x89});
        put(caller - 17, std::array<BYTE, 24>{0x8B,0x10,0x6A,0,0x6A,0,0x68,0xD0,0x52,0x58,0,0x50,0x8B,0x42,0x0C,0xFF,0xD0,0xC6,0x86,0x4B,0x01,0,0,0x01});
        put(base + 0x80EC98, app); put(base + 0x80FDB4, connection); put(base + 0x80EA90, profile);
        put(app + 60, stage); put(stage, uintptr_t{base + 0x6E9ABC}); put(stage + 331, BYTE{});
        put(app + 270, BYTE{1}); put(connection + 100, BYTE{1}); put(profile + 12, uint32_t{0});
    }

    auto inspect(uintptr_t image = base, uintptr_t target = callback, uintptr_t origin = caller) {
        return bubblefighter::login::inspect_redirect_login(image, target, origin,
            [&](uintptr_t address, void* output, size_t size) {
                auto found = memory.upper_bound(address);
                if (found == memory.begin()) return false;
                --found;
                const auto offset = address - found->first;
                if (offset > found->second.size() || size > found->second.size() - offset) return false;
                std::memcpy(output, found->second.data() + offset, size);
                return true;
            });
    }
};

void login_reentry_tests() {
    using namespace bubblefighter::login;
    LoginReentryFixture fixture;
    const auto observed = fixture.inspect();
    CHECK(observed && observed->connection == fixture.connection && observed->selected && observed->connected && observed->fresh_identity);
    CHECK(!fixture.inspect(0x500000));
    CHECK(!fixture.inspect(fixture.base, fixture.callback + 1));
    CHECK(!fixture.inspect(fixture.base, fixture.callback, fixture.caller + 1));
    for (const auto address : {fixture.base, fixture.base + 0x100, fixture.callback,
        fixture.base + 0x80EC98, fixture.base + 0x80FDB4, fixture.base + 0x80EA90,
        fixture.app + 270, fixture.connection + 100, fixture.profile + 12, fixture.caller - 17,
        fixture.app + 60, fixture.stage, fixture.stage + 331}) {
        auto incomplete = fixture;
        incomplete.memory.erase(address);
        CHECK(!incomplete.inspect());
    }
    auto damaged = fixture;
    damaged.memory[fixture.callback][0] ^= 1;
    CHECK(!damaged.inspect());
    damaged = fixture; damaged.memory[fixture.caller - 17][0] ^= 1;
    CHECK(!damaged.inspect());
    damaged = fixture; damaged.put(fixture.stage, uintptr_t{});
    CHECK(!damaged.inspect());
    damaged = fixture; damaged.put(fixture.stage + 331, BYTE{1});
    CHECK(!damaged.inspect());
    IMAGE_NT_HEADERS32 pe{};
    std::memcpy(&pe, fixture.memory[fixture.base + 0x100].data(), sizeof(pe));
    for (int fault = 0; fault < 6; ++fault) {
        auto changed = pe;
        if (fault == 0) changed.Signature = 0;
        if (fault == 1) changed.FileHeader.TimeDateStamp = 0;
        if (fault == 2) changed.FileHeader.Machine = IMAGE_FILE_MACHINE_AMD64;
        if (fault == 3) changed.OptionalHeader.Magic = IMAGE_NT_OPTIONAL_HDR64_MAGIC;
        if (fault == 4) changed.OptionalHeader.ImageBase = 0x500000;
        if (fault == 5) changed.OptionalHeader.SizeOfImage = 0xA2C000;
        damaged = fixture; damaged.put(fixture.base + 0x100, changed);
        CHECK(!damaged.inspect());
    }
    pe.OptionalHeader.SizeOfImage = 0xA28000;
    damaged = fixture; damaged.put(fixture.base + 0x100, pe);
    CHECK(damaged.inspect().has_value());
    for (const auto address : {fixture.base + 0x80EC98, fixture.base + 0x80FDB4, fixture.base + 0x80EA90}) {
        damaged = fixture; damaged.put(address, uintptr_t{});
        CHECK(!damaged.inspect());
    }

    RedirectLoginDelivery delivery;
    auto initial = observed;
    initial->selected = false;
    delivery.remember_initial(initial);
    CHECK(!delivery.claim_redirect(observed));
    auto target = *observed;
    target.connection += 0x1000;
    CHECK(!delivery.claim_redirect(std::nullopt));
    for (int fault = 0; fault < 4; ++fault) {
        auto rejected = target;
        if (fault == 0) rejected.connection = 0;
        if (fault == 1) rejected.selected = false;
        if (fault == 2) rejected.connected = false;
        if (fault == 3) rejected.fresh_identity = false;
        CHECK(!delivery.claim_redirect(rejected));
    }
    CHECK(delivery.claim_redirect(target));
    CHECK(!delivery.claim_redirect(target));
    target.connection += 0x1000;
    CHECK(!delivery.claim_redirect(target));
    RedirectLoginDelivery unknown;
    unknown.remember_initial(std::nullopt);
    CHECK(!unknown.claim_redirect(target));

    RedirectLoginDelivery concurrent;
    concurrent.remember_initial(observed);
    std::atomic<unsigned> accepted{};
    std::thread first([&] { if (concurrent.claim_redirect(target)) ++accepted; });
    std::thread second([&] { if (concurrent.claim_redirect(target)) ++accepted; });
    first.join(); second.join();
    CHECK(accepted.load() == 1);
}
} // namespace

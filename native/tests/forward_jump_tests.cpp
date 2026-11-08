#include "../../games/BubbleFighter/native/forward-jump/forward_jump.cpp"
#include "check.h"
#include <array>

namespace {
__declspec(naked) void fake_resolver() {
    __asm {
        lea eax, [esi + 16]
        ret
    }
}
}

int main() {
    auto* memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    CHECK(memory != nullptr);
    if (!memory) return finish("forward_jump_tests");
    memory[31] = 0x43; memory[32] = 0; memory[33] = 0x87;
    CHECK(enable_capability(reinterpret_cast<uintptr_t>(memory + 32)));
    CHECK(memory[31] == 0x43 && memory[32] == 1 && memory[33] == 0x87);
    CHECK(enable_capability(reinterpret_cast<uintptr_t>(memory + 32)));
    memory[32] = 2;
    CHECK(!enable_capability(reinterpret_cast<uintptr_t>(memory + 32)));
    CHECK(memory[32] == 2);
    DWORD previous = 0;
    CHECK(VirtualProtect(memory, 4096, PAGE_READONLY, &previous));
    CHECK(!enable_capability(reinterpret_cast<uintptr_t>(memory + 32)));
    CHECK(VirtualProtect(memory, 4096, PAGE_EXECUTE_READWRITE, &previous));
    CHECK(!enable_capability(reinterpret_cast<uintptr_t>(memory + 32)));
    CHECK(VirtualProtect(memory, 4096, PAGE_READWRITE, &previous));
    CHECK(!enable_capability(0));
    State state;
    state.profile.resolver = reinterpret_cast<uintptr_t>(&fake_resolver);
    state.profile.action = 0;
    uintptr_t block = 0;
    CHECK(resolve_capabilities(state, reinterpret_cast<uintptr_t>(memory), block));
    CHECK(block == reinterpret_cast<uintptr_t>(memory + 16));
    state.profile.capability = 16;
    memory[0] = 101;
    memory[32] = 0;
    CHECK(arm(state, reinterpret_cast<uintptr_t>(memory), false));
    CHECK(memory[32] == 0);
    CHECK(arm(state, reinterpret_cast<uintptr_t>(memory), true));
    CHECK(memory[31] == 0x43 && memory[32] == 1 && memory[33] == 0x87);
    memory[0] = 102;
    CHECK(arm(state, reinterpret_cast<uintptr_t>(memory), true));
    CHECK(memory[32] == 1);
    memory[0] = 104;
    CHECK(arm(state, reinterpret_cast<uintptr_t>(memory), false));
    CHECK(memory[31] == 0x43 && memory[32] == 0 && memory[33] == 0x87);
    VirtualFree(memory, 0, MEM_RELEASE);
    return finish("forward_jump_tests");
}

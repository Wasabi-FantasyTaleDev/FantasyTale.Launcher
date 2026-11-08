#include "../../games/BubbleFighter/native/auto-dash/auto_dash.cpp"
#include "check.h"
#include <array>
#include <cstring>

namespace {
int __fastcall capture_arm(void* timer, void*, int duration) {
    *static_cast<int*>(timer) = duration;
    return duration;
}
void timer_actor_tests() {
    std::array<uint32_t, 1024> manager{};
    std::array<uint32_t, 5000> player{};
    std::array<uint32_t, 4> indices{0, 2, 1, 3};
    auto manager_address = reinterpret_cast<uintptr_t>(manager.data());
    State s;
    s.base = reinterpret_cast<uintptr_t>(&manager_address) - 4;
    s.profile.manager = 4; s.profile.index = 616; s.profile.list = 604; s.profile.list_end = 608;
    s.profile.slots = 472; s.profile.capacity = 16; s.profile.action = 16700; s.profile.timer = 18220;
    s.profile.timer_vtable = 0x40;
    manager[616 / 4] = 1;
    manager[604 / 4] = reinterpret_cast<uintptr_t>(indices.data());
    manager[608 / 4] = reinterpret_cast<uintptr_t>(indices.data() + indices.size());
    manager[472 / 4 + 2] = reinterpret_cast<uintptr_t>(player.data());
    player[16700 / 4] = 101;
    player[18220 / 4] = s.base + 0x40;
    CHECK(actor(s) == reinterpret_cast<uintptr_t>(player.data()));
    player[16700 / 4] = 103;
    CHECK(actor(s) != 0);
    player[16700 / 4] = 102;
    CHECK(actor(s) == 0);
    player[16700 / 4] = 101;
    ++player[18220 / 4];
    CHECK(actor(s) == 0);
    --player[18220 / 4];
    indices[1] = 16;
    CHECK(actor(s) == 0);
    indices[1] = 2;
    manager[616 / 4] = 4;
    CHECK(actor(s) == 0);
    manager[616 / 4] = 1;
    ++manager[608 / 4];
    CHECK(actor(s) == 0);
    --manager[608 / 4];
    manager[608 / 4] = manager[604 / 4] - 4;
    CHECK(actor(s) == 0);

    State call;
    call.profile.timer = 4;
    call.profile.arm = reinterpret_cast<uintptr_t>(&capture_arm);
    std::array<int, 3> timer{17, 0, 19};
    CHECK(arm(call, reinterpret_cast<uintptr_t>(timer.data())));
    CHECK(timer[0] == 17 && timer[1] == 0x3FFFFFFF && timer[2] == 19);
}
}

int main() {
    timer_actor_tests();
    const auto config = toml::parse("schema = 1\n[implementations]\ncn-279 = 'timer'\nkr-812 = 'patch'\nbad = 'unknown'\n");
    CHECK(config);
    CHECK(implementation(config.table(), "cn-279") == Implementation::timer);
    CHECK(implementation(config.table(), "kr-812") == Implementation::patch);
    CHECK(implementation(config.table(), "kr-1256") == Implementation::invalid);
    CHECK(implementation(config.table(), "bad") == Implementation::invalid);
    CHECK(implementation(config.table(), nullptr) == Implementation::invalid);
    State dispatch;
    dispatch.message = WM_APP + 19;
    current = &dispatch;
    CWPSTRUCT message{};
    message.message = dispatch.message + 1;
    on_message(HC_ACTION, 0, reinterpret_cast<LPARAM>(&message));
    CHECK(!dispatch.dispatched.load());
    message.message = dispatch.message;
    on_message(HC_ACTION, 0, reinterpret_cast<LPARAM>(&message));
    CHECK(dispatch.dispatched.load());
    CHECK(!dispatch.failed.load());
    CHECK(!dispatch.reported.load());
    current = nullptr;
    return finish("auto_dash_tests");
}

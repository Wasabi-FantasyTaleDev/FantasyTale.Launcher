#include "check.h"
#include <fthook/engine.h>
#include <fthook/memory.h>
#include <windows.h>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>

namespace fs = std::filesystem;
void services_tests();
using namespace fthook;
namespace {
fs::path executable() {
    wchar_t path[32768]{};
    const DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    return std::wstring(path, length);
}
void write(const fs::path& path, const std::string& value) { std::ofstream(path, std::ios::binary) << value; }
std::string read(const fs::path& path) {
    std::ifstream input(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}
struct FakeImage {
    uint8_t* page = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    FakeImage() {
        std::memset(page, 0xCC, 4096);
        const uint8_t code[] = {0x91, 0x23, 0x45, 0x67, 0xAA, 0xBB};
        std::memcpy(page + 0x100, code, sizeof(code));
        DWORD old = 0;
        VirtualProtect(page, 4096, PAGE_EXECUTE_READ, &old);
    }
    ~FakeImage() { VirtualFree(page, 0, MEM_RELEASE); }
    ImageInfo info() const {
        ImageInfo image;
        image.base = reinterpret_cast<uintptr_t>(page);
        image.image_size = 4096;
        image.code.push_back({image.base, 4096});
        return image;
    }
};
void handled_exception(DWORD code) {
    __try { RaiseException(code, 0, 0, nullptr); }
    __except (EXCEPTION_EXECUTE_HANDLER) {}
}
Session session(const fs::path& folder, const char* id, const char* build = "synthetic") {
    Session value;
    value.game = "bubblefighter";
    value.build = build;
    value.branch = "kr";
    value.log_path = (folder / L"fthook.log").wstring();
    value.plugins.push_back({id, "1.0.0", folder.wstring(), false, {}});
    return value;
}
std::string fixture_manifest() {
    return R"(schema = 1
[plugin]
id = "test.native"
version = "1.0.0"
game = "bubblefighter"
kind = "native"
name = { en = "Native test" }
[native]
library = "native_fixture.dll"
[[options]]
key = "behavior"
type = "string"
default = "normal"
label = { en = "Behavior" }
[[options]]
key = "cleanup_event"
type = "string"
default = ""
label = { en = "Cleanup event" }
[[options]]
key = "site"
type = "int"
default = 0
label = { en = "Synthetic patch address" }
)";
}
const char* patch = R"(
[[patches]]
id = "gate"
locate = { locator = "test.native.site", offset = 4 }
expect = "AA BB"
write = "90 90"
timing = "immediate"
)";
void fixture_test(const std::string& scenario, const fs::path& folder) {
    const auto library = folder / "native_fixture.dll";
    if (scenario != "missing-library") fs::copy_file(executable().parent_path() / "native_fixture.dll", library, fs::copy_options::overwrite_existing);
    auto text = fixture_manifest();
    if (scenario == "companion-api") text += "\n[companion.messages]\n\"test.echo\" = 1\n";
    if (scenario == "missing-entry") text.replace(text.find("library ="), 7, "entry = \"absent\"\nlibrary");
    if (scenario == "locator" || scenario == "locator-fails" || scenario == "locator-seh"
        || scenario == "mismatch" || scenario == "overlap" || scenario == "fingerprint") text += patch;
    if (scenario == "mismatch" || scenario == "stage-mismatch")
        text += "\n[[patches]]\nid = \"bad\"\nlocate = { pattern = \"91 23 45 67\" }\nexpect = \"00\"\nwrite = \"FF\"\ntiming = \"immediate\"\n";
    if (scenario == "overlap") text += "\n[[patches]]\nid = \"overlap\"\nlocate = { locator = \"test.native.site\", offset = 5 }\nexpect = \"BB\"\nwrite = \"90\"\ntiming = \"immediate\"\n";
    write(folder / "plugin.toml", text);
    FakeImage image;
    const std::string event_name = "Local\\FtHookCleanup-" + std::to_string(GetCurrentProcessId());
    HANDLE event = CreateEventA(nullptr, TRUE, FALSE, event_name.c_str());
    CHECK(event != nullptr);
    auto value = session(folder, "test.native");
    if (scenario.rfind("authentication-", 0) == 0) {
        value.launch_id = "0123456789abcdef0123456789abcdef";
        value.ticket = "fixture-private-ticket";
        value.endpoint_host = "127.0.0.1";
        value.endpoint_port = 7100;
        if (scenario != "authentication-denied") value.credential_plugin = scenario == "authentication-other-owner" ? "test.other" : "test.native";
        if (scenario == "authentication-incomplete") value.endpoint_port = 0;
    }
    if (scenario == "companion-api") { value.launch_id = "0123456789abcdef0123456789abcdef"; value.launcher_pid = GetCurrentProcessId(); }
    value.plugins[0].required = true;
    value.plugins[0].options = {{"behavior", scenario == "stage-mismatch" ? std::string("stage") : scenario},
        {"cleanup_event", event_name}, {"site", static_cast<int64_t>(reinterpret_cast<uintptr_t>(image.page + 0x104))}};
    if (scenario == "fingerprint") value.pe_timestamp = 17;
    LocatorRegistry locators;
    const auto reports = run_session(value, locators, [&] { return image.info(); }, 1);
    CHECK(reports.size() == 1 && reports[0].required);
    const bool success = scenario == "normal" || scenario == "locator" || scenario == "stage" || scenario == "companion-api"
        || (scenario.rfind("authentication-", 0) == 0 && scenario != "authentication-incomplete");
    if (success) {
        CHECK(reports[0].outcome == Outcome::Installed);
        const HMODULE module = GetModuleHandleW(library.c_str());
        CHECK(module != nullptr);
        const auto proc = GetProcAddress(module, "fixture_api_alive");
        int32_t (__cdecl *alive)() = nullptr;
        std::memcpy(&alive, &proc, sizeof(alive));
        CHECK(alive && alive() == 1);
        CHECK(WaitForSingleObject(event, 0) == WAIT_TIMEOUT);
        CHECK(image.page[0x104] == (scenario == "normal" || scenario == "companion-api" || scenario.rfind("authentication-", 0) == 0 ? 0xAA : 0x90));
        // Reusing a DLL cannot run its singleton entry a second time.
        const auto duplicate = run_session(value, locators, [&] { return image.info(); }, 1);
        CHECK(duplicate[0].outcome == Outcome::Failed && duplicate[0].detail.find("already has") != std::string::npos);
        CHECK(alive() == 1);
    } else {
        CHECK(reports[0].outcome == Outcome::Failed || reports[0].outcome == Outcome::SignatureMismatch);
        CHECK(!reports[0].detail.empty());
        CHECK(image.page[0x104] == 0xAA && image.page[0x105] == 0xBB);
        CHECK(!locators.find("test.native.site"));
        CHECK((GetModuleHandleW(library.c_str()) != nullptr) == (scenario == "cleanup-fails"));
        const bool entered = scenario != "missing-library" && scenario != "missing-entry" && scenario != "fingerprint" && scenario != "authentication-incomplete";
        CHECK(WaitForSingleObject(event, 0) == (entered ? WAIT_OBJECT_0 : WAIT_TIMEOUT));
    }
    CloseHandle(event);
    if (scenario.rfind("authentication-", 0) == 0) CHECK(read(folder / "fthook.log").find("fixture-private-ticket") == std::string::npos);
}
void sample_test(const fs::path& folder) {
    const auto base = executable().parent_path();
    fs::copy_file(base / "sample_locator.dll", folder / "sample_locator.dll", fs::copy_options::overwrite_existing);
    fs::copy_file(base / "../../../native/samples/sample.locator/plugin.toml", folder / "plugin.toml", fs::copy_options::overwrite_existing);
    auto value = session(folder, "sample.locator");
    FakeImage image;
    LocatorRegistry locators;
    const auto reports = run_session(value, locators, [&] { return image.info(); });
    CHECK(reports.size() == 1 && reports[0].outcome == Outcome::Installed);
    CHECK(image.page[0x104] == 0x90 && image.page[0x105] == 0x90);
}
void provider_test(const std::string& scenario, const fs::path& folder) {
    fs::copy_file(executable().parent_path() / "native_fixture.dll", folder / "native_fixture.dll", fs::copy_options::overwrite_existing);
    std::string manifest = fixture_manifest();
    const bool fails = scenario == "provider-fails";
    if (fails) manifest += "\n[[patches]]\nid = \"pending\"\nlocate = { pattern = \"01 02 03 04 05\" }\nexpect = \"01\"\nwrite = \"90\"\ndeadline_ms = 5\n";
    write(folder / "plugin.toml", manifest);
    const auto consumer = folder / "consumer";
    fs::create_directory(consumer);
    write(consumer / "plugin.toml", R"(schema = 1
[plugin]
id = "test.consumer"
version = "1.0.0"
game = "bubblefighter"
kind = "patch"
name = { en = "Consumer" }
)" + std::string(patch));
    const std::string event_name = "Local\\FtHookCleanup-" + std::to_string(GetCurrentProcessId());
    HANDLE event = CreateEventA(nullptr, TRUE, FALSE, event_name.c_str());
    auto value = session(folder, "test.native");
    value.plugins[0].options["cleanup_event"] = event_name;
    value.plugins.push_back({"test.consumer", "1.0.0", consumer.wstring(), false, {}});
    FakeImage image;
    LocatorRegistry locators;
    const auto result = run_session(value, locators, [&] { return image.info(); }, 1);
    CHECK(result.size() == 2);
    CHECK(result[0].outcome == (fails ? Outcome::Timeout : Outcome::Installed));
    CHECK(result[1].outcome == (fails ? Outcome::SignatureMismatch : Outcome::Installed));
    CHECK(image.page[0x104] == (fails ? 0xAA : 0x90));
    CHECK(WaitForSingleObject(event, 0) == (fails ? WAIT_OBJECT_0 : WAIT_TIMEOUT));
    CloseHandle(event);
}
void rollback_test() {
    FakeImage image;
    const uintptr_t start = reinterpret_cast<uintptr_t>(image.page + 0x100);
    PatchSpec a, b;
    a.id = "first"; a.expect = {{0x91}, {255}}; a.write = {0x90};
    b.id = "second"; b.expect = {{0}, {255}}; b.write = {0x90};
    PreparedPlugin plugin;
    plugin.patches = {&a, &b};
    std::string detail;
    bool incomplete = true;
    CHECK(apply(plugin, {start, start + 1}, detail, &incomplete) == Outcome::SignatureMismatch);
    CHECK(!incomplete && image.page[0x100] == 0x91 && image.page[0x101] == 0x23);
    auto* non_code = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    CHECK(non_code != nullptr);
    if (!non_code) return;
    non_code[0] = 0x23;
    b.expect = {{0x23}, {255}};
    // Both original snapshots match, but the second site cannot accept a code write.
    // The first actual write must be restored, not merely rejected by preflight.
    CHECK(apply(plugin, {start, reinterpret_cast<uintptr_t>(non_code)}, detail, &incomplete) == Outcome::Failed);
    CHECK(!incomplete && image.page[0x100] == 0x91 && non_code[0] == 0x23);
    const Pattern initial{{0x91}, {255}};
    CHECK(write_code(start, initial, {0x90}) == WriteResult::Installed);
    CHECK(apply(plugin, {start, reinterpret_cast<uintptr_t>(non_code)}, detail, &incomplete) == Outcome::Failed);
    CHECK(!incomplete && image.page[0x100] == 0x90 && non_code[0] == 0x23);
    VirtualFree(non_code, 0, MEM_RELEASE);
}
uint8_t* snapshot_site = nullptr;
uintptr_t snapshot_trigger = 0;
unsigned snapshot_changes = 0;
LONG CALLBACK change_journal_snapshot(EXCEPTION_POINTERS* fault) {
    if (fault->ExceptionRecord->ExceptionCode != STATUS_GUARD_PAGE_VIOLATION
        || fault->ExceptionRecord->NumberParameters < 2
        || fault->ExceptionRecord->ExceptionInformation[1] != snapshot_trigger || !snapshot_site)
        return EXCEPTION_CONTINUE_SEARCH;
    DWORD previous = 0, ignored = 0;
    if (!VirtualProtect(snapshot_site, 2, PAGE_EXECUTE_READWRITE, &previous)) return EXCEPTION_CONTINUE_SEARCH;
    snapshot_site[1] = 0x24;
    VirtualProtect(snapshot_site, 2, previous, &ignored);
    ++snapshot_changes;
    return EXCEPTION_CONTINUE_EXECUTION;
}
void snapshot_change_test() {
    FakeImage image;
    auto* trigger = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    CHECK(trigger != nullptr);
    if (!trigger) return;
    trigger[0] = 0xAA;
    snapshot_site = image.page + 0x100;
    snapshot_trigger = reinterpret_cast<uintptr_t>(trigger);
    snapshot_changes = 0;
    PVOID observer = AddVectoredExceptionHandler(1, change_journal_snapshot);
    CHECK(observer != nullptr);
    DWORD previous = 0;
    CHECK(VirtualProtect(trigger, 4096, PAGE_READWRITE | PAGE_GUARD, &previous));
    PatchSpec first, second;
    first.id = "wildcard"; first.expect = {{0x91, 0}, {255, 0}}; first.write = {0x90, 0x90};
    second.id = "trigger"; second.expect = {{0xAA}, {255}}; second.write = {0x90};
    PreparedPlugin plugin;
    plugin.patches = {&first, &second};
    std::string detail;
    bool incomplete = true;
    // Reading the second journal entry deterministically changes a wildcard byte
    // in the first entry. A stale rollback would lose the newer client value.
    const auto result = apply(plugin, {reinterpret_cast<uintptr_t>(snapshot_site), snapshot_trigger}, detail, &incomplete);
    CHECK(result == Outcome::SignatureMismatch);
    CHECK(!incomplete && snapshot_changes == 1);
    CHECK(image.page[0x100] == 0x91 && image.page[0x101] == 0x24 && trigger[0] == 0xAA);
    if (observer) CHECK(RemoveVectoredExceptionHandler(observer));
    snapshot_site = nullptr; snapshot_trigger = 0;
    VirtualFree(trigger, 0, MEM_RELEASE);
}
using ProtectMemory = BOOL(WINAPI*)(LPVOID, SIZE_T, DWORD, PDWORD);
ProtectMemory original_protect = nullptr;
uintptr_t failing_import = 0;
bool rejected_code_write = false;
BOOL WINAPI fail_patch_protection(LPVOID address, SIZE_T size, DWORD protection, PDWORD previous) {
    if (reinterpret_cast<uintptr_t>(address) == failing_import && !rejected_code_write)
        return original_protect(address, size, protection, previous);
    rejected_code_write = true;
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}
void rollback_failure_test() {
    FakeImage image;
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    const auto iat = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT];
    const auto original = reinterpret_cast<uintptr_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "VirtualProtect"));
    for (DWORD offset = 0; offset + 4 <= iat.Size; offset += 4)
        if (*reinterpret_cast<const uintptr_t*>(base + iat.VirtualAddress + offset) == original) {
            failing_import = base + iat.VirtualAddress + offset;
            break;
        }
    CHECK(failing_import != 0 && original != 0);
    if (!failing_import || !original) return;
    original_protect = reinterpret_cast<ProtectMemory>(original);
    rejected_code_write = false;
    const auto bytes = [](uintptr_t value) { std::vector<uint8_t> result(4); std::memcpy(result.data(), &value, 4); return result; };
    PatchSpec first, second;
    first.id = "protection-import"; first.import_slot = true;
    first.expect = {bytes(original), {255, 255, 255, 255}};
    first.write = bytes(reinterpret_cast<uintptr_t>(&fail_patch_protection));
    second.id = "code"; second.expect = {{0x91}, {255}}; second.write = {0x90};
    PreparedPlugin plugin;
    plugin.patches = {&first, &second};
    std::string detail;
    bool incomplete = false;
    // The first patch intercepts protection in this fixture process. It rejects
    // the second write and restoration of the first, exercising the real journal.
    const auto result = apply(plugin, {failing_import, reinterpret_cast<uintptr_t>(image.page + 0x100)}, detail, &incomplete);
    const auto retained = *reinterpret_cast<const uintptr_t*>(failing_import);
    DWORD previous = 0, ignored = 0;
    const bool opened = original_protect(reinterpret_cast<void*>(failing_import), 4, PAGE_READWRITE, &previous) != FALSE;
    if (opened) {
        InterlockedExchange(reinterpret_cast<volatile LONG*>(failing_import), static_cast<LONG>(original));
        CHECK(original_protect(reinterpret_cast<void*>(failing_import), 4, previous, &ignored));
    }
    CHECK(opened);
    CHECK(result == Outcome::RollbackFailed && incomplete && rejected_code_write);
    CHECK(retained == reinterpret_cast<uintptr_t>(&fail_patch_protection) && image.page[0x100] == 0x91);
    PluginReport optional;
    optional.id = "test.optional"; optional.outcome = result;
    CHECK(std::strcmp(activation_error({optional}), "patch-restoration") == 0);
    optional.outcome = Outcome::SignatureMismatch;
    CHECK(std::strcmp(activation_error({optional}), "") == 0);
    optional.required = true;
    CHECK(std::strcmp(activation_error({optional}), "required-plugin") == 0);
    optional.outcome = Outcome::SkippedOption;
    CHECK(std::strcmp(activation_error({optional}), "") == 0);
    failing_import = 0;
}
uintptr_t restoration_target = 0;
bool restoration_persistent = false;
bool restoration_cache_test = false;
unsigned restoration_rejections = 0;
using FlushCache = BOOL(WINAPI*)(HANDLE, LPCVOID, SIZE_T);
FlushCache original_flush = nullptr;
unsigned cache_rejections = 0;
BOOL WINAPI fail_restoration_cache(HANDLE process, LPCVOID address, SIZE_T size) {
    if (restoration_persistent || cache_rejections == 0) {
        ++cache_rejections;
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    return original_flush(process, address, size);
}
BOOL WINAPI fail_restoration_protection(LPVOID address, SIZE_T size, DWORD protection, PDWORD previous) {
    if (!restoration_cache_test && reinterpret_cast<uintptr_t>(address) == restoration_target
        && protection != PAGE_EXECUTE_READWRITE && protection != PAGE_READWRITE
        && (restoration_persistent || restoration_rejections == 0)) {
        ++restoration_rejections;
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    return original_protect(address, size, protection, previous);
}
void write_restoration_test(const std::string& scenario) {
    FakeImage image;
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    const auto iat = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT];
    const auto original = reinterpret_cast<uintptr_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "VirtualProtect"));
    uintptr_t slot = 0, cache_slot = 0;
    const auto original_cache = reinterpret_cast<uintptr_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "FlushInstructionCache"));
    for (DWORD offset = 0; offset + 4 <= iat.Size; offset += 4) {
        const auto candidate = base + iat.VirtualAddress + offset;
        const auto value = *reinterpret_cast<const uintptr_t*>(candidate);
        if (value == original) slot = candidate;
        if (value == original_cache) cache_slot = candidate;
    }
    CHECK(slot != 0 && original != 0);
    if (!slot || !original) return;
    original_protect = reinterpret_cast<ProtectMemory>(original);
    DWORD iat_protection = 0, ignored = 0;
    CHECK(original_protect(reinterpret_cast<void*>(slot), 4, PAGE_READWRITE, &iat_protection));
    InterlockedExchange(reinterpret_cast<volatile LONG*>(slot), reinterpret_cast<LONG>(&fail_restoration_protection));
    CHECK(original_protect(reinterpret_cast<void*>(slot), 4, iat_protection, &ignored));

    const bool import_test = scenario.rfind("import-restoration-", 0) == 0;
    const bool already_present = scenario == "import-restoration-present";
    restoration_cache_test = scenario.rfind("cache-restoration-", 0) == 0;
    CHECK(!(import_test || restoration_cache_test) || cache_slot != 0);
    if ((import_test || restoration_cache_test) && !cache_slot) {
        CHECK(original_protect(reinterpret_cast<void*>(slot), 4, PAGE_READWRITE, &ignored));
        InterlockedExchange(reinterpret_cast<volatile LONG*>(slot), static_cast<LONG>(original));
        CHECK(original_protect(reinterpret_cast<void*>(slot), 4, iat_protection, &ignored));
        return;
    }
    restoration_target = import_test ? cache_slot : reinterpret_cast<uintptr_t>(image.page + 0x100);
    restoration_persistent = scenario.ends_with("fails");
    restoration_rejections = 0;
    cache_rejections = 0;
    original_flush = reinterpret_cast<FlushCache>(original_cache);
    if (restoration_cache_test) {
        CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, PAGE_READWRITE, &ignored));
        InterlockedExchange(reinterpret_cast<volatile LONG*>(cache_slot), reinterpret_cast<LONG>(&fail_restoration_cache));
        CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, iat_protection, &ignored));
    }
    PatchSpec code_patch;
    code_patch.id = "code"; code_patch.expect = {{0x91}, {255}}; code_patch.write = {0x90};
    const auto bytes = [](uintptr_t value) { std::vector<uint8_t> result(4); std::memcpy(result.data(), &value, 4); return result; };
    constexpr uintptr_t replacement_cache = 0x12345678;
    if (import_test) {
        code_patch.import_slot = true;
        code_patch.expect = {bytes(original_cache), {255, 255, 255, 255}};
        code_patch.write = bytes(replacement_cache);
        if (already_present) {
            CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, PAGE_READWRITE, &ignored));
            InterlockedExchange(reinterpret_cast<volatile LONG*>(cache_slot), static_cast<LONG>(replacement_cache));
            CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, iat_protection, &ignored));
        }
    }
    PreparedPlugin plugin;
    plugin.patches = {&code_patch};
    std::string detail;
    bool incomplete = false;
    Outcome outcome;
    if (already_present) {
        const auto result = write_import(restoration_target, code_patch.expect, code_patch.write);
        CHECK(result == WriteResult::Failed);
        outcome = Outcome::Failed;
    } else outcome = apply(plugin, {restoration_target}, detail, &incomplete);
    MEMORY_BASIC_INFORMATION page{};
    CHECK(VirtualQuery(reinterpret_cast<void*>(restoration_target), &page, sizeof(page)));
    if (import_test) CHECK(*reinterpret_cast<const uintptr_t*>(cache_slot) == (already_present ? replacement_cache : original_cache));
    else CHECK(image.page[0x100] == 0x91);
    CHECK(outcome == (restoration_persistent ? Outcome::RollbackFailed : Outcome::Failed));
    CHECK(incomplete == restoration_persistent);
    const DWORD expected_protection = import_test ? (restoration_persistent ? PAGE_READWRITE : iat_protection)
        : static_cast<DWORD>(restoration_persistent && !restoration_cache_test ? PAGE_EXECUTE_READWRITE : PAGE_EXECUTE_READ);
    CHECK(page.Protect == expected_protection);
    CHECK(restoration_rejections == (restoration_cache_test ? 0u : restoration_persistent ? 2u : 1u));
    if (restoration_cache_test) CHECK(cache_rejections == (restoration_persistent ? 2u : 1u));

    if (scenario == "write-restoration-fails") {
        CHECK(original_protect(image.page, 4096, PAGE_EXECUTE_READ, &ignored));
        const auto folder = fs::temp_directory_path() / ("fthook-restoration-" + std::to_string(GetCurrentProcessId()));
        fs::create_directories(folder);
        write(folder / "plugin.toml", "schema = 1\n[plugin]\nid = \"test.restore\"\nversion = \"1.0.0\"\n"
            "game = \"bubblefighter\"\nkind = \"patch\"\nname = { en = \"Fixture\" }\n[[patches]]\n"
            "id = \"code\"\nlocate = { locator = \"test.restore.site\" }\nexpect = \"91\"\nwrite = \"90\"\ntiming = \"immediate\"\n");
        LocatorRegistry locators;
        CHECK(locators.add("test.restore.site", [](const ImageInfo& current, std::string&) { return current.base + 0x100; }));
        Session value;
        value.game = "bubblefighter"; value.build = "synthetic"; value.branch = "kr";
        value.plugins.push_back({"test.restore", "1.0.0", folder.wstring(), false, {}});
        value.plugins.push_back({"test.later", "1.0.0", (folder / "missing").wstring(), false, {}});
        const auto reports = run_session(value, locators, [&] { return image.info(); }, 1);
        CHECK(reports.size() == 2);
        CHECK(reports[0].outcome == Outcome::RollbackFailed && !reports[0].required);
        CHECK(reports[1].outcome == Outcome::Failed && reports[1].detail == "session stopped after incomplete patch restoration");
        CHECK(std::strcmp(activation_error(reports), "patch-restoration") == 0);
        fs::remove_all(folder);
    }

    // Restore through the saved system entry before fixture teardown, regardless
    // of the observed recovery outcome. No production injection API is needed.
    CHECK(original_protect(reinterpret_cast<void*>(slot), 4, PAGE_READWRITE, &ignored));
    InterlockedExchange(reinterpret_cast<volatile LONG*>(slot), static_cast<LONG>(original));
    if (import_test || restoration_cache_test) {
        CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, PAGE_READWRITE, &ignored));
        InterlockedExchange(reinterpret_cast<volatile LONG*>(cache_slot), static_cast<LONG>(original_cache));
        CHECK(original_protect(reinterpret_cast<void*>(cache_slot), 4, iat_protection, &ignored));
    }
    CHECK(original_protect(reinterpret_cast<void*>(slot), 4, iat_protection, &ignored));
    CHECK(original_protect(image.page, 4096, PAGE_EXECUTE_READ, &ignored));
    restoration_target = 0;
}
void cross_plugin_overlap_test(const std::string& scenario) {
    const fs::path root = fs::temp_directory_path() / ("fthook-cross-overlap-" + std::to_string(GetCurrentProcessId()));
    const fs::path first = root / "first";
    const fs::path second = root / "second";
    fs::create_directories(first);
    fs::create_directories(second);
    const auto manifest = [](const char* id, const char* expect, const char* write, int offset = 0) {
        return std::string("schema = 1\n[plugin]\nid = \"") + id + "\"\nversion = \"1.0.0\"\ngame = \"bubblefighter\"\n"
            "kind = \"patch\"\nname = { en = \"Fixture\" }\n[[patches]]\n"
            "id = \"write\"\nlocate = { locator = \"test.site\", offset = " + std::to_string(offset)
            + " }\nexpect = \"" + expect + "\"\nwrite = \"" + write + "\"\ntiming = \"immediate\"\n";
    };
    write(first / "plugin.toml", manifest("test.first", "AA BB", "90 90"));
    const bool adjacent = scenario == "cross-adjacent";
    if (adjacent) write(second / "plugin.toml", manifest("test.second", "CC", "88", 2));
    else if (scenario == "cross-partial") write(second / "plugin.toml", manifest("test.second", "90", "88", 1));
    else if (scenario == "cross-identical") write(second / "plugin.toml", manifest("test.second", "AA BB", "90 90"));
    else write(second / "plugin.toml", manifest("test.second", "90 90", "88 88"));
    const auto third = root / "third";
    fs::create_directories(third);
    write(third / "plugin.toml", manifest("test.third", "CC", "77", 3));
    FakeImage image;
    LocatorRegistry locators;
    CHECK(locators.add("test.site", [&](const ImageInfo& current, std::string&) {
        return current.base + 0x104;
    }));
    Session value;
    value.game = "bubblefighter";
    value.build = "synthetic";
    value.branch = "kr";
    value.plugins.push_back({"test.first", "1.0.0", first.wstring(), true, {}});
    value.plugins.push_back({"test.second", "1.0.0", second.wstring(), true, {}});
    value.plugins.push_back({"test.third", "1.0.0", third.wstring(), false, {}});
    const auto reports = run_session(value, locators, [&] { return image.info(); }, 1);
    CHECK(reports.size() == 3);
    CHECK(reports[0].outcome == Outcome::Installed);
    CHECK(reports[1].outcome == (adjacent ? Outcome::Installed : Outcome::Failed));
    CHECK(adjacent || reports[1].detail.find("overlaps patch 'write' from plugin 'test.first'") != std::string::npos);
    CHECK(reports[2].outcome == Outcome::Installed);
    CHECK(image.page[0x104] == 0x90 && image.page[0x105] == 0x90);
    CHECK(image.page[0x106] == (adjacent ? 0x88 : 0xCC) && image.page[0x107] == 0x77);
    fs::remove_all(root);
}
void crash_test(const std::string& scenario, const fs::path& folder) {
    const auto base = executable().parent_path();
    fs::copy_file(base / "crash_recorder.dll", folder / "crash_recorder.dll", fs::copy_options::overwrite_existing);
    fs::copy_file(base / "../../../games/BubbleFighter/plugins/bubblefighter.fantasy.diagnostics/plugin.toml",
        folder / "plugin.toml", fs::copy_options::overwrite_existing);
    auto value = session(folder, "bubblefighter.fantasy.diagnostics", "id-400");
    std::string manifest_error;
    const auto manifest = load_manifest(folder.wstring(), manifest_error);
    CHECK(manifest.has_value());
    if (!manifest) return;
    value.plugins[0].version = manifest->version;
    value.plugins[0].options = {{"max_records", int64_t{2}}, {"max_kib", int64_t{16}}};
    if (scenario == "crash-byte-limit") value.plugins[0].options["max_records"] = int64_t{128};
    if (scenario == "crash-disabled") value.plugins[0].options["enabled"] = false;
    if (scenario == "crash-file-failure") value.log_path = (folder / "missing/fthook.log").wstring();
    LocatorRegistry locators;
    const auto reports = run_session(value, locators, [] { return describe_main_image(); });
    CHECK(reports.size() == 1);
    if (scenario == "crash-file-failure") { CHECK(reports[0].outcome == Outcome::Failed); return; }
    CHECK(reports[0].outcome == Outcome::Installed);
    if (scenario == "crash-fatal") {
        SetErrorMode(SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS);
        auto* denied = static_cast<volatile uint32_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_NOACCESS));
        const uint32_t fault = *denied;
        std::printf("unexpected read: %u\n", fault);
        return;
    }
    if (scenario == "crash-byte-limit") {
        for (int i = 0; i < 200; ++i) handled_exception(EXCEPTION_ACCESS_VIOLATION);
        const auto text = read(folder / "crash.log");
        CHECK(text.size() <= 16 * 1024 && text.find("byte limit reached") != std::string::npos);
        return;
    }
    if (scenario == "crash-first-fault-late") {
        handled_exception(0xE06D7363u);
        handled_exception(0xE06D7363u);
        handled_exception(EXCEPTION_ACCESS_VIOLATION);
        const auto text = read(folder / "crash.log");
        CHECK(text.find("record limit reached") != std::string::npos);
        CHECK(text.find("C0000005") != std::string::npos);
        CHECK(text.find("context_flags=") != std::string::npos);
        CHECK(text.find("dispatcher_frames") != std::string::npos && text.find("seh_chain") != std::string::npos);
        CHECK(text.size() <= 16 * 1024);
        return;
    }
    handled_exception(0xE06D7363u);
    handled_exception(EXCEPTION_INT_DIVIDE_BY_ZERO);
    handled_exception(EXCEPTION_ACCESS_VIOLATION);
    const std::string before = read(folder / "crash.log");
    for (int i = 0; i < 8; ++i) handled_exception(EXCEPTION_ILLEGAL_INSTRUCTION);
    const std::string after = read(folder / "crash.log");
    if (scenario == "crash-disabled") { CHECK(!fs::exists(folder / "crash.log")); return; }
    CHECK(before == after && after.size() <= 16 * 1024);
    // A C++ throw is recorded; records past the budget are kept only for the exit record.
    CHECK(after.find("E06D7363") != std::string::npos && after.find("C0000094") != std::string::npos);
    CHECK(after.find("C0000005") == std::string::npos && after.find("C000001D") == std::string::npos);
    CHECK(after.find("registers eip=") != std::string::npos && after.find("stack_candidates") != std::string::npos);
    CHECK(after.find("context_flags=") != std::string::npos);
    CHECK(after.find("dispatcher_frames") != std::string::npos && after.find("seh_chain") != std::string::npos);
    CHECK(after.find("module=") != std::string::npos && after.find("record limit reached") != std::string::npos);
}
}

int main(int argc, char** argv) {
    if (argc == 2 && std::strcmp(argv[1], "services") == 0) {
        services_tests();
        return finish("native-services");
    }
    if (argc == 1) services_tests();
    if (argc > 1) {
        const std::string scenario = argv[1];
        const auto folder = executable().parent_path() / "test-sessions" / (scenario + "-" + std::to_string(GetCurrentProcessId()));
        fs::create_directories(folder);
        if (scenario == "sample") sample_test(folder);
        else if (scenario.rfind("provider-", 0) == 0) provider_test(scenario, folder);
        else if (scenario == "rollback") rollback_test();
        else if (scenario == "snapshot-change") snapshot_change_test();
        else if (scenario == "rollback-failure") rollback_failure_test();
        else if (scenario.rfind("write-restoration-", 0) == 0 || scenario.rfind("import-restoration-", 0) == 0
            || scenario.rfind("cache-restoration-", 0) == 0) write_restoration_test(scenario);
        else if (scenario.rfind("cross-", 0) == 0) cross_plugin_overlap_test(scenario);
        else if (scenario.rfind("crash-", 0) == 0) crash_test(scenario, folder);
        else fixture_test(scenario, folder);
        return finish(scenario.c_str());
    }
    // Successful plugins are process-lifetime: every case gets an actual fresh loader.
    const char* scenarios[] = {"normal", "companion-api", "authentication-api", "authentication-denied", "authentication-other-owner", "authentication-incomplete", "locator", "stage", "entry-fails", "abi-mismatch", "throws", "locator-fails",
        "mismatch", "stage-mismatch", "overlap", "missing-library", "missing-entry", "fingerprint", "sample",
        "entry-seh", "locator-seh", "cleanup-fails", "crash-records", "crash-disabled", "crash-file-failure",
        "crash-byte-limit", "crash-fatal", "crash-first-fault-late", "provider-succeeds", "provider-fails", "rollback", "cross-overlap",
        "cross-identical", "cross-partial", "cross-adjacent", "snapshot-change", "rollback-failure",
        "write-restoration-fails", "write-restoration-recovers", "import-restoration-fails",
        "import-restoration-recovers", "import-restoration-present", "cache-restoration-fails", "cache-restoration-recovers"};
    for (const char* scenario : scenarios) {
        std::wstring command = L"\"" + executable().wstring() + L"\" " + widen(scenario);
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), nullptr, TRUE};
        const auto log = executable().parent_path() / (std::string(scenario) + "-" + std::to_string(GetCurrentProcessId()) + ".log");
        HANDLE output = CreateFileW(log.c_str(), GENERIC_WRITE, FILE_SHARE_READ, &attributes, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        CHECK(output != INVALID_HANDLE_VALUE);
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdOutput = output; startup.hStdError = output;
        PROCESS_INFORMATION child{};
        const BOOL started = CreateProcessW(nullptr, command.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW,
            nullptr, executable().parent_path().c_str(), &startup, &child);
        CHECK(started);
        if (!started) { CloseHandle(output); continue; }
        const DWORD wait = WaitForSingleObject(child.hProcess, 15000);
        CHECK(wait == WAIT_OBJECT_0);
        if (wait != WAIT_OBJECT_0) TerminateProcess(child.hProcess, 99);
        DWORD code = 99;
        GetExitCodeProcess(child.hProcess, &code);
        const bool fatal = std::strcmp(scenario, "crash-fatal") == 0;
        if (code && !fatal) std::printf("case %s exited %lu\n", scenario, code);
        CHECK(code == (fatal ? EXCEPTION_ACCESS_VIOLATION : 0));
        if (fatal) {
            const auto crash = executable().parent_path() / "test-sessions"
                / (std::string(scenario) + "-" + std::to_string(child.dwProcessId)) / "crash.log";
            const auto text = read(crash);
            CHECK(text.find("C0000005") != std::string::npos && text.find("operation=0 target=") != std::string::npos);
        }
        if (std::strcmp(scenario, "crash-records") == 0) {
            // Returning from main ends the process through ExitProcess, which reports the newest exception and its stack.
            const auto text = read(executable().parent_path() / "test-sessions"
                / (std::string(scenario) + "-" + std::to_string(child.dwProcessId)) / "crash.log");
            const auto exit = text.find("newest exception before exit:");
            CHECK(exit != std::string::npos && text.find("C000001D", exit) != std::string::npos);
            CHECK(text.find("process exit tid=") != std::string::npos && text.find("exit_stack_candidates") != std::string::npos);
            CHECK(text.size() <= 16 * 1024);
        }
        CloseHandle(child.hThread); CloseHandle(child.hProcess);
        CloseHandle(output);
        std::printf("%s", read(log).c_str());
    }
    return finish("native_tests");
}

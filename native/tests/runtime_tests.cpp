#include "check.h"

#include <fthook/engine.h>
#include <fthook/memory.h>
#include <fthook/semver.h>
#include <fthook/status.h>
#include <fthook/launch_metadata.h>
#include <fthook/companion.h>

#include <windows.h>
#define TOML_EXCEPTIONS 0
#define TOML_ENABLE_FORMATTERS 0
#include <toml.hpp>

#include <cstring>
#include <array>
#include <string>
#include <thread>

using namespace fthook;

namespace {

void launch_metadata_tests() {
    const std::string base = "schema=2\n[session]\ngame='fixture'\nbuild='fixture'\nlaunch_id='0123456789abcdef0123456789abcdef'\nlauncher_pid=1\n";
    const auto envelope = [](const std::string& payload) { return "[module_data]\ncapability='fixture-v2'\npayload='" + payload + "'\n"; };
    std::string error;
    auto session = parse_session(base + envelope("{}"), "fixture", error);
    CHECK(session && session->module_data && session->module_data->payload == "{}");
    CHECK(parse_session(base + envelope(std::string(32768, 'x')), "fixture", error));
    CHECK(!parse_session(base + envelope(std::string(32769, 'x')), "fixture", error));
    CHECK(!parse_session(base + envelope(""), "fixture", error));
    CHECK(!parse_session(base + "[module_data]\ncapability='Fixture'\npayload='{}'", "fixture", error));
    CHECK(!parse_session(base + envelope("{}") + "unknown=1", "fixture", error));
    CHECK(!parse_session(base + "[module_data]\ncapability='fixture'\npayload=\"\\u0000\"", "fixture", error));
    std::array<char, 4> output{'X','X','X','X'};
    CHECK(session && copy_launch_metadata(*session, "fixture-v2", nullptr, 0) == 3);
    CHECK(session && copy_launch_metadata(*session, "fixture-v2", output.data(), 2) == -2 && output[0] == 'X');
    CHECK(session && copy_launch_metadata(*session, "fixture-v2", output.data(), 3) == 3 && output[2] == '\0');
    CHECK(session && copy_launch_metadata(*session, "other", nullptr, 0) == 0);
    CHECK(session && copy_launch_metadata(*session, "fixture-v2", nullptr, 1) == -1);
    CHECK(session && copy_launch_metadata(*session, "fixture-v2", output.data(), 0) == -1);
}

void status_replacement_tests() {
    wchar_t temp[MAX_PATH]{};
    CHECK(GetTempPathW(MAX_PATH, temp) != 0);
    const auto folder = std::wstring(temp) + L"ft-status-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64());
    CHECK(CreateDirectoryW(folder.c_str(), nullptr) != 0);
    Session session;
    session.launch_id = "0123456789abcdef0123456789abcdef";
    session.game = "fixture"; session.build = "fixture"; session.instance = "fixture";
    const auto path = folder + L"\\fthook-status-" + widen(session.launch_id) + L".toml";
    CHECK(publish_status(folder, session, "starting", "", {}));
    HANDLE reader = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    CHECK(reader != INVALID_HANDLE_VALUE);
    std::thread release([reader] { Sleep(50); CloseHandle(reader); });
    const bool replaced = publish_status(folder, session, "ready", "", {}, true);
    release.join();
    CHECK(replaced);
    reader = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    const auto started = GetTickCount64();
    CHECK(!publish_status(folder, session, "failed", "status-write", {}));
    CHECK(GetTickCount64() - started < 2000);
    CloseHandle(reader);
    std::string text;
    CHECK(read_text_file(path, 64 * 1024, text) && text.find("state = \"ready\"") != std::string::npos);
    CHECK(text.find("bootstrap_complete = true") != std::string::npos);
    CHECK(GetFileAttributesW((path + L".tmp").c_str()) == INVALID_FILE_ATTRIBUTES);
    DeleteFileW(path.c_str()); RemoveDirectoryW(folder.c_str());
}

void pattern_tests() {
    std::string error;
    auto p = parse_pattern("83 3D ?? ?? 02 75 03", error);
    CHECK(p && p->size() == 7 && !p->is_exact());
    const uint8_t data[] = {0x83, 0x3D, 0x11, 0x22, 0x02, 0x75, 0x03};
    CHECK(p && p->matches(data));
    CHECK(!parse_pattern("", error));
    CHECK(!parse_pattern("8", error));
    CHECK(!parse_pattern("ZZ", error));
    CHECK(parse_pattern("aa ? BB", error).has_value());
}

void semver_tests() {
    CHECK(parse_semver("1.2.3").has_value());
    CHECK(!parse_semver("1.2").has_value());
    CHECK(!parse_semver("01.2.3").has_value());
    CHECK(parse_semver("1.0.0-alpha")->compare(*parse_semver("1.0.0")) < 0);
    CHECK(parse_semver("1.0.0-alpha.2")->compare(*parse_semver("1.0.0-alpha.10")) < 0);
    CHECK(parse_semver("1.0.0+build")->compare(*parse_semver("1.0.0")) == 0);
    const auto v = *parse_semver("3.0.0");
    CHECK(satisfies(v, ">=3.0.0 <4.0.0") == true);
    CHECK(satisfies(v, ">3.0.0") == false);
    CHECK(satisfies(v, "3.0.0") == true);
    CHECK(!satisfies(v, ">=x").has_value());
}

const char* kGoodManifest = R"(
schema = 1
[plugin]
id = "test.sample"
version = "1.2.0"
game = "bubblefighter"
kind = "patch"
runtime = ">=3.0.0"
name = { en = "Sample", "zh-CN" = "示例" }
[compatibility]
builds = ["*"]
exclude = ["cn-279"]
[[options]]
key = "extra"
type = "bool"
default = false
label = { en = "Extra" }
[[patches]]
id = "gate"
locate = { pattern = "11 22 ?? 44 75 03", offset = 4 }
expect = "75 03"
write = "90 90"
[[patches]]
id = "extra"
when = "extra"
locate = { pattern = "AA BB CC DD" }
expect = "AA"
write = "BB"
)";

void manifest_tests() {
    std::string error;
    auto m = parse_manifest(kGoodManifest, "good", error);
    CHECK(m.has_value());
    if (m) {
        CHECK(m->id == "test.sample" && m->name.at("zh-CN") == "示例" && m->patches.size() == 2 && m->options.size() == 1);
        CHECK(m->patches[0].locate.offset == 4 && m->patches[0].write.size() == 2);
    }
    const auto rejects = [](const std::string& text, const char* fragment) {
        std::string why;
        const bool rejected = !parse_manifest(text, "bad", why).has_value();
        const bool named = why.find(fragment) != std::string::npos;
        if (!rejected || !named) std::printf("  manifest error was: %s\n", why.c_str());
        return rejected && named;
    };
    const std::string head = "schema = 1\n[plugin]\nid = \"t.x\"\nversion = \"1.0.0\"\ngame = \"g\"\nkind = \"patch\"\nname = { en = \"X\" }\n";
    auto renamed_metadata = std::string(kGoodManifest);
    renamed_metadata.insert(renamed_metadata.find("[plugin]") + 8, "\naliases = ['test.former']");
    const auto renamed = parse_manifest(renamed_metadata, "renamed", error);
    CHECK(renamed && renamed->id == "test.sample" && renamed->aliases == std::vector<std::string>{"test.former"});
    CHECK(rejects(head + "aliases=['t.x']\n", "plugin.aliases"));
    CHECK(rejects(head + "aliases=['t.old','t.old']\n", "plugin.aliases"));
    CHECK(rejects(head + "aliases=['../old']\n", "plugin.aliases"));
    CHECK(rejects(head + "aliases='t.old'\n", "plugin.aliases"));
    auto launcher_metadata = std::string(kGoodManifest);
    launcher_metadata.insert(launcher_metadata.find("[plugin]") + 8, "\nminimum_launcher_version = \"0.1.14\"");
    CHECK(parse_manifest(launcher_metadata, "launcher-version", error).has_value());
    auto diagnostic_metadata = std::string(kGoodManifest);
    diagnostic_metadata.insert(diagnostic_metadata.find("[plugin]") + 8, "\ndiagnostics = true");
    CHECK(parse_manifest(diagnostic_metadata, "diagnostics", error).has_value());
    auto warning_metadata = std::string(kGoodManifest);
    warning_metadata.insert(warning_metadata.find("[plugin]") + 8, "\nwarning = { en = 'Enable component', 'zh-CN' = '请启用组件' }");
    CHECK(parse_manifest(warning_metadata, "warning", error).has_value());
    CHECK(rejects(head + "warning = 42\n", "warning"));
    const auto file_head = head.substr(0, head.find("kind =")) + "kind = 'files'\nname = { en = 'Component' }\n";
    const std::string payload = "\n[[files]]\nsource='component.dll'\ntarget='component.dll'\nsize=1\nsha256='" + std::string(64, 'a') + "'\n";
    const std::string file_patch = "\n[[patches]]\nid='startup'\nbuilds=['fixture']\nlocate={pattern='AA BB CC'}\nexpect='AA'\nwrite='90'\n";
    auto file_component = parse_manifest(file_head + "warning={en='Enable component'}\n" + payload + file_patch, "file-component", error);
    CHECK(file_component && file_component->kind == "files" && file_component->patches.size() == 1);
    if (file_component) {
        SessionPlugin entry; entry.id = "t.x"; entry.version = "1.0.0";
        PluginReport report;
        CHECK(prepare(*file_component, entry, "fixture", "", report).has_value());
        CHECK(!prepare(*file_component, entry, "other", "", report).has_value());
    }
    CHECK(rejects(file_head + file_patch, "files"));
    CHECK(rejects(file_head + payload, "patches"));
    CHECK(rejects(head + payload, "files"));
    CHECK(rejects(file_head + payload + "typo=1\n", "typo"));
    CHECK(rejects(file_head + payload + file_patch + "\n[[options]]\nkey='extra'\ntype='bool'\ndefault=false\nlabel={en='Extra'}\n", "files"));
    auto optional_file_patch = file_patch;
    optional_file_patch.insert(optional_file_patch.find("builds="), "when='extra'\nclient_languages=['zh-CN']\n");
    const auto optional_file = parse_manifest(file_head + payload + optional_file_patch
        + "\n[[options]]\nkey='extra'\ntype='bool'\ndefault=false\nlabel={en='Extra'}\n", "optional-file", error);
    CHECK(optional_file && optional_file->options.size() == 1);
    CHECK(rejects(file_head + payload + "client_languages=['zh-CN','ZH-cn']\n" + file_patch, "client_languages"));
    CHECK(rejects(file_head + payload + "client_languages=['zh-CN']\nexclude_client_languages=['ZH-cn']\n" + file_patch, "exclude_client_languages"));
    CHECK(rejects(file_head + payload + "exclude_client_languages=['../bad']\n" + file_patch, "exclude_client_languages"));
    auto excluded_patch = file_patch;
    excluded_patch.insert(excluded_patch.find("builds="), "client_languages=['ko']\nexclude_client_languages=['KO']\n");
    CHECK(rejects(file_head + payload + excluded_patch, "exclude_client_languages"));
    CHECK(rejects(head + "diagnostics = \"yes\"\n", "diagnostics"));
    CHECK(rejects(head + "minimum_launcher_version = \"1.0\"\n", "minimum_launcher_version"));
    CHECK(rejects("schema = 2\n", "schema"));
    CHECK(rejects(head + "typo = 1\n", "typo"));
    CHECK(rejects("schema = 1\n[plugin]\nid = \"T X\"\nversion = \"1.0.0\"\ngame = \"g\"\nkind = \"patch\"\nname = { en = \"X\" }\n", "plugin.id"));
    CHECK(rejects("schema = 1\n[plugin]\nid = \"t.x\"\nversion = \"1.0\"\ngame = \"g\"\nkind = \"patch\"\nname = { en = \"X\" }\n", "plugin.version"));
    CHECK(rejects("schema = 1\n[plugin]\nid = \"t.x\"\nversion = \"1.0.0\"\ngame = \"g\"\nkind = \"patch\"\nname = { ko = \"X\" }\n", "'en'"));
    CHECK(rejects(head + "[[patches]]\nid = \"p\"\nlocate = { rva = 0x10 }\nexpect = \"AA\"\nwrite = \"BB\"\n", "rva"));
    CHECK(rejects(head + "[[patches]]\nid = \"p\"\nlocate = { pattern = \"AA\", rva = 1 }\nexpect = \"AA\"\nwrite = \"BB\"\n", "exactly one"));
    CHECK(rejects(head + "[[patches]]\nid = \"p\"\nlocate = { pattern = \"AA\" }\nexpect = \"AA BB\"\nwrite = \"CC\"\n", "same length"));
    CHECK(rejects(head + "[[patches]]\nid = \"p\"\nlocate = { pattern = \"AA\" }\nexpect = \"AA\"\nwrite = \"??\"\n", "wildcards"));
    CHECK(rejects(head + "[[patches]]\nid = \"p\"\nwhen = \"nope\"\nlocate = { pattern = \"AA\" }\nexpect = \"AA\"\nwrite = \"BB\"\n", "when"));
    CHECK(rejects(head + "required_on = [\"kr-1\"]\n", "required_on"));
    CHECK(rejects(head + "warn_on = [\"kr-1\"]\n", "warn_on"));
    CHECK(rejects(head + "require_latest = true\n", "require_latest"));
    CHECK(rejects(head + "[[options]]\nkey = \"n\"\ntype = \"int\"\ndefault = 9\nmin = 0\nmax = 5\nlabel = { en = \"N\" }\n", "default"));
    CHECK(rejects(head + "[native]\nlibrary = \"x.dll\"\n", "native"));
    const auto native = head.substr(0, head.find("kind =")) + "kind = \"native\"\nname = { en = \"Native\" }\n[native]\nlibrary = \"test.dll\"\n";
    const auto grants = native + "[companion.messages]\n\"test.echo\" = 1\n";
    auto consumer = parse_manifest(grants, "consumer", error);
    CHECK(consumer && consumer->companion_messages.at("test.echo") == 1);
    CHECK(rejects(head + "[companion.messages]\n\"test.echo\" = 1\n", "companion"));
    CHECK(rejects(native + "[companion.messages]\n\"test.echo\" = 0\n", "companion.messages"));
    CHECK(rejects(native + "[companion.messages]\n\"test.echo\" = 1.0\n", "companion.messages"));
    CHECK(rejects(native + "[companion.messages]\n\"test.echo\" = true\n", "companion.messages"));
    CHECK(rejects(native + "[companion.messages]\n\"Test.echo\" = 1\n", "companion.messages"));
    CHECK(rejects(native + "[companion]\nendpoint = \"wss://example.test\"\n", "companion.endpoint"));
}

void session_tests() {
    std::string error;
    auto s = parse_session(R"(
schema = 1
[session]
game = "bubblefighter"
build = "kr-1256"
branch = "kr"
messenger = "forward"
[session.fingerprint]
pe_timestamp = 0x69FD90C8
[login]
account = "player"
[endpoint]
host = "127.0.0.1"
port = 7100
[[plugins]]
id = "test.sample"
version = "1.2.0"
path = "C:/plugins/test.sample"
[plugins.options]
extra = true
)", "session", error);
    CHECK(s.has_value());
    if (s) {
        CHECK(s->build == "kr-1256" && s->pe_timestamp == 0x69FD90C8u && s->endpoint_port == 7100 && s->plugins.size() == 1);
        CHECK(std::get<bool>(s->plugins[0].options.at("extra")));
    }
    auto overlay = parse_session(R"(
schema = 2
[session]
game = "bubblefighter"
build = "kr-812"
branch = "kr"
instance = "kr-812"
messenger = "forward"
log = "C:/logs/fthook.log"
log_max_kib = 1024
launch_id = "0123456789abcdef0123456789abcdef"
startup_timeout_ms = 120000
launcher_pid = 1234
[overlay]
pipe = "FantasyTale.OverlayBroker.0123456789abcdef0123456789abcdef"
)", "overlay", error);
    CHECK(overlay.has_value());
    if (overlay) CHECK(overlay->overlay_pipe == L"FantasyTale.OverlayBroker.0123456789abcdef0123456789abcdef");
    CHECK(!parse_session("schema = 2\n[session]\ngame = \"g\"\nbuild = \"b\"\n[overlay]\npipe = \"bad\\npipe\"\n", "bad-overlay", error));
    CHECK(!parse_session("schema = 1\n[session]\ngame = \"g\"\n", "s", error));
    CHECK(!parse_session("schema = 1\n[session]\ngame = \"g\"\nbuild = \"b\"\nextra = 1\n", "s", error));
    CHECK(!parse_session("schema = 1\n[session]\ngame = \"g\"\nbuild = \"b\"\n[endpoint]\nhost = \"h\"\nport = 70000\n", "s", error));
    const std::string grant = "schema=2\n[session]\ngame=\"g\"\nbuild=\"b\"\nlaunch_id=\"0123456789abcdef0123456789abcdef\"\nlauncher_pid=123\n"
        "[login]\nticket=\"fixture-private-ticket\"\ncredential_plugin=\"test.auth\"\n[endpoint]\nhost=\"127.0.0.1\"\nport=7100\n"
        "[[plugins]]\nid=\"test.auth\"\nversion=\"1.0.0\"\npath=\"C:/plugins/test.auth\"\n";
    auto granted = parse_session(grant, "authentication", error);
    CHECK(granted && granted->credential_plugin == "test.auth");
    for (const auto& removed : {std::string("ticket=\"fixture-private-ticket\"\n"), std::string("[endpoint]\nhost=\"127.0.0.1\"\nport=7100\n"),
        std::string("[[plugins]]\nid=\"test.auth\"\nversion=\"1.0.0\"\npath=\"C:/plugins/test.auth\"\n")}) {
        auto invalid = grant;
        invalid.erase(invalid.find(removed), removed.size());
        CHECK(!parse_session(invalid, "authentication", error));
        CHECK(error.find("login.credential_plugin") != std::string::npos && error.find("fixture-private-ticket") == std::string::npos);
    }

    auto name_grant = grant;
    name_grant.replace(name_grant.find("ticket=\"fixture-private-ticket\""), std::string("ticket=\"fixture-private-ticket\"").size(),
        "mode=\"name\"\naccount=\"Player_1\"");
    auto named = parse_session(name_grant, "name authentication", error);
    CHECK(named && named->name_login && named->account == "Player_1" && named->ticket.empty());
    for (const auto& account : {std::string(""), std::string("bad name"), std::string(33, 'a')}) {
        auto invalid = name_grant;
        invalid.replace(invalid.find("Player_1"), 8, account);
        CHECK(!parse_session(invalid, "invalid name", error));
    }
    auto mixed = name_grant;
    mixed.insert(mixed.find("credential_plugin="), "ticket=\"private-ticket\"\n");
    CHECK(!parse_session(mixed, "mixed credentials", error));
    auto old_name = name_grant; old_name.replace(0, 8, "schema=1");
    CHECK(!parse_session(old_name, "old name session", error));

    // [messenger]: optional, schema 2 only; url and ticket are required together and validated; errors never repeat the ticket.
    const std::string base = "schema=2\n[session]\ngame=\"g\"\nbuild=\"b\"\nlaunch_id=\"0123456789abcdef0123456789abcdef\"\nlauncher_pid=123\n";
    const std::string ticket = "v1.eyJzdWIiOiJhIn0.c2lnbmF0dXJl";
    auto messenger = parse_session(base + "[messenger]\nurl=\"http://127.0.0.1:8091\"\nticket=\"" + ticket + "\"\n", "messenger", error);
    CHECK(messenger && messenger->messenger_transport == "http" && messenger->messenger_url == "http://127.0.0.1:8091"
        && messenger->messenger_ticket == ticket);
    auto explicit_http = parse_session(base + "[messenger]\ntransport=\"http\"\nurl=\"https://h:8091/m\"\nticket=\"" + ticket + "\"\n", "messenger", error);
    CHECK(explicit_http && explicit_http->messenger_transport == "http");
    auto without = parse_session(base, "no-messenger", error);
    CHECK(without && without->messenger_transport.empty() && without->messenger_url.empty() && without->messenger_ticket.empty());
    CHECK(!parse_session("schema=1\n[session]\ngame=\"g\"\nbuild=\"b\"\n[messenger]\nurl=\"http://h/\"\nticket=\"" + ticket + "\"\n", "schema-1", error));
    const std::string invalid[] = {
        "url=\"http://127.0.0.1:8091\"\n",
        "ticket=\"" + ticket + "\"\n",
        "url=\"ftp://h/\"\nticket=\"" + ticket + "\"\n",
        "url=\"http://user@h/\"\nticket=\"" + ticket + "\"\n",
        "url=\"http://h/?q=1\"\nticket=\"" + ticket + "\"\n",
        "url=\"http://\"\nticket=\"" + ticket + "\"\n",
        "url=\"http://h/\"\nticket=\"has space\"\n",
        "url=\"http://h/\"\nticket=\"" + ticket + "\"\nextra=1\n",
        "transport=\"gateway\"\nurl=\"http://h/\"\nticket=\"" + ticket + "\"\n",
        "transport=\"tcp\"\nurl=\"http://h/\"\nticket=\"" + ticket + "\"\n",
    };
    for (const auto& bad : invalid) {
        CHECK(!parse_session(base + "[messenger]\n" + bad, "bad-messenger", error));
        CHECK(error.find(ticket) == std::string::npos);
    }
}

// A synthetic executable "image": a committed page made executable after filling.
struct FakeImage {
    uint8_t* page = nullptr;
    explicit FakeImage(const uint8_t* bytes, size_t size) {
        page = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
        std::memset(page, 0xCC, 4096);
        std::memcpy(page + 0x100, bytes, size);
        DWORD old = 0;
        VirtualProtect(page, 4096, PAGE_EXECUTE_READ, &old);
    }
    ~FakeImage() { VirtualFree(page, 0, MEM_RELEASE); }
    ImageInfo info() const {
        ImageInfo i;
        i.base = reinterpret_cast<uintptr_t>(page);
        i.image_size = 4096;
        i.code.push_back({i.base, 4096});
        return i;
    }
};

void font_weight_tests(const std::wstring& folder) {
    std::string error;
    const auto manifest = load_manifest(folder, error);
    CHECK(manifest.has_value());
    if (!manifest) return;
    const std::array<std::string, 3> builds{"kr-812", "id-400", "id-1809"};
    const std::array<std::string, 5> languages{"zh-CN", "ko", "en", "id", ""};
    LocatorRegistry locators;
    register_builtin_locators(locators);
    for (const auto& build : builds) {
        for (const auto& language : languages) {
            for (const bool force_bold : {false, true}) {
                DWORD language_group = language == "zh-CN" ? 2u : 0u;
                // kr-812 sub_AE3E50 keeps the 700-weight override only for native Chinese group 2.
                std::array<uint8_t, 19> code{0xB8, 0xBC, 0x02, 0x00, 0x00,
                    0x83, 0x3D, 0, 0, 0, 0, 0x02, 0x75, 0x03, 0x89, 0x41, 0x3C, 0xC3, 0xCC};
                const auto group_address = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&language_group));
                std::memcpy(code.data() + 7, &group_address, sizeof(group_address));
                FakeImage image(code.data(), code.size());
                SessionPlugin entry{manifest->id, manifest->version, folder, false, {{"force_bold", force_bold}}};
                PluginReport report;
                auto prepared = prepare(*manifest, entry, build, build.starts_with("kr-") ? "kr" : "id", report, language);
                CHECK(prepared.has_value() == (force_bold || language == "zh-CN"));
                if (prepared) {
                    std::vector<uintptr_t> sites;
                    CHECK(probe(*prepared, image.info(), locators, sites, error) == Probe::Ready);
                    CHECK(apply(*prepared, sites, error) == Outcome::Installed);
                } else CHECK(report.outcome == Outcome::SkippedOption);
                std::array<DWORD, 16> style{};
                style[15] = 400;
                using StoreWeight = void(__fastcall*)(void*);
                reinterpret_cast<StoreWeight>(image.page + 0x100)(style.data());
                CHECK(style[15] == (force_bold || language_group == 2 ? 700u : 400u));
            }
        }
    }
}

void modern_font_weight_tests(const std::wstring& folder) {
    std::string error;
    const auto manifest = load_manifest(folder, error);
    CHECK(manifest.has_value());
    if (!manifest) return;
    LocatorRegistry locators;
    register_builtin_locators(locators);
    for (const auto& language : {"zh-CN", "ZH-cn", "zh-TW", "ko", "en"}) {
        for (const bool force_bold : {false, true}) {
            for (const DWORD input_weight : {400u, 700u}) {
                // kr-1256 sub_1052710 normalizes EBX and writes style +56, with no language gate.
                const uint8_t code[]{0x53, 0x56, 0x57, 0x8B, 0xF1, 0x8B, 0xDA,
                    0xB8, 0x90, 0x01, 0, 0, 0x81, 0xFB, 0xBC, 0x02, 0, 0, 0x0F, 0x45, 0xD8,
                    0xB0, 0x01, 0x5F, 0x89, 0x5E, 0x38, 0x5E, 0x5B, 0xC3};
                FakeImage image(code, sizeof(code));
                SessionPlugin entry{manifest->id, manifest->version, folder, false, {{"force_bold", force_bold}}};
                PluginReport report;
                auto prepared = prepare(*manifest, entry, "kr-1256", "kr", report, language);
                const bool chinese = std::string(language).starts_with("zh") || std::string(language).starts_with("ZH");
                CHECK(prepared.has_value() == (chinese || force_bold));
                if (prepared) {
                    CHECK(prepared->patches.size() == 1);
                    std::vector<uintptr_t> sites;
                    CHECK(probe(*prepared, image.info(), locators, sites, error) == Probe::Ready);
                    CHECK(apply(*prepared, sites, error) == Outcome::Installed);
                }
                std::array<DWORD, 16> style{};
                using StoreWeight = void(__fastcall*)(void*, DWORD);
                reinterpret_cast<StoreWeight>(image.page + 0x100)(style.data(), input_weight);
                CHECK(style[14] == (chinese || force_bold ? 700u : input_weight));
            }
        }
    }
}

void engine_tests() {
    std::string error;
    const uint8_t code[] = {0x11, 0x22, 0x33, 0x44, 0x75, 0x03, 0x00};
    FakeImage image(code, sizeof(code));
    LocatorRegistry locators;
    register_builtin_locators(locators);

    SessionPlugin entry{"test.sample", "1.2.0", L"", true, {}};
    PluginReport report;
    auto prepared = prepare(*parse_manifest(kGoodManifest, "good", error), entry, "kr-1256", "kr", report);
    CHECK(prepared && prepared->required && prepared->patches.size() == 1);
    auto language_manifest = *parse_manifest(kGoodManifest, "language", error);
    for (auto& patch : language_manifest.patches) patch.client_languages = {"zh-CN", "zh-TW"};
    PluginReport language_report;
    CHECK(prepare(language_manifest, entry, "kr-1256", "kr", language_report, "zh-CN").has_value());
    CHECK(prepare(language_manifest, entry, "kr-1256", "kr", language_report, "ZH-cn").has_value());
    CHECK(prepare(language_manifest, entry, "kr-1256", "kr", language_report, "zh-TW").has_value());
    CHECK(!prepare(language_manifest, entry, "kr-1256", "kr", language_report, "ko").has_value());
    CHECK(language_report.outcome == Outcome::SkippedOption);
    CHECK(!prepare(language_manifest, entry, "kr-1256", "kr", language_report).has_value());
    for (auto& patch : language_manifest.patches) { patch.client_languages.clear(); patch.exclude_client_languages = {"zh-CN"}; }
    CHECK(prepare(language_manifest, entry, "kr-1256", "kr", language_report, "ko").has_value());
    CHECK(!prepare(language_manifest, entry, "kr-1256", "kr", language_report, "ZH-cn").has_value());
    auto optional_entry = entry;
    optional_entry.required = false;
    PluginReport optional_report;
    auto optional = prepare(*parse_manifest(kGoodManifest, "good", error), optional_entry, "kr-1256", "kr", optional_report);
    CHECK(optional && !optional->required);

    PluginReport excluded;
    CHECK(!prepare(*parse_manifest(kGoodManifest, "good", error), entry, "cn-279", "cn", excluded) && excluded.outcome == Outcome::UnavailableBuild);
    PluginReport wrong_version;
    SessionPlugin old_entry{"test.sample", "1.0.0", L"", false, {}};
    CHECK(!prepare(*parse_manifest(kGoodManifest, "good", error), old_entry, "kr-1256", "kr", wrong_version));
    PluginReport bad_option;
    SessionPlugin opt_entry{"test.sample", "1.2.0", L"", false, {{"extra", int64_t{1}}}};
    CHECK(!prepare(*parse_manifest(kGoodManifest, "good", error), opt_entry, "kr-1256", "kr", bad_option));

    if (prepared) {
        std::vector<uintptr_t> sites;
        std::string detail;
        CHECK(probe(*prepared, image.info(), locators, sites, detail) == Probe::Ready);
        CHECK(sites.size() == 1 && sites[0] == reinterpret_cast<uintptr_t>(image.page) + 0x104);
        CHECK(apply(*prepared, sites, detail) == Outcome::Installed);
        CHECK(image.page[0x104] == 0x90 && image.page[0x105] == 0x90);
        CHECK(apply(*prepared, sites, detail) == Outcome::AlreadyPresent);
    }

    // Unexpected bytes at the located site: nothing is written.
    const uint8_t other[] = {0x11, 0x22, 0x33, 0x44, 0x75, 0x05};
    FakeImage mismatch(other, sizeof(other));
    const char* strict = R"(
schema = 1
[plugin]
id = "test.strict"
version = "1.0.0"
game = "bubblefighter"
kind = "patch"
name = { en = "Strict" }
[[patches]]
id = "gate"
locate = { pattern = "11 22 33 44", offset = 4 }
expect = "75 03"
write = "90 90"
)";
    PluginReport r2;
    SessionPlugin strict_entry{"test.strict", "1.0.0", L"", false, {}};
    auto strict_plugin = prepare(*parse_manifest(strict, "strict", error), strict_entry, "id-400", "id", r2);
    CHECK(strict_plugin.has_value());
    if (strict_plugin) {
        std::vector<uintptr_t> sites;
        std::string detail;
        CHECK(probe(*strict_plugin, mismatch.info(), locators, sites, detail) == Probe::Mismatch);
        CHECK(mismatch.page[0x104] == 0x75 && mismatch.page[0x105] == 0x05);
        // Pattern absent: not yet (the image may still be unpacking).
        const uint8_t none[] = {0x00};
        FakeImage empty(none, 1);
        CHECK(probe(*strict_plugin, empty.info(), locators, sites, detail) == Probe::NotYet);
    }
}

void write_tests() {
    const uint8_t code[] = {0x32, 0xC0};
    FakeImage image(code, sizeof(code));
    std::string error;
    const uintptr_t site = reinterpret_cast<uintptr_t>(image.page) + 0x100;
    CHECK(write_code(site, *parse_pattern("33 C0", error), {0xB0, 0x01}) == WriteResult::Mismatch);
    CHECK(image.page[0x100] == 0x32);
    CHECK(write_code(site, *parse_pattern("32 C0", error), {0xB0, 0x01}) == WriteResult::Installed);
    CHECK(image.page[0x100] == 0xB0 && image.page[0x101] == 0x01);
    // Spanning two pages is refused.
    CHECK(write_code(reinterpret_cast<uintptr_t>(image.page) + 4095, *parse_pattern("CC ??", error), {0x90, 0x90}) == WriteResult::Failed);
    // Non-executable memory is refused.
    uint8_t data[2] = {0x32, 0xC0};
    CHECK(write_code(reinterpret_cast<uintptr_t>(data), *parse_pattern("32 C0", error), {0xB0, 0x01}) == WriteResult::Failed);
}

DWORD WINAPI import_probe() { return 4242; }

__declspec(noinline) DWORD call_process_id_slot(uintptr_t slot) {
    // Optimized callers may cache a dllimport pointer across the write being tested.
    const auto address = *reinterpret_cast<const volatile uintptr_t*>(slot);
    return reinterpret_cast<DWORD(WINAPI*)()>(address)();
}

void import_write_tests() {
    const auto actual_pid = GetCurrentProcessId();
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    const auto iat = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT];
    const auto original = reinterpret_cast<uintptr_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "GetCurrentProcessId"));
    uintptr_t slot = 0;
    for (DWORD offset = 0; offset + 4 <= iat.Size; offset += 4)
        if (*reinterpret_cast<const uintptr_t*>(base + iat.VirtualAddress + offset) == original) { slot = base + iat.VirtualAddress + offset; break; }
    CHECK(slot != 0);
    if (!slot) return;
    const auto bytes = [](uintptr_t value) { std::vector<uint8_t> result(4); std::memcpy(result.data(), &value, 4); return result; };
    const auto changed = bytes(reinterpret_cast<uintptr_t>(&import_probe));
    Pattern expected{bytes(original), {255, 255, 255, 255}};
    MEMORY_BASIC_INFORMATION before{}, after{};
    CHECK(VirtualQuery(reinterpret_cast<void*>(slot), &before, sizeof(before)) != 0);
    CHECK(!is_import_slot(reinterpret_cast<uintptr_t>(&actual_pid)));
    CHECK(write_import(slot + 1, expected, changed) == WriteResult::Failed);
    CHECK(write_import(slot, Pattern{expected.bytes, {}}, changed) == WriteResult::Failed);
    CHECK(write_import(slot, Pattern{expected.bytes, {255, 0, 255, 255}}, changed) == WriteResult::Failed);
    CHECK(write_import(slot, Pattern{{0, 0, 0, 0}, expected.mask}, changed) == WriteResult::Mismatch);
    CHECK(call_process_id_slot(slot) == actual_pid);
    CHECK(write_import(slot, expected, changed) == WriteResult::Installed);
    CHECK(call_process_id_slot(slot) == 4242);
    CHECK(write_import(slot, expected, changed) == WriteResult::AlreadyPresent);
    CHECK(write_import(slot, Pattern{changed, expected.mask}, expected.bytes) == WriteResult::Installed);
    CHECK(call_process_id_slot(slot) == actual_pid);
    CHECK(VirtualQuery(reinterpret_cast<void*>(slot), &after, sizeof(after)) != 0 && after.Protect == before.Protect);
}

std::wstring exe_folder() {
    wchar_t path[MAX_PATH]{};
    const DWORD n = GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring folder(path, n);
    return folder.substr(0, folder.find_last_of(L'\\'));
}

// Only hook packages use this parser; launcher-owned packages must never enter a hook session.
void shipped_plugin_tests(const std::wstring& root) {
    WIN32_FIND_DATAW item{};
    HANDLE find = FindFirstFileW((root + L"\\*").c_str(), &item);
    CHECK(find != INVALID_HANDLE_VALUE);
    int loaded = 0;
    do {
        if (!(item.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || item.cFileName[0] == L'.') continue;
        std::string error;
        std::string text;
        CHECK(read_text_file(root + L"\\" + item.cFileName + L"\\plugin.toml", 1024 * 1024, text));
        const auto metadata = toml::parse(text);
        CHECK(metadata);
          if (metadata && ((metadata["plugin"]["kind"].value<std::string>() == "files" && !metadata.table().contains("patches"))
            || metadata["plugin"]["kind"].value<std::string>() == "overlay"
            || metadata["plugin"]["kind"].value<std::string>() == "service")) {
            // Launcher-only packages must be rejected, rather than silently accepted by the native parser.
            CHECK(!load_manifest(root + L"\\" + item.cFileName, error));
            continue;
        }
        const auto manifest = load_manifest(root + L"\\" + item.cFileName, error);
        if (!manifest) std::printf("  %s\n", error.c_str());
        CHECK(manifest.has_value() && manifest->id == metadata["plugin"]["id"].value<std::string>()
            && manifest->version == metadata["plugin"]["version"].value<std::string>());
        ++loaded;
    } while (FindNextFileW(find, &item));
    FindClose(find);
    CHECK(loaded > 0);
}

void gpk_stub_tests() {
    const auto library = LoadLibraryW((exe_folder() + L"\\gpk_stub.dll").c_str());
    CHECK(library != nullptr);
    if (!library) return;
    using Create = void*(__stdcall*)(void*, void*);
    using Release = int(__stdcall*)();
    using Accept = int(__stdcall*)(void*, void*, void*);
    using Stop = int(__stdcall*)(void*);
    const auto create = reinterpret_cast<Create>(GetProcAddress(library, MAKEINTRESOURCEA(1)));
    const auto release = reinterpret_cast<Release>(GetProcAddress(library, MAKEINTRESOURCEA(2)));
    CHECK(create && release);
    if (create && release) {
        void* object = create(nullptr, nullptr);
        CHECK(object != nullptr);
        if (object) {
            const auto methods = *static_cast<void***>(object);
            for (int i = 0; i < 3; ++i) CHECK(reinterpret_cast<Accept>(methods[i])(object, nullptr, nullptr) == 1);
            CHECK(reinterpret_cast<Stop>(methods[3])(object) == 0);
        }
        CHECK(release() == 0);
    }
    CHECK(FreeLibrary(library));
}

} // namespace

int companion_probe(char** argv) {
    const auto pid = static_cast<uint32_t>(std::strtoul(argv[3], nullptr, 10));
    CompanionClient client(widen(argv[2]), pid, "test.native", {{"test.echo", 1}});
    std::string buffer(131073, '\0');
    const auto invoke = [&](const char* method, const char* type, const char* data, uint32_t timeout = 0u) {
        return client.invoke(method, type, 1, data, timeout, nullptr, buffer.data(), static_cast<uint32_t>(buffer.size()));
    };
    if (invoke("state", nullptr, nullptr) != FTHOOK_OK || std::strstr(buffer.c_str(), "connected") == nullptr) return 61;
    if (invoke("subscribe", "test.echo", nullptr) != FTHOOK_OK) return 62;
    if (invoke("send", "forbidden.type", "{}") != FTHOOK_INVALID) return 63;
    if (invoke("send", "test.echo", "{\"text\":\"中文 한국어\"}") != FTHOOK_OK || std::strstr(buffer.c_str(), "accepted") == nullptr) return 64;
    if (invoke("request", "test.echo", "{\"request\":true}", 1000) != FTHOOK_OK || std::strstr(buffer.c_str(), "completed") == nullptr
        || std::strstr(buffer.c_str(), "\"acknowledged\":true") == nullptr) return 65;
    const auto deadline = GetTickCount64() + 5000;
    int32_t polled = FTHOOK_NOT_FOUND;
    while (polled == FTHOOK_NOT_FOUND && GetTickCount64() < deadline) { polled = invoke("poll", nullptr, nullptr); if (polled == FTHOOK_NOT_FOUND) Sleep(10); }
    if (polled != FTHOOK_OK || std::strstr(buffer.c_str(), "test.echo") == nullptr || std::strstr(buffer.c_str(), "通知") == nullptr) return 66;
    if (invoke("unsubscribe", "test.echo", nullptr) != FTHOOK_OK) return 67;
    std::puts("companion-probe: passed");
    return 0;
}

int main(int argc, char** argv) {
    launch_metadata_tests();
    if (argc == 4 && std::strcmp(argv[1], "--companion-probe") == 0) return companion_probe(argv);
    status_replacement_tests();
    pattern_tests();
    semver_tests();
    manifest_tests();
    session_tests();
    engine_tests();
    font_weight_tests(exe_folder() + L"\\..\\..\\..\\store-plugins\\BubbleFighter\\bubblefighter.fantasy.font-support-cn");
    modern_font_weight_tests(exe_folder() + L"\\..\\..\\..\\store-plugins\\BubbleFighter\\bubblefighter.fantasy.font-support-cn");
    write_tests();
    import_write_tests();
    gpk_stub_tests();
    // Default: the repository plugins, seen from native/out/<Configuration>/.
    shipped_plugin_tests(argc > 1 ? widen(argv[1]) : exe_folder() + L"\\..\\..\\..\\games\\BubbleFighter\\plugins");
    return finish("fthook_tests");
}

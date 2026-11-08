#include <fthook/plugin_abi.h>
#include <windows.h>
#include <toml.hpp>
#include "check.h"
#include <algorithm>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <vector>

namespace {
struct Host {
    int legacy_option = -1;
    unsigned locators = 0, patches = 0, option_queries = 0;
    std::vector<std::string> messages;
    std::mutex lock;
};
struct ImageFixture {
    uintptr_t base{}, slot{}, handler{};
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS32 nt{};
    std::vector<uint8_t> signature;
};
// Set before activation and clear only after cleanup has joined the reader.
const ImageFixture* image_fixture = nullptr;
int32_t FTHOOK_CALL option(void* context, const char* key, fthook_option* value) {
    auto& state = *static_cast<Host*>(context);
    ++state.option_queries;
    if (std::string_view(key) != "all_modes" || state.legacy_option < 0) return FTHOOK_NOT_FOUND;
    *value = {FTHOOK_OPTION_BOOL, state.legacy_option, nullptr};
    return FTHOOK_OK;
}
int32_t FTHOOK_CALL read(uintptr_t address, void* output, uint32_t length) {
    if (!image_fixture) return FTHOOK_NOT_FOUND;
    const auto& image = *image_fixture;
    const void* source = nullptr;
    if (address == image.base && length == sizeof(image.dos)) source = &image.dos;
    if (address == image.base + static_cast<uint32_t>(image.dos.e_lfanew) && length == sizeof(image.nt)) source = &image.nt;
    if (address == image.slot && length == sizeof(image.handler)) source = &image.handler;
    if (address == image.handler && length == image.signature.size()) source = image.signature.data();
    if (!source) return FTHOOK_NOT_FOUND;
    std::memcpy(output, source, length);
    return FTHOOK_OK;
}

void FTHOOK_CALL log(void* context, const char* message) {
    auto& state = *static_cast<Host*>(context);
    std::lock_guard guard(state.lock);
    state.messages.emplace_back(message);
}
int32_t FTHOOK_CALL locator(void* context, const char*, fthook_locator, void*) {
    auto& state = *static_cast<Host*>(context);
    ++state.locators;
    return FTHOOK_OK;
}
int32_t FTHOOK_CALL patch(void* context, uintptr_t, const uint8_t*, const uint8_t*, const uint8_t*, uint32_t) {
    ++static_cast<Host*>(context)->patches;
    return FTHOOK_OK;
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) {
        std::printf("Usage: mid_match_exit_activation_tests <unpacked-package-directory>\n");
        return 2;
    }
    const auto directory = std::filesystem::absolute(argv[1]);
    const auto encoded = directory.u8string();
    const std::string folder(encoded.begin(), encoded.end());
    std::ifstream manifest(directory / "plugin.toml");
    const std::string manifest_text((std::istreambuf_iterator<char>(manifest)), {});
    const auto manifest_parsed = toml::parse(manifest_text);
    CHECK(manifest_parsed);
    if (!manifest_parsed) return finish("Mid-match exit activation");
    const auto version = manifest_parsed["plugin"]["version"].value_or(std::string{});
    CHECK(!version.empty());
    std::ifstream profile(directory / "exit.toml");
    const std::string text((std::istreambuf_iterator<char>(profile)), {});
    const auto parsed = toml::parse(text);
    CHECK(parsed);
    if (!parsed) return finish("Mid-match exit activation");
    const auto* clients = parsed["clients"].as_table();
    CHECK(clients != nullptr);
    if (!clients) return finish("Mid-match exit activation");
    const auto module = LoadLibraryExW((directory / "mid_match_exit.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(module != nullptr);
    if (!module) return finish("Mid-match exit activation");
    const auto address = GetProcAddress(module, "fthook_plugin_main");
    fthook_plugin_entry entry = nullptr;
    static_assert(sizeof(entry) == sizeof(address));
    std::memcpy(&entry, &address, sizeof(entry));
    CHECK(entry != nullptr);
    if (!entry) { FreeLibrary(module); return finish("Mid-match exit activation"); }
    for (const auto& [key, row] : *clients) {
        const std::string build(key.str());
        // Old selections may retain either boolean; fresh installs have no option at all.
        for (const int legacy_option : {-1, 0, 1}) {
            const auto* client = row.as_table();
            CHECK(client != nullptr);
            const bool companion = client && (*client)["route"].value<std::string>() == "companion";
            ImageFixture image;
            if (companion) {
                image.base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
                image.slot = image.base + static_cast<uintptr_t>((*client)["key_slot_rva"].value_or(int64_t{}));
                image.handler = image.base + static_cast<uintptr_t>((*client)["handler_rva"].value_or(int64_t{}));
                image.dos.e_magic = IMAGE_DOS_SIGNATURE; image.dos.e_lfanew = 0x100;
                image.nt.Signature = IMAGE_NT_SIGNATURE; image.nt.FileHeader.Machine = IMAGE_FILE_MACHINE_I386;
                image.nt.FileHeader.TimeDateStamp = static_cast<DWORD>((*client)["pe_timestamp"].value_or(int64_t{}));
                image.nt.OptionalHeader.SizeOfImage = static_cast<DWORD>((*client)["image_sizes"][legacy_option == 1 ? 1 : 0].value_or(int64_t{}));
                const auto signature = (*client)["handler_signature"].value_or(std::string{});
                for (size_t offset = 0; offset + 1 < signature.size(); offset += 3)
                    image.signature.push_back(static_cast<uint8_t>(std::stoul(signature.substr(offset, 2), nullptr, 16)));
                image_fixture = &image;
            }
            Host state; state.legacy_option = legacy_option;
            fthook_host host{};
            host.abi_version = FTHOOK_ABI_VERSION; host.struct_size = sizeof(host); host.context = &state;
            host.game = "bubblefighter"; host.build = build.c_str(); host.plugin_directory = folder.c_str();
            host.option = option; host.read = read; host.log = log;
            // Fresh activation must also work without any memory-mutation API.
            host.register_locator = legacy_option < 0 ? nullptr : locator;
            host.stage_patch = legacy_option < 0 ? nullptr : patch;
            // The runtime initializes this descriptor before invoking every native entry.
            fthook_plugin plugin{FTHOOK_ABI_VERSION, sizeof(fthook_plugin), nullptr, nullptr};
            const auto result = entry(&host, &plugin);
            std::printf("%s legacy_option=%d status=%d patches=%u locators=%u\n", build.c_str(), legacy_option,
                result, state.patches, state.locators);
            if (companion && result == FTHOOK_OK) {
                bool ready = false;
                const auto deadline = GetTickCount64() + 2000;
                do {
                    {
                        std::lock_guard guard(state.lock);
                        ready = std::any_of(state.messages.begin(), state.messages.end(), [](const auto& message) {
                            return message.find("client image, keyboard pointer and handler signature verified") != std::string::npos;
                        });
                    }
                    if (!ready) Sleep(10);
                } while (!ready && GetTickCount64() < deadline);
                CHECK(ready);
            }
            if (plugin.cleanup) CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
            image_fixture = nullptr;
            for (const auto& message : state.messages) std::printf("  %s\n", message.c_str());
            CHECK(result == FTHOOK_OK);
            CHECK(state.option_queries == 0);
            CHECK(plugin.abi_version == FTHOOK_ABI_VERSION && plugin.struct_size == sizeof(plugin));
            CHECK(!state.messages.empty());
            const auto expected = "version=" + version + " build=" + build + " modes=all";
            CHECK(std::any_of(state.messages.begin(), state.messages.end(), [&](const auto& message) {
                return message.find(expected) != std::string::npos;
            }));

            CHECK(state.patches == 0);
            CHECK(state.locators == 0);
            CHECK((plugin.cleanup != nullptr) == companion);
        }
    }
    for (const bool missing_profile : {false, true}) {
        Host state;
        const auto missing = folder + "/missing-profile-test";
        fthook_host host{};
        host.abi_version = FTHOOK_ABI_VERSION; host.struct_size = sizeof(host); host.context = &state;
        host.game = "bubblefighter"; host.build = missing_profile ? "cn-717" : "unknown-test-build";
        host.plugin_directory = missing_profile ? missing.c_str() : folder.c_str();
        host.option = option; host.read = read; host.log = log;
        host.register_locator = locator; host.stage_patch = patch;
        fthook_plugin plugin{FTHOOK_ABI_VERSION, sizeof(fthook_plugin), nullptr, nullptr};
        CHECK(entry(&host, &plugin) == FTHOOK_INVALID);
        CHECK(state.patches == 0 && state.locators == 0 && plugin.cleanup == nullptr);
        CHECK(state.messages.size() == 2);
        CHECK(!state.messages.empty() && state.messages.back().find(
            missing_profile ? "cannot be opened" : "does not declare") != std::string::npos);
    }
    FreeLibrary(module);
    return finish("Mid-match exit activation");
}

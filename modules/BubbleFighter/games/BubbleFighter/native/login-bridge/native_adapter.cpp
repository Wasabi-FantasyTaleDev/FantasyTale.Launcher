#include "native_adapter.h"
#define TOML_EXCEPTIONS 0
#define TOML_ENABLE_FORMATTERS 0
#include "../../../../native/third_party/tomlplusplus/toml.hpp"
#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <fstream>
#include <limits>
#include <map>
#include <mutex>
#include <optional>
#include <shared_mutex>
#include <string>
#include <vector>

namespace {
struct Function { uint32_t rva{}; std::vector<BYTE> signature; };
struct Passport {
    uint32_t timestamp{}, connection{}, offset{};
    std::vector<uint32_t> sizes;
    bool cow{}, span{};
    Function assign, construct, destroy;
    uint32_t pending{}, application{}, selected{}, connected{}, complete{};
    Function region, feature;
    bool feature_fastcall{};
    std::vector<int> automatic_regions;
    uint32_t automatic_feature{}, abort_feature{}, abort_flag{};
};
struct Labels {
    std::string build;
    uint32_t system{}, modal{}, chooser{}, chooser_secondary{}, buttons_begin{}, buttons_end{};
    uint32_t centers_begin{}, centers_end{}, identity{}, button{}, button_secondary{}, caption{}, poll{};
    Function assign, construct, destroy;
    Function draw, text, region;
    uint32_t renderer{}, draw_slot{}, table_entries{};
};
struct Drawing {
    std::vector<uintptr_t> table;
    uintptr_t original_table{}, original_draw{}, text{}, region{}, renderer{}, destroy{};
    uint32_t caption{};
    std::atomic<DWORD> thread{};
    std::atomic<bool> enabled{};
};
Drawing g_drawing;
thread_local bool g_caption_drawing{};
uintptr_t g_base{};
uint32_t g_image_size{};
std::optional<Passport> g_passport;
std::optional<Labels> g_labels;
std::map<int, std::wstring> g_names;
std::shared_mutex g_names_mutex;
HWND g_label_window{};
UINT_PTR g_label_timer{};
bool g_label_dispatching{};
uintptr_t g_seeded_connection{};
bool g_resume_attempted{};

bool read(uintptr_t address, void* output, size_t count) {
    SIZE_T copied = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), output, count, &copied)
        && copied == count;
}
template<class T> bool read(uintptr_t address, T& value) { return read(address, &value, sizeof(value)); }
bool executable(uintptr_t address, size_t count) {
    MEMORY_BASIC_INFORMATION region{};
    if (!VirtualQuery(reinterpret_cast<void*>(address), &region, sizeof(region)) || region.State != MEM_COMMIT
        || region.Protect & (PAGE_GUARD | PAGE_NOACCESS)
        || !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) return false;
    const auto offset = address - reinterpret_cast<uintptr_t>(region.BaseAddress);
    return offset < region.RegionSize && count <= region.RegionSize - offset;
}
uint32_t number(const toml::node_view<const toml::node>& node) {
    const auto value = node.value<int64_t>();
    if (!value || *value <= 0 || *value > UINT32_MAX) throw std::runtime_error("Invalid native adapter integer");
    return static_cast<uint32_t>(*value);
}
std::vector<uint32_t> sizes(const toml::table& row) {
    const auto* array = row["image_sizes"].as_array();
    if (!array || array->empty() || array->size() > 8) throw std::runtime_error("Invalid image sizes");
    std::vector<uint32_t> result;
    for (const auto& value : *array) result.push_back(number(toml::node_view<const toml::node>(&value)));
    return result;
}
std::vector<BYTE> hex(const std::string& value) {
    if (value.empty() || value.size() % 2 || value.size() > 128) throw std::runtime_error("Invalid native signature");
    auto digit = [](char c) -> unsigned {
        if (c >= '0' && c <= '9') return static_cast<unsigned>(c - '0');
        if (c >= 'a' && c <= 'f') return static_cast<unsigned>(c - 'a' + 10);
        throw std::runtime_error("Invalid signature digit");
    };
    std::vector<BYTE> result;
    for (size_t i = 0; i < value.size(); i += 2) result.push_back(static_cast<BYTE>(digit(value[i]) * 16 + digit(value[i + 1])));
    return result;
}
Function function(const toml::table& row, const char* rva, const char* signature) {
    const auto text = row[signature].value<std::string>();
    if (!text) throw std::runtime_error("Missing function signature");
    return {number(row[rva]), hex(*text)};
}
bool matches(uintptr_t address, const BYTE* expected, size_t size) {
    std::array<BYTE, 64> actual{};
    return size <= actual.size() && read(address, actual.data(), size) && !std::memcmp(actual.data(), expected, size);
}
bool checked(const Function& function, uint32_t image_size) {
    return function.rva < image_size && function.signature.size() <= image_size - function.rva
        && executable(g_base + function.rva, function.signature.size())
        && matches(g_base + function.rva, function.signature.data(), function.signature.size());
}
toml::table load(const std::filesystem::path& file, size_t maximum) {
    std::ifstream input(file, std::ios::binary | std::ios::ate);
    if (!input || input.tellg() <= 0 || input.tellg() > static_cast<std::streamoff>(maximum))
        throw std::runtime_error("Missing or oversized native adapter document");
    std::string content(static_cast<size_t>(input.tellg()), '\0');
    input.seekg(0);
    if (!input.read(content.data(), static_cast<std::streamsize>(content.size()))) throw std::runtime_error("Cannot read adapter document");
    auto result = toml::parse(content);
    SecureZeroMemory(content.data(), content.size());
    if (!result) throw std::runtime_error("Invalid native adapter document");
    return std::move(result).table();
}
bool header(uintptr_t base, IMAGE_NT_HEADERS32& pe) {
    IMAGE_DOS_HEADER dos{};
    return read(base, dos) && dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0 && dos.e_lfanew < 0x100000
        && read(base + static_cast<uintptr_t>(dos.e_lfanew), pe) && pe.Signature == IMAGE_NT_SIGNATURE
        && pe.FileHeader.Machine == IMAGE_FILE_MACHINE_I386 && pe.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC
        && pe.OptionalHeader.ImageBase == 0x400000 && base == 0x400000;
}
bool preserve_native_names() {
    OutputDebugStringA("Chinese authentication: optional Center title override is unavailable; preserving native titles\n");
    g_labels.reset(); g_names.clear();
    return true;
}
void names(const std::filesystem::path& folder, const std::string& build) {
    // Module metadata contains display names only; authentication remains exclusively in the provider.
    const auto file = folder / L"session.toml";
    if (!std::filesystem::exists(file)) return;
    const auto session = load(file, 256 * 1024);
    if (session["module_data"]["capability"].value_or<std::string>("") != "bubblefighter.cn-center-names.v2") return;
    const auto launch_id = session["session"]["launch_id"].value_or<std::string>("");
    if (session["schema"].value_or<int64_t>(0) != 2 || session["session"]["build"].value_or<std::string>("") != build
        || launch_id.size() != 32 || launch_id.find_first_not_of("0123456789abcdefABCDEF") != std::string::npos)
        throw std::runtime_error("Center names require a matching managed launch");
    const auto payload = session["module_data"]["payload"].value<std::string>();
    if (!payload || payload->size() > 32768) throw std::runtime_error("Invalid Center name envelope");
    auto parsed = toml::parse(*payload);
    if (!parsed || parsed["schema"].value_or<int64_t>(0) != 1) throw std::runtime_error("Invalid Center name schema");
    const auto* rows = parsed["centers"].as_array();
    if (!rows) throw std::runtime_error("Missing or excessive Center names");
    for (const auto& value : *rows) {
        const auto* row = value.as_table();
        if (!row) throw std::runtime_error("Invalid Center name row");
        const auto id = number((*row)["id"]);
        const auto text = (*row)["name"].value_or<std::string>("");
        if (text.empty()) continue;
        if (id > INT_MAX || text.size() > 512 || std::any_of(text.begin(), text.end(), [](unsigned char c) { return c < 32 || c == 127; }))
            throw std::runtime_error("Invalid Center name");
        const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
        if (count < 1 || count > 128) throw std::runtime_error("Invalid Center name encoding");
        std::wstring wide(static_cast<size_t>(count), L'\0');
        if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), wide.data(), count) != count)
            throw std::runtime_error("Invalid Center name encoding");
        // cn-279 sub_8868F0 passes the caption as sub_5375B0's format without additional arguments.
        std::wstring literal;
        literal.reserve(wide.size()*2);
        for (const auto character:wide) {
            literal.push_back(character);
            if (character == L'%') literal.push_back(L'%');
        }
        if (!g_names.emplace(static_cast<int>(id),std::move(literal)).second) throw std::runtime_error("Duplicate Center name identity");
    }
}
bool writable(uintptr_t address, size_t count) {
    MEMORY_BASIC_INFORMATION region{};
    if (!VirtualQuery(reinterpret_cast<void*>(address), &region, sizeof(region)) || region.State != MEM_COMMIT
        || region.Protect & (PAGE_GUARD | PAGE_NOACCESS)
        || !(region.Protect & (PAGE_READWRITE | PAGE_WRITECOPY))) return false;
    const auto offset = address - reinterpret_cast<uintptr_t>(region.BaseAddress);
    return offset < region.RegionSize && count <= region.RegionSize - offset;
}
bool native_object(uintptr_t object, uint32_t primary, uint32_t secondary) {
    uintptr_t first = 0, second = 0;
    return object && object < UINTPTR_MAX - 0x10000 && read(object, first) && read(object + 4, second)
        && first == g_base + primary && second == g_base + secondary;
}
bool server_button(uintptr_t object, const Labels& labels) {
    uintptr_t first=0,second=0;
    return object && object < UINTPTR_MAX-0x10000 && read(object,first) && read(object+4,second)
        && second == g_base+labels.button_secondary
        && (first == g_base+labels.button || (!g_drawing.table.empty()
            && g_drawing.original_table == g_base+labels.button
            && first == reinterpret_cast<uintptr_t>(g_drawing.table.data()+1)));
}
int __fastcall draw_caption(void* object, void*) {
    using Draw=int (__thiscall*)(void*);
    using Text=int (__cdecl*)(uintptr_t,int,int,int,int,int,float,const wchar_t*,...);
    using Region=unsigned char (__cdecl*)(int);
    const auto original=reinterpret_cast<Draw>(g_drawing.original_draw);
    if (!g_drawing.enabled.load(std::memory_order_acquire) || g_caption_drawing
        || GetCurrentThreadId()!=g_drawing.thread.load()) return original(object);
    alignas(uintptr_t) std::array<BYTE,340> view{};
    if (!read(reinterpret_cast<uintptr_t>(object),view.data(),view.size())) return original(object);
    auto* field=reinterpret_cast<const wchar_t**>(view.data()+g_drawing.caption);
    const auto caption=*field;
    uintptr_t renderer=0;
    if (!read(g_drawing.renderer,renderer) || !renderer) return original(object);
    // cn-279 sub_8868F0 reads only sprite +332 and caption +336; a borrowed view leaves the live object untouched.
    *field=L"";
    auto held=reinterpret_cast<uintptr_t>(caption);
    InterlockedIncrement(reinterpret_cast<volatile LONG*>(held-12));
    g_caption_drawing=true;
    __try {
        original(view.data());
        const auto x=reinterpret_cast<Region>(g_drawing.region)(2)?16:12;
        // cn-279 sub_540FF0 flag 4 retains vertical centering without the fixed-width horizontal offset.
        return reinterpret_cast<Text>(g_drawing.text)(renderer,x,15,70,20,4,1.0f,caption);
    } __finally {
        __try { reinterpret_cast<void(__thiscall*)(void*)>(g_drawing.destroy)(&held); }
        __finally { g_caption_drawing=false; }
    }
}
bool prepare_drawing(const Labels& labels) {
    if (!checked(labels.draw,g_image_size) || !checked(labels.text,g_image_size) || !checked(labels.region,g_image_size)) return false;
    if (!g_drawing.table.empty()) {
        if (g_drawing.original_table!=g_base+labels.button || g_drawing.original_draw!=g_base+labels.draw.rva
            || g_drawing.text!=g_base+labels.text.rva || g_drawing.region!=g_base+labels.region.rva
            || g_drawing.renderer!=g_base+labels.renderer || g_drawing.destroy!=g_base+labels.destroy.rva
            || g_drawing.caption!=labels.caption || g_drawing.table.size()!=labels.table_entries+1
            || g_drawing.table[labels.draw_slot+1]!=reinterpret_cast<uintptr_t>(&draw_caption)) return false;
    } else {
        std::vector<uintptr_t> table(labels.table_entries+1);
        if (!read(g_base+labels.button-4,table.data(),table.size()*sizeof(uintptr_t))
            || table[labels.draw_slot+1]!=g_base+labels.draw.rva) return false;
        HMODULE pinned=nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&draw_caption),&pinned)) return false;
        table[labels.draw_slot+1]=reinterpret_cast<uintptr_t>(&draw_caption);
        g_drawing.original_table=g_base+labels.button;
        g_drawing.original_draw=g_base+labels.draw.rva;
        g_drawing.text=g_base+labels.text.rva; g_drawing.region=g_base+labels.region.rva;
        g_drawing.renderer=g_base+labels.renderer; g_drawing.caption=labels.caption;
        g_drawing.destroy=g_base+labels.destroy.rva;
        g_drawing.table=std::move(table);
    }
    g_drawing.thread=GetCurrentThreadId();
    g_drawing.enabled.store(true,std::memory_order_release);
    return true;
}
bool vector_range(uintptr_t begin, uintptr_t end, uint32_t& count) {
    if (!begin || end < begin || begin % 4 || end % 4 || end - begin > 256 * 1024) return false;
    count = static_cast<uint32_t>((end - begin) / sizeof(uintptr_t));
    return count != 0;
}
bool caption_equals(uintptr_t field, const std::wstring& text) {
    uintptr_t data = 0; uint32_t length = 0, capacity = 0;
    if (!read(field, data) || data < 20 || data > UINTPTR_MAX - 1026
        || !read(data - 20, length) || !read(data - 16, capacity)
        || length > 512 || capacity < length) return false;
    if (length != text.size()) return false;
    std::array<wchar_t, 257> actual{};
    return read(data, actual.data(), (text.size() + 1) * sizeof(wchar_t))
        && !std::wmemcmp(actual.data(), text.c_str(), text.size() + 1);
}
bool assign_caption(uintptr_t field, const Labels* labels, const wchar_t* text) {
    using Construct = void* (__thiscall*)(void*, const wchar_t*);
    using Assign = void (__thiscall*)(void*, const void*);
    using Destroy = void (__thiscall*)(void*);
    uintptr_t temporary{};
    __try {
        reinterpret_cast<Construct>(g_base + labels->construct.rva)(&temporary, text);
        __try { reinterpret_cast<Assign>(g_base + labels->assign.rva)(reinterpret_cast<void*>(field), &temporary); }
        __finally { reinterpret_cast<Destroy>(g_base + labels->destroy.rva)(&temporary); }
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void update_captions() {
    const auto& labels = *g_labels;
    uintptr_t system = 0, dialog = 0, begin = 0, end = 0, centers = 0, centers_end = 0;
    uint32_t count = 0, center_count = 0;
    // cn-279 sub_4A78A0 publishes the complete chooser as UiSystem.modal before pumping messages.
    if (!read(g_base + labels.system, system) || !system || system > UINTPTR_MAX - 0x10000
        || !read(system + labels.modal, dialog) || !native_object(dialog, labels.chooser, labels.chooser_secondary)
        || !read(dialog + labels.buttons_begin, begin) || !read(dialog + labels.buttons_end, end)
        || !read(g_base + labels.centers_begin, centers) || !read(g_base + labels.centers_end, centers_end)
        || !vector_range(begin, end, count) || !vector_range(centers, centers_end, center_count)
        || count != center_count) return;
    struct Row { uintptr_t field{},button{}; const std::wstring* text{}; };
    std::vector<Row> rows(count);
    for (uint32_t i = 0; i < count; ++i) {
        uintptr_t button = 0, center = 0, data = 0; uint32_t identity = 0, length = 0, capacity = 0;
        // cn-279 sub_64C8C0 builds both vectors in the same order; +368 is selection and is never written.
        if (!read(begin + i * 4, button) || !server_button(button, labels)
            || !read(centers + i * 4, center) || !center || center > UINTPTR_MAX - 0x10000
            || !read(center + labels.identity, identity)) return;
        const auto found = identity <= INT_MAX ? g_names.find(static_cast<int>(identity)) : g_names.end();
        if (found == g_names.end()) continue;
        const auto field = button + labels.caption;
        if (!writable(field, 4) || !read(field, data) || data < 20 || data > UINTPTR_MAX - 1026
            || !read(data - 20, length) || !read(data - 16, capacity) || length > 512 || capacity < length) return;
        if (!writable(button,4)) return;
        rows[i] = {field, button, &found->second};
    }
    if (!checked(labels.assign, g_image_size) || !checked(labels.construct, g_image_size)
        || !checked(labels.destroy, g_image_size)) return;
    if (!prepare_drawing(labels)) return;
    // cn-279 sub_8868F0 reads this COW caption on every draw; no texture invalidation is needed.
    for (const auto& row : rows)
        if (row.text) {
            if (!caption_equals(row.field, *row.text) && !assign_caption(row.field, &labels, row.text->c_str())) return;
            InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(row.button),
                static_cast<LONG>(reinterpret_cast<uintptr_t>(g_drawing.table.data()+1)),static_cast<LONG>(g_base+labels.button));
        }
}
void CALLBACK caption_timer(HWND window, UINT, UINT_PTR timer, DWORD) {
    std::unique_lock lock(g_names_mutex, std::try_to_lock);
    if (!lock.owns_lock() || g_caption_drawing || !g_labels || g_names.empty() || window != g_label_window
        || timer != g_label_timer || g_label_dispatching) return;
    g_label_dispatching = true;
    struct Guard { ~Guard() { g_label_dispatching = false; } } guard;
    try { update_captions(); } catch (...) {
        OutputDebugStringA("Chinese authentication: optional Center caption refresh failed\n");
    }
}
BOOL CALLBACK label_window(HWND window, LPARAM argument) {
    if (GetWindow(window, GW_OWNER) || !IsWindowVisible(window)
        || GetWindowLongPtrW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) return TRUE;
    *reinterpret_cast<HWND*>(argument) = window;
    return FALSE;
}
void start_caption_timer() {
    if (!g_labels || g_names.empty() || g_label_timer) return;
    HWND window = nullptr;
    EnumThreadWindows(GetCurrentThreadId(), label_window, reinterpret_cast<LPARAM>(&window));
    if (!window) return;
    HMODULE pinned = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&caption_timer), &pinned)) return;
    g_label_window = window;
    g_label_timer = SetTimer(window, reinterpret_cast<UINT_PTR>(&g_label_timer), g_labels->poll, caption_timer);
    if (g_label_timer) OutputDebugStringA("Chinese authentication: Center captions use native UI-thread strings and button-local drawing; original image unchanged\n");
}
struct ModernString { union { const wchar_t* pointer; wchar_t local[8]; }; uint32_t length; uint32_t capacity; };
static_assert(sizeof(ModernString) == 24);
bool assign_ticket(uintptr_t field, const Passport* profile, const wchar_t* ticket) {
    // SEH is isolated from C++ owners. cn-324 uses native COW allocation; later builds use a 24-byte native string.
    using Construct = void* (__thiscall*)(void*, const wchar_t*);
    using AssignCow = void* (__thiscall*)(void*, const void*);
    using Destroy = void (__thiscall*)(void*);
    using AssignModern = void* (__thiscall*)(void*, const void*, uint32_t, uint32_t);
    using AssignSpan = void* (__thiscall*)(void*, const wchar_t*, uint32_t);
    uintptr_t temporary{};
    __try {
        if (profile->cow) {
            reinterpret_cast<Construct>(g_base + profile->construct.rva)(&temporary, ticket);
            __try { reinterpret_cast<AssignCow>(g_base + profile->assign.rva)(reinterpret_cast<void*>(field), &temporary); }
            __finally { reinterpret_cast<Destroy>(g_base + profile->destroy.rva)(&temporary); }
        } else if (profile->span) {
            // cn-774 sub_44E030 consumes a character span, not a native string object.
            reinterpret_cast<AssignSpan>(g_base + profile->assign.rva)(reinterpret_cast<void*>(field), ticket, 75);
        } else {
            const ModernString source{ticket, 75, 75};
            reinterpret_cast<AssignModern>(g_base + profile->assign.rva)(reinterpret_cast<void*>(field), &source, 0, UINT32_MAX);
        }
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
enum class NativeEntry { Unavailable, Skipped, Submitted };
unsigned char native_feature(const Passport* profile, int value) {
    if (profile->feature_fastcall)
        return reinterpret_cast<unsigned char (__fastcall*)(int)>(g_base + profile->feature.rva)(value);
    return reinterpret_cast<unsigned char (__cdecl*)(int)>(g_base + profile->feature.rva)(value);
}
NativeEntry native_entry_state(const Passport* profile) {
    using Region = BOOL (__cdecl*)(int);
    __try {
        const auto region = reinterpret_cast<Region>(g_base + profile->region.rva);
        BYTE abort = 0;
        if (profile->abort_flag && (!read(g_base + profile->abort_flag, abort)
            || (abort && native_feature(profile, static_cast<int>(profile->abort_feature))))) return NativeEntry::Unavailable;
        for (size_t i = 0; i < profile->automatic_regions.size(); ++i)
            if (region(profile->automatic_regions[i])) return NativeEntry::Submitted;
        return native_feature(profile, static_cast<int>(profile->automatic_feature)) ? NativeEntry::Submitted : NativeEntry::Skipped;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return NativeEntry::Unavailable; }
}
}

bool cn_native::prepare(uintptr_t image, const std::filesystem::path& folder) {
    std::unique_lock lock(g_names_mutex);
    if (g_label_timer) return false;
    g_passport.reset(); g_labels.reset(); g_names.clear(); g_base = image;
    g_seeded_connection = 0; g_resume_attempted = false;
    IMAGE_NT_HEADERS32 pe{};
    if (!header(image, pe)) return false;
    g_image_size = pe.OptionalHeader.SizeOfImage;
    const auto document = load(folder / L"native-passports.toml", 64 * 1024);
    if (document["schema"].value_or<int64_t>(0) != 1) return false;
    const auto* profiles = document["profiles"].as_array();
    if (!profiles || profiles->size() > 64) return false;
    for (const auto& value : *profiles) {
        const auto* row = value.as_table();
        if (!row) return false;
        Passport profile;
        profile.timestamp = number((*row)["pe_timestamp"]);
        if (profile.timestamp != pe.FileHeader.TimeDateStamp) continue;
        if (g_passport) return false;
        profile.sizes = sizes(*row);
        const auto kind = (*row)["string_kind"].value_or<std::string>("");
        if (kind != "cow" && kind != "modern" && kind != "span") return false;
        profile.cow = kind == "cow";
        profile.span = kind == "span";
        profile.connection = number((*row)["connection_rva"]);
        profile.offset = number((*row)["passport_offset"]);
        profile.assign = function(*row, "assign_rva", "assign_signature");
        if (const auto* resume = (*row)["resume"].as_table()) {
            profile.pending = number((*resume)["pending_connection_rva"]);
            profile.application = number((*resume)["application_rva"]);
            profile.selected = number((*resume)["selected_offset"]);
            profile.connected = number((*resume)["connected_offset"]);
            profile.complete = number((*resume)["complete_offset"]);
            if (profile.pending > pe.OptionalHeader.SizeOfImage - sizeof(uintptr_t)
                || profile.application > pe.OptionalHeader.SizeOfImage - sizeof(uintptr_t)
                || profile.selected > 0x10000 || profile.connected > 0x10000 || profile.complete > 0x10000) return false;
            if ((*resume)["region_rva"] || (*resume)["feature_rva"]) {
                profile.feature = function(*resume, "feature_rva", "feature_signature");
                profile.automatic_feature = number((*resume)["automatic_feature"]);
                if ((*resume)["feature_calling_convention"]) {
                    const auto convention = (*resume)["feature_calling_convention"].value<std::string>();
                    if (!convention || (*convention != "cdecl" && *convention != "fastcall")) return false;
                    profile.feature_fastcall = *convention == "fastcall";
                }
                if (profile.automatic_feature > INT_MAX || !checked(profile.feature, pe.OptionalHeader.SizeOfImage)) return false;
                if ((*resume)["region_rva"]) {
                    profile.region = function(*resume, "region_rva", "region_signature");
                    const auto* regions = (*resume)["automatic_regions"].as_array();
                    if (!regions || regions->empty() || regions->size() > 8
                        || !checked(profile.region, pe.OptionalHeader.SizeOfImage)) return false;
                    for (const auto& item : *regions) {
                        const auto region_id = item.value<int64_t>();
                        if (!region_id || *region_id < 0 || *region_id > INT_MAX) return false;
                        profile.automatic_regions.push_back(static_cast<int>(*region_id));
                    }
                } else if ((*resume)["automatic_regions"] || (*resume)["region_signature"]) return false;
                if ((*resume)["abort_feature"] || (*resume)["abort_flag_rva"]) {
                    profile.abort_feature = number((*resume)["abort_feature"]);
                    profile.abort_flag = number((*resume)["abort_flag_rva"]);
                    if (profile.abort_feature > INT_MAX || profile.abort_flag >= pe.OptionalHeader.SizeOfImage) return false;
                }
            }
        }
        if (profile.cow) {
            profile.construct = function(*row, "constructor_rva", "constructor_signature");
            profile.destroy = function(*row, "destructor_rva", "destructor_signature");
        }
        const auto size = pe.OptionalHeader.SizeOfImage;
        if (std::find(profile.sizes.begin(), profile.sizes.end(), size) == profile.sizes.end()
            || profile.connection > size || sizeof(uintptr_t) > size - profile.connection || profile.offset > 0x10000
            || !checked(profile.assign, size) || (profile.cow && (!checked(profile.construct, size) || !checked(profile.destroy, size)))) return false;
        g_passport = std::move(profile);
    }
    try {
        const auto* row = document["labels"].as_table();
        if (!row || number((*row)["pe_timestamp"]) != pe.FileHeader.TimeDateStamp) return true;
        const auto allowed = sizes(*row);
        if (std::find(allowed.begin(), allowed.end(), g_image_size) == allowed.end()) return preserve_native_names();
        Labels labels;
        labels.build = (*row)["build"].value_or<std::string>("");
        if (labels.build.empty()) return preserve_native_names();
        names(folder, labels.build);
        if (g_names.empty()) return true;
        auto rva = [&](const char* key) {
            const auto value = number((*row)[key]);
            if (value > g_image_size - 8) throw std::runtime_error("Invalid caption image address");
            return value;
        };
        auto offset = [&](const char* key) {
            const auto value = number((*row)[key]);
            if (value > 0x10000) throw std::runtime_error("Invalid caption object offset");
            return value;
        };
        labels.system = rva("system_rva"); labels.modal = offset("modal_offset");
        labels.chooser = rva("chooser_vtable_rva"); labels.chooser_secondary = rva("chooser_secondary_vtable_rva");
        labels.buttons_begin = offset("buttons_begin_offset"); labels.buttons_end = offset("buttons_end_offset");
        labels.centers_begin = rva("centers_begin_rva"); labels.centers_end = rva("centers_end_rva");
        labels.identity = offset("identity_offset"); labels.button = rva("button_vtable_rva");
        labels.button_secondary = rva("button_secondary_vtable_rva"); labels.caption = offset("caption_offset");
        // cn-279 sub_8868F0 reads exactly sprite +332 and caption +336 from the borrowed draw view.
        if (labels.caption != 336) return preserve_native_names();
        labels.poll = number((*row)["poll_ms"]);
        if (labels.poll < 16 || labels.poll > 1000) return preserve_native_names();
        labels.assign = function(*row, "assign_rva", "assign_signature");
        labels.construct = function(*row, "constructor_rva", "constructor_signature");
        labels.destroy = function(*row, "destructor_rva", "destructor_signature");
        labels.draw=function(*row,"draw_rva","draw_signature");
        labels.text=function(*row,"text_rva","text_signature");
        labels.region=function(*row,"region_rva","region_signature");
        labels.renderer=rva("renderer_rva");
        labels.draw_slot=number((*row)["draw_slot"]); labels.table_entries=number((*row)["table_entries"]);
        if (labels.table_entries!=48 || labels.draw_slot!=3
            || labels.button_secondary<=labels.button || (labels.button_secondary-labels.button)%4
            || (labels.button_secondary-labels.button)/4-1!=labels.table_entries) return preserve_native_names();
        if (!checked(labels.assign, g_image_size) || !checked(labels.construct, g_image_size)
            || !checked(labels.destroy, g_image_size)) return preserve_native_names();
        g_labels = std::move(labels);
        start_caption_timer();
        return true;
    } catch (...) {
        // Optional display metadata never disables the authenticated provider.
    }
    return preserve_native_names();
}

bool cn_native::adapts_passport() noexcept {
    return g_passport.has_value();
}

bool cn_native::seed_passport(const char* marked_ticket) {
    start_caption_timer();
    if (!g_passport) return true;
    if (!checked(g_passport->assign, g_image_size)
        || (g_passport->cow && (!checked(g_passport->construct, g_image_size) || !checked(g_passport->destroy, g_image_size)))) return false;
    struct Buffers {
        std::array<char, 76> narrow{};
        std::array<wchar_t, 76> wide{}, actual{};
        ~Buffers() { SecureZeroMemory(this, sizeof(*this)); }
    } buffers;
    auto& narrow = buffers.narrow;
    const auto address = reinterpret_cast<uintptr_t>(marked_ticket);
    for (size_t i = 0; i < 11; ++i) {
        if (!read(address + i, narrow[i])) return false;
        // The host may explicitly authorize a non-ticket room login. Preserve that provider flow without seeding a passport.
        if (!narrow[i] || narrow[i] != "bff-ticket:"[i]) return true;
    }
    if (!read(address + 11, narrow.data() + 11, narrow.size() - 11) || narrow.back()) return false;
    for (size_t i = 11; i < 75; ++i)
        if (!((narrow[i] >= '0' && narrow[i] <= '9') || (narrow[i] >= 'a' && narrow[i] <= 'f'))) return false;
    auto& wide = buffers.wide;
    for (size_t i = 0; i < 75; ++i) wide[i] = static_cast<wchar_t>(narrow[i]);
    uintptr_t connection = 0;
    bool success = read(g_base + g_passport->connection, connection) && connection
        && connection <= UINTPTR_MAX - g_passport->offset - sizeof(ModernString);
    uintptr_t field = success ? connection + g_passport->offset : 0;
    std::array<BYTE, sizeof(ModernString)> previous{};
    success = success && read(field, previous.data(), g_passport->cow ? sizeof(uintptr_t) : sizeof(ModernString))
        && assign_ticket(field, &*g_passport, wide.data());
    uintptr_t data = 0;
    uint32_t length = 0, capacity = 0;
    if (success && g_passport->cow)
        success = read(field, data) && data >= 20 && read(data - 20, length);
    else if (success) {
        success = read(field + 16, length) && read(field + 20, capacity);
        if (success) success = capacity < 8 ? (data = field, true) : read(field, data);
    }
    auto& actual = buffers.actual;
    success = success && length == 75 && read(data, actual.data(), sizeof(actual)) && actual == wide;
    if (success) g_seeded_connection = connection;
    return success;
}

cn_native::Continuation cn_native::begin_connection_resume(const char* marked_ticket) noexcept {
    if (!g_passport || !g_passport->application || !g_seeded_connection || g_resume_attempted) return Continuation::None;
    uintptr_t connection = 0, pending = 0, application = 0, data = 0;
    BYTE selected = 0, connected = 0, complete = 0;
    uint32_t length = UINT32_MAX;
    const auto& profile = *g_passport;
    if (!read(g_base + profile.connection, connection) || !connection || connection == g_seeded_connection
        || connection > UINTPTR_MAX - 0x10000
        || !read(g_base + profile.pending, pending) || pending
        || !read(g_base + profile.application, application) || !application || application > UINTPTR_MAX - 0x10000
        || !read(application + profile.selected, selected) || selected != 1
        || !read(connection + profile.connected, connected) || connected != 1
        || !read(connection + profile.complete, complete) || complete != 0) return Continuation::None;
    // cn-279 sub_6FA250 and cn-324 sub_6FEEE0 construct empty replacement passports.
    // The new stage's verified provider request resumes the selected connection through its original callback.
    if (profile.cow) {
        if (!read(connection + profile.offset, data) || data < 20 || !read(data - 20, length)) return Continuation::None;
    } else if (!read(connection + profile.offset + 16, length)) return Continuation::None;
    auto result = Continuation::Deliver;
    if (length != 0) {
        if (!marked_ticket || length != 75 || !profile.feature.rva
            || (profile.region.rva && !checked(profile.region, g_image_size))
            || !checked(profile.feature, g_image_size)) return Continuation::None;
        const auto entry = native_entry_state(&profile);
        if (entry == NativeEntry::Unavailable) return Continuation::None;
        if (!profile.cow) {
            uint32_t capacity = 0;
            if (!read(connection + profile.offset + 20, capacity) || capacity < 75
                || !read(connection + profile.offset, data)) return Continuation::None;
        }
        struct Comparison {
            std::array<wchar_t,76> text{};
            ~Comparison() { SecureZeroMemory(text.data(), sizeof(text)); }
        } comparison;
        if (!read(data, comparison.text.data(), sizeof(comparison.text))) return Continuation::None;
        for (size_t i = 0; i < comparison.text.size(); ++i)
            if (comparison.text[i] != static_cast<unsigned char>(marked_ticket[i])) return Continuation::None;
        if (entry == NativeEntry::Submitted) result = Continuation::AlreadySubmitted;
    }
    // Consume the attempt before invoking client callbacks, including a failed assignment or reentrant request.
    g_resume_attempted = true;
    return result;
}

bool cn_native::shutdown() {
    std::unique_lock lock(g_names_mutex);
    g_drawing.enabled.store(false,std::memory_order_release);
    if (g_label_timer && !KillTimer(g_label_window, g_label_timer) && IsWindow(g_label_window)) return false;
    g_label_timer = 0; g_label_window = nullptr;
    g_passport.reset(); g_labels.reset(); g_names.clear();
    g_seeded_connection = 0; g_resume_attempted = false;
    return true;
}

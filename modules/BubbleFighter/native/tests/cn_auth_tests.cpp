#include <windows.h>
#include "check.h"
#include <array>
#include <cstdarg>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <string>
#include <vector>

namespace {
BYTE fixture_storage[0x1100000]{};
constexpr uintptr_t base = 0x400000;
BYTE* image{};
std::array<BYTE, 2600> connection{};
uintptr_t target{};
int assign_count{}, construct_count{}, destroy_count{}, callback_count{}, failure_count{};
int native_region = 9, native_feature = 0;
BOOL __cdecl region_predicate(int value) { return value == native_region; }
unsigned char __cdecl feature_predicate(int value) { return static_cast<unsigned char>(value == native_feature); }
unsigned char __fastcall fast_feature_predicate(int value) {
    CHECK(value == 1502);
    if (native_feature == -1) RaiseException(0xe0420001, 0, 0, nullptr);
    return static_cast<unsigned char>(value == native_feature);
}
using Method = void(*)();
struct Object { const Method* methods; };
using Callback = int (__stdcall*)(int,const void*,int,int);
using Initialize = int (__stdcall*)(unsigned,const void*);
using GetModule = int (__stdcall*)(const void*,void**);
using Terminal = int (__cdecl*)();
struct Modern { union { const wchar_t* pointer; wchar_t local[8]; }; uint32_t length, capacity; };
std::array<wchar_t, 76> owned{};
struct CowHeader { uint32_t length, capacity; LONG refs; uint32_t unknown12, unknown16; };
void release(uintptr_t pointer) {
    auto* header = reinterpret_cast<CowHeader*>(pointer - 20);
    if (!--header->refs) CHECK(HeapFree(GetProcessHeap(),0,header));
}
void* __fastcall modern_assign(void* field, void*, const Modern* source, unsigned start, unsigned count) {
    CHECK(reinterpret_cast<uintptr_t>(field) == target);
    CHECK(start == 0 && count == UINT32_MAX && source->length == 75);
    CHECK(!std::wmemcmp(source->pointer, L"bff-ticket:",11));
    std::copy_n(source->pointer,76,owned.data());
    *static_cast<Modern*>(field) = Modern{owned.data(),75,75};
    ++assign_count; return field;
}
void* __fastcall span_assign(void* field, void*, const wchar_t* text, unsigned count) {
    CHECK(count == 75);
    const Modern source{text, count, count};
    return modern_assign(field, nullptr, &source, 0, UINT32_MAX);
}
void* __fastcall cow_construct(void* field, void*, const wchar_t* text) {
    auto* header = static_cast<CowHeader*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,20+152));
    CHECK(header != nullptr);
    header->length = header->capacity = 75; header->refs = 1;
    auto* data = reinterpret_cast<wchar_t*>(header+1); std::copy_n(text,76,data);
    *static_cast<uintptr_t*>(field) = reinterpret_cast<uintptr_t>(data);
    ++construct_count; return field;
}
void* __fastcall cow_assign(void* field, void*, const uintptr_t* source) {
    CHECK(reinterpret_cast<uintptr_t>(field) == target);
    auto* header = reinterpret_cast<CowHeader*>(*source - 20); ++header->refs;
    *static_cast<uintptr_t*>(field) = *source; ++assign_count; return field;
}
void __fastcall cow_destroy(void* field, void*) { release(*static_cast<uintptr_t*>(field)); ++destroy_count; }
int __stdcall receive(int status,const void* data,int a,int b) {
    if (status) { CHECK(data == nullptr); ++failure_count; return 19; }
    CHECK(a == 45 && b == 67 && data != nullptr);
    CHECK(assign_count == 1);
    ++callback_count; return 18;
}
int __stdcall receive_native(int status,const void* data,int a,int b) {
    CHECK(status == 0 && data != nullptr && a == 45 && b == 67);
    CHECK(assign_count == 0);
    ++callback_count; return 18;
}
std::string jump(uint32_t rva, void* function) {
    std::array<BYTE,7> bytes{0xB8,0,0,0,0,0xFF,0xE0};
    const auto pointer = reinterpret_cast<uintptr_t>(function);
    std::memcpy(bytes.data()+1,&pointer,4); std::memcpy(image+rva,bytes.data(),bytes.size());
    std::ostringstream text;
    for (auto value:bytes) text << std::hex << std::setfill('0') << std::setw(2) << static_cast<unsigned>(value);
    return text.str();
}
int caption_assignments{};
DWORD caption_thread{};
int draw_calls{},text_calls{};
std::wstring drawn_caption;
void pump_caption_timer();
void* live_button{};
std::array<BYTE,340> live_snapshot{};
unsigned draw_behavior{};
bool alternate_region{};
int call_draw_caught(int(__thiscall* draw)(void*),void* object) {
    __try { draw(object); return 0; }
    __except(GetExceptionCode()==0xe0420001?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH) { return 1; }
}
int __fastcall native_caption_draw(void* object,void*) {
    if (live_button) CHECK(!std::memcmp(live_button,live_snapshot.data(),live_snapshot.size()));
    ++draw_calls;
    if (object==live_button) return 23;
    CHECK(**reinterpret_cast<const wchar_t**>(static_cast<BYTE*>(object)+336) == 0);
    if (draw_behavior==1) {
        pump_caption_timer();
        const auto* table=*static_cast<uintptr_t**>(live_button);
        CHECK(reinterpret_cast<int(__thiscall*)(void*)>(table[3])(live_button) == 23);
    }
    if (draw_behavior==2) RaiseException(0xe0420001,0,0,nullptr);
    return 23;
}
int __cdecl native_caption_text(uintptr_t renderer,int x,int y,int width,int height,int flags,float alpha,const wchar_t* text,...) {
    CHECK(renderer == 0x1234 && x == (alternate_region?16:12) && y == 15 && width == 70 && height == 20 && flags == 4 && alpha == 1.0f);
    if (live_button) CHECK(!std::memcmp(live_button,live_snapshot.data(),live_snapshot.size()));
    if (draw_behavior==3) RaiseException(0xe0420001,0,0,nullptr);
    drawn_caption=text; ++text_calls; return 29;
}
unsigned char __cdecl caption_region(int region) { CHECK(region == 2); return static_cast<unsigned char>(alternate_region); }
void* __fastcall caption_construct(void* field, void*, const wchar_t* text) {
    const auto length=std::wcslen(text);
    auto* header=static_cast<CowHeader*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,20+(length+1)*2));
    CHECK(header != nullptr);
    header->length=header->capacity=static_cast<uint32_t>(length); header->refs=1;
    auto* data=reinterpret_cast<wchar_t*>(header+1); std::copy_n(text,length+1,data);
    *static_cast<uintptr_t*>(field)=reinterpret_cast<uintptr_t>(data); return field;
}
void __fastcall caption_assign(void* field, void*, const uintptr_t* source) {
    CHECK(GetCurrentThreadId() == caption_thread);
    auto* header=reinterpret_cast<CowHeader*>(*source-20); ++header->refs;
    release(*static_cast<uintptr_t*>(field)); *static_cast<uintptr_t*>(field)=*source; ++caption_assignments;
}
void pump_caption_timer() {
    const auto until=GetTickCount64()+40;
    do {
        MSG message{};
        while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(1);
    } while (GetTickCount64()<until);
    MSG message{};
    while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
}
void write(const std::filesystem::path& file,const std::string& value) { std::ofstream(file,std::ios::binary) << value; }

}
int main() {
    setvbuf(stdout,nullptr,_IONBF,0);

    image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    CHECK(image == reinterpret_cast<void*>(base));
    CHECK(reinterpret_cast<uintptr_t>(fixture_storage+sizeof(fixture_storage)) > base+0x1093000);
    DWORD old = 0;
    CHECK(VirtualProtect(image,4096,PAGE_READWRITE,&old));
    CHECK(VirtualProtect(image+0x200000,0x1000,PAGE_EXECUTE_READWRITE,&old));
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(image);
    auto* pe = reinterpret_cast<IMAGE_NT_HEADERS32*>(image+dos->e_lfanew);
    const auto saved = *pe;
    pe->OptionalHeader.SizeOfImage = 0x1093000; pe->FileHeader.TimeDateStamp = 123;
    const auto modern = jump(0x200000,reinterpret_cast<void*>(&modern_assign));
    const auto construct = jump(0x200020,reinterpret_cast<void*>(&cow_construct));
    const auto assign = jump(0x200040,reinterpret_cast<void*>(&cow_assign));
    const auto destroy = jump(0x200060,reinterpret_cast<void*>(&cow_destroy));
    const auto caption_make=jump(0x200180,reinterpret_cast<void*>(&caption_construct));
    const auto caption_copy=jump(0x2001A0,reinterpret_cast<void*>(&caption_assign));
    const auto caption_free=jump(0x2001C0,reinterpret_cast<void*>(&cow_destroy));
    const auto caption_draw=jump(0x200200,reinterpret_cast<void*>(&native_caption_draw));
    const auto caption_text=jump(0x200220,reinterpret_cast<void*>(&native_caption_text));
    const auto region_draw=jump(0x200240,reinterpret_cast<void*>(&caption_region));
    const auto span = jump(0x2000C0,reinterpret_cast<void*>(&span_assign));
    const auto region = jump(0x2000E0,reinterpret_cast<void*>(&region_predicate));
    const auto feature = jump(0x200100,reinterpret_cast<void*>(&feature_predicate));
    const auto fast_feature = jump(0x200120,reinterpret_cast<void*>(&fast_feature_predicate));
    CHECK(VirtualProtect(image+0x300000,0x1000,PAGE_READWRITE,&old));
    const auto pointer = reinterpret_cast<uintptr_t>(connection.data());
    std::memcpy(image+0x300000,&pointer,4);
    const auto folder = std::filesystem::path([] { std::array<wchar_t,32768> path{}; GetModuleFileNameW(nullptr,path.data(),static_cast<DWORD>(path.size())); return std::wstring(path.data()); }()).parent_path();
    const auto component = folder/L"../cn-auth";
    const auto config = component/L"native-passports.toml";
    CHECK(!std::filesystem::exists(config));
    std::string document;
    auto configure = [&](bool cow, uint32_t offset, bool text = false) {
        target = pointer+offset;
        document = "schema=1\n[[profiles]]\nbuild=\"fixture\"\npe_timestamp=123\nimage_sizes=[0x1093000]\nconnection_rva=0x300000\npassport_offset="+std::to_string(offset)+"\nstring_kind=\""+(cow?"cow":text?"span":"modern")+"\"\nassign_rva="+(cow?"0x200040":text?"0x2000c0":"0x200000")+"\nassign_signature=\""+(cow?assign:text?span:modern)+"\"\n";
        if (cow) document += "constructor_rva=0x200020\nconstructor_signature=\""+construct+"\"\ndestructor_rva=0x200060\ndestructor_signature=\""+destroy+"\"\n";
        write(config,document);
    };
    const auto host = LoadLibraryW((folder/L"nmcogame.dll").c_str());
    const auto bridge = LoadLibraryW((component/L"login_bridge.dll").c_str());
    CHECK(host && bridge);
    const auto host_login_calls = reinterpret_cast<int(__cdecl*)()>(GetProcAddress(host,"FixtureLoginCalls"));
    CHECK(host_login_calls != nullptr);
    const auto initialize = reinterpret_cast<Initialize>(GetProcAddress(bridge,"igwInitialize"));
    const auto get_module = reinterpret_cast<GetModule>(GetProcAddress(bridge,"igwGetModule"));
    const auto terminal = reinterpret_cast<Terminal>(GetProcAddress(bridge,"igwTerminal"));
    CHECK(initialize && get_module && terminal);
    constexpr std::array<BYTE,16> id{0x84,0x74,0x18,0xc8,0x5f,0x1c,0xdf,0x48,0x84,0x97,0x21,0x36,0xa7,0x2a,0x57,0x33};
    constexpr std::array<BYTE,16> legacy_id{0x6c,0x13,0x35,0x3f,0x61,0x70,0x69,0x4e,0xbf,0x3a,0xcc,0x78,0xd0,0x26,0xf4,0x8f};
    int mode = 0;
    CHECK(initialize(511,&mode) == static_cast<int>(E_INVALIDARG));
    CHECK(initialize(512,&mode) == static_cast<int>(E_FAIL));
    for (bool cow:{false,true}) {
        for (uint32_t offset:{2252u,2392u,2440u}) {
            connection.fill(0); assign_count=construct_count=destroy_count=callback_count=failure_count=0;
            configure(cow,offset);
            CHECK(initialize(512,&mode) == 0);
            CHECK(initialize(512,&mode) == static_cast<int>(E_UNEXPECTED));
            Object* object{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&object)) == 0);
            CHECK(reinterpret_cast<int(__stdcall*)(Object*)>(object->methods[2])(object) == 20);
            CHECK(reinterpret_cast<int(__stdcall*)(Object*,void*)>(object->methods[10])(object,reinterpret_cast<void*>(123)) == 21);
            auto login = reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(object->methods[3]);
            CHECK(login(object,&receive,45,67) == 0);
            CHECK(assign_count == 1 && callback_count == 1 && failure_count == 0);
            CHECK(cow ? construct_count == 1 && destroy_count == 1 : construct_count == 0 && destroy_count == 0);
            CHECK(login(object,&receive,45,67) == static_cast<int>(E_FAIL));
            CHECK(assign_count == 1 && failure_count == 1);
            if (cow) { uintptr_t owned_pointer{}; std::memcpy(&owned_pointer,reinterpret_cast<void*>(target),4);
                CHECK(!std::wmemcmp(reinterpret_cast<const wchar_t*>(owned_pointer),L"bff-ticket:",11)); release(owned_pointer); }
            CHECK(std::all_of(connection.begin(),connection.begin()+offset,[](BYTE value) { return value == 0; }));
            CHECK(terminal() == 17);
        }
    }
    configure(false,2492,true);
    const auto span_sizes=document.find("image_sizes=[0x1093000]");
    CHECK(span_sizes!=std::string::npos);
    document.replace(span_sizes,std::strlen("image_sizes=[0x1093000]"),"image_sizes=[0x1091000,0x1093000]");
    write(config,document);
    for (uint32_t span_size:{0x1091000u,0x1093000u}) {
        pe->OptionalHeader.SizeOfImage=span_size;
        connection.fill(0); assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        CHECK(initialize(512,&mode) == 0);
        Object* span_object{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&span_object)) == 0);
        CHECK(reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(span_object->methods[3])(span_object,&receive,45,67) == 0);
        CHECK(assign_count == 1 && callback_count == 1 && failure_count == 0);
        CHECK(construct_count == 0 && destroy_count == 0);
        CHECK(terminal() == 17);
    }
    pe->OptionalHeader.SizeOfImage=0x1092000;
    CHECK(initialize(512,&mode)==static_cast<int>(E_FAIL));
    CHECK(terminal()==17);
    pe->OptionalHeader.SizeOfImage=0x1093000;
    configure(true,2252);
    document += "\n[profiles.resume]\npending_connection_rva=0x300004\napplication_rva=0x300008\nselected_offset=270\nconnected_offset=100\ncomplete_offset=2240\n";
    write(config,document);
    std::array<BYTE,2600> replacement_connection{};
    std::array<BYTE,300> application{};
    const auto application_pointer = reinterpret_cast<uintptr_t>(application.data());
    std::memcpy(image+0x300008,&application_pointer,4);
    for (const auto& provider_id:{id,legacy_id}) for (int scenario=0; scenario<8; ++scenario) {
        const size_t selected_offset=provider_id==legacy_id?254u:270u;
        auto provider_document=document;
        const auto selected_key=provider_document.find("selected_offset=270");
        CHECK(selected_key!=std::string::npos);
        provider_document.replace(selected_key,std::strlen("selected_offset=270"),"selected_offset="+std::to_string(selected_offset));
        write(config,provider_document);
        connection.fill(0); replacement_connection.fill(0); application.fill(0);
        std::memcpy(image+0x300000,&pointer,4); std::memset(image+0x300004,0,4);
        target=pointer+2252;
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        CHECK(initialize(512,&mode) == 0);
        Object* continuation{}; CHECK(get_module(provider_id.data(),reinterpret_cast<void**>(&continuation)) == 0);
        const auto request=reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(continuation->methods[3]);
        CHECK(request(continuation,&receive,45,67) == 0);
        uintptr_t initial_owned{}; std::memcpy(&initial_owned,connection.data()+2252,4);
        auto* empty=static_cast<CowHeader*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,24));
        CHECK(empty != nullptr); empty->refs=1;
        const auto empty_pointer=reinterpret_cast<uintptr_t>(empty+1);
        std::memcpy(replacement_connection.data()+2252,&empty_pointer,4);
        replacement_connection[100]=1; application[selected_offset]=1;
        const auto replacement_pointer=reinterpret_cast<uintptr_t>(replacement_connection.data());
        std::memcpy(image+0x300000,&replacement_pointer,4);
        target=replacement_pointer+2252;
        if (scenario==1) { std::memcpy(image+0x300000,&pointer,4); target=pointer+2252; }
        if (scenario==2) replacement_connection[100]=0;
        if (scenario==3) application[selected_offset]=0;
        if (scenario==4) replacement_connection[2240]=1;
        if (scenario==5) std::memcpy(image+0x300004,&replacement_pointer,4);
        if (scenario==6) empty->length=1;
        if (scenario==7) image[0x200040]^=1;
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        const auto resumed=request(continuation,&receive,45,67);
        CHECK(host_login_calls()==(scenario==0 || scenario==7?1:2));
        if (scenario==0) {
            CHECK(resumed==0 && assign_count==1 && callback_count==1 && failure_count==0);
            uintptr_t resumed_owned{}; std::memcpy(&resumed_owned,replacement_connection.data()+2252,4);
            CHECK(!std::wmemcmp(reinterpret_cast<const wchar_t*>(resumed_owned),L"bff-ticket:",11));
            CHECK(request(continuation,&receive,45,67)==static_cast<int>(E_FAIL));
            CHECK(callback_count==1 && failure_count==1); release(resumed_owned);
        } else {
            CHECK(resumed==static_cast<int>(E_FAIL));
            CHECK(assign_count==0 && callback_count==0 && failure_count==1);
        }
        if (scenario==7) image[0x200040]^=1;
        release(initial_owned); CHECK(HeapFree(GetProcessHeap(),0,empty));
        CHECK(terminal()==17);
    }
    std::memcpy(image+0x300000,&pointer,4);
    configure(false,2440);
    document += "\n[profiles.resume]\npending_connection_rva=0x300004\napplication_rva=0x300008\nselected_offset=550\nconnected_offset=266\ncomplete_offset=2428\nregion_rva=0x2000e0\nregion_signature=\""+region+"\"\nfeature_rva=0x200100\nfeature_signature=\""+feature+"\"\nautomatic_regions=[0,3,4]\nautomatic_feature=1503\nabort_feature=1203\nabort_flag_rva=0x300010\n";
    write(config,document);
    std::array<BYTE,600> modern_application{};
    const auto modern_application_pointer=reinterpret_cast<uintptr_t>(modern_application.data());
    std::memcpy(image+0x300008,&modern_application_pointer,4);
    for (int scenario=0; scenario<9; ++scenario) {
        connection.fill(0); replacement_connection.fill(0); modern_application.fill(0);
        std::memcpy(image+0x300000,&pointer,4); std::memset(image+0x300004,0,4); image[0x300010]=0;
        target=pointer+2440; native_region=9; native_feature=0;
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        CHECK(initialize(512,&mode)==0);
        Object* continuation{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&continuation))==0);
        const auto request=reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(continuation->methods[3]);
        CHECK(request(continuation,&receive,45,67)==0);
        auto copied_ticket=owned;
        const Modern copied_passport{copied_ticket.data(),75,75};
        std::memcpy(replacement_connection.data()+2440,&copied_passport,sizeof(copied_passport));
        replacement_connection[266]=1; modern_application[550]=1;
        const auto replacement_pointer=reinterpret_cast<uintptr_t>(replacement_connection.data());
        std::memcpy(image+0x300000,&replacement_pointer,4); target=replacement_pointer+2440;
        if (scenario>=1 && scenario<=3) native_region=scenario==1?0:scenario==2?3:4;
        if (scenario==4) native_feature=1503;
        if (scenario==5) { native_feature=1203; image[0x300010]=1; }
        if (scenario==6) copied_ticket[20]=L'b';
        if (scenario==7) image[0x2000E0]^=1;
        if (scenario==8) { const uint32_t empty=0; std::memcpy(replacement_connection.data()+2456,&empty,4); native_region=0; }
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        const auto resumed=request(continuation,&receive,45,67);
        CHECK(host_login_calls()==(scenario<=4 || scenario==8 ? 1 : 2));
        if (scenario==0 || scenario==8) CHECK(resumed==0 && assign_count==1 && callback_count==1 && failure_count==0);
        else if (scenario<=4) CHECK(resumed==0 && assign_count==0 && callback_count==0 && failure_count==0);
        else CHECK(resumed==static_cast<int>(E_FAIL) && assign_count==0 && callback_count==0 && failure_count==1);
        if (scenario<=4 || scenario==8) {
            CHECK(request(continuation,&receive,45,67)==static_cast<int>(E_FAIL));
            CHECK(failure_count==1);
        }
        if (scenario==7) image[0x2000E0]^=1;
        CHECK(terminal()==17);
    }
    std::memcpy(image+0x300000,&pointer,4);
    configure(false,2492,true);
    document += "\n[profiles.resume]\npending_connection_rva=0x300004\napplication_rva=0x300008\nselected_offset=510\nconnected_offset=310\ncomplete_offset=2480\nfeature_rva=0x200120\nfeature_signature=\""+fast_feature+"\"\nfeature_calling_convention=\"fastcall\"\nautomatic_feature=1502\n";
    write(config,document);
    for (int scenario=0; scenario<11; ++scenario) {
        connection.fill(0); replacement_connection.fill(0); modern_application.fill(0);
        std::memcpy(image+0x300000,&pointer,4); std::memset(image+0x300004,0,4);
        target=pointer+2492; native_feature=0;
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        CHECK(initialize(512,&mode)==0);
        Object* continuation{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&continuation))==0);
        const auto request=reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(continuation->methods[3]);
        CHECK(request(continuation,&receive,45,67)==0);
        auto copied_ticket=owned;
        const Modern copied_passport{copied_ticket.data(),75,75};
        std::memcpy(replacement_connection.data()+2492,&copied_passport,sizeof(copied_passport));
        replacement_connection[310]=1; modern_application[510]=1;
        const auto replacement_pointer=reinterpret_cast<uintptr_t>(replacement_connection.data());
        std::memcpy(image+0x300000,&replacement_pointer,4); target=replacement_pointer+2492;
        if (scenario==1) native_feature=1502;
        if (scenario==2) copied_ticket[20]=L'b';
        if (scenario==3) image[0x200120]^=1;
        if (scenario==4) std::memcpy(image+0x300004,&replacement_pointer,4);
        if (scenario==5) replacement_connection[2480]=1;
        if (scenario==6) modern_application[510]=0;
        if (scenario==7) replacement_connection[310]=0;
        if (scenario==8) { std::memcpy(image+0x300000,&pointer,4); target=pointer+2492; }
        if (scenario==9) { const uint32_t empty=0; std::memcpy(replacement_connection.data()+2508,&empty,4); native_feature=1502; }
        if (scenario==10) native_feature=-1;
        assign_count=construct_count=destroy_count=callback_count=failure_count=0;
        const auto resumed=request(continuation,&receive,45,67);
        CHECK(host_login_calls()==(scenario==0 || scenario==1 || scenario==9 ? 1 : 2));
        if (scenario==0 || scenario==9) CHECK(resumed==0 && assign_count==1 && callback_count==1 && failure_count==0);
        else if (scenario==1) CHECK(resumed==0 && assign_count==0 && callback_count==0 && failure_count==0);
        else CHECK(resumed==static_cast<int>(E_FAIL) && assign_count==0 && callback_count==0 && failure_count==1);
        if (scenario==0 || scenario==1 || scenario==9) {
            CHECK(request(continuation,&receive,45,67)==static_cast<int>(E_FAIL));
            CHECK(failure_count==1);
        }
        if (scenario==3) image[0x200120]^=1;
        CHECK(terminal()==17);
    }
    for (const auto& invalid:{
        std::string("feature_calling_convention=\"stdcall\""),
        std::string("feature_calling_convention=true"),
        std::string("automatic_regions=[0]"),
        std::string("abort_feature=1203"),
        std::string("abort_flag_rva=0x300010")}) {
        auto invalid_document=document;
        if (invalid.starts_with("feature_calling_convention")) {
            const auto at=invalid_document.find("feature_calling_convention=\"fastcall\"");
            CHECK(at!=std::string::npos);
            invalid_document.replace(at,std::strlen("feature_calling_convention=\"fastcall\""),invalid);
        } else invalid_document+=invalid+'\n';
        write(config,invalid_document);
        CHECK(initialize(512,&mode)==static_cast<int>(E_FAIL));
        CHECK(terminal()==17);
    }
    std::memcpy(image+0x300000,&pointer,4);
    configure(false,2392);
    image[0x200000] ^= 1; CHECK(initialize(512,&mode) == static_cast<int>(E_FAIL)); image[0x200000] ^= 1;
    pe->OptionalHeader.SizeOfImage++; CHECK(initialize(512,&mode) == static_cast<int>(E_FAIL)); pe->OptionalHeader.SizeOfImage--;
    for (mode=1;mode<=4;++mode) {
        assign_count=callback_count=failure_count=0;
        CHECK(initialize(512,&mode) == 0);
        Object* object{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&object)) == 0);
        const auto result = reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(object->methods[3])(object,&receive,45,67);
        CHECK(mode == 2 ? result == 0 && callback_count == 1 && assign_count == 1 : result == static_cast<int>(E_FAIL) && assign_count == 0);
        CHECK(terminal() == 17);
    }
    mode=9; CHECK(initialize(512,&mode) == static_cast<int>(E_FAIL)); CHECK(terminal() == 17);
    mode=0;
    CHECK(initialize(512,&mode) == 0);
    std::array<BYTE,16> unknown{}; void* forwarded{};
    CHECK(get_module(unknown.data(),&forwarded) == 42 && forwarded == unknown.data());
    CHECK(get_module(id.data(),nullptr) == static_cast<int>(E_POINTER));
    Object* object{}; CHECK(get_module(id.data(),reinterpret_cast<void**>(&object)) == 0);
    std::memset(image+0x300000,0,4); assign_count=callback_count=failure_count=0;
    CHECK(reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(object->methods[3])(object,&receive,45,67) == static_cast<int>(E_FAIL));
    CHECK(assign_count == 0 && callback_count == 0 && failure_count == 1);
    CHECK(terminal() == 17); std::memcpy(image+0x300000,&pointer,4);
    CHECK(initialize(512,&mode) == 0);
    CHECK(get_module(id.data(),reinterpret_cast<void**>(&object)) == 0);
    image[0x200000] ^= 1;
    CHECK(reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(object->methods[3])(object,&receive,45,67) == static_cast<int>(E_FAIL));
    image[0x200000] ^= 1; CHECK(terminal() == 17);
    pe->FileHeader.TimeDateStamp = 456;
    // The real Windows message pump must update only COW captions, with no instruction patch.
    const auto window=CreateWindowExW(0,L"STATIC",L"Caption fixture",WS_POPUP|WS_VISIBLE,-32000,-32000,1,1,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    CHECK(window != nullptr); caption_thread=GetWindowThreadProcessId(window,nullptr);
    std::array<BYTE,64> ui{};
    std::array<BYTE,400> chooser{};
    std::vector<std::array<BYTE,340>> buttons(65);
    std::vector<std::array<BYTE,2400>> centers(65);
    std::array<uintptr_t,65> button_rows{},center_rows{};
    auto put=[](void* field, uintptr_t value) { std::memcpy(field,&value,4); };
    put(image+0x300010,reinterpret_cast<uintptr_t>(ui.data()));
    put(ui.data()+8,reinterpret_cast<uintptr_t>(chooser.data()));
    put(chooser.data(),base+0x210000); put(chooser.data()+4,base+0x210004);
    for (size_t i=0;i<buttons.size();++i) {
        button_rows[i]=reinterpret_cast<uintptr_t>(buttons[i].data());
        center_rows[i]=reinterpret_cast<uintptr_t>(centers[i].data());
        put(buttons[i].data(),base+0x210100); put(buttons[i].data()+4,base+0x2101c4);
        put(centers[i].data()+2328,i+1);
        caption_construct(buttons[i].data()+336,nullptr,L"Native title");
    }
    put(chooser.data()+356,reinterpret_cast<uintptr_t>(button_rows.data()));
    put(chooser.data()+360,reinterpret_cast<uintptr_t>(button_rows.data()+button_rows.size()));
    put(image+0x300014,reinterpret_cast<uintptr_t>(center_rows.data()));
    put(image+0x300018,reinterpret_cast<uintptr_t>(center_rows.data()+center_rows.size()));
    for (unsigned i=0;i<48;++i) put(image+0x210100+i*4,base+0x200200);
    put(image+0x300020,0x1234);
    document += "\n[labels]\nbuild=\"cn-279\"\npe_timestamp=456\nimage_sizes=[0x1093000,0x9C6000]\nsystem_rva=0x300010\nmodal_offset=8\nchooser_vtable_rva=0x210000\nchooser_secondary_vtable_rva=0x210004\nbuttons_begin_offset=356\nbuttons_end_offset=360\ncenters_begin_rva=0x300014\ncenters_end_rva=0x300018\nidentity_offset=2328\nbutton_vtable_rva=0x210100\nbutton_secondary_vtable_rva=0x2101c4\ncaption_offset=336\ntable_entries=48\ndraw_slot=3\nrenderer_rva=0x300020\ndraw_rva=0x200200\ndraw_signature=\""+caption_draw+"\"\ntext_rva=0x200220\ntext_signature=\""+caption_text+"\"\nregion_rva=0x200240\nregion_signature=\""+region_draw+"\"\npoll_ms=16\nassign_rva=0x2001a0\nassign_signature=\""+caption_copy+"\"\nconstructor_rva=0x200180\nconstructor_signature=\""+caption_make+"\"\ndestructor_rva=0x2001c0\ndestructor_signature=\""+caption_free+"\"\n";
    write(config,document);
    const auto session_file=component/L"session.toml";
    const std::string valid_session="schema=2\n[session]\nlaunch_id=\"0123456789abcdef0123456789abcdef\"\nbuild=\"cn-279\"\n[module_data]\ncapability=\"bubblefighter.cn-center-names.v2\"\npayload='''schema=1\n[[centers]]\nid=1\nname=\"Automatic\"\n[[centers]]\nid=2\nname=\"\"\n'''\n";
    const auto original_get_module=reinterpret_cast<GetModule>(GetProcAddress(host,"FT_IGW_GetModule_v1"));
    CHECK(original_get_module != nullptr);
    Object* original_legacy{}; CHECK(original_get_module(legacy_id.data(),reinterpret_cast<void**>(&original_legacy)) == 0);
    std::array<BYTE,4096> unchanged_code{}; std::memcpy(unchanged_code.data(),image+0x200000,unchanged_code.size());
    const auto unchanged_connection=connection;
    const auto unchanged_centers=centers;
    const auto unchanged_chooser=chooser;
    auto caption=[&](size_t i) { uintptr_t data=0; std::memcpy(&data,buttons[i].data()+336,4); return std::wstring(reinterpret_cast<const wchar_t*>(data)); };
    auto reset_caption=[&](size_t i) { uintptr_t data=0; std::memcpy(&data,buttons[i].data()+336,4); release(data); caption_construct(buttons[i].data()+336,nullptr,L"Native title"); };
    // Missing names do not start UI work and cannot change the provider identity.
    CHECK(initialize(512,&mode) == 0); pump_caption_timer(); CHECK(caption_assignments == 0); CHECK(terminal() == 17);
    write(session_file,valid_session);
    auto invalid_build=valid_session; invalid_build.replace(invalid_build.find("cn-279"),6,"cn-324");
    auto zero_id=valid_session; zero_id.replace(zero_id.find("id=1"),4,"id=0");
    auto duplicate=valid_session; duplicate.replace(duplicate.find("id=2"),4,"id=1"); duplicate.replace(duplicate.find("name=\"\""),7,"name=\"duplicate\"");
    auto oversized=valid_session; oversized.replace(oversized.find("Automatic"),9,std::string(513,'A'));
    auto malformed=valid_session; malformed.replace(malformed.find("schema=1"),8,"schema=[");
    auto old_capability=valid_session; old_capability.replace(old_capability.find("names.v2"),8,"names.v1");
    for (const auto& invalid: {invalid_build,zero_id,duplicate,oversized,malformed,old_capability}) {
        write(session_file,invalid); assign_count=callback_count=0;
        CHECK(initialize(512,&mode) == 0); pump_caption_timer();
        CHECK(caption_assignments == 0 && caption(0) == L"Native title");
        Object* login_object{}; CHECK(get_module(legacy_id.data(),reinterpret_cast<void**>(&login_object)) == 0);
        CHECK(login_object == original_legacy);
        CHECK(reinterpret_cast<int(__stdcall*)(Object*,Callback,int,int)>(login_object->methods[3])(login_object,&receive_native,45,67) == 0);
        CHECK(callback_count == 1 && assign_count == 0); CHECK(terminal() == 17);
    }
    write(session_file,valid_session);
    for (const auto& edit:std::array<std::pair<std::string,std::string>,6>{{
        {"caption_offset=336","caption_offset=340"},{"table_entries=48","table_entries=4"},
        {"table_entries=48","table_entries=47"},{"table_entries=48","table_entries=49"},
        {"draw_slot=3","draw_slot=2"},{"caption_offset=336","caption_offset=332"}}}) {
        auto invalid=document; invalid.replace(invalid.find(edit.first),edit.first.size(),edit.second);
        write(config,invalid); CHECK(initialize(512,&mode)==0); pump_caption_timer();
        CHECK(caption_assignments==0 && caption(0)==L"Native title"); CHECK(terminal()==17);
    }
    write(config,document);
    // All object identities and vector extents are checked before the first caption changes.
    for (int scenario=0;scenario<9;++scenario) {
        auto saved_ui=ui; auto saved_buttons=buttons; auto saved_dialog=chooser;
        const auto saved_center_end=*reinterpret_cast<uintptr_t*>(image+0x300018);
        if (scenario == 0) put(ui.data()+8,0);
        if (scenario == 1) put(chooser.data(),base+0x210100);
        if (scenario == 2) put(chooser.data()+4,base+0x210100);
        if (scenario == 3) put(buttons[2].data(),base+0x210300);
        if (scenario == 4) put(buttons[2].data()+4,base+0x210100);
        if (scenario == 5) put(chooser.data()+360,reinterpret_cast<uintptr_t>(button_rows.data()+2));
        if (scenario == 6) put(chooser.data()+360,reinterpret_cast<uintptr_t>(button_rows.data())+1);
        if (scenario == 7) put(image+0x300018,reinterpret_cast<uintptr_t>(center_rows.data())+256*1024+4);
        if (scenario == 8) image[0x2001A0]^=1;
        CHECK(initialize(512,&mode) == 0); pump_caption_timer();
        CHECK(caption_assignments == 0 && caption(0) == L"Native title"); CHECK(terminal() == 17);
        ui=saved_ui; buttons=saved_buttons; chooser=saved_dialog; put(image+0x300018,saved_center_end);
        if (scenario == 8) image[0x2001A0]^=1;
    }
    pe->OptionalHeader.SizeOfImage=0x9C6000;
    CHECK(initialize(512,&mode) == 0); pump_caption_timer();
    CHECK(caption(0) == L"Automatic" && caption(1) == L"Native title" && caption(2) == L"Native title");
    CHECK(caption_assignments == 1); pump_caption_timer(); CHECK(caption_assignments == 1);
    CHECK(connection == unchanged_connection && centers == unchanged_centers && chooser == unchanged_chooser);
    CHECK(!std::memcmp(unchanged_code.data(),image+0x200000,unchanged_code.size()));
    uintptr_t table=0; std::memcpy(&table,buttons[0].data(),4);
    const auto draw=reinterpret_cast<int(__thiscall*)(void*)>(reinterpret_cast<uintptr_t*>(table)[3]);
    live_button=buttons[0].data(); std::memcpy(live_snapshot.data(),live_button,live_snapshot.size());
    CHECK(draw(buttons[0].data()) == 29);
    CHECK(draw_calls == 1 && text_calls == 1 && drawn_caption == L"Automatic" && caption(0) == L"Automatic");
    CHECK(*reinterpret_cast<uintptr_t*>(buttons[1].data()) == base+0x210100);
    CHECK(reinterpret_cast<uintptr_t*>(table)[-1] == *reinterpret_cast<uintptr_t*>(image+0x2100fc));
    for (unsigned slot=0;slot<48;++slot) if (slot!=3)
        CHECK(reinterpret_cast<uintptr_t*>(table)[slot] == *reinterpret_cast<uintptr_t*>(image+0x210100+slot*4));
    uintptr_t held=0; std::memcpy(&held,buttons[0].data()+336,4);
    const auto refs=reinterpret_cast<CowHeader*>(held-20)->refs;
    for (unsigned behavior=1;behavior<=3;++behavior) {
        draw_behavior=behavior;
        if (behavior==1) { const auto assignments=caption_assignments; CHECK(draw(live_button)==29); CHECK(caption_assignments==assignments); }
        else CHECK(call_draw_caught(draw,live_button)==1);
        CHECK(reinterpret_cast<CowHeader*>(held-20)->refs == refs);
        draw_behavior=0; CHECK(draw(live_button)==29);
    }
    alternate_region=true; CHECK(draw(live_button)==29); alternate_region=false;
    const auto rendered_count=text_calls;
    CHECK(terminal()==17); CHECK(draw(live_button)==23 && text_calls==rendered_count);
    CHECK(initialize(512,&mode)==0); pump_caption_timer(); CHECK(draw(live_button)==29);
    live_button=nullptr;
    // A newly constructed chooser caption is refreshed again, without remembering dangling UI pointers.
    reset_caption(0); pump_caption_timer(); CHECK(caption(0) == L"Automatic" && caption_assignments == 2);
    CHECK(terminal() == 17); reset_caption(0); pump_caption_timer(); CHECK(caption(0) == L"Native title");
    uintptr_t old_data=0; std::memcpy(&old_data,buttons[0].data()+336,4); release(old_data);
    caption_construct(buttons[0].data()+336,nullptr,L"");
    std::memcpy(&old_data,buttons[1].data()+336,4); release(old_data);
    uintptr_t shared_empty=0; std::memcpy(&shared_empty,buttons[0].data()+336,4);
    ++reinterpret_cast<CowHeader*>(shared_empty-20)->refs; put(buttons[1].data()+336,shared_empty);
    CHECK(initialize(512,&mode) == 0); pump_caption_timer();
    CHECK(caption(0) == L"Automatic" && caption(1).empty()); CHECK(terminal() == 17);
    CHECK(reinterpret_cast<CowHeader*>(shared_empty-20)->refs == 1);
    auto literal=valid_session; literal.replace(literal.find("Automatic"),9,"\xE6\xB5\x8B\xE8\xAF\x95 %s %n 100%");
    write(session_file,literal); CHECK(initialize(512,&mode) == 0); pump_caption_timer();
    CHECK(caption(0) == L"\u6D4B\u8BD5 %%s %%n 100%%");
    std::array<wchar_t,513> rendered{};
    CHECK(_snwprintf_s(rendered.data(),rendered.size(),_TRUNCATE,caption(0).c_str()) >= 0);
    CHECK(std::wstring(rendered.data()) == L"\u6D4B\u8BD5 %s %n 100%"); CHECK(terminal() == 17);
    auto percent_boundary=valid_session;
    percent_boundary.replace(percent_boundary.find("Automatic"),9,std::string(128,'%'));
    write(session_file,percent_boundary); CHECK(initialize(512,&mode) == 0); pump_caption_timer();
    CHECK(caption(0) == std::wstring(256,L'%'));
    rendered.fill(0);
    CHECK(_snwprintf_s(rendered.data(),rendered.size(),_TRUNCATE,caption(0).c_str()) == 128);
    CHECK(std::wstring(rendered.data()) == std::wstring(128,L'%'));
    const auto stable_assignments=caption_assignments; pump_caption_timer(); CHECK(caption_assignments == stable_assignments);
    CHECK(terminal() == 17);
    for (auto& button:buttons) { uintptr_t data=0; std::memcpy(&data,button.data()+336,4); release(data); }
    CHECK(DestroyWindow(window)); CHECK(std::filesystem::remove(session_file)); CHECK(std::filesystem::remove(config));
    pe->OptionalHeader.SizeOfImage=0x1093000;
    pe->FileHeader.TimeDateStamp = 789;
    write(config,document);
    CHECK(initialize(512,&mode) == 0);
    CHECK(get_module(id.data(),reinterpret_cast<void**>(&object)) == 0);
    // Builds without an adapter retain the original provider callback and never assign a passport.
    CHECK(terminal() == 17);
    CHECK(std::filesystem::remove(config));
    *pe=saved; CHECK(FreeLibrary(bridge)); CHECK(FreeLibrary(host));
    return finish("cn_auth");
}

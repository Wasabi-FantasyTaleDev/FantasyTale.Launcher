#ifndef FTHOOK_PLUGIN_ABI_H
#define FTHOOK_PLUGIN_ABI_H
/* ABI 1: x86 C, cdecl, eight-byte packing. No CRT objects or exceptions cross it. */
#include <stdint.h>

#define FTHOOK_ABI_VERSION 1u
#define FTHOOK_CALL __cdecl
#define FTHOOK_OK 0
#define FTHOOK_NOT_FOUND 1
#define FTHOOK_INVALID 2
#define FTHOOK_FAILED 3
#define FTHOOK_ABI_UNSUPPORTED 4
#define FTHOOK_OPTION_BOOL 1u
#define FTHOOK_OPTION_INT 2u
#define FTHOOK_OPTION_STRING 3u

#pragma pack(push, 8)
/* Readable executable ranges borrowed only for the duration of a locator call. */
typedef struct fthook_region {
    uintptr_t address;
    uint32_t size;
} fthook_region;

/* A current image snapshot; callers must not retain its regions pointer. */
typedef struct fthook_image {
    uintptr_t base;
    uint32_t timestamp;
    uint32_t size;
    const fthook_region* regions;
    uint32_t region_count;
} fthook_image;

/* Strings remain valid for the plugin's process lifetime; bool uses integer 0/1. */
typedef struct fthook_option {
    uint32_t kind;
    int64_t integer;
    const char* text;
} fthook_option;

/* Return NOT_FOUND while unpacking; other failures stop the patch attempt. */
typedef int32_t (FTHOOK_CALL *fthook_locator)(void* context, const fthook_image* image, uintptr_t* address);

/* Optional x86-to-launcher overlay bridge. The owner is supplied by the runtime. */
typedef struct fthook_overlay_host {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    int32_t (FTHOOK_CALL *register_panel)(void* context, const char* panel_id,
        const char* inline_html, const char* html_file_name);
    int32_t (FTHOOK_CALL *unregister_panel)(void* context, const char* panel_id);
    int32_t (FTHOOK_CALL *show_panel)(void* context, const char* panel_id);
    int32_t (FTHOOK_CALL *hide_panel)(void* context, const char* panel_id);
    int32_t (FTHOOK_CALL *send_panel)(void* context, const char* panel_id,
        const char* event_name, const char* json_data);
    /* Optional tail: consume one owner-scoped JSON event, or return NOT_FOUND.
       Call on a worker, never a render callback. Supply at least 4097 bytes. */
    int32_t (FTHOOK_CALL *poll_event)(void* context, const char* panel_id,
        char* json_event, uint32_t capacity);
} fthook_overlay_host;

/* Optional launch-scoped companion API. Call on workers, never render callbacks.
   JSON output buffers need 131073 bytes. OK reports local success; inspect the
   JSON status for accepted/completed/not_bound/busy/outcome_unknown results.
   FAILED after request means its server-side outcome may be unknown; do not replay. */
typedef struct fthook_companion_host {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    int32_t (FTHOOK_CALL *invoke)(void* context, const char* method, const char* type,
        uint32_t version, const char* json_data, uint32_t timeout_ms, const char* operation_id,
        char* json_output, uint32_t capacity);
} fthook_companion_host;

/* On failed activation, remove all observers and drain callbacks before returning.
   Set this callback before acquiring external resources. Return OK only after draining
   callbacks. On failure the runtime retains the DLL and API storage for safety. */
typedef int32_t (FTHOOK_CALL *fthook_cleanup)(void* context);

/* Host-owned API, retained through process exit after successful activation.
   Calls are synchronous. UTF-8 strings must be NUL-terminated. */
typedef struct fthook_host {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    const char* game;
    const char* build;
    const char* branch;
    const char* plugin_directory;
    const char* log_path;
    /* Writes one bounded line. Not suitable for exception handlers. */
    void (FTHOOK_CALL *log)(void* context, const char* message);
    /* Returns a validated effective option; unknown names return NOT_FOUND. */
    int32_t (FTHOOK_CALL *option)(void* context, const char* key, fthook_option* value);
    /* Finds a unique pattern in this snapshot; ambiguity returns INVALID. */
    int32_t (FTHOOK_CALL *scan)(const fthook_image* image, const char* pattern, uintptr_t* address);
    /* Reads at most 4096 bytes without propagating an access violation. */
    int32_t (FTHOOK_CALL *read)(uintptr_t address, void* destination, uint32_t size);
    /* Entry-time only: register up to 32 names under '<plugin id>.'. */
    int32_t (FTHOOK_CALL *register_locator)(void* context, const char* name, fthook_locator locate, void* locator_context);
    /* Entry-time only: stage up to 64 verified writes (1..64 bytes each).
       Nothing is written until every native and declarative patch passes preflight.
       A zero mask byte is a wildcard; other mask bytes must be 255. */
    int32_t (FTHOOK_CALL *stage_patch)(void* context, uintptr_t address, const uint8_t* expected,
        const uint8_t* mask, const uint8_t* replacement, uint32_t size);
    /* ABI 1 tail extension, runtime >=3.2.0: stage one aligned main-image IAT slot.
       Both pointer values must be exact. Uses an atomic compare/exchange and the same rollback journal. */
    int32_t (FTHOOK_CALL *stage_import)(void* context, uintptr_t slot, uintptr_t expected, uintptr_t replacement);
    /* Optional tail extension. Check struct_size before reading this pointer. */
    const fthook_overlay_host* overlay;
    /* Optional tail, runtime >=3.3.3. Check size; absent grants leave this null. */
    const fthook_companion_host* companion;
} fthook_host;

/* Filled by the plugin entry. Cleanup also runs if entry returns an error. */
typedef struct fthook_plugin {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    fthook_cleanup cleanup;
} fthook_plugin;

/* Export the undecorated entry name through a .def file. Validate the host version
   and size before using it. Success keeps this DLL loaded until process exit;
   this ABI deliberately provides no live unload or reload operation. */
typedef int32_t (FTHOOK_CALL *fthook_plugin_entry)(const fthook_host* host, fthook_plugin* plugin);
#pragma pack(pop)
#endif

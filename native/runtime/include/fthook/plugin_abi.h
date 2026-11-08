#ifndef FTHOOK_PLUGIN_ABI_H
#define FTHOOK_PLUGIN_ABI_H
/* ABI 1: x86 C, cdecl, eight-byte packing. No CRT objects or exceptions cross it. */
#include <stdint.h>
#include <stddef.h>

#define FTHOOK_ABI_VERSION 1u
#define FTHOOK_CALL __cdecl
#define FTHOOK_OK 0
#define FTHOOK_NOT_FOUND 1
#define FTHOOK_INVALID 2
#define FTHOOK_FAILED 3
#define FTHOOK_ABI_UNSUPPORTED 4
#define FTHOOK_BUFFER_TOO_SMALL 5
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

/* Caller-owned UTF-8 buffers. Required capacities include the terminating NUL.
   No buffer is written unless both fit. Clear ticket storage after use. */
typedef struct fthook_credentials {
    uint32_t struct_size;
    char* ticket;
    uint32_t ticket_capacity;
    uint32_t ticket_required;
    char* endpoint_host;
    uint32_t endpoint_capacity;
    uint32_t endpoint_required;
    uint32_t endpoint_port;
} fthook_credentials;

/* Explicitly granted to one selected plugin by the launch plan, never its manifest.
   Entry-time only. Correlation is borrowed; credentials copy into caller storage.
   A mismatched launch/game/build returns INVALID without exposing any material. */
typedef struct fthook_authentication_host {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    const char* launch_id;
    int32_t (FTHOOK_CALL *copy_credentials)(void* context, const char* launch_id,
        const char* game, const char* build, fthook_credentials* output);
    /* Revision 2: 1 copies a plain name in the credential buffer; 0 copies a server ticket. */
    uint32_t name_login;
} fthook_authentication_host;

/* On failed activation, remove all observers and drain callbacks before returning.
   Set this callback before acquiring external resources. Return OK only after draining
   callbacks. On failure the runtime retains the DLL and API storage for safety. */
typedef int32_t (FTHOOK_CALL *fthook_cleanup)(void* context);

/* A service callback must return promptly. Stateful UI work is queued to its
   owning thread; responses contain acceptance/results, never an arbitrary address. */
typedef int32_t (FTHOOK_CALL *fthook_service_handler)(void* context, const char* caller, const char* request,
    char* response, uint32_t capacity);

/* Launch-local services are activated only after plugin preflight commits.
   Names are unique within the launch. Callbacks may run concurrently; cleanup drains them. */
typedef struct fthook_services_host {
    uint32_t abi_version;
    uint32_t struct_size;
    void* context;
    int32_t (FTHOOK_CALL *register_service)(void* context, const char* name,
        fthook_service_handler handler, void* handler_context);
    int32_t (FTHOOK_CALL *invoke)(void* context, const char* name, const char* request,
        char* response, uint32_t capacity);
} fthook_services_host;

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
    /* Optional tail, runtime >=3.4.0. Null unless this launch grants access. */
    const fthook_authentication_host* authentication;
    /* Optional tail, runtime >=3.5.0: local services with activation-scoped ownership. Check struct_size before use. */
    const fthook_services_host* services;
} fthook_host;

/* Checks the optional local service API without reading beyond an older host table. */
static inline const fthook_services_host* fthook_get_services(const fthook_host* host) {
    if (!host || host->abi_version != FTHOOK_ABI_VERSION
        || host->struct_size < offsetof(fthook_host, services) + sizeof(host->services)) return NULL;
    const fthook_services_host* api = host->services;
    return api && api->abi_version == 1u && api->struct_size >= sizeof(fthook_services_host)
        && api->register_service && api->invoke ? api : NULL;
}

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

/* Checks the host tail before reading it; absence or an invalid subtable denies access. */
static inline const fthook_authentication_host* fthook_get_authentication(const fthook_host* host) {
    if (!host || host->abi_version != FTHOOK_ABI_VERSION
        || host->struct_size < offsetof(fthook_host, authentication) + sizeof(host->authentication)) return NULL;
    const fthook_authentication_host* api = host->authentication;
    return api && ((api->abi_version == 1u && api->struct_size >= offsetof(fthook_authentication_host, name_login))
        || (api->abi_version == 2u && api->struct_size >= sizeof(fthook_authentication_host)))
        && api->copy_credentials && api->launch_id ? api : NULL;
}
#endif

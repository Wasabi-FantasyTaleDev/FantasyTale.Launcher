#include <fthook/plugin_abi.h>

static const fthook_host* runtime;

static int32_t FTHOOK_CALL locate(void* context, const fthook_image* image, uintptr_t* address) {
    const fthook_host* host = (const fthook_host*)context;
    return host->scan(image, "91 23 45 67 AA BB", address);
}

static int32_t FTHOOK_CALL cleanup(void* context) {
    const fthook_host* host = (const fthook_host*)context;
    host->log(host->context, "sample activation rolled back");
    runtime = 0;
    return FTHOOK_OK;
}

/* The sample only targets a synthetic test image, never a game client. */
int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < sizeof(fthook_host))
        return FTHOOK_ABI_UNSUPPORTED;
    runtime = host;
    plugin->abi_version = FTHOOK_ABI_VERSION;
    plugin->struct_size = sizeof(fthook_plugin);
    plugin->context = (void*)host;
    plugin->cleanup = cleanup;
    return runtime->register_locator(runtime->context, "sample.locator.site", locate, (void*)runtime);
}

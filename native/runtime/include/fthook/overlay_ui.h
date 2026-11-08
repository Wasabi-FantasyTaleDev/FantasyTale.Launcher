#pragma once
#include "plugin_abi.h"
#include <stddef.h>

/* Register a centered, hidden game-UI surface. Call from the plugin's worker;
   missing overlay is optional and must not stop ordinary plugin behavior. */
inline int32_t fthook_create_game_ui(const fthook_overlay_host* overlay, const char* panel_id) {
    if (!overlay || overlay->struct_size < offsetof(fthook_overlay_host, poll_event)
        || !overlay->register_panel) return FTHOOK_NOT_FOUND;
    return overlay->register_panel(overlay->context, panel_id,
        "<style>:host{left:50%;top:50%;transform:translate(-50%,-50%)}</style>"
        "<div data-game-ui-root></div>", nullptr);
}

/* Show a native-style modal without HTML or styling. options_json supplies
   title, text and buttons:[{name,label}]; all visible strings belong to the plugin. */
inline int32_t fthook_show_message_box(const fthook_overlay_host* overlay,
    const char* panel_id, const char* options_json) {
    if (!overlay || overlay->struct_size < offsetof(fthook_overlay_host, poll_event)
        || !overlay->send_panel) return FTHOOK_NOT_FOUND;
    return overlay->send_panel(overlay->context, panel_id, "game-ui.message-box", options_json);
}

/* Read one action/change/error as JSON {name,data}; NOT_FOUND means no queued
   event. Check this optional tail before using it with an older runtime. */
inline int32_t fthook_poll_ui_event(const fthook_overlay_host* overlay,
    const char* panel_id, char* output, uint32_t capacity) {
    if (!overlay || overlay->struct_size < sizeof(fthook_overlay_host)
        || !overlay->poll_event) return FTHOOK_NOT_FOUND;
    return overlay->poll_event(overlay->context, panel_id, output, capacity);
}

/* Push a hook or image-locator rectangle. JSON contains x,y,width,height,
   optional referenceWidth/referenceHeight, visible, placement and gap. Passing
   "null" removes the anchor. Publish moves on a worker, not the render thread. */
inline int32_t fthook_set_ui_anchor(const fthook_overlay_host* overlay,
    const char* panel_id, const char* anchor_json) {
    if (!overlay || overlay->struct_size < offsetof(fthook_overlay_host, poll_event)
        || !overlay->send_panel) return FTHOOK_NOT_FOUND;
    return overlay->send_panel(overlay->context, panel_id, "game-ui.anchor", anchor_json);
}

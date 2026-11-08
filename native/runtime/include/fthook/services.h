#pragma once
#include "plugin_abi.h"
#include <string>

namespace fthook {
/// Stages one named callback under an activation identity; it is invisible until commit.
int32_t register_service(const void* owner, const char* name,
    fthook_service_handler handler, void* context);
/// Makes staged services visible after native activation and patch preflight succeed.
void activate_services(const void* owner);
/// Hides an owner's services and waits for in-flight callbacks before cleanup.
void remove_services(const void* owner);
/// Invokes an active service with bounded UTF-8 request/response storage on the calling worker.
int32_t invoke_service(const char* caller, const char* name, const char* request, char* response, uint32_t capacity);
}

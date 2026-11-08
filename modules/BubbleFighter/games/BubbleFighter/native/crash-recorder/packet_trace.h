#pragma once
#include <fthook/plugin_abi.h>
#include <string>

// Stages checked IAT replacements for synchronous receive imports in the main executable.
bool start_packet_trace(const fthook_host* host, const std::wstring& path, unsigned maximum_mib);
// Stops recording; import replacements remain owned by the runtime's verified patch transaction.
void stop_packet_trace();

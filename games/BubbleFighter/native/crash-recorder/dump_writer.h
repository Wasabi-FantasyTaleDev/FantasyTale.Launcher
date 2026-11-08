#pragma once
#include <windows.h>
#include <string>

// Preloads DbgHelp and starts a writer with a bounded outcome file before the exception observer is armed.
bool start_dump_writer(const std::wstring& path, unsigned maximum_mib);
// Captures the first serious exception, preserving the faulting thread until the bounded write completes.
void request_dump(EXCEPTION_POINTERS* exception) noexcept;
// Queues one all-thread close snapshot without blocking the window-message caller.
void request_snapshot() noexcept;
// Drains the writer before failed plugin activation unloads this DLL.
void stop_dump_writer();

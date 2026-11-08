# Native plugins (FtHook 3.x, ABI 1)

The public contract is `native/runtime/include/fthook/plugin_abi.h`. It is a
C-compatible x86 header with explicit cdecl calls, eight-byte packing, sized
structures and an ABI version. DLLs use their own CRT. No C++ object, exception
or allocator ownership crosses the interface. Credentials are available
only through the explicitly authorized authentication extension.

Export `fthook_plugin_main` (or the manifest's `native.entry`) as an undecorated
name using a `.def` file. Validate the host ABI and structure size before using
any function pointer. Fill the plugin descriptor and install its cleanup
callback before acquiring observers, handles or threads. Return a status code.
The pure C example is `native/samples/sample.locator`; its synthetic build is
deliberately unavailable to real clients.

## Activation

1. Validate the session fingerprint, manifest, build compatibility and options.
2. Reserve one of 64 process activation slots. Load the exact DLL from its
   plugin directory with Windows' DLL-directory and System32 dependency search.
   The same loaded module cannot initialize twice in this runtime.
3. Call the entry with stable host API storage and effective typed options.
4. Permit entry-time locator registration and staged writes. Locator names must
   start with the plugin id followed by a dot. Other plugins may use the names
   in declarative patches after the provider succeeds. Plugins finish activation in
   session order; list a locator provider before its consumers. A failed provider
   never exposes its locators to later plugins. Put early observers before plugins
   that wait for unpacking.
5. Resolve and check all the plugin's native and declarative patches together.
   Check executable ranges, expected bytes and overlapping writes before applying.
   An unpacked declarative patch can retry its locator until its deadline.
6. On success, retain the DLL, options and host API until process exit. On failure
   before writes, remove the registered locators, call cleanup and unload the DLL.

The runtime accepts 64 plugins per session, 256 declarative patches per plugin,
32 explicitly registered native locators and 64 staged writes per native plugin.
Each staged write is at most 64 bytes. Reads are at most 4096 bytes. Pattern
arguments are at most 1024 bytes and scans require a unique match.

## Lifetime and failure

ABI 1 has no live unload or reload operation. A successful observer or detour can
remain active after the activation worker exits. The Bubble Fighter host pins
itself before loading plugins so its API function pointers remain valid.
The process-lifetime allocations are intentional and bounded by the activation
limit; they are not released by CRT destructors under the loader lock.

An unsuccessful entry still receives cleanup, including when it returns an
incompatible descriptor. Cleanup must unregister its observers, stop and join
its workers, and drain callbacks before returning `FTHOOK_OK`. It must not throw.
If cleanup returns an error or faults, the runtime logs the failure and keeps the
DLL and API storage alive. That quarantined attempt continues to consume a slot.
Likewise, a failed code write whose restoration cannot be guaranteed retains the
DLL so an uncertain branch target cannot point into unloaded code.

This does not sandbox native code. Plugins must honor the ABI and use staged
writes instead of modifying instructions directly during initialization.
Exception observation is best effort; an unrecoverably damaged process cannot
be made safe by an ABI wrapper.

## Calls and borrowed data

`option` returns effective values after manifest validation. String results and
the host's identity/path strings remain valid with the retained plugin. `log`
uses the runtime's bounded logger and must not be called from exception handlers.

`register_locator`, `stage_patch` and `stage_import` are valid only during entry. A locator
receives a current image view whose ranges are borrowed for that call. It may use
`scan` or `read`; it must not retain the view or modify registration from a
callback. `FTHOOK_NOT_FOUND` means unpacking has not exposed the site yet. Any
other nonzero status, zero address on success, C++ exception or structured
exception fails that plugin's activation.

FtHook 3.2 appends `stage_import` to the sized host structure without changing
ABI 1. Consumers check `struct_size` and require runtime >=3.2.0 before accessing
it. It stages an aligned four-byte pointer replacement inside the main image's
IAT. All four expected bytes must be exact. The runtime uses compare/exchange,
restores page protection, and includes the write in preflight and rollback.
`stage_patch` continues to require executable memory; data writes are not a
general-purpose ABI capability.

Runtime 3.3.3 adds the optional `companion` tail, without changing ABI 1. Use
`fthook_get_companion` from `fthook/companion.h` and declare exact message schemas
in `[companion.messages]`; see [the companion contract](companion.md#native-consumers-runtime-333).
The API forwards credential-free worker operations through a separate local broker,
independently of Overlay. It does not expose network authentication or sockets.
Consumers that only need earlier host members should check the end of those members,
rather than requiring `sizeof(fthook_host)` from newer headers.

## Authentication extension

The module selects one native credential owner per launch. Runtime 3.6.0 appends
`name_login` to `fthook_authentication_host`. Ticket grants continue to expose
revision 1; an explicit schema-2 name grant exposes revision 2 and sets that field.
Validate both revision and structure size before reading the extension. Its copy
operation returns the plain name for a name grant and the ticket for a ticket
grant, together with the selected endpoint. Access is entry-only and temporary
buffers must be cleared. This extension does not change the plugin host's ABI 1.
Older authentication components must be updated before name-only login.

## Launch module metadata

The shared launcher writes an optional `[module_data]` table containing `capability`
and `payload`. The capability is a 1..128-character lowercase ASCII identifier
using letters, digits, hyphens, underscores and dots. The payload is 1..32768
UTF-8 bytes, contains no NUL and belongs to the current schema-2 launch identity.
The shared runtime preserves this data without interpreting game-specific JSON.

`FT_GetLaunchMetadata_v1(capability, buffer, capacity)` returns the required byte
count including the terminating NUL. A null buffer with zero capacity queries
the size; zero means absent, -1 means invalid arguments and -2 means insufficient
capacity. Consumers verify the current `FT_GetLaunchId_v1` value independently.

Gateway Center login does not use this envelope to replace the game's original
chooser. The former `bf-native-selector-presentation-v2` producer and CN2012
renderer/click adapter are removed. Original Center responses and native redirects
carry server choices; authenticated automatic admission is controlled outside
the game. The generic metadata ABI remains available to independent module
capabilities and does not itself authorize any native memory patch.

## Native validation

Build `native/FantasyTale.Native.sln` as Release|x86 and run all three test
executables: `fthook_tests`, `nmcogame_tests`, `native_tests`. The native suite
starts a fresh process for each actual DLL activation. It covers a C-only sample,
API lifetime after the worker, duplicate modules, rejected entry points/versions,
cleanup, SEH/C++ failures, preflight atomicity, and the crash recorder.
Child output is retained in separate files under `native/out/Release`.

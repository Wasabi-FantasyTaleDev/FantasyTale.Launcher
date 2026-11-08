# Native plugin SDK

[Chinese](README.zh.md)

This distribution contains the public FtHook C ABI header, project license and a
standalone x86 locator example. It does not include the runtime implementation,
game clients, authentication data or C++ implementation helpers. No runtime
static library is needed: the host supplies operations through `fthook_host`.

Build the extracted distribution from a Visual Studio developer terminal with
the v145 C++ toolset and Windows SDK installed:

```powershell
msbuild sample/sample_locator.vcxproj -p:Configuration=Release -p:Platform=Win32
```

The result is `sample/out/sample_locator.dll`. The example registers a locator
for a synthetic image only; it does not enable a game feature and is not an
installable store package. Its sources are the same locator example used by the
runtime tests. The project references only the distributed header and Windows
build tools. It uses C17, the static CRT and warnings as errors.

The package's `sdk.json` records the runtime source version, ABI number and each
allowlisted payload file's length and SHA-256. It is a local integrity inventory,
not a signature or a promise that the package was publicly released.

## Compatibility

Plugins export the undecorated `fthook_plugin_main` name through a module
definition file and validate the host ABI and structure size before reading
members. The example requires the complete current host structure. A plugin
supporting older hosts must check the end offset of every member it needs.
Optional overlay and companion pointers may be null even when the host structure
is large enough. Set the plugin's required runtime version in its manifest when
using a newer operation; the SDK source version is not the ABI number.

Runtime 3.4.0 adds the optional `authentication` tail. Obtain it through
`fthook_get_authentication`; it is absent unless the game module's launch plan
explicitly authorizes this selected native plugin. A plugin manifest cannot grant
itself access. During entry, call `copy_credentials` with the API's launch ID and
the host's game/build. Query required capacities with zero-capacity null buffers,
then provide separate caller-owned ticket and endpoint buffers. Capacities include
the terminating NUL. `FTHOOK_BUFFER_TOO_SMALL` copies neither string. Mismatched
identities, invalid descriptors and calls after entry return `FTHOOK_INVALID`.
The account name and private session representation are not exposed. Never log
the ticket; securely clear its caller-owned buffer after creating the client
passport. The runtime clears its temporary credential copy when entry finishes.

Keep CRT objects, exceptions, STL containers and ownership of allocated memory
inside their originating module. Register cleanup before acquiring resources;
failed activation must remove observers and drain callbacks. Successful plugins
remain loaded until process exit. This ABI does not support live DLL replacement.

## Repository boundary

The launcher owns the ABI header and runtime. Game repositories consume a pinned
SDK distribution and publish their own package manifests and binaries. Existing
authentication and movement implementations still link internal C++ helpers and
have not yet been migrated to this standalone SDK. See
`docs/native-repositories.md` in the launcher source for the migration gates.

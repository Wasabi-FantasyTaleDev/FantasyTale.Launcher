# Auto Dash standalone build

Auto Dash builds against the checked-in FtHook 3.3.3 SDK export and Windows
system libraries. Its project has no project reference into the launcher
checkout and does not link the runtime static library.

The plugin uses `toml++` v3.4.0 to read its bounded movement configuration. Exact
signature parsing remains local to this plugin because its configuration only
accepts fixed bytes; wildcard matching is not part of this plugin contract.
The dependency's original MIT license is retained at
`native/third_party/tomlplusplus/LICENSE`.

Before migration is accepted, build the plugin from a clean checkout, verify the
SDK inventory, inspect the exported `fthook_plugin_main` symbol and validate the
plugin with the launcher host across its supported client builds. Preserve the
existing store ID, options and profile policy. Do not publish a rebuilt package
as an existing immutable version.

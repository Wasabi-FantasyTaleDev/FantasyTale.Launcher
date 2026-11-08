# Bubble Fighter integration sources

[简体中文](README.zh.md)

This directory contains the Bubble Fighter module's source snapshot: client definitions, native plugin implementations, resource composition tools and module-specific documentation. It is included as ordinary files in the launcher source repository, with no nested repository or earlier Git history.

The shared launcher solution and SDK are at the repository root. Start with the root [contribution guide](../../CONTRIBUTING.md) for managed, native and frontend build commands. Some standalone integration project files retain shared-source layout requirements; their presence does not establish that every project builds independently from this directory. The standalone native SDK and its verification tools remain pinned.

Plugin definitions describe compatibility, saved options, resource edits and verified native patches. End-user installation packages are distributed through the configured plugin store. This source snapshot contains no compiled game plugins, client archives, original or generated game fonts, model resources or proprietary media. Font and resource packaging tools require user-supplied originals; follow each tool's documented inputs.

See [LICENSE](LICENSE), the root [third-party notices](../../NOTICE.md), and the module's `docs/` directory. Game names and trademarks belong to their respective owners. Source availability grants no rights to redistribute game assets.

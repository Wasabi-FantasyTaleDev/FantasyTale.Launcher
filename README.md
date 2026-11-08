# FantasyTale.Launcher

[简体中文](README.zh.md)

A multi-game launcher for community servers of classic online games, built
with .NET 10, Photino and Vue 3, and designed to look and feel like a native
Windows 11 WinUI 3 application.

> Status: early development. Architecture and the client hook runtime come
> first; see [docs/roadmap.md](docs/roadmap.md).

## Games

| Game | Status |
| --- | --- |
| Bubble Fighter (泡泡战士 / 버블파이터 / 泡泡大亂鬥 / Crazy Shooter) | In development, works with [BubbleFighter.Fantasy](https://github.com/Wasabi-FantasyTaleDev/BubbleFighter.Fantasy) |
| TalesRunner | Planned (module not part of this repository) |
| AirRider | Planned (module not part of this repository) |

## Highlights

- **Many clients side by side.** Each installed client is an independent
  instance; keep several copies of the same version for different servers.
- **Servers you choose.** Add a server by address. Servers that provide the
  launcher API offer verified login, profile information and fully automatic
  client installation and updates.
- **Client hook plugins.** Hooks run inside the game through a small runtime
  and are installed, enabled and updated like mods. Most plugins are pure data
  (`plugin.toml`), no compiler needed. Plugins declare the client versions they
  support and the versions on which they are mandatory.
- **Plugin stores and download sources.** Add your own store or download
  source. Downloads use aria2 (multi-connection, resumable) or plain HTTP;
  AList is supported as a source.
- **Extensible by definition.** Games, client versions, servers and plugins are
  TOML files. Supporting a new client version is a new file, not a new build.
- **Seven interface languages:** 简体中文, 繁體中文, 한국어, Bahasa Indonesia, Tiếng Việt, ไทย, English. Thai retains English text for translations still pending and displays client date labels in the Buddhist calendar.

## Documentation

- [Plugin source map](docs/plugin-source-map.md): implementation files and generators for each plugin.
- [Development tools](tools/README.md): retained build tools and local-only analysis exclusions.

- [Architecture](docs/architecture.md)
- [Hook plugin format](docs/hook/plugin-format.md)
- [Session contract](docs/hook/session-contract.md)
- [Decisions](docs/decisions/README.md)
- [Roadmap](docs/roadmap.md)
- [Contributing guide](CONTRIBUTING.md)

## Related projects

- [BubbleFighter.Fantasy](https://github.com/Wasabi-FantasyTaleDev/BubbleFighter.Fantasy): the Bubble Fighter server this launcher pairs with.
- [Photino.NET](https://github.com/tryphotino/photino.NET): native window and web view host.
- [AList](https://github.com/alist-org): file listing and download service supported as a download source.

## License

MIT. See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md). This project contains
no game client files; game names and trademarks belong to their owners.

# Third-party notices

| Component | Use | License |
| --- | --- | --- |
| [Photino.NET](https://github.com/tryphotino/photino.NET) | Native window and web view host | Apache-2.0 |
| [Photino.Native](https://github.com/tryphotino/photino.Native) | Native window host implementation | Apache-2.0; source/version provenance remains under review |
| [.NET](https://github.com/dotnet/runtime) | Managed runtime and cryptography packages | MIT and component-specific notices; see `ThirdPartyLicenses/DotNet.txt` |
| [Tomlyn](https://github.com/xoofx/Tomlyn) | Managed TOML parsing | BSD-2-Clause |
| [Vue](https://github.com/vuejs/core) | Launcher frontend and vendored overlay runtime | MIT; see `ThirdPartyLicenses/Vue.txt` |
| [Vue Router](https://github.com/vuejs/router) | Frontend navigation | MIT; see `ThirdPartyLicenses/VueRouter.txt` |
| [Vue I18n](https://github.com/intlify/vue-i18n) | Frontend localization | MIT; see `ThirdPartyLicenses/VueI18n.txt` |
| [Octicons](https://github.com/primer/octicons) | GitHub mark in connection settings | MIT; see `ThirdPartyLicenses/Octicons.txt` |
| [WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | External overlay web view | Redistribution terms and component notices; see `ThirdPartyLicenses/WebView2License.txt` and `ThirdPartyLicenses/WebView2Notice.txt` |
| [AList](https://github.com/AlistGo/alist) | Supported download-source API (no code included) | AGPL-3.0 (not linked) |
| [toml++](https://github.com/marzer/tomlplusplus) | TOML parser in the native hook runtime (`native/third_party/tomlplusplus`) | MIT |
| [fontTools](https://github.com/fonttools/fonttools) | Optional private font-packaging tool; not included in the launcher runtime | MIT |
| [7-Zip](https://www.7-zip.org/) | Shared native decoder for client archives, launcher archive fallback and plugin ZIP import (`native/third_party/7zip`) | LGPL-2.1-or-later with the applicable unRAR restriction and BSD components; see the included `License.txt` |

Game names, client files and trademarks belong to their respective owners. This
project ships no game client files.

The repository's MIT license applies to original project code. It does not
relicense dependencies, fonts, game artwork, rank images or trademarks.
Dependency notices are copied into launcher distributions under
`ThirdPartyLicenses`; vendored components retain their upstream notices as well.
Redistribution evidence for historical font blobs and game-derived artwork is
still under review; current frontend builds use system fonts without bundling
them. See `docs/release/public-repository-audit.md`.


## IP allocation data

`Servers/china-networks.txt` is derived from the APNIC delegated statistics
snapshot retrieved on 2026-10-07 from
https://ftp.apnic.net/stats/apnic/delegated-apnic-latest . It contains CN allocation
records, not a guarantee of physical server location. A server-advertised region
or explicit routing policy takes precedence. APNIC statistics terms:
https://www.apnic.net/about-apnic/corporate-documents/documents/resource-guidelines/rir-statistics-exchange-format/ .

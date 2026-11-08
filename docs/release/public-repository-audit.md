# Public repository audit

[Chinese](public-repository-audit.zh.md)

Status: publication decision pending. This report records verified review results,
not approval to make the repository public. The latest checkpoint takes precedence
over the historical sections below. Frozen releases are unchanged.

## 2026-11-06 checkpoint

The final dependency refresh pins Vue I18n to official 11.4.18, covering
[mixed interpolation escaping](https://github.com/intlify/vue-i18n/security/advisories/GHSA-8rc2-428q-7ww3),
[embedded-document sanitization](https://github.com/intlify/vue-i18n/security/advisories/GHSA-83rm-6c8r-g728)
and the [script-element fix in 11.4.18](https://github.com/intlify/vue-i18n/releases/tag/v11.4.18).
The previous audit's five high entries came from the same dependency tree.
No translation-to-HTML sink was identified in the Launcher; the sandboxed plugin
settings document is a separate path. These advisories do not establish an
exploitable Launcher path. Tests use the real six-language catalog and translation
engine, including named/list escaping and text rendering. All 413 frontend tests
pass with three optional private-resource skips; the refreshed npm audit reports
zero known vulnerabilities. The exact package license matches the collected text.

The current review uses [an explicit source export](../source-export.md), not a
recursive copy or a change to repository visibility. The final application candidate
at `f379ba415d12e6efd6cba201947b6c2cc347f6e9` contains 1,137 hash-checked files;
72 paths are excluded, including game media, level assets, numeric-health sprites,
local contributor instructions and private sound extraction records. The exported
preview level catalog is empty. The independent module candidate at
`dcc780f0476520a289e46e3b102c5f807625a23f` contains 172 hash-checked files.
These inventories establish candidate contents, not permission to redistribute them.

The local reachable-history scan covered 341 Launcher commits and 3,871 blobs,
and 69 module commits and 421 blobs. Module history includes a native PDB with
developer paths. The remote inventories also expose 59 Launcher and 23 module
pull-request head refs whose complete contents have not been reviewed. Existing
repositories and the empty public-target repository remain private. No visibility,
push or history changes were made during this review; whole-history publication
is not approved by the source-candidate checks.

The Photino.Native 4.0.22 official release package and the local NuGet package
have identical SHA-256
`a3033b446c0577a5f96f0d6db4c81a43aa9ffb5e7b776387bf9209a7e565617b`.
Its Windows x64/ARM64 native and WebView2 loader DLLs match byte for byte; the
official tag identifies source commit `3ba4b937d6337b5c58344445db08a5a9a63f07c8`.
This establishes official-package identity, not a reproducible source build.
The two 7-Zip binaries and corresponding source archive match the hashes in
their committed provenance record. Both final x64 and ARM64 portable candidates
completed full packaging, including runtime notices, decoder source and detached
updater execution. Independent checks compare each package file, update object,
GitHub asset and ZIP member by size and hash. No package was uploaded or published.

The production frontend contains rendered code from nine dependency packages;
their complete licenses match the collected Vue, Vue Router and Vue I18n texts
byte for byte. The six resolved managed/native NuGet dependencies were inventoried
from their actual package metadata, separately from the bundled framework runtimes.
Current controls/styles were compared with local read-only study material: literal
overlaps were common layout declarations and theme values. This limited comparison
does not establish legal originality or cover rewritten historical material; it
did not establish substantive implementation copying requiring a new attribution.
The maintainer's removed design-reference acknowledgement was not restored.

The main window's upstream message callback does not convey trusted document
source information, and its wrapper provides no navigation gate. A reliable fix
needs source validation and navigation/new-window control at the native event
boundary. No remote-code-execution path was reproduced from the ordinary UI;
external links use the system browser, and plugin settings already use sandboxed
frames and message checks. This unresolved boundary must remain visible in the
release decision. Controlled tests and clean builds are recorded in
[the build checkpoint](clean-checkout.md); real-client and interactive elevation
acceptance are separate.

The external WPF overlay has its own event-level source and navigation checks;
29 checks passed on each of Windows x64 and ARM64 using WebView2 157.0.4341.42.
The [browser fixture](../../tools/webview-policy-test/README.md) uses the actual
Overlay policy. These results do not cover the main Photino host. Third-party
source remains read-only evidence and the official dependency packages remain
unchanged; the main host's missing source/navigation contract is an open limitation.

## Historical scope and evidence (2026-10-21)

The 2026-10-21 local scan considered 710 tracked and nonignored untracked candidate
paths, 266 reachable commits and 5,364 reachable Git objects. It inspected 3,546
working-tree files and historical blobs, totaling 66,694,494 bytes. Binary payloads
were inventoried but not treated as text. The redacted findings are retained in an
ignored local artifact; source excerpts and credential values are not in this report.

No scanned text matched the selected private-key or provider-token patterns.
Credential-assignment matches were confined to tests, translation labels and the
publication example. This is a pattern scan, not proof that arbitrary passwords,
binary secrets, unreachable objects or external services are safe. No production
credential was established by these matches.

Three operational handoffs have local preserved copies in the ignored
`docs/private/deprecated` directory and are absent from the delivered source tree.
On 2026-10-21, Launcher remote `main` was replaced with the new root commit
`6bb6b337709f089704a903faa568ab6e4977c174`; its tree matches the reviewed local
source snapshot. Original history remains in local archive branches. Old remote
development branches were removed after local preservation. No files under
`docs/private` or `artifacts` are tracked in this snapshot. This establishes the
new main ancestry, not removal of old GitHub pull-request refs, caches or objects.
Pull request 1 is closed but still references the old development commit; this
external reference needs disclosure review before public visibility.

Bubble Fighter remote `main` is
`09b7e88d996da1bfac13aece53dfb48e7390e6fb`, containing the existing plugin
source inventory. Both repositories were confirmed private through the GitHub
API after delivery. Source availability does not close the redistribution gates
below or prove independent builds of the integrated native modules.

## Corrections in this audit

- Added exact upstream license texts for Vue, Vue Router, Vue I18n, toml++,
  7-Zip and the installed WebView2 SDK, copied from the corresponding local
  dependency distributions. The vendored overlay Vue header identifies 3.5.43,
  matching the installed package supplying its license.
- Added missing dependency entries to `NOTICE.md` and clarified that the project
  MIT license does not relicense third-party code, fonts or game artwork.
- Configured launcher output/publication to carry the project license, notices
  and collected dependency licenses. Standalone overlay output carries its Vue
  and WebView2 notices as well. Frozen packages were not modified.
- Replaced developer-specific client corpus and IDE paths in the public roadmap
  with placeholders. The preceding roadmap is preserved privately.

## Outstanding release gates

### Installed managed dependency evidence (2026-10-21)

The installed NuGet package manifests supply the following version and license
declarations. These are package metadata observations, not an approval of the
complete published binary distribution. Test-only dependencies are outside this
table.

| Package | Version | Declared license | Repository commit in package metadata |
| --- | --- | --- | --- |
| Photino.NET | 4.0.16 | Apache-2.0 | `4e8b40a3732d7bacedccc4c718865990c148823a` |
| Photino.Native | 4.0.22 | Apache-2.0 | Not supplied |
| Tomlyn | 2.10.1 | BSD-2-Clause | `6cc82dd23a2c0aefb858f92a51c1b184e2812103` |
| System.Security.Cryptography.Pkcs | 10.0.13 | MIT | `4099f83977f4d29604a9aeb971fb0a7bc1d5f699` |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | `5535e31a712343a63f5d7d796cd874e563e5ac14` |

Photino.NET declares Photino.Native 4.0.22 as a dependency. Its native package
does not identify a source commit, so that binary-to-source provenance remains
open. The repository already contains the Apache license text in
`ThirdPartyLicenses/Photino.NET.txt` and the Tomlyn copyright and BSD terms in
`ThirdPartyLicenses/Tomlyn.txt`. Self-contained runtime component notices and
the final output inventory still require separate verification.

Portable packaging now collects runtime notices from the exact package versions
in each executable's published runtime configuration. NETCore supplies
`LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`; WindowsDesktop supplies `LICENSE`.
Each executable receives its own `ThirdPartyLicenses/runtime` subtree. Missing
or empty inputs fail collection, and the complete-payload gate checks the outputs
independently. Eight payload tests pass, including missing and empty notice cases;
collection against actual x64 10.0.12 packages also passed. Frozen release output
has not been amended, and this does not verify a complete new publication.

1. The bundled icon font and its URL were removed from the current source tree;
   system fonts and two SVG fallbacks are used instead (see `system-fonts.md`).
   The font is absent from the new main ancestry. Old GitHub refs and caches still
   require review; local preserved history intentionally retains the original.
2. Game-derived rank atlases/icons and the proposed Tales Runner cover need
   provenance and redistribution evidence or exclusion from public source.
   The current notice cannot substitute for that evidence. UI assets have not
   been removed or replaced by this audit.
3. Complete the license inventory for direct and transitive managed/native
   dependencies, including Photino native components, Tomlyn and
   self-contained runtime components. The collected notices are a partial set.
4. Verify the final release carries the 7-Zip corresponding source and complete
   license files. Both decoder DLLs now match the official 26.04 installers;
   `native/third_party/7zip/PROVENANCE.md` records installer, source and binary
   hashes. The unmodified corresponding source archive and complete LGPL text
   are included beside the binaries for subsequent builds. Frozen releases
   remain unchanged and need a separate distribution review.
5. UI control provenance and combined-distribution licensing remain under
   review. Detailed source evidence is retained in the private audit records.
6. Review historical operational documents and commit the reviewed changes
   explicitly before changing repository visibility. A clean export has now
   completed the managed `Release` build with zero warnings and zero errors;
   final public-package checks remain open. A dirty-tree
   build does not prove that ignored inputs are unnecessary.
7. Re-run the complete package from a clean export with package-local NuGet
   package and HTTP caches. The 0.1.20 verification package completed on the
   available D: volume, including frontend checks, self-contained win-x64
   publication, managed and detached-updater verification, file-index
   publication and portable archive creation. The packaging script now restores
   the caller's NuGet environment after both success and failure; this prevents
   a full system volume from aborting publication or contaminating later runs.

## Compression dependency review

Application runtime extraction now uses one engine: the packaged native 7-Zip
library. Client installation, launcher full-archive fallback and plugin ZIP import
share `NativeArchiveDecoder`. SharpCompress and the old RAR-only adapter have
been removed. ZIP creation in release tooling and synthetic test fixtures may
still use standard-library writers; those are not shipped extraction engines.

The shared decoder lists and validates metadata before writing, rejects unsafe
paths, aliases, links and special entries, and preserves consumer-specific file
and byte limits. It verifies native checksums and hashes each output while
writing. Selected release file hashes are checked before transactional install.
Passwords and manifest-pinned RAR volumes use native callbacks; byte-split ZIP
and 7z volumes use a seekable view without copying the compressed payload.

The decoder currently requires Windows and packaged x64 or ARM64 binaries.
Other platforms receive an explicit unsupported-platform error. This change does
not establish that frozen releases carry the newly collected source and notices.
Regression coverage includes ZIP, 7z, RAR4/RAR5, encryption, solid archives,
volumes, bad passwords, missing volumes, invalid paths, corruption, selected
hash mismatches, cancellation and transactional recovery.

## Verification boundary

The latest archive, plugin import and launcher update regression selection passed
202 tests with two environment-dependent skips. New tests cover consumer callback
exception preservation, output handle release and repeated decoder disposal.

An isolated export of the staged source passed all 17 archive tests, including
the external real-format fixture matrix. Its first solution build exposed a
previously committed publication-tool reference to an uncommitted SDK catalog
contract. Including that contract restored a zero-warning, zero-error solution
build. This verifies managed compilation; native builds, frontend builds and
complete publication from a clean checkout remain separate release gates.

The Launcher solution Release build completed with zero warnings and errors.
An isolated export containing only the tracked, nonignored source also restored
and built the complete managed solution with zero warnings and errors; no local
workspace outputs were required for that build. The same export's native
`Release|x86` solution built with zero warnings and errors, and all seven native
test executables passed when their temporary directory was placed on the
available volume. The initial default-directory run failed only because `C:`
had no free space.
The full managed suite passed 735 tests with six environment-dependent skips.
Recovery bootstrap now records and waits for the restored test process before the
fixture is reused, so the process-lock race is covered by the full run.
The focused archive, plugin import and download suite passed 239 tests with one
public-network skip; the separately built win-x64 archive matrix passed all 13
tests. Publish output and a clean checkout remain unverified. The native decoder
currently targets Windows x64 and ARM64. Previous game and self-update acceptance
results remain separate evidence; they do not prove source redistribution
readiness or that every game build works.

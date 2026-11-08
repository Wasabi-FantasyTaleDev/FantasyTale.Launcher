# Decoder binary and source provenance

The x64 and ARM64 decoder libraries are unmodified 7-Zip 26.04 binaries.
On 2026-10-21, both files were compared by SHA-256 with `7z.dll` extracted
from the corresponding official installer. Both comparisons matched.

| Input | Official URL | SHA-256 |
| --- | --- | --- |
| x64 installer | https://www.7-zip.org/a/7z2604-x64.exe | `d54bf805f9f3704d1e8db2fa3498ae7ef2df0312b40b558e7c71c734430a665d` |
| ARM64 installer | https://www.7-zip.org/a/7z2604-arm64.exe | `d4117b95495b0e925334ae7b5bddf2853513274603de15dee6acf03999fbc09c` |
| Corresponding source | https://www.7-zip.org/a/7z2604-src.tar.xz | `9691944c0fe0d01bb49373a704fb983fd33bc98b1738695179dfbf99ac1734f6` |

| Distributed binary | SHA-256 |
| --- | --- |
| `x64/7z.dll` | `899cd73d7fc60e1c1c35c7de0f5bed3dc69bfe2cecbd48014d868a347970a0b1` |
| `arm64/7z.dll` | `eb84a6eb184c378d84742f372e7501d9a99c34a718ea81d4c71cd62b5a207dee` |

The corresponding source archive is retained byte-for-byte at
`source/7z2604-src.tar.xz`. Launcher build and publish output copies this entire
directory into `archive/`, including the source, this record, `License.txt` and
the complete LGPL text in `COPYING.LGPL.txt`. These files must remain with the
decoder when redistributing it. No separate source request is necessary.

Extract the source archive and consult its `DOC` directory and the makefiles
under `CPP/7zip/Bundles/Format7zF` for the library build. The source archive
retains upstream notices and the applicable unRAR restriction. The launcher
loads the decoder dynamically; replacing it with an ABI-compatible library
does not require rebuilding the launcher. Release integrity checks can reject
a locally modified installed file during a later managed update.

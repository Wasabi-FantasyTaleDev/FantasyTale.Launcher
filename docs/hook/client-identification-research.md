# Client identification evidence

## Offline probe

`tools/inspect-client/inspect_client.py` reads PE headers and embedded
Authenticode metadata without executing or changing the candidate. Install its
pinned `requirements.txt`, then pass explicit executable paths:

```powershell
python -m pip install -r tools/inspect-client/requirements.txt
python tools/inspect-client/inspect_client.py C:/Games/Client/BubbleFighter.exe --output artifacts/client-identification/probe.json
python -m unittest discover -s tools/inspect-client -p test_inspect_client.py -v
```

The certificate table is bounded to 16 MiB and 16 records. Both traditional
countersignatures and Microsoft RFC3161 tokens are decoded. The signer's own
`signing_time` is separate from timestamp-authority time. Zero padding inside
retail certificate records is accepted; nonzero trailing data is rejected.
Stale certificate directories remain visible as `signature_metadata_error`,
without discarding otherwise readable PE header evidence.

This tool performs **metadata extraction only**, not file-signature, timestamp
token or certificate-chain verification. Every result says
`signature_verification = "not_performed"`. A certificate or time copied into
a modified executable is not proof that its contents are authentic. The
SHA-256 shown here hashes the signer certificate, not the executable. No
whole-file MD5 is used for build identification.

## Observed retail identities

On 2026-10-10, 41 local candidates were inspected: 19 retail releases and 22
renamed, unpacked or repaired variants. All 19 retail signer-certificate plus
timestamp-authority-time tuples were distinct. Certificate identity alone was
not distinct: nine retail releases, including different regions, shared one
certificate. FileVersion and ProductVersion were absent in the inspected retail
metadata.

| Client release | PE linker timestamp | Timestamp-authority time, KST (UTC+09:00) |
| --- | --- | --- |
| CN 2011, 0.1.5.005 | `4D9ECC57` | 2011-04-08 18:35:38 |
| CN 2012 | `505DD60C` | 2012-09-23 00:16:40 |
| CN 2013, 0.2.35.001 | `51CD5DE5` | 2013-06-28 19:02:07 |
| CN 2014, 0.2.46.001 | `533CFD5F` | 2014-04-03 15:20:53 |
| CN 2014, 0.2.48.001 | `5383216F` | 2014-05-26 20:16:05 |
| CN 2014, 0.2.50.004 | `53DA0A65` | 2014-07-31 20:19:07 |
| CN 2014, 0.2.51.002 | `5420DCC3` | 2014-09-23 11:41:03 |
| CN 2015 | `5582B5CD` | 2015-06-18 22:03:52 |
| CN 2016, 0.2.67.004 | `56F102B0` | 2016-03-22 08:32:26 |
| CN 2016, 0.2.70.4 | `5767E697` | 2016-06-20 12:52:06 |
| CN 2016, 0.2.72.01 | `57B6D7EF` | 2016-08-19 09:58:23 |
| CN 2017, 0.2.74.01 | `582C1AC7` | 2016-11-16 08:38:48 |
| CN 2018, 0.92.1 | `5BDC5A1F` | 2018-11-02 14:10:04 |
| CN 2019, 0.2.98.3 | `5CEF8FF9` | 2019-05-30 08:12:03 |
| ID 2014 | `52E07057` | 2014-01-23 10:40:10 |
| ID 2015 | `54C99FF6` | 2015-01-29 11:51:56 |
| KR 2015 | `55376C47` | 2015-04-22 18:43:53 |
| China Taiwan 2019 | `5C0F4941` | 2018-12-11 14:22:30 |
| KR 2026 | `69FD90C8` | 2026-05-08 16:31:01 |

Raw local results are retained in the ignored
`artifacts/client-identification/authenticode-and-unpacked.json`. Retail labels
come from local installation folders; they do not assign an unproven wire build
number to a release absent from the launcher catalog.

## Unpacked candidates and remaining implementation

All inspected unpacked candidates retained their corresponding retail PE linker
timestamp. Twenty candidates had an unusable certificate table; another repaired
candidate had no table. A renamed packed variant retained the original signature
metadata, which is expected to identify the same release rather than a new build.
These are observations for this corpus, not a guarantee that every unpacker keeps
the timestamp.

Known candidate names include `BubbleFighter.exe`, `CrazyShooter.exe`, their
`U.exe`, `.Unpacked.exe`, `.asInvoker.exe` and `.asInvokerU.exe` variants, plus
`unpacked_BubbleFighter.exe` and `unpacked_BubbleFighter.repaired.exe`.
Names identify candidate files only. They do not establish a build or patch
compatibility. Launcher aliases must be definition data, and an unknown or
ambiguous candidate must not be silently assigned to a build.

The intended default is signer certificate plus timestamp-authority time for
signed candidates, corroborated by PE/build evidence. The external table is named
`client-versions.toml`; it is data shipped beside the game definitions and may be
replaced by a verified launcher catalog update. Its internal key remains the stable
wire id such as `kr-1256`; its externally displayed release date is the signing
instant converted to Korea Standard Time (UTC+09:00), for example `2015-04-22`. The localized external name therefore reads
like `韩国 2015-04-22`; the internal build id remains `kr-1256`.
Multiple resources that share the same executable identity can intentionally point
to one build row; a resource-only update does not create another build.
Unsigned unpacked candidates
need the documented header fingerprint and, where headers no longer distinguish
them, corpus-proven byte locators. A locator must be anchored in readable code,
have a bounded search and reject ambiguous matches. No such additional byte
locator has been established by this metadata investigation.

This probe does not change launcher registration or launch behavior. The current
catalog contains ten builds; three CN launch definitions remain `unverified`.
Existing installation display and launch resolution still use the one configured
executable. Follow-up work must integrate candidate aliases, default signed
identification and explicit unsigned fallback into registration, launch planning,
installation display and stopped-client recovery together. Preserve per-plugin
expected-byte checks independently of release identification. Desktop add-client
and per-build launch acceptance remain separate from these offline observations.

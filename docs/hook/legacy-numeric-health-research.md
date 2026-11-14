# Legacy numeric-health compatibility research

The numeric-health feature remains one plugin identity. Its external descriptor
selects client-specific image checks, number forwarding and HP access. The
original Life counter and the added HP digits are separate values: the integer
accessor divided by 20 feeds Life, not HP. The original number wrapper is retained
as a possible draw interception point without replacing its Life text.

## Measured HP and Life calls

The following call sites were checked against local unpacked PE images. Exact
five-byte call signatures are retained in `client-versions.toml`; the offline
verifier checks the image identity, exact call bytes, relative target and the
HP call's subsequent x87 floating-point store. It separately checks Life calls
and the number wrapper's caller cleanup of seven argument slots.

| Build | HP getter / call VA | HP contract | Life getter / call VA | Number wrapper / call VA |
| --- | --- | --- | --- | --- |
| cn-279 | 0x4063E0 / 0x717457 | EDI field, ST(0) | 0x4066D0 / 0x7174D1 | 0x53AD30 / 0x7175D0 |
| cn-324 | 0x40FD50 / 0x6D36B2 | thiscall, ST(0) | 0x64ED70 / 0x6D3AB9 | 0x8F6F50 / 0x6D3C36 |
| cn-406 | 0x41C9F0 / 0x6E5F23 | thiscall, ST(0) | 0x659110 / 0x6E6361 | 0x9CC640 / 0x6E6534 |
| cn-408 | 0x41EFB0 / 0x795591 | thiscall, ST(0) | 0x4AD140 / 0x7959C2 | 0xA9EEF0 / 0x795B86 |
| cn-705 | 0x41FF40 / 0x769F22 | thiscall, ST(0) | 0x4B86D0 / 0x76A354 | 0xA65590 / 0x76A51B |
| cn-706 | 0x4205E0 / 0x76B462 | thiscall, ST(0) | 0x4B8800 / 0x76B894 | 0xA675B0 / 0x76BA5B |

All six Life paths divide their integer result by 20. This divisor is recorded
as `life_divisor`, never a normalization rule for HP. Health call evidence requires
an explicit `health_accessor_abi`: `thiscall_f64` or `edi_field_f64`. An integer
Life call substituted for an HP call fails the offline floating-consumer check.
This check verifies the consuming instruction; it does not prove full runtime
object provenance or resource ABI compatibility.

## Player and gauge provenance

All six measured HUDs obtain their player through the HUD's first pointer,
recorded as `player_capture = "hud_owner"`. Research rows also retain the exact
argument-preparation bytes immediately preceding the HP call. The offline tool
checks their exact contents, 1..64-byte bound and contiguous end at the HP call;
their meaning is derived from the consuming code, not inferred from byte matching.

| Build | Argument preparation VA | HUD register | Gauge X/Y byte offsets | Gauge consumer |
| --- | --- | --- | --- | --- |
| cn-279 | 0x71744F | EBX | 184 / 188 | cn-279 sub_74D200 |
| cn-324 | 0x6D36A1 | EBP | 224 / 228 | cn-324 sub_63BC90 |
| cn-406 | 0x6E5F12 | EBP | 248 / 252 | cn-406 sub_645AB0 |
| cn-408 | 0x79557F | EBP | 248 / 252 | cn-408 sub_6F2FA0 |
| cn-705 | 0x769F0F | EBP | 248 / 252 | cn-705 sub_6C4970 |
| cn-706 | 0x76B44F | EBP | 276 / 280 | cn-706 sub_6C5180 |

CN-279 reads `[EBX]` into EAX and prepares EDI = EAX + 372. CN-324, CN-406
and cn-408 prepare ECX from `[EBP]`; cn-705 and cn-706 read
`[EBP]` into EDI before copying EDI to ECX. Therefore a preserved caller EDI
must not be assumed to be the player at every later number-wrapper call.

The gauge consumers read the adjacent float coordinates shown above. These are
static layout evidence; matching the Indonesian visual placement, scaling and
selected/spectated player still require runtime acceptance.

## CN 2011 (`cn-279`)

The unpacked image has PE timestamp `0x4D9ECC57` and image size `0x9C8000`.
CN-279 `sub_716880` holds the HUD in EBX. Its HP gauge calls
`sub_4063E0` with EDI pointing to player + 372, returning floating HP through
ST(0). Maximum HP is read as a float at player + 5184. The same gauge ratio
feeds the HP blink and glow paths. The later Life text comes from
`sub_4066D0(player + 4732) / 20` and is passed to `sub_53AD30`.

Virtual byte offset 544 occurs in a later reload/inventory path; it is not the
maximum-HP accessor. This corrects the previous research record that conflated
Life with HP. The descriptor now records `health_field_offset = 372`,
`maximum_health_field_offset = 5184` and the independent Life field offset.
The player and anchor paths above are statically established; their runtime
capture and sprite contracts still need acceptance before this row can be supported.

## CN 2012 and CN 2013

CN-324 `sub_6D3450` reads floating HP with `sub_40FD50` and maximum HP through
virtual byte offset 328. Its unpacked image has timestamp `0x505DD60C` and size
`0xA2B000`. CN-406 `sub_6E5BD0` uses `sub_41C9F0` and maximum-HP byte offset
340; timestamp is `0x51CD5DE5`, image size `0xBCD000`. Both hold the HUD in EBP.
Their separate integer `/20` paths feed Life text. CN-406 remains an early
controlled-render candidate because that client has already been used for
launcher login testing; this does not establish plugin acceptance.

## CN 2014 resource releases

The `cn-408` HUD (`sub_795250`) uses maximum-HP byte offset 348;
timestamp is `0x5383216F`, measured unpacked image size `0xCDC000`.
The `cn-705` HUD (`sub_769BE0`) and the later `cn-706` HUD
(`sub_76B120`, resource release `0.2.51.002`) use byte offset 352. Their
respective timestamps are `0x53DA0A65` and `0x5420DCC3`, both with measured
unpacked image size `0xC79000`. The HUD is retained in EBP.

`cn-404` (CN2014 0.2.46.001) uses `sub_41F0A0` from `sub_791CA0`, maximum-HP
slot 348 and the number call at `0x7925D6` to `sub_A961F0`. Its measured unpacked
image has timestamp `0x533CFD5F` and size `0xCD8000`; the earlier `0xCD5000`
identity remains recorded separately. Resource releases must keep their
own PE preflight until executable equivalence and wire/build aliases are proven.
A resource-only change does not by itself imply a new executable build.

## Later visible builds

The descriptor now covers every visible non-Indonesian build. These additional
rows remain research-only. Exact native getter or HUD call bytes, image identity
and the seven-slot number call were checked against each unpacked executable.

| Build | HUD / HP getter | Maximum HP slot | Number call / target | HUD / selected player at number call | Anchor / white style |
| --- | --- | --- | --- | --- | --- |
| cn-404 | cn-404 sub_791CA0 / cn-404 sub_41F0A0 | 348 | 0x7925D6 / 0xA961F0 | EBP / first HUD pointer | 248 / 5 |
| cn-717 | cn-717 sub_85EA50 / cn-717 sub_42D7C0 | 356 | 0x85F459 / 0xBB1AF0 | EBP / EBX | 304 / 5 |
| cn-735 | cn-735 sub_9C80D0 / cn-735 sub_445D10 | 400 | 0x9C8E2A / 0xD50E50 | EBP / saved stack slot | 1120 / 5 |
| cn-774 | cn-774 sub_85D830 / cn-774 sub_531A60 | 420 | 0x85E5DF / 0x1004430 | EDI / ESI | 1116 / 6 |
| kr-1256 | kr-1256 sub_68DD90 / kr-1256 sub_4A1250 | 368 | 0x68EAAD / 0x106FEF0 | EDI / ESI | 1120 / 6 |
| tw-454 | tw-454 sub_A2DE60 / tw-454 sub_748EC0 | 436 | 0xA2EDD9 / 0xF82E90 | EDI / ESI | 1116 / 6 |

CN-717 `sub_85EA50` resolves the HUD's index holder through the player table and
retains the selected object in EBX. Its first HUD pointer is not the player.
CN-735 `0x9C8121` saves its resolved player at the function's working ESP+48;
the number call adds 28 bytes of arguments, so the saved pointer is 76 bytes
past the first original argument. EDI is reused for timer values before this
call. `caller_stack` must read that saved pointer and never fall back to EDI.
The shared interception stub captures the registers before C++ runs and preserves
all original argument slots. Controlled tests exercise EBX/ESI selection, the
saved stack slot, rejected pointers and the later white style 6.

CN-774 and TW-454 inline protected HP reads in their HUDs. Their native `GetHp`
functions read the same player +480 field, validate it through the client's own
helper and return the float through ST(0). The descriptor retains the entire
getter bytes instead of inventing a direct HUD call. KR-1256 `sub_4A1250` reads
its protected field at +496. At `0x68E28C` the HUD reloads ECX before storing
ST(0), so `health_result_bytes` pins both that MOV and the following FSTP.
The offline checker rejects a missing, changed or nonfloating consumer.

Life becomes a direct integer field in the later builds: CN-735 +8976,
CN-774 +796, KR-1256 +684 and TW-454 +800, each divided by 20. It remains
independent of the floating HP getter.

Measured reconstructed sizes are `0x1093000` for CN-717, `0x17BE000` for
CN-735 and `0x1B80000` for CN-774. The earlier recorded identities remain
separate entries in each research row. TW-454 is timestamp `0x5C0F4941`, size
`0x1AD5000`; KR-1256 is timestamp `0x69FD90C8`, size `0x1B36000`.

## Native digit and sprite layouts

Runtime resource reads and plugin-owned number tables now use explicit
`native_style_slot`, `native_style_sprite_index`, `sprite_frames_offset`,
`sprite_texture_offset` and `texture_resident_offset` fields. The renderer still
uses 128 character mappings starting at word 2; that common structure is proven
by the consuming functions below. Private tables are bounded to 4096 words.
The sprite index must follow the selected character map; member offsets must be
four-byte aligned in 4..4096, with texture outside the frame begin/end pair.

| Build | Number consumer | Private style / sprite word | Frame begin/end | Sprite texture | Resident texture member |
| --- | --- | --- | --- | --- | --- |
| cn-279 | cn-279 sub_53AA50 | 21 / 2822 | 48 / 52 | 60 | 72 (cn-279 sub_535410) |
| cn-324 | cn-324 sub_8F6CE0 | 21 / 2822 | 48 / 52 | 60 | 72 (cn-324 sub_8D0B40) |
| cn-406 | cn-406 sub_9CC3E0 | 21 / 2822 | 120 / 124 | 136 | 144 (cn-406 sub_9B9FD0) |
| cn-408 | cn-408 sub_A9EC80 | 22 / 3079 | 120 / 124 | 136 | 144 (cn-408 sub_A78B00) |
| cn-705 | cn-705 sub_A65320 | 22 / 3079 | 120 / 124 | 136 | 144 (cn-705 sub_A3C930) |
| cn-706 | cn-706 sub_A67340 | 22 / 3079 | 120 / 124 | 136 | 144 (cn-706 sub_A3F290) |
| kr-812 | kr-812 sub_AE6A40 | 22 / 3079 | 120 / 124 | 136 | 144 (kr-812 sub_ABDA20) |

The early renderers only switch on private styles 18..21. Style 22 leaves the
frame pointer unset there and must not be reused from the active KR descriptor.
CN2013 has the later sprite members but still the earlier private-style table.
CN2014 adds style 22 and the later sprite-word locations. Each frame retains its
sprite owner, ordinal and rectangle in the common eight-word record.

Controlled x86 fixtures read each row's layout and exercise ten-frame yellow
digits plus the supplied percent artwork, map selection, residency checks and
white fallback after a rejected frame owner. They do not execute the original
client renderer. Sprite loading still requires separate verification of each
build's string object, options object, manager and callable entry signatures;
the active borrowed string/options contracts remain KR-specific.

## Safety boundary

### Resource path and options evidence

CN-279 `sub_52F3F0` and CN-324 `sub_8D3110` consume a four-byte reference-counted
UTF-16 string object. Both access the length at string data -20; their copies
retain or release the preceding header. This is `resource_string_abi =
"cow_wstring4"`, with no inline capacity. A borrowed MSVC string cannot be
passed to these functions. The runtime rejects this ABI for supported rows
and suppresses the resource call if it reaches the loader without an adapter.

CN-279's options object is 36 bytes: `sub_408D70` initializes the trailing
string at +32, and `sub_74DBE0` destroys that member through `sub_401510`.
The earlier 32-byte record and shared-string claim were incorrect. CN-324
`sub_401A40` additionally writes the flag at +36, requiring 40 bytes.
CN-406 `sub_99E930` uses `msvc_wstring24`, with inline capacity seven, and
the 88-byte options record initialized by CN-406 `sub_4170B0`.

The runtime reads the string ABI, inline capacity and options size from a
descriptor. KR2015 remains the only supported row and continues to use its
verified 88-byte options contract. The CN rows retain research status because
their manager globals, callable entry signatures, resource residency paths and
retail draw have not been jointly accepted. A matching object size alone is not
enough to enable a hook.

Early sprite layouts also differ. CN-279 `sub_53A0B0` reads frame begin/end
at sprite +48/+52 and texture at +60; cn-324 `sub_6D3450` uses the same frame
and texture offsets. CN-406 `sub_6E5BD0` reads frames at +120 and texture
at +136, matching the active KR layout at those members. This does not establish
the loading-options, string, texture-residency or number-map ABI. The KR resource
structures must not be used in early CN builds merely because a wrapper matches.

The Indonesian build already supplies this display and is not a plugin target.
The current package advertises only the verified KR 2015 (`kr-812`) descriptor.
The external `native/samples/bubblefighter.fantasy.numeric-health/client-versions.toml`
contains the supported KR row and research-only CN rows through the 2019 corpus.
The loader rejects research rows before registering a locator. Earlier Life/HP
mislabeling therefore did not activate an incorrect CN hook.

Thirteen measured images pass 61 offline checks. The CLI retains fourteen
deferred runtime checks, including the manifest locator. The runtime selects thiscall or EDI-field floating HP and virtual or
player-field maximum HP from explicit supported-row fields. The EDI stub preserves
EDI and returns ST(0); controlled x86 tests verify fractional values, repeated
calls, original Life text and failure suppression. These adapters do not prove
that any CN player or sprite object has the required runtime layout.
Remaining work includes runtime player/anchor acceptance,
resource contracts and controlled render traces, followed by
retail acceptance when authorized. Adding a research row to `plugin.toml` before
these contracts are complete would allow an unsafe hook and is prohibited.

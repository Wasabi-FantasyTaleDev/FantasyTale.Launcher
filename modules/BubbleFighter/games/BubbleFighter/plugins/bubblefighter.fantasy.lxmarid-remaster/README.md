# Luxury Marid remastered model

An optional store plugin for Luxury Marid (LXMarid, character item 300010).
It replaces the base body, face and default textures with remastered resources.
Marid (300008), costumes, character ownership and server behavior are unchanged.

Enable the plugin for the selected client before launching. Launcher verifies the
payload, backs up the target archive and restores it after the game exits.
Disabling the plugin leaves original resources in use on the next launch.
Stopped-client recovery uses the existing Launcher deployment journal.

## Supported clients

| Build | Client | Replacement |
| --- | --- | --- |
| cn-324 | China 2012 | Default.bfz |
| cn-406 | China 2013, 0.2.35.001 | Default.bfz |
| cn-404 | China 2014, 0.2.46.001 | Default.bfz |
| cn-706 | China 2014, 0.2.51.002 | Default.bfz |
| cn-717 | China 2015 | Default.bfz |
| cn-735 | China 2016, 0.2.70.4 | Build-specific PKN |
| kr-812 | Korea 2015, including the matching Classic client | Default.bfz |
| id-400 | Indonesia 2014 | Default.bfz |
| id-1809 | Indonesia 2015 | Default.bfz |

Identity comes from executable fingerprints, not folder names. cn-717 and cn-735
retain their modern-family classification. cn-279 has no LXMarid. cn-774, tw-454
and kr-1256 already include the remaster. cn-738/cn-742 are not current Launcher
profiles and are not advertised here.

## Resource evidence

- cn-774: 46 members under `Models/Characters/LXMarid/Default/` span the
  `Packs/ModelsChrPlLXMarid_*.pkn` family. All 46 hash-match kr-1256.
- kr-1256 adds a 40-byte `Head.smf`, whose purpose is unverified; it is excluded.
- Loose archives target `Models/Characters/LXMarid/Default/Default.bfz`, retaining
  its path-derived BFZ key. CN2012 manual replacement passed an owner in-game test.
- cn-735 `Packs/ModelsChrPlLXMarid_00003.pkn` has 1029 members. Its 44 old Default
  members are replaced with 46 new ones; all 985 other members remain byte-identical,
  producing 1031 members. cn-738/cn-742 packs are not interchangeable with this build:
  they contain 50 additional non-Default members and eight changed files.
- tw-454 `Config/ShopItem2.xml`, row 300010, supplies `優雅小蜜桃`;
  kr-1256's same row supplies `럭셔리 마리드`.

The maker verifies output archives and all decoded member hashes. These checks do
not replace visual acceptance on each client; only CN2012 has an owner-confirmed
in-game resource-swap test at initial publication.

## Packaging

Run the following as one command, using the roots containing client executables:

```text
python tools/package-lxmarid-remaster.py --archive-tool <bffarchive.exe> --source-client <cn-774-root> --reference-client <kr-1256-root> --pkn-client <cn-735-root> --output <new-output-directory>
```

Output contains a clean plugin folder, an importable ZIP and a verification report.
Source and round-trip trees remain outside the distributable folder. The generated
manifest records actual archive sizes and SHA-256 digests; encrypted archive bytes
can vary between runs while decoded members remain identical.

Client resources are not committed and are not covered by the software license.
Packaging requires locally available client archives. Users install this optional
plugin manually from the store; it adds no client-policy entries.

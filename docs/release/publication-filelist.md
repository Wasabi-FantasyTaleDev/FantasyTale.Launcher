# Publication file lists

Launcher releases already carry a locally generated file list in `current.toml`.
Each row contains the installation path, content-addressed source path, byte size
and SHA-256. Publication reads the previous public index and binds its bytes to
the initial catalog digest. Objects with the same source path, digest and size
are reused without downloading or uploading them again. Only paths under an
`objects` directory whose filename is their full SHA-256 qualify for reuse.
New or otherwise unlisted objects still use public byte verification. The new
index is published after content verification, with the existing concurrent
catalog-change checks. This optimization does not audit later out-of-band
changes to already published objects; clients verify downloaded bytes against
the selected index. Store/plugin publication and complete-archive verification
still have separate paths and are not claimed to use this optimization.

Launcher version discovery reads the small release index and compares revisions;
it does not download all payloads. Local staging and download integrity checks
are separate from version discovery.

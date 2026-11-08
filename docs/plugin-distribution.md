# Plugin distribution

[Chinese](plugin-distribution.zh.md)

File payloads and memory patches may declare `client_languages` and
`exclude_client_languages` starting with runtime 3.7.3 / Launcher 0.1.68. These
are exact selected **game** language tags, compared without case, independently
of the Launcher UI. An omitted inclusion list permits every language; exclusions
then suppress the named tags. Duplicate tags and overlapping lists are errors.
Only active files participate in deployment destination conflicts. Keep all
`[[files]]` rows contiguous and all `[[patches]]` rows contiguous for both parsers.

For archive-format store releases, the immutable public import ZIP is also the
installation payload. Its size and digest are verified as usual; publication
does not upload a second identical ZIP under another storage path.

## Identity migration and retirement

A renamed plugin declares every former identifier in `[plugin].aliases`, for
example `aliases = ["example.first-name", "example.second-name"]`. Use a strictly
newer package version and retain the former game identity. The publisher includes
the complete definition inline so update discovery can find the successor before
downloading it. The shared installer does not contain a table of special plugin
names. Later renames retain all earlier aliases; an installed intermediate owner
is accepted only when the final successor explicitly supersedes it and advances
its version. Unrelated owners and cycles are rejected.

Selection migration waits for a verified compatible installed successor. It
preserves enabled state and supported option overrides, reconciles server rules
and saved/imported schemes, and does not activate an interrupted download. Running
clients keep their current session; pending upgrades apply when their client is
idle and only if the user has not changed the saved selection in the meantime.
The native manifest reader supports aliases starting with runtime 3.7.2; these
releases require Launcher 0.1.57 or newer.

Retirement is distinct from renaming. Schema-two catalogs may retain
`[[deprecated_plugins]]` rows with `id`, `game`, localized `name`, and optional
localized `reason` after withdrawing the package rows. Retired releases are hidden
from store offerings. An enabled catalog removes every installed version and
its future-launch selections, then shows one information card per removed plugin
in a retirement dialog. Reasons remain below the cards. Saved schemes remove that
identity too. Active sessions retain payloads until all client leases are free.
Active transfers defer retirement until a subsequent catalog pass.

Retirement matches plugin ID and game, independently of the package's original
download source or receipt. Changing a source ID or address and importing a ZIP
do not prevent later retirement through an enabled store. Bundled application
files are never uninstalled. Renamed
IDs must not also be declared retired: that would discard the settings migration
is intended to retain. Apply publisher retirement data with `compose_catalog.py`
only after the lifecycle-capable Launcher is publicly installable; preparing this
metadata does not itself change the public catalog.

The launcher distributes exactly five Bubble Fighter plugin packages under
`games/BubbleFighter/plugins`: communication, crash recorder, Overlay foundation,
Game Control Support and Native Style Components. Game Control Support currently
retains the existing notification service pending its separate native provider.

`game.default_plugins` explicitly lists communication, Overlay foundation, Game
Control Support and Native Style Components. Diagnostics is not listed. There is
no rule enabling every bundled package. New compatible clients receive those
four selections. Existing explicit disabled selections retain their state.
An unavailable default is not selected; when an updated package first supports
the client's build, inventory/launch reconciliation adds and enables it. Merely
downloading an optional plugin does not authorize enabling it.

The same five identities are store-managed and declare an update source, so they
can discover independent store releases without requiring a newer launcher.
Launcher bundles also advance older selections, preserving activation and
compatible option overrides. A newer installed store release is never downgraded
to an older bundled release. Store installation still verifies the immutable
manifest, package identity, size and hashes.

Other distributable authoring packages live in `store-plugins/BubbleFighter`.
They are not part of the host's game-file copy rules or the browser's bundled
inventory. Authentication source ownership is declared in `game.plugin_sources`; activation
and version rules live in each build's typed `client_policy_rules`. These declarations
contain no executable payload and do not install components. The normal client installation/recommendation flow
downloads them explicitly. KR1256 requires its authentication and shop-access
components; Chinese profiles require their authentication component. Required
and recommended rules cannot overlap on the same build.

Unfinished Forward Jump packages live in `experimental-plugins/BubbleFighter`.
They are neither bundled nor published; the store publisher also rejects their
identities. Moving authoring folders does not remove user-installed library
packages, client files, accounts or saved settings. Public store publication is a
separate step from changing local authoring metadata and previews.

After a renamed plugin is publicly installed and its `aliases` metadata names
the former IDs, the publisher can withdraw the obsolete store cards with
`--retire-aliases-for <current-id>`. This reads the published definitions rather
than a hard-coded rename list. It retains every version of the current ID,
including rollback releases, and does not delete immutable package objects or
uninstall existing selections. The publisher rejects ambiguous/cyclic ownership
and replacements that lose a former release's client-build coverage. Verify the
replacement downloads before withdrawing its former cards.

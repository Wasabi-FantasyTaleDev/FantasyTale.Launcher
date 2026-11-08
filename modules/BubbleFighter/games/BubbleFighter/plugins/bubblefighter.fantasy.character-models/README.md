# Character model switcher

This resource plugin lets each client choose Original, New, or Classic for every
character that the client or an installed expansion actually provides. The first
installation stores an empty selection and leaves every client archive unchanged.
Selecting the generation already supplied by the client is also a no-op.

The launcher reads the current BFZ or PKN files from the selected client, merges
all selected member edits, verifies unchanged archive records, and stages the
result through the recoverable runtime transaction. Originals are restored after
the game exits. A damaged or interrupted preparation is recovered before another
launch, and an unchanged input/selection reuses its verified cache. Launch shows
measured archive preparation progress and can be cancelled before replacement.

The package definition contains only plans and content-addressed payload names;
the proprietary payload files are delivered by the store and are not part of the
source repository. Future resource plugins can use the game module's archive
composer and character-model capability to share one physical archive safely.

The model scope follows same-region generation comparisons of cn-717, cn-735,
cn-738, cn-742 and cn-774, checked against kr-812, kr-1256 and tw-454. Later
outfits that have no matching source are preserved. Unrelated regional updates
and the two Bazzi effect-only event changes are excluded.

Classic Dizni includes the reviewed equipment heads and their exact companion
textures. Where keyed transparent pixels would write depth over the separate
face, the offline maker subtracts that footprint from the head triangles. The
classic face stays at its native position. Opaque headgear, material subsets and
skin influences are retained; a head with no intersecting aperture is unchanged.
The default head matches the owner-accepted 1.0.4 repair byte for byte. The
equipment set has three-view Item Viewer comparisons; these are not a claim of
in-game acceptance for every outfit or regional client.

Package creation requires Python with NumPy and Pillow, a source-member inventory,
boundary evidence, the reviewed aperture evidence and the companion .NET
materializer. Both the source members and referenced textures are checked against
their recorded hashes before conversion. Converted payload hashes must match the
reviewed outputs. The maker puts each equipment texture beside its mesh to avoid
regional shared-pool differences, and emits only edits needed by that target.

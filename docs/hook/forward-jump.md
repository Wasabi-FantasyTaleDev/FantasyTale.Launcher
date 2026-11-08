# Native forward-jump capability research

The capability preview is rejected as an implementation of ordinary forward
jump. Local gameplay feedback identifies its behavior as the forced-jump
bracelet effect: it also permits a jump from a roll. This exceeds the requested
change to ordinary W + Space input. The effect setter below is useful evidence
for that equipment capability, not proof of the newer client's default movement.
Keep this prototype disabled for normal testing; it has not been published.

Follow-up testing also showed that the capability is conditional: it does not
reliably convert a held W key into a jump, and the original effect can fail under
some native state, resource or timing conditions. Treat it as an eligibility
input only. The requested behavior still requires the ordinary input and action
transition path to be identified.

The CN2011 and CN2012 input loops are otherwise structurally the same: W arms
the forward timer, Space produces the jump/roll request, and the jump predicate
runs before the roll fallback. CN2012's extra timed attributes 21/22 are supplied
by native attribute/event code rather than by key handling. Their lifecycle is
not yet proven, so a plugin must not grant them as a shortcut.

The CN2011 (`cn-279`) preview enables an existing native capability for the
ordinary path. The running path has a separate client gate in `sub_7D6980`:
because this legacy client has no `bubbleJumpEnable` setting, state 301 rejects
the request before the normal jump checks. While W/Up plus Space/Num0 is held
in state 301, the preview temporarily changes the gate at RVA `0x7D69E4`
(`74 13` to `EB 13`) and restores it as soon as the request or running state
ends. The rest of `sub_7D6980`, including state, effect, resource and cooldown
checks, remains authoritative. This is a local research candidate, not a
general executable patch.

## Client evidence

CN2011 `sub_7CC230` first checks the actor state. Outside state 101, forward
movement requires the byte at `sub_7DEF40(actor) + 240`. Later, the ordinary
direction branch permits a stationary jump or, when that byte is set, positive
forward movement with zero sideways movement. The resource comparison at
`0x7CC3FE`, action availability, cooldown and status checks still follow.

CN2011 `sub_731060` and `sub_731150` use the same byte when exposing native
direction/action eligibility. Updating only one instruction in the jump
predicate would leave these other decisions inconsistent.

The byte is a native capability, not an inferred shortcut: CN2011 `sub_651960`
sets exactly byte 240 for effect identifiers 100171 and 100224. CN2011
`sub_661270` initializes an effective capability block at object offset 220 and
applies the selected character/equipment effects to it. CN2011 `sub_7DEF40`
resolves the effective block, including the embedded fallback at actor offset
3776. The preview uses that accessor rather than assuming all actors own the
same block address.

CN2012 (`cn-324`) `sub_51C0C0` checks a corresponding protected capability at
offset 1668 of the object returned by virtual slot 292. Its ordinary direction
branch also requires positive forward movement and zero sideways movement when
the capability is active. There are additional checks for active statuses 21
and 22 when that capability is absent. Their broader gameplay semantics and the
initialization source of the newer default are not established here. The
preview does not grant either status or copy those exceptional paths.

## Rejected preview implementation

The native x86 plugin validates the build fingerprint, full accessor signature,
local actor selection and timer identity. It follows recreated game windows
and runs through the game window thread. The native accessor supplies the
effective block; only a boolean value in committed, writable, non-image memory
can change from zero to one. No other stat, inventory slot, executable byte or
network packet is changed. The capability is process-local and rebuilt from
game data when the client starts again.

The research branch now has a guarded experiment: it handles ordinary W + Space
and the separate running W + Space path without granting the equipment
capability or allowing a jump to interrupt a roll. It sets the capability byte
only for states 101/102/103 and uses the guarded two-byte running gate only for
state 301. Both changes require W/Up plus Space/Num0 to be physically held and
are restored when the request or state ends. It remains local and disabled pending real-client
verification. Verify stationary jump, lateral
and backward rolls, repeated Space, equipment changes, respawns, round changes
and blocked/dead states retain their native behavior. Auto Dash can remain
enabled to test their interaction. A successful build or capability log does
not establish gameplay acceptance.

The corpus also shows a client-side path: `sub_4B1920` and `sub_506C70` call
`sub_46B670` during local character or scene transitions, where the attribute
holder is updated without requiring a dedicated movement packet. This makes a
local initialization defect a stronger hypothesis than a missing server feature,
but the protected value's complete lifecycle still needs to be traced.

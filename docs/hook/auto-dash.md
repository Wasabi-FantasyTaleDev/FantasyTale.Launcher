# Auto Dash movement gate

Version 0.2.2 adds TW454 to the package and its implementation table. The prior
recommendation could not resolve a compatible store package, leaving an ID-only
card and an unavailable-plugin error. The binary stays unchanged; the existing
patch implementation reads the new build mapping from the package sidecar.

tw-454 sub_801440 reads the configured forward key and Up-arrow key-down state.
Its timer-empty branch at tw-454 VA 0x801AA4 normally reaches the 300ms arm path
at 0x801AF0. Replacing `74 4A` with `EB 26` reaches the original checks at
0x801ACC instead: vtable slot +948 and tw-454 sub_80D1C0 still decide whether
tw-454 sub_80BE70 may run. That eligibility function retains ordinary actor
states 101/103, forward movement, resources and movement restrictions. State 104
still uses tw-454 sub_80BB10 when the forward key is released. A unique signature
guards the instruction, and modeled x86 tests cover first press, active/expired
timers, denied eligibility, the blocking virtual check and key release.

The optional `bubblefighter.fantasy.auto-dash` patch package initially targets
`kr-812`. Other builds must prove their own input and movement path before a
build-scoped variant is added. `kr-1256` has native support and is excluded.

In kr-812 `sub_5DBA00`, the configured forward key is read from actor offset
11756. The key-down path at VA `0x5DBDE1` precedes the timer query at
`0x5DBDF1` (kr-812 `sub_A5FD80`). This timer tests an active interval; the
alternative path calls kr-812 `sub_A5FCB0` with 300 milliseconds to arm it.
The conditional branch at `0x5DBDF8` (`74 23`, RVA `0x1DBDF8`) selects that
alternative when the timer is not active, producing the double-press gate.

Replacing just this branch with two NOPs lets the first key-down reach the
original checks and kr-812 `sub_5E0A50`. The virtual check at slot 780 and
kr-812 `sub_5E1750` remain unchanged. The latter checks forward movement,
actor state and other movement eligibility conditions; it is not bypassed.

The package uses a unique 53-byte executable signature surrounding the branch,
with wildcard bytes only for the two bytes being changed. This permits both
original and already-installed detection while the expected-byte check rejects
other modifications at the write site. The runtime's existing memory writer
and client-exit lifecycle apply. No extra thread or external input hook is used.

Gameplay acceptance must cover first press, release, remapped forward input,
restricted actor states and a new game process with the option disabled.

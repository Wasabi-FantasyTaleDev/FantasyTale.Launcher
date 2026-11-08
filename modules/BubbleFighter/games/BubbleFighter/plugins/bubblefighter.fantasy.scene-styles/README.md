# Scene style selection

Choose **Original** or **Snow** independently for **Lobby** and **Plaza**.
Original uses the selected client's own scene files. Snow restores the Korean 2011
winter scene resources, with matching texture payloads. Version 1.0.2 supports
cn-279, cn-324 and cn-406 and requires Launcher 0.1.66.

**Use Christmas BGM with Snow** and **Dress Lomi and Pini in Christmas outfits**
both default to on. Christmas BGM selects the client's built-in carol event for
each scene set to Snow. Turning it off preserves the client's original sound table;
some clients already use a Christmas event in their original plaza.

Choose **Default / Original / Christmas** for Lomi and
**Default / Original / Christmas / Little zombie** for Pini. Both start at Default,
which preserves the selected client's own NPC resources. CN2011's Pini already
uses the normal model, so Original also preserves it. On CN2012/CN2013, Pini's
Original uses the Korean 2015 model; Little zombie uses the Chinese 2012 model.
Lomi Original retains its ordinary client outfit. These choices work independently of scene styles. With automatic
outfits on, switching either the lobby or plaza to Snow selects Christmas for both NPCs. Enabling
automatic outfits while either scene is already Snow also selects Christmas. You can
then choose another outfit manually, and saving or reloading preserves that choice.

Changes apply on the next launch. The shared resource composer combines member edits,
reports conflicting plugins through the existing conflict dialog, and restores originals
when the game exits. Scene styles do not replace UI forms, authentication, executable
instructions, camera code or resolution-extension hooks. Native-mode layout and the
CN2013 extended canvas correction remain owned by the original client and Resolution Extension.

Future styles can add enum values and manifest plans without changing the option keys.
Game assets are generated into ignored package outputs and are not stored in this repository.

# Changelog

## Auto Dash 0.2.2 (2026-11-08)

- Add a verified TW454 forward-input branch and its package compatibility/implementation mapping, so the recommended plugin can be found and installed on Taiwan clients. Retain native running eligibility and key-release behavior.
- Check the unique original signature and execute 20 original/patched x86 cases covering first press, timer states and denied running conditions. Live gameplay remains a separate acceptance check.

## Chinese font support 1.0.10 (2026-11-08)

- Default Use bold font weight to off and preserve saved option choices. Non-Chinese game languages use the accepted prebaked fusions; Chinese game languages use the unmodified Chinese font plus an independent Chinese weight repair.
- Add kr-1256 with its own Nanum Barun Gothic Bold Korean donor and a build-scoped weight normalization patch. Keep the accepted legacy font payloads unchanged.
- Keep original and generated TTF/OTF files outside source control. The source distribution contains generation scripts, input hashes and configuration only.
- Refresh plugin descriptions and the pirate repair's display name. Align required plugins and initial recommendation check states with each client profile; omit scene styles from CN2014 recommendations.

## Chinese font support 1.0.9 (2026-11-08)

- Bake a native-style outside stroke into the Chinese-client Chinese, Latin, number and punctuation outlines. Use the native quarter-pixel stroke at an explicit 11-pixel reference size while preserving advance widths and each fusion's Korean outlines.
- Preserve the existing runtime bold patch, option behavior and defaults. Retain the verified 1.0.8 package and source for rollback. Stored outlines scale with text size, so this approximates the native fixed-pixel stroke at the reference size.

## Chinese font support 1.0.8 (2026-11-08)

- Fuse each available native font separately. Chinese and Latin retain the Chinese client glyphs; Korean retains the corresponding Arita, Nexon or Nanum glyphs and weight. Incomplete NexonGothic syllables use the matching Arita weight only for missing glyphs.
- Retain an explicit complete-Korean fallback for referenced fonts whose original files are absent. Preserve the bold option and recoverable file deployment. Verify serialized outlines, widths, complete modern Hangul coverage and CFF conversion origins.

## Chinese font support 1.0.7 (2026-11-08)

- Make Use bold font weight extend the native Chinese-only bold behavior to every game language. Turning the option off retains Chinese-only bold instead of suppressing it; font replacement remains independent.
- Keep the existing option key, default-on state and verified font assets. Execute 30 native conditional-store cases across three builds, both option values, Chinese/non-Chinese languages and absent language metadata.

## Chinese font support 1.0.6 (2026-11-08)

- Replace native Korean and Indonesian font destinations with the Chinese font containing complete modern Hangul. Version 1.0.5 only replaced the Chinese language table's ngulim.ttf, leaving native-language interfaces unchanged.
- Reuse one private font payload for all destinations and retain file restoration. The optional Chinese bold gate remains independent of font deployment.

## Scene style selection 1.0.2 (2026-11-08)

- Select Christmas for both NPCs when either the lobby or plaza changes to Snow while automatic outfits are enabled. Enabling the switch while either scene is Snow also selects Christmas; later manual choices still survive save acknowledgements and reloads.
- Keep both switches beside their labels in narrow settings panels and rename Main screen to Lobby. Use the current plugin name for the settings page title in every language.
- Retain the exact accepted music, scene and outfit resources from 1.0.1. Eight settings interaction checks cover both snow transitions, disabled automation, defaults, manual changes and reloads.

## Scene style selection 1.0.1 (2026-11-08)

- Rename the plugin to Scene style selection and add default-on Christmas BGM and automatic NPC outfit switches. Use the native lobby and plaza carol events only for scenes set to Snow, with exact compound resource conditions requiring Launcher 0.1.66.
- Add Default/Original/Christmas for Lomi and Default/Original/Christmas/Little zombie for Pini. Both start at Default, preserving the current client's resources. Pini Original preserves CN2011's normal model and uses Korean 2015 resources on CN2012/CN2013; Little zombie uses Chinese 2012 resources. Switching the plaza to Snow selects Christmas when automatic outfits are on; subsequent manual choices survive configuration acknowledgements and reloads.
- Verify 288 setting combinations and 37 real archive composition checks across cn-279, cn-324 and cn-406, including unchanged members, missing NPC resources, conflict rejection and original archive preservation. The final 24 plans retain the previously verified resource bytes; three additional compositions cover Snow with Default outfits. Default contributes no outfit changes. Live rendering and music playback remain client acceptance checks.

## ESC mid-match exit 0.2.5 (2026-11-08)

- Remove CN2011/CN2012 keyboard-table replacements and staged readiness patches following a startup regression report. Observe foreground Escape edges on a worker using read-only readiness checks, preserving native input consumers. Server-confirmed ordinary-match eligibility and existing native UI delivery remain required. Startup and live departure still require client acceptance.

## Mid-match exit 0.2.4 (2026-11-07)

- Accept the verified original and unpacked CN2011/CN2012 image sizes. The original executable previously failed readiness forever because only its unpacked analysis size was declared; timestamp, keyboard pointer and exact instruction checks remain required.
- Invoke the packaged DLL's real readiness locator in regression tests and distinguish image, pointer and signature readiness in initialization diagnostics. This addresses activation, not unverified startup hangs or unavailable companion bindings.

## Mid-match exit 0.2.3 (2026-11-07)

- Preserve CN2011/CN2012's original keyboard return value after scheduling an asynchronous match-state query. Scheduling no longer consumes an otherwise unhandled Escape key, and input never waits for the adapter state lock.
- Keep all-mode operation and the original tutorial handler. Live startup-hang acceptance and early-client server command deployment remain required.

## Mid-match exit 0.2.2 (2026-11-07)

- Remove the separate Allow all modes setting. Enabling the plugin applies its mode extensions across every supported client, including upgrades that previously saved the option as false.
- Preserve native active-match, loading, existing-dialog and tutorial guards. CN2015's native relay departure still requires the matching server handler; this release does not change that packet or claim server-side acceptance.

## Chinese authentication component 1.0.16 (2026-11-05)

- Repair the retired security startup chain in cn-706 only, preserving the original continuation and leaving other client builds unchanged. Require Launcher 0.1.54 and FtHook 3.7.1 for the combined file and startup-patch package. No identity-report compatibility patch is included.

## Character model switcher 1.0.3 (2026-11-02)

- Include exact source textures when a new outfit mesh references a missing or different shared texture in an older client. Keep these companions beside the affected mesh instead of replacing the shared texture pool, preserving other characters and the reviewed classic Dizni heads.
- Verify mesh material dependencies during package creation and reject missing source textures. Equipment eligibility rules remain unchanged.

## Character model switcher 1.0.2 (2026-11-02)

- Include missing split leg meshes and companion textures when applying remade outfits to older clients, and remove the separate leg slot when restoring an integrated classic body.
- Apply matching new motions and facial timelines for actions already present in the selected client. Preserve the reviewed classic Dizni head apertures and their exact payloads.
- Use plain WinUI-style radio indicators for Default, New and Classic, without a rectangular selected background. Keep light/dark themes and keyboard operation.

## Character model switcher 1.0.1 (2026-11-01)

- Follow the Launcher light or dark theme in model settings and use accessible radio choices. Describe the unchanged client selection as Default. Existing model payloads and saved choices remain unchanged.

## Unreleased

- Scene styles 1.0.0 adds independent Original/Snow choices for the main screen and plaza in CN2011, CN2012 and CN2013. Compose scene-local resources from the existing archives, retain unchanged members and preserve the CN2013 extended canvas adapter. Requires Launcher 0.1.65; live visual acceptance remains pending.

- Resolution Extension 0.2.5 expands only CN2013's main-stage canvas for extended display modes, using the native layout setters and scaled UI size. Preserve original native menu resolutions and every other build's scene behavior. Original-client visual acceptance remains pending.

- Chinese authentication component 1.0.15 continues CN2019's native login after Automatic redirects, using the existing bounded provider adapter and its build-specific feature predicate. Preserve native serialization and prevent a duplicate callback when the original Entry already selects its sender.

- Chinese authentication component 1.0.14 left-aligns supplied CN2011 server names through button-local native drawing, retaining original executable bytes, original tables and login behavior. Escape literal percent signs for the native formatter.

- Chinese authentication component 1.0.13 updates optional CN2011 server captions through native UI string storage on the window thread. Remove the former formatter call replacement, retain full directory names and native defaults for missing labels, and isolate earlier components with a new metadata capability. Selection, capacity and authentication behavior are unchanged.

- Chinese authentication component 1.0.12 applies the existing native connection continuation to CN2011's earlier provider interface and empty redirected passport. Keep native login serialization and caption policy unchanged. Recognize CN2019's original executable image size alongside the expanded image while retaining runtime function-signature checks.

- Chinese authentication component 1.0.11 resumes a verified replacement-connection provider request after native redirection in CN2012, CN2013 and CN2015 without adding executable or vtable patches. Preserve the native sender path when it already applies, bound the cached authorized record to one continuation, and add CN2019's native character-span passport assignment.

- Chinese authentication component 1.0.10 isolates optional CN2011 Center captions from authentication: return the original provider object when no passport adaptation is needed, preserve native titles for invalid display metadata, and exercise the patched CALL and caller cleanup in native regression tests.

- Chinese authentication component 1.0.9 preserves native authentication when the optional CN2011 Center title override cannot match the loaded image. Recognize the original executable image size while retaining runtime instruction checks; unsupported display overrides retain native titles instead of blocking loading.

- Chinese authentication component 1.0.8 adapts native connection passports for CN2012, CN2013, audited CN2014 images and CN2015 through one data-driven provider wrapper. Add optional CN2011 Center names by native identity, preserving default titles when absent. Remove the former CN2012 chooser-continuation Hook; CN2019 passport adaptation remains excluded.

- Chinese authentication component 1.0.7 removes native selector rendering/click patches and their metadata/completion dependencies, retaining only CN2012's verified authentication continuation repair. Requires Launcher 0.1.39 for the matching native credential lifecycle.

- Chinese authentication component 1.0.4 resumes CN2012 authentication after native server selection and adds the evidenced cn-408 file gates. Keep selector projection inactive.

- Add Character Model Switcher 1.0.0 with per-character Original/New/Classic settings, strict initial/native no-op choices, verified member plans for nineteen client archives and provider-owned expansion capabilities. Include classic Dizni's reviewed head apertures, matching textures and motions. Resource payloads remain outside the repository.
- Classic Dizni model 1.0.1 restores matching facial selectors and existing classic animations alongside Default meshes and textures, fixing mixed-generation faces and Korean idle animations. Newer-only actions and unrelated resources remain intact.

- Add optional Classic Dao and Classic Dizni model store plugins with verified regional names, per-build reversible PKN replacements, unchanged-shard omission and a source-validating package maker.

- Add the optional Luxury Marid remastered-model plugin for nine client builds, with verified resources, an independent cn-735 PKN preserving other members, automatic restoration and a source-checking package maker.

- Resolution Extension 0.2.4 prepares the accepted settings UI for store distribution and declares Launcher 0.1.33 as its minimum version.

- Resolution Extension 0.2.3 aligns list rows with the entry controls, moves the next-launch note to the bottom and keeps focus borders inside controls.

- Resolution Extension 0.2.2 declares a single HTML settings page, places field captions below the controls, preserves focus outlines and animates list additions and removals without recreating unchanged rows.
- Resolution Extension 0.2.1 removes the repeated settings heading and excess
  blank space. The page reports its content height for smooth Launcher resizing.
- Resolution Extension 0.2.0 adds a six-language custom-resolution settings page,
  preserving original and automatic choices. Add independently checked TOML
  descriptors for cn-404, cn-406, cn-706, cn-717, cn-735, id-400 and id-1809 alongside
  kr-812; exclude cn-279/cn-324. Preserve native hardware checks and next-launch
  settings. New ports await real-client display acceptance.

- Add an optional Resolution Extension plugin preview for kr-812. It expands the
  original menu from hardware display modes and adapts saved-resolution validation
  through per-build TOML, retaining native display checks and restart behavior.
  Static and synthetic validation do not establish real-client display acceptance.

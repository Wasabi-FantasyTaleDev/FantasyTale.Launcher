# Legacy security compatibility

This optional patch plugin skips the retired native security SDK's operation-19 identity report. On affected legacy clients, a server transfer can reach that call while the module entry is unavailable, causing a null-address crash. It does not restore an external security service or change the game, relay, Center or authentication protocols.

Import this folder or its ZIP through the Launcher's Plugins page, then enable **Legacy security compatibility** for the client instance that you will test. Restart the game after changing the selection. No Launcher application update or additional DLL is required. Disabling the selection and restarting restores the original in-memory behavior; the executable on disk is not modified.

## Supported images

| Build | Client period | Identity-report wrapper |
| --- | --- | --- |
| kr-812 | Korea 2015 | kr-812 sub_B72F20 |
| cn-406 | China 2013 | cn-406 sub_A53E14 |
| cn-404 | China 2014 | cn-404 sub_B245C0 |
| cn-408 | China 2014 | cn-408 sub_B2D5E0 |
| cn-705 | China 2014 | cn-705 sub_AED5E0 |
| cn-706 | China 2014 | cn-706 sub_AEF790 |
| cn-717 | China 2015 | cn-717 sub_C43B60 |
| id-400 | Indonesia 2014 | id-400 sub_AF91B0 |
| id-1809 | Indonesia 2015 | id-1809 sub_B04FA0 |

Each image has its own complete function signature. The call bytes alone are wildcarded in the locator so the runtime can recognize both the original and already-installed patch. The separate expected bytes still require the exact original call before the runtime writes. A missing or nonunique signature fails without writing.

## Calling contract

The original wrapper initializes its error output to zero, pushes three arguments, calls a stdcall function pointer with operation 19, masks EAX to its low byte and compares that value with 1. The replacement consumes the same twelve argument bytes and sets AL to 1. The original mask, success branch, exception-frame teardown and wrapper return remain intact. The plugin does not supply fabricated check data or seed tables.

## Verification and limits

Use `python tools/verify-plugin/verify_plugin.py <plugin-folder> --image <build>=<unpacked-executable>` for each claimed image. Image-signature and calling-contract checks establish patch compatibility; they do not establish that every migration path or the rest of the obsolete security SDK is repaired. Real-client server-transfer and round-start acceptance must be recorded separately. This package specifically repairs identity reporting and does not claim to disable every native security entry point.

[Chinese](README.zh.md)

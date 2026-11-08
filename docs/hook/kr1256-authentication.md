# KR1256 authentication component

The required native package `bubblefighter.fantasy.kr-auth` is selected only by
`kr-1256`. Its latest-only policy uses the existing required-plugin mechanism;
it does not add a privileged launch path to the shared launcher.

## Native contract

All image timestamps, permitted image sizes, RVAs, signatures and bounded wait
settings live in `native-login.toml`. The component rejects an unsupported image
or mismatched executable signature. A packed executable may finish unpacking
before those signatures become readable.

- `kr-1256 sub_87EA20` constructs the login destination for initial and retry
  connections. Its verified entry redirects only that destination to the managed
  session endpoint. Shared socket connection and relay destinations are untouched.
- `kr-1256 sub_887A80` waits on the application's startup-completion flag. After
  native connection and GamePatch, that flag must be completed on the UI thread
  before waiting for the login-stage object; reversing the order blocks startup.
- `kr-1256 sub_85E700` constructs the login stage. Before it exists, the
  connection identity must contain the full marked launcher ticket.
- `kr-1256 sub_40C2E0` assigns the allocating native wide string at connection
  offset 2516. The full marker and 64-character ticket fit without truncation.
- `kr-1256 sub_85EDB0` sends login only with a nonempty connection passport;
  the original login stage consumes it without callback submission.
- `kr-1256 sub_DABE90` and `kr-1256 sub_DAC540` are verified initialization
  and queue-pump entry points adapted for managed startup without the separate
  game-security service. Their original implementations are not preserved.

The component places the passport once through the game's message thread after
native connection, GamePatch and startup completion. Protected-value decoding
and device-information handling remain original. Phase logs omit ticket contents.
Passport placement establishes local preparation only; server redemption and
reaching the existing character's main menu are separate acceptance criteria.

## Cold startup and acceptance

The readiness wait is bounded at 120 seconds. Ticket validity belongs to the
server and must cover cold startup; extending the local wait cannot extend an
already issued ticket. Every test requires a fresh ticket for the exact build.

The owner accepted login with the original packed client, the existing character
main menu and working shop, as recorded in the handoff. Public version 0.1.1 uses
the same native DLL (SHA-256
`4567c5351b48f8bfec7529623db61384316621f46cf27472ee2edaa54ab1acaa`).
Release x86 rebuilding reproduced that hash; 50 native contract checks passed.
The frozen Launcher 0.1.18 distribution remains unchanged. Store installation
requires the updated trusted game catalog to declare this required identity.

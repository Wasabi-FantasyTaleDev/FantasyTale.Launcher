# Contributor instructions

Read `CONTRIBUTING.md`, `docs/architecture.md` and `docs/decisions/README.md` before changing code.

- Keep shared launcher behavior data driven. Game and build differences belong in validated definitions or the relevant game module.
- Build managed Release code with zero warnings. Do not suppress warnings to bypass a failed build. Document public APIs with meaningful XML summaries.
- Use C++20 and `/W4 /WX` for native code. Verify every native memory write against the original bytes and executable fingerprint.
- Keep frontend code strict TypeScript and Vue composition APIs. Translate user-visible strings through the shared locale resources.
- Preserve cancellation, bounded resources, explicit failures, owned lifecycle cleanup and recovery after process exit.
- Never commit passwords, access tokens, session tickets, local configuration, logs, dumps, original client archives or proprietary fonts and media.
- Keep code and technical comments in English. Update English and Simplified Chinese user documentation together.
- Validate changed behavior with focused regression tests and record actual results and skipped integration prerequisites.
- Submit changes on a separate branch through a reviewed pull request. Do not change repository visibility, publish releases or rewrite history without maintainer authorization.

Build and test commands are in `CONTRIBUTING.md`. Optional game resources remain external and are not required to inspect the source.

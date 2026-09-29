# VS Code Extension Maintenance

Paths below are relative to the repository root. See [Extension development](../../README.md#extension-development) for commands and setup.

- Keep extension source, regression tests, lockfile and build configuration in `src/kimi-ext/`. Root `.vscode/` contains its development launch/task configuration.
- Maintain extension usage and QuickStart in the root `README.md`, under **Visual Studio Code**. Packaging generates the extension's README and LICENSE from the root documents; do not edit or commit these generated copies.
- Restore locked npm dependencies and run the extension tests for extension changes. Before packaging a release, run the integration tests with a managed Kimi executable and its toolchain, using isolated VS Code profiles. These checks complement compiler verification when compiler code also changes.
- Use the documented packaging workflow to build a VSIX. Keep dependencies, compiled output, test profiles and VSIX files out of Git; exclude development-only files from the VSIX.
- Kimi.exe owns toolchain discovery and management. Do not duplicate that logic in the extension.
- Extension versions use `major.minor.patch`. Automatic or agent-initiated releases may increment only `patch`; change `major` or `minor` only on explicit user instruction, including for feature additions.
- Apply the documented patch-version command once per new release. Keep `src/kimi-ext/CHANGELOG.md`, the VSIX and the installed version consistent; compilation, tests, packaging retries and reinstalls do not increment the version.

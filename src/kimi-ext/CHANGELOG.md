# Change Log

## 0.0.7

- Integrate the extension into the Kimigayo repository with shared maintenance rules, documentation and development configuration.
- Generate packaged documentation and the MIT license from the repository's canonical files; exclude build output and test profiles from version control.
- Document `kimi.runBuilds: true` for the repository's current build-and-run CLI; retain compatibility with older executables.

## 0.0.6

- Select unambiguous targets automatically for palette commands and Ctrl+F5; prefer the active project or a project beside the current source, then the current workspace folder's projects.
- Add optional Select Target commands for Build, Run, Build and Run, and Check. Preserve exact file targets from context menus and launch configurations.
- Prepare for a build-capable `kimi run` with `kimi.runBuilds`: save inputs and invoke `run` once when enabled, while preserving the current CLI sequence by default.
- Replace the README with a concise QuickStart, target-selection rules, and configuration reference.

## 0.0.5

- Add standard Run Without Debugging (Ctrl+F5) support for Kimi, reusing Build and Run and target selection without requiring launch.json.
- Support optional `.kimi` or `.kimiproj` launch programs with VS Code variable substitution and workspace-relative paths.
- Connect Stop and Shift+F5 to the session's task; cancel the Run step when Build is stopped.
- Preserve the four palette commands and existing toolchain ownership; report unsupported debugging requests explicitly.

## 0.0.4

- Correct the previously assigned `0.1.0` version and document patch-only automatic release increments; major and minor changes require explicit user instructions.
- Limit the extension's single-file save step to the selected source; skip untitled and non-file inputs when saving projects, and allow project selection with an untitled editor active. Document VS Code's separate `task.saveBeforeRun` behavior without changing user settings.
- Ignore optional nearby-project discovery failures instead of blocking other targets.
- Reject Windows executable paths and PATH entries without a drive or UNC share.
- Stop command preparation quietly if the extension is deactivated during saving.
- Remove the unused extension-generator quickstart template.
- Rename the extension to `kimi-ext` (`local.kimi-ext`), retaining existing `kimi.*` settings.
- Add Build, Run, Build and Run, and Check commands with project or single-source selection.
- Expose commands in the palette, file context menus, and a Build and Run editor button.
- Save Kimi inputs before building or checking, validate the executable, and preserve process arguments and working directories.
- Wait for successful build completion before running; prevent concurrent commands sharing an output directory.
- Keep toolchain configuration under Kimi.exe control and retain independent LSP diagnostics.

## 0.0.3

- Activate after VS Code startup so an invalid configured server path is reported even when only settings are open.
- Validate configuration changes before any Kimi document is opened, while deferring server launch until it is needed.
- Test automatic activation without explicitly activating the extension in the test harness.

## 0.0.2

- Fix Windows file URI encoding that prevented the Kimi server from processing open documents.
- Validate `kimi.serverPath` before starting the server, including PATH lookup, file type, and access checks.
- Report configuration and startup failures with actions to open settings or output.
- Recover after configuration changes and provide a restart command.
- Serialize restarts, bound initialization time, and clean up clients after failures or deactivation.
- Register `kimi.trace.server` for protocol troubleshooting.
- Add regression and VS Code integration tests; exclude test output from installation packages.

## 0.0.1

- Initial client for Kimi diagnostics over standard input and output.

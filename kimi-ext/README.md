# Kimi Extension

The [kimi-ext](https://github.com/archi-Doc/Kimigayo/tree/main/kimi-ext/) extension provides diagnostics and build/run/check commands. It shares the compiler and toolchain described above.

## QuickStart

1. Install .NET 10, Node.js LTS and VS Code 1.138 or later (1.x). From the repository root, build the compiler and [prepare the toolchain](https://github.com/archi-Doc/Kimigayo#toolchain-windows-x64) for native builds:

   ```powershell
   dotnet build Kimi/Kimi.csproj -c Release
   ./backend/windows-x64/setup.ps1 -LlvmBin 'C:/path/to/LLVM/bin'
   ```

   Use `Kimi/bin/Release/net10.0/Kimi.exe` with its accompanying files. Diagnostics and Check need no LLVM tools. Standalone installations keep `toolchain/` beside the executable, or set `KIMI_TOOLCHAIN_ROOT` before starting VS Code.

2. Package and install the extension from the repository root:

   ```powershell
   npm --prefix kimi-ext ci
   npm --prefix kimi-ext run package
   code --install-extension .\kimi-ext\kimi-ext.vsix --force
   ```

   Packaging compiles the extension and includes this section and the root MIT license. After updating, run **Developer: Reload Window** in VS Code.

3. Run **Preferences: Open User Settings (JSON)** and set your actual compiler path:

   ```json
   {
     "kimi.serverPath": "C:/path/to/Kimigayo/Kimi/bin/Release/net10.0/Kimi.exe",
     "kimi.runBuilds": true
   }
   ```

   Current Kimi builds sources in `run`, so enable `kimi.runBuilds` to avoid a separate build. Leave it `false` only for older executables whose `run` requires an existing build.

4. Open a trusted source folder and a saved `.kimi` or `.kimiproj`. Press **Ctrl+F5** or **F1 > Kimi: Build and Run**. If asked, choose **Kimi (Run Without Debugging)**. An unambiguous target runs without a picker; `.kimiproj` is optional. Diagnostics appear in the editor and Problems panel.

## Commands and targets

With the settings above:

| F1 command | Action |
| --- | --- |
| Kimi: Build | Save inputs and build. |
| Kimi: Run / Build and Run | Save inputs, then invoke `kimi run` once to build and run. |
| Kimi: Check | Save inputs and check without building. |
| Kimi: Select Target and ... | Choose a target explicitly for any of the four actions. |

Palette commands and Ctrl+F5 prefer the active `.kimiproj`, then a project beside the active source, then a project in the active editor's workspace folder. Without an active folder, all workspace folders and open project tabs are candidates. One project is selected automatically; multiple projects show a picker. With no project, the active saved `.kimi` is used. Outside-workspace sources search only their own directory. **Select Target and ...** always allows a manual choice or Browse; no selection is remembered.

The editor play button and **Kimi** context menus use the clicked file exactly. A single `.kimi` includes only that source. Build and Run keeps one target throughout execution.

Output and input use task terminals. **Stop / Shift+F5** stops a Ctrl+F5 session; **Tasks: Terminate Task** stops palette tasks. Commands sharing a target directory cannot overlap. Execution is supported; breakpoints, stepping and application arguments are not.

Ctrl+F5 needs no `launch.json`. If another language's configuration is selected, choose a **Kimi: Build and Run** configuration instead. Its optional `program` accepts an exact `.kimi` or `.kimiproj` path, `${file}`, or a path relative to its workspace folder. Omit it for automatic selection.

## Settings and troubleshooting

- **`kimi.serverPath`**: absolute executable path or a name on PATH, without arguments. Invalid paths and startup errors offer **Open Settings** and **Show Output**. Path changes restart the server; **Kimi: Restart Language Server** retries it.
- **`kimi.runBuilds`**: enable for current Kimi. The compatibility default is `false`, which uses `build` then `run` for Build and Run and leaves Run's behavior to the executable.
- **`kimi.trace.server`**: `off` (default), `messages`, or `verbose`; see the **Kimi** Output channel.

Before compilation, the extension saves the selected source, or all dirty open Kimi files for a project. Untitled files are skipped. VS Code separately saves editors through its task/debug settings; `task.saveBeforeRun: "never"` disables task-wide saving for all extensions.

Tasks need an open folder; diagnostics also work in standalone editor windows. Avoid `%` in native build paths with the verified Windows linker. Completion, hover, navigation and syntax highlighting are not included. Kimi.exe manages the toolchain.

## Extension development

Run from the repository root:

```powershell
npm --prefix kimi-ext test
$env:KIMI_TEST_SERVER_PATH = (Resolve-Path Kimi/bin/Release/net10.0/Kimi.exe).Path
npm --prefix kimi-ext run test:integration
```

Integration tests use isolated VS Code profiles. Set `VSCODE_EXECUTABLE_PATH` to reuse an installed VS Code; otherwise the test runner downloads it. Compiler-dependent cases are skipped without `KIMI_TEST_SERVER_PATH`. Open this repository and select **Kimi Extension** in Run and Debug to launch its development host.

Edit this README section and the root `LICENSE`; packaging generates ignored copies under `kimi-ext/`. For a new release, run `npm --prefix kimi-ext run version:patch` once, update `kimi-ext/CHANGELOG.md`, test and package. Only the final number increments automatically; major/minor changes require explicit user instruction. See [maintenance rules](https://github.com/archi-Doc/Kimigayo/blob/main/AGENTS.md#vs-code-extension-kimi-ext).

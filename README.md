## Kimigayo
Work in progress

### Toolchain (Windows x64)

Use LLVM **22.1.8**, as specified in [profile.json](backend/windows-x64/profile.json).
Place `toolchain/` at the repository root, or beside `Kimi.exe` / `Kimi.dll` for a standalone installation.

For building Kimi applications (`kimi build`), include:

```text
toolchain/
  opt.exe
  llc.exe
  lld-link.exe
  llvm-nm.exe
  llvm-readobj.exe
  llvm-dlltool.exe
  <supporting LLVM DLLs, if required>
  windows_x64/
    kimi_backend_windows_x64_v1.lib
```

To also build the native backend library, add `clang.exe`, `llvm-lib.exe`, and
`llvm-objdump.exe` to `toolchain/`. No Windows SDK or extra Clang headers are required.
The backend archive and `llvm-dlltool.exe` must match the hashes in `profile.json`.

From the repository root, copy the tools and supporting DLLs from an existing LLVM
installation, then build, test, and install the backend library with:

```powershell
./backend/windows-x64/setup.ps1 -LlvmBin 'C:/path/to/LLVM/bin'
```

To rebuild the native library later, run `./backend/windows-x64/build.ps1`.
Building the C# compiler itself (`dotnet build`) does not require this LLVM toolchain.

### Add toolchain to PATH

From the repository root, run this in PowerShell to use the tools in the current session:

```powershell
$env:Path = "$(Join-Path $PWD 'toolchain');$env:Path"
```

For a permanent setting, open Windows **Environment Variables**, edit your user
**Path**, add the full path to `toolchain`, and open a new terminal.

Kimigayo resolves tools directly from its toolchain folder, without searching PATH.
For a custom location, set `KIMI_TOOLCHAIN_ROOT` or pass `--ToolchainRoot <path>`.

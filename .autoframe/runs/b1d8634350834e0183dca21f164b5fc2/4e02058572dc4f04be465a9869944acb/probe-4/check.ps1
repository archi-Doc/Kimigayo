$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
foreach ($name in @('clang','opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-objdump','llvm-lib','llvm-dlltool')) { Get-FileHash -LiteralPath (Join-Path 'toolchain' ($name + '.exe')) -Algorithm SHA256 }
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

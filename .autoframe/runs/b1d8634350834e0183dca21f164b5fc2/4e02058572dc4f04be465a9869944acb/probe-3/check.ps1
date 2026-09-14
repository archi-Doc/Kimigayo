$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
foreach ($name in @('clang','opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-objdump')) { $path = Join-Path 'toolchain' ($name + '.exe'); & $path --version; if ($LASTEXITCODE -ne 0) { throw ('Version probe failed: ' + $path) } }
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
Get-FileHash -LiteralPath toolchain/windows_x64/kimi_backend_windows_x64_v1.lib -Algorithm SHA256
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
Get-Content -LiteralPath backend/windows-x64/profile.json -Raw
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

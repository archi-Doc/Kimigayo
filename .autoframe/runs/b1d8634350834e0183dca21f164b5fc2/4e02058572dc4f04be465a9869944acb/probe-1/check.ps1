$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
$PSVersionTable | ConvertTo-Json -Depth 4
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

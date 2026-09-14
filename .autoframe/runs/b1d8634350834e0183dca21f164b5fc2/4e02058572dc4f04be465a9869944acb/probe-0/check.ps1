$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
try {
dotnet --info
if (-not $?) { exit 1 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }

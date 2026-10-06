# Associate a successful Verify run with a commit, using Git-normalized content rather than the old HEAD/dirty label.
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Evidence, [string] $Commit = 'HEAD')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification-inputs.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$summary = Get-Content -LiteralPath (Join-Path $Evidence 'summary.json') -Raw | ConvertFrom-Json
if ($summary.inputStability -cne 'passed' -or @($summary.steps | Where-Object result -ne 'PASS').Count -gt 0) { throw 'Evidence must be a successful Verify run with stable inputs.' }
$manifestPath = Join-Path $Evidence 'inputs-before.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$resolved = Assert-KimiVerificationCommit $repo $manifest $Commit
[ordered]@{ commit = $resolved; manifestSha256 = Get-KimiVerificationFileHash $manifestPath; matchedFiles = $manifest.files.Count; comparison = 'Git-normalized blobs; raw bytes and NuGet inputs remain recorded in the manifest'; checkedUtc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'commit-match.json')
Write-Output "Verified inputs match commit $resolved ($($manifest.files.Count) files)."

# Restore locked dependencies and package the current extension version.
# Output: artifacts/packages/kimi-ext.vsix. Requires Node.js and npm.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$extension = Join-Path $repo 'src/kimi-ext'

Push-Location $repo
try {
    & npm --prefix $extension ci
    if ($LASTEXITCODE -ne 0) {
        throw "Extension dependency restore failed with exit code $LASTEXITCODE."
    }

    & npm --prefix $extension run package
    if ($LASTEXITCODE -ne 0) {
        throw "Extension packaging failed with exit code $LASTEXITCODE."
    }
    Write-Host "Extension packaged to $(Join-Path $repo 'artifacts/packages/kimi-ext.vsix')"
}
finally {
    Pop-Location
}

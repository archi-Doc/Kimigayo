# Publish the Windows x64 Release binary to artifacts/packages/kimi/win-x64/.
# Requires the .NET SDK and the Windows NativeAOT build prerequisites.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/packages/kimi/win-x64'

Push-Location $repo
try {
    & dotnet publish (Join-Path $repo 'src/Kimi/Kimi.csproj') `
        -c Release -r win-x64 `
        -p:PublishAot=true `
        -p:DebugType=None `
        -p:GenerateDocumentationFile=false `
        -p:PublishDocumentationFiles=false `
        -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Kimi publish failed with exit code $LASTEXITCODE."
    }
    Write-Host "Kimi published to $output"
}
finally {
    Pop-Location
}

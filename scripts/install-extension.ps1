# Build the current extension version and install it into VS Code.
# Use -CodeCommand for another VS Code CLI and -ExtensionsDirectory for an isolated install.
[CmdletBinding()]
param(
    [string] $CodeCommand = 'code',
    [string] $ExtensionsDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$code = Get-Command $CodeCommand -CommandType Application -ErrorAction Stop | Select-Object -First 1
$vsix = Join-Path $repo 'artifacts/packages/kimi-ext.vsix'
$installArguments = @('--install-extension', $vsix, '--force')
if ($ExtensionsDirectory) {
    $installArguments += @('--extensions-dir', $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ExtensionsDirectory))
}

& (Join-Path $PSScriptRoot 'build-extension.ps1')
if (-not (Test-Path -LiteralPath $vsix -PathType Leaf)) {
    throw "Extension package was not created: $vsix"
}

& $code.Source @installArguments
if ($LASTEXITCODE -ne 0) {
    throw "VS Code extension installation failed with exit code $LASTEXITCODE."
}
Write-Host 'Kimi Extension installed. Run Developer: Reload Window in VS Code to load the update.'

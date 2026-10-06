[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [string] $ToolchainRoot = '',
    [string] $LlvmBin = '',
    [string] $Compiler = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [switch] $Run
)
$ErrorActionPreference = 'Stop'
# Keep a separate manifest entry point, with one native build implementation in the compiler.
# Backend bootstrap and native test oracles do not depend on this wrapper.
$manifestPath = [IO.Path]::GetFullPath($Manifest)
if (-not $Compiler) { $Compiler = Join-Path $PSScriptRoot "../../Kimi/bin/$Configuration/net10.0/Kimi.dll" }
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
$arguments = @('build', '--Manifest', $manifestPath)
if ($ToolchainRoot) { $arguments += @('--ToolchainRoot', $ToolchainRoot) }
if ($LlvmBin) { $arguments += @('--LlvmBin', $LlvmBin) }
$output = if ([IO.Path]::GetExtension($Compiler) -ieq '.dll') { & dotnet $Compiler @arguments 2>&1 | Out-String }
          else { & $Compiler @arguments 2>&1 | Out-String }
if ($LASTEXITCODE -ne 0) { throw "Manifest build failed ($LASTEXITCODE): $output" }
Write-Output $output.TrimEnd()
if ($Run) {
    $recordPath = [IO.Path]::ChangeExtension($manifestPath, '.build.json')
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    $exe = [IO.Path]::GetFullPath($record.executable, (Split-Path -Parent $manifestPath))
    & $exe
    $code = $LASTEXITCODE
    $record | Add-Member -NotePropertyName exitCode -NotePropertyValue $code -Force
    $record.status = 'executed'
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
    if ($code -ne 0) { throw "Application exited with code $code" }
}

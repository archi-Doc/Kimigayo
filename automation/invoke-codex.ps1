#requires -Version 7.4
# A separate process keeps native stdout/stderr out of the runner's result stream.
param([Parameter(Mandatory)][string]$InvocationPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    $invocation = Get-Content -LiteralPath $InvocationPath -Raw | ConvertFrom-Json
    $arguments = @($invocation.arguments)
    $promptText = [IO.File]::ReadAllText($invocation.prompt_path)
    $global:LASTEXITCODE = 0
    $promptText | & $invocation.command @arguments
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

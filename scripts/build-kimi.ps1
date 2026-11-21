# Publish the Windows x64 Release kimi.exe to artifacts/packages/kimi/win-x64/ and check it (Tatara B6b2): one file carrying Tatara,
# linked from src/Tatara/build.ps1's objects and link.rsp on the static UCRT.
# Requires the .NET SDK, Visual Studio's C++ x64 tools with the DIA SDK, and the pinned LLVM release archive, extracted (-LlvmRoot);
# reads no toolchain root.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/packages/kimi/win-x64'
$objects = Join-Path $repo 'artifacts/packages/kimi/tatara'
$verifyReport = Join-Path $repo 'artifacts/packages/kimi/verify.json'

# NativeAOT's findvcvarsall.bat finds vswhere.exe by its path, but vcvarsall.bat runs it from PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (($env:PATH -split ';' | ForEach-Object { $_.TrimEnd('\') }) -notcontains $installer) { $env:PATH = "$installer;$env:PATH" }

Push-Location $repo
try {
    # 1. Tatara's objects and link.rsp; build.ps1 links and checks them as tatara.dll (identity, ABI, exports).
    & (Join-Path $repo 'src/Tatara/build.ps1') -LlvmRoot $LlvmRoot -OutputDirectory $objects
    . (Join-Path $repo 'src/Tatara/identity.ps1')
    $identity = Get-TataraBuildIdentity

    # 2. Publish into an emptied folder: an earlier publish's files never pass or fail the one-file check.
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    & dotnet publish (Join-Path $repo 'src/Kimi/Kimi.csproj') `
        -c Release -r win-x64 `
        -p:PublishAot=true `
        "-p:TataraObjects=$objects" `
        -p:DebugType=None `
        -p:GenerateDocumentationFile=false `
        -p:PublishDocumentationFiles=false `
        -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Kimi publish failed with exit code $LASTEXITCODE."
    }

    # 3. The product: kimi.exe alone; on the static UCRT with one allocator (no UCRT DLL, no HeapAlloc or HeapFree import);
    # its self-check passes and reports the Tatara of this checkout.
    $files = @(Get-ChildItem -LiteralPath $output -Force | ForEach-Object Name)
    if (($files -join ', ') -cne 'Kimi.exe') { throw "The published folder holds $($files -join ', '), not Kimi.exe alone: $output" }
    $exe = Join-Path $output 'Kimi.exe'
    $listing = & (Join-Path $LlvmRoot 'bin/llvm-readobj.exe') --coff-imports $exe
    if ($LASTEXITCODE -ne 0) { throw "llvm-readobj --coff-imports failed with exit code ${LASTEXITCODE}: $exe" }
    $module = $null
    $modules = [Collections.Generic.List[string]]::new()
    $refused = @(foreach ($line in $listing) {
            if ($line -match '^\s*Name: (\S+)\s*$') { $module = $Matches[1]; $modules.Add($module); if ($module -match '^(api-ms-win-crt-.*|ucrtbase\.dll)$') { $module } }
            elseif ($line -cmatch '^\s*Symbol: (HeapAlloc|HeapFree) ') { "$module!$($Matches[1])" }
        })
    if ($modules -notcontains 'KERNEL32.dll') { throw "llvm-readobj listed no KERNEL32.dll import (an unread listing): $exe" }
    if ($refused.Count -ne 0) { throw "Kimi.exe is not on the static UCRT with one allocator; it imports $($refused -join ', ')" }
    & $exe toolchain verify --Report $verifyReport
    if ($LASTEXITCODE -ne 0) { throw "Kimi.exe toolchain verify failed with exit code $LASTEXITCODE (report: $verifyReport)" }
    $verified = Get-Content -LiteralPath $verifyReport -Raw | ConvertFrom-Json
    if ($verified.status -cne 'passed' -or $verified.tatara.build -cne $identity) {
        throw "Kimi.exe carries Tatara build $($verified.tatara.build) (verify $($verified.status)); this checkout's is $identity"
    }
    Write-Host "Kimi published to $output (Kimi.exe, $((Get-Item -LiteralPath $exe).Length) bytes, Tatara build $identity)"
}
finally {
    Pop-Location
}

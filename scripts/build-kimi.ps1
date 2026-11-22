# Publish the Windows x64 Release kimi.exe to artifacts/packages/kimi/win-x64/ and check it (Tatara B6b2): one file carrying Tatara,
# linked from src/Tatara/build.ps1's objects and link.rsp on the static UCRT.
# Requires the .NET SDK, Visual Studio's C++ x64 tools with the DIA SDK, and the pinned LLVM release archive, extracted (-LlvmRoot,
# relative to the caller's location as setup.ps1's); reads no toolchain root.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot
)

$ErrorActionPreference = 'Stop'
$LlvmRoot = [IO.Path]::GetFullPath($LlvmRoot, (Get-Location).ProviderPath)
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/packages/kimi/win-x64'
$objects = Join-Path $repo 'artifacts/packages/kimi/tatara'
$verifyReport = Join-Path $repo 'artifacts/packages/kimi/verify.json'

# NativeAOT's findvcvarsall.bat finds vswhere.exe by its path, but vcvarsall.bat runs it from PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (($env:PATH -split ';' | ForEach-Object { $_.TrimEnd('\') }) -notcontains $installer) { $env:PATH = "$installer;$env:PATH" }

Push-Location $repo
try {
    # 1. The script's outputs start empty, so no earlier run's file reaches this run's link or checks: kimi.exe links every .obj in
    # the objects folder, which are then exactly build.ps1's objects of this run (one per checkout *.cpp, the list tatara.dll links);
    # the folder check sees this publish alone; the identity check reads this run's verify report.
    foreach ($earlier in $objects, $output, $verifyReport) {
        if (Test-Path -LiteralPath $earlier) { Remove-Item -LiteralPath $earlier -Recurse -Force }
    }

    # 2. Tatara's objects and link.rsp; build.ps1 links and checks them as tatara.dll (identity, ABI, exports).
    & (Join-Path $repo 'src/Tatara/build.ps1') -LlvmRoot $LlvmRoot -OutputDirectory $objects
    . (Join-Path $repo 'src/Tatara/identity.ps1')
    $identity = Get-TataraBuildIdentity

    # 3. Publish.
    $project = Join-Path $repo 'src/Kimi/Kimi.csproj'
    $properties = '-p:PublishAot=true', "-p:TataraObjects=$objects", '-p:DebugType=None', '-p:GenerateDocumentationFile=false',
        '-p:PublishDocumentationFiles=false'
    & dotnet publish $project -c Release -r win-x64 @properties -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Kimi publish failed with exit code $LASTEXITCODE."
    }

    # 4. The product: kimi.exe alone; on the static CRT with one allocator (no CRT DLL, no HeapAlloc or HeapFree import, read from
    # KERNEL32.dll's symbol lines); naming no package DLL the publish leaves out (KimiOneFile's list); its self-check passes and
    # reports the Tatara of this checkout.
    $files = @(Get-ChildItem -LiteralPath $output -Force | ForEach-Object Name)
    if (($files -join ', ') -cne 'Kimi.exe') { throw "The published folder holds $($files -join ', '), not Kimi.exe alone: $output" }
    $exe = Join-Path $output 'Kimi.exe'
    $listing = & (Join-Path $LlvmRoot 'bin/llvm-readobj.exe') --coff-imports $exe
    if ($LASTEXITCODE -ne 0) { throw "llvm-readobj --coff-imports failed with exit code ${LASTEXITCODE}: $exe" }
    $module = $null
    $kernelSymbols = 0
    $refused = @(foreach ($line in $listing) {
            if ($line -match '^\s*Name: (\S+)\s*$') {
                $module = $Matches[1]
                if ($module -match '^(api-ms-win-crt-.*|ucrtbased?|vcruntime.*|msvcp.*|msvcr.*|concrt.*)\.dll$') { $module }
            }
            elseif ($line -cmatch '^\s*Symbol: (\S*) \(\d+\)\s*$') {
                if ($module -eq 'KERNEL32.dll') { $kernelSymbols++ }
                if ($Matches[1] -cin 'HeapAlloc', 'HeapFree') { "$module!$($Matches[1])" }
            }
        })
    if ($kernelSymbols -eq 0) { throw "llvm-readobj listed no KERNEL32.dll symbol (an unread listing): $exe" }
    if ($refused.Count -ne 0) { throw "Kimi.exe is not on the static CRT with one allocator; it imports $($refused -join ', ')" }
    # NativeAOT's P/Invoke cells carry the module name (ASCII or UTF-16LE). The modules are the file names of Kimi.csproj's
    # KimiLeftOut items as MSBuild evaluates them with the publish's properties, the list KimiOneFile removes.
    $evaluated = & dotnet msbuild $project -p:Configuration=Release -p:RuntimeIdentifier=win-x64 @properties -getItem:KimiLeftOut
    if ($LASTEXITCODE -ne 0) { throw "Evaluating Kimi.csproj's KimiLeftOut items failed with exit code ${LASTEXITCODE}: $evaluated" }
    $leftOut = @(($evaluated -join "`n" | ConvertFrom-Json).Items.KimiLeftOut | ForEach-Object Filename)
    if ($leftOut.Count -eq 0) { throw 'Kimi.csproj evaluates no KimiLeftOut item (an unread list).' }
    $image = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($exe))
    foreach ($name in $leftOut) {
        foreach ($form in $name, [Text.Encoding]::Latin1.GetString([Text.Encoding]::Unicode.GetBytes($name))) {
            if ($image.IndexOf($form, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Kimi.exe names $name, a package DLL the publish leaves out (decision 20 Q1 (a)); whether the package DLLs stay beside kimi.exe is decision 20's later question for the owner."
            }
        }
    }
    & $exe toolchain verify --Report $verifyReport
    if ($LASTEXITCODE -ne 0) { throw "Kimi.exe toolchain verify failed with exit code $LASTEXITCODE (report: $verifyReport)" }
    if (-not (Test-Path -LiteralPath $verifyReport -PathType Leaf)) { throw "Kimi.exe toolchain verify wrote no report: $verifyReport" }
    $verified = Get-Content -LiteralPath $verifyReport -Raw | ConvertFrom-Json
    if ($verified.status -cne 'passed' -or $verified.tatara.build -cne $identity) {
        throw "Kimi.exe carries Tatara build $($verified.tatara.build) (verify $($verified.status)); this checkout's is $identity"
    }
    Write-Host "Kimi published to $output (Kimi.exe, $((Get-Item -LiteralPath $exe).Length) bytes, Tatara build $identity)"
}
finally {
    Pop-Location
}

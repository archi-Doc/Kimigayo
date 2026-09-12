[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [string] $LlvmBin = '',
    [switch] $Run,
    [switch] $AllowUnpinnedToolchain
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'artifact-paths.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$catalog = Read-KimiWindowsProfile
$expectedVersion = $catalog.llvmVersion
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$directory = Split-Path -Parent $manifestPath
$data = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$recordPath = [IO.Path]::ChangeExtension($manifestPath, '.build.json')
@{ status = 'incomplete' } | ConvertTo-Json | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $recordPath -Encoding utf8
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
function Resolve-Input([string] $inputName) {
    if ($inputName -match '[\x00\r\n"]' -or $inputName.StartsWith('-')) { throw 'Invalid input path' }
    # Simple linker search names require an explicit local file; never guess SDK installation paths.
    return (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($inputName, $directory))).Path
}
if ($data.schemaVersion -ne 2) { throw 'Link manifest schema 2 is required. Re-emit the manifest for generated kernel32 imports.' }
if ($data.target -cne 'x86_64-pc-windows-msvc' -or
    $data.outputKind -cne 'Application' -or $data.entry -cne '__kimi_start' -or $data.subsystem -cne 'console' -or
    $data.codegen.profile -cne 'windows-x64-v1' -or $data.codegen.llvmVersion -cne $expectedVersion -or
    $data.codegen.cpu -cne 'x86-64' -or ($data.codegen.features -join ',') -cne '+sse2' -or
    $data.codegen.relocationModel -cne 'pic' -or $data.codegen.codeModel -cne 'small' -or
    $data.codegen.unwindTables -cne 'async' -or $data.codegen.optimization -cnotin @('O0', 'O2')) {
    throw 'Manifest does not describe the supported windows-x64-v1 Application profile'
}
if (-not $LlvmBin) {
    if ($data.toolchain.llvmBin) { $LlvmBin = [IO.Path]::GetFullPath($data.toolchain.llvmBin, $directory) }
    else { throw 'Specify -LlvmBin or configure LlvmBin in the .kimiproj file' }
}
$LlvmBin = (Resolve-Path -LiteralPath $LlvmBin).Path
$tools = @{}
$identities = [ordered]@{}
$matched = $true
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-nm', 'llvm-readobj')) {
    $exe = Join-Path $LlvmBin "$name.exe"
    $identity = Get-KimiLlvmToolIdentity $exe $expectedVersion -AllowUnpinnedToolchain:$AllowUnpinnedToolchain
    $matched = $matched -and $identity.versionMatched
    $tools[$name] = $exe
    $identities[$name] = $identity
}
@{ status = 'incomplete'; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = -not $matched; tools = $identities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $recordPath -Encoding utf8
$tools['llvm-dlltool'] = Join-Path $LlvmBin 'llvm-dlltool.exe'
$identities['llvm-dlltool'] = Get-KimiDlltoolIdentity $tools['llvm-dlltool'] -AllowUnpinnedToolchain:$AllowUnpinnedToolchain
$unverified = -not $matched -or -not $identities['llvm-dlltool'].hashMatched
@{ status = 'incomplete'; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = $unverified; tools = $identities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $recordPath -Encoding utf8
$ir = Resolve-Input $data.irFile
$irHash = (Get-FileHash -LiteralPath $ir -Algorithm SHA256).Hash.ToLowerInvariant()
if ($irHash -cne $data.irSha256) { throw 'IR/manifest SHA-256 mismatch; do not use mixed or stale outputs' }
$support = $data.backendSupport
if ($support.packageId -cne 'kimi-backend-windows-x64' -or $support.abiVersion -ne 1 -or
    $support.packageVersion -cne $catalog.packageVersion -or $support.artifactSha256 -cne $catalog.artifactSha256 -or $support.library -cne 'kimi_backend' -or
    ($support.providedSymbols -join ',') -cne '__chkstk,memcpy,memmove,memset') { throw 'Invalid backend supply identity' }
if (($data.providedRuntimeSymbols -join ',') -cne '_fltused') { throw 'Invalid generated runtime symbol list' }
foreach ($dependency in $data.expectedUndefinedSymbols) {
    if ($dependency.provider -cne 'kimi_backend' -or $dependency.symbol -cnotin $support.providedSymbols) { throw 'Unknown anticipated backend dependency' }
}
$libraries = @()
$libraryIdentities = @()
$seen = @{}
foreach ($entry in $data.libraries) {
    if ($seen.ContainsKey($entry.name) -or $entry.kind -cnotin @('import', 'static')) { throw 'Invalid/duplicate library entry' }
    $seen[$entry.name] = $true
    if ($entry.name -ceq 'kernel32') {
        Assert-KimiKernel32Manifest $entry
        $kernel = New-KimiKernel32Library $tools (Join-Path $directory ([IO.Path]::GetFileNameWithoutExtension($ir) + ".$($data.codegen.optimization).kernel32.lib"))
        $path = $kernel.path
    }
    else { $path = Resolve-Input $entry.input }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entry.name -ceq 'kimi_backend' -and ($entry.kind -cne 'static' -or $hash -cne $support.artifactSha256)) { throw 'Backend archive SHA-256/kind mismatch' }
    if ($entry.name -ceq 'kernel32' -and $entry.kind -cne 'import') { throw 'kernel32 must be an import library' }
    $libraries += $path
    $libraryIdentities += @{ name = $entry.name; path = $path; sha256 = $hash }
}
if (-not $seen.ContainsKey('kernel32') -or -not $seen.ContainsKey('kimi_backend')) { throw 'Missing profile library' }
$level = $data.codegen.optimization
$stem = Join-Path $directory ([IO.Path]::GetFileNameWithoutExtension($ir) + ".$level")
Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $ir)
$selectedIr = $ir
if ($level -ceq 'O2') {
    $selectedIr = "$stem.ll"
    Invoke-Tool $tools.opt @('-S', '-passes=default<O2>', '-mtriple=x86_64-pc-windows-msvc', $ir, '-o', $selectedIr)
    Protect-KimiLlvmPaths $selectedIr
    Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $selectedIr)
}
$obj = "$stem.obj"
Invoke-KimiLlvmOutput $tools.llc @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2', '-relocation-model=pic', '-code-model=small', $selectedIr) $obj
$undefined = & $tools['llvm-nm'] --undefined-only --format=posix $obj | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect object dependencies' }
$allowed = @('__chkstk', 'memcpy', 'memmove', 'memset', '__imp_GetProcessHeap', '__imp_HeapAlloc', '__imp_HeapFree', '__imp_GetStdHandle', '__imp_WriteFile', '__imp_GetLastError', '__imp_ExitProcess')
foreach ($line in ($undefined -split '\r?\n')) {
    if ($line.Trim() -and ($line.Trim() -split '\s+')[0] -cnotin $allowed) { throw "Unsupported actual object dependency: $line" }
}
$defined = & $tools['llvm-nm'] --defined-only --extern-only --format=posix $obj | Out-String
if ($LASTEXITCODE -ne 0 -or [regex]::Matches($defined, '(?m)^_fltused [BD] ').Count -ne 1) { throw 'Expected one strong _fltused definition' }
$inspection = & $tools['llvm-readobj'] --unwind --coff-directives $obj | Out-String
if ($LASTEXITCODE -ne 0 -or $inspection -notmatch 'RuntimeFunction' -or $inspection -match '(?i)DEFAULTLIB') { throw 'Invalid unwind information or hidden default library' }
$inspection | ConvertTo-KimiArtifactText | Set-Content -LiteralPath "$stem.inspection.txt" -Encoding utf8
$exe = "$stem.exe"
Invoke-Tool $tools['lld-link'] (@($obj) + $libraries + @('/entry:__kimi_start', '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$exe"))
$record = @{ status = 'linked'; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = $unverified; irSha256 = $irHash; tools = $identities; libraries = $libraryIdentities; kernel32 = $kernel; optimization = $level; executable = $exe; objectUndefinedSymbols = $undefined.Trim(); executableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() }
if ($Run) {
    & $exe
    $record.exitCode = $LASTEXITCODE
    $record.status = 'executed'
}
$record | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $recordPath -Encoding utf8
Write-Output "Manual build $($record.status): $exe; record: $recordPath"
if ($Run -and $record.exitCode -ne 0) { throw "Application exited with code $($record.exitCode)" }

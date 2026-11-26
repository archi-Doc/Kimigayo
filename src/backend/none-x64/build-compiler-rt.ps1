# Builds x86_64-unknown-none's kimi_backend, compiler-rt 22.1.8's soft-float builtins, from the pinned LLVM source tarball (Tatara B9e,
# decision 33; development only: the archive kimi_backend_none_x64_v1.a and compiler-rt's LICENSE.TXT, as LICENSE-compiler-rt.txt, are
# committed beside profile.json and carried by the compiler, Kimi.csproj). Route: Windows' tar.exe (libarchive with liblzma, probe P3)
# extracts only <top>/compiler-rt/lib/builtins/ and <top>/compiler-rt/LICENSE.TXT, <top> the tarball's one top folder (every listed
# entry under the first entry's first segment); the pinned clang compiles the catalog's sources, run in the builtins folder so each
# object names its source file alone, with $Flags below, the row's ABI (soft float through -mgeneral-regs-only, no red zone, the kernel
# code model; T's ruling 142); llvm-ar rcD archives the objects in the catalog's order, run in their folder so the members are file
# names. The catalog's sources are probe P1's list: the files defining the routines the generator's float operations call on the row
# (probe P0b), and the generic lib/builtins files those need, so no member is unreferenced.
# Checks, all before anything is copied to -OutputDirectory: the catalog's llvmVersion and compilerRt.version are the pinned LLVM version
# (Windows' profile, the tools' version); the tarball's SHA-256 is the catalog's sourceSha256 (its signature is
# checked at the download); every undefined symbol of every member is defined in the archive; no member holds an xmm register or an
# x87 instruction (llvm-objdump -d); no product holds the build directory's path; then the catalog match: the archive's defined external
# symbols are the catalog's providedSymbols (Ordinal order), the row's supply, the archive is its catalog row's SHA-256 and LICENSE.TXT
# the catalog's licenseSha256. The report (build-compiler-rt.json in the build directory, written before the catalog match) states the
# tools, the flags, the sources, the symbol counts, the defined symbols and each product's SHA-256 against the catalog's, so a changed
# list's first run fails the match and its report gives the catalog's values.
# Reproduction (T-M8) is a procedure of two runs around this script, not the script itself: run it twice with new build directories at
# different depths; both runs passing the catalog match gives byte-equal products, equal to the committed files.
# Adoption commits the archive with its stored facts (kimi_backend_none_x64_v1.a.facts beside it, Tatara's inspection that a native
# build reads instead of inspecting the file): run NativeToolchainTest's facts row (EachCompilerSupplysFactsAreItsInspection) with
# KIMI_SUPPLY_FACTS=write, build Kimi again and run the row again; the products, their catalog hashes and their facts land in one commit.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot,
    [Parameter(Mandatory)] [string] $Tarball,
    [Parameter(Mandatory)] [string] $BuildDirectory,
    [string] $OutputDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../windows-x64/toolchain.ps1')
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'profile.json') -Raw | ConvertFrom-Json
$llvmVersion = (Read-KimiWindowsProfile).llvmVersion
if ($catalog.llvmVersion -cne $llvmVersion -or $catalog.compilerRt.version -cne $llvmVersion) { throw "The catalog's llvmVersion $($catalog.llvmVersion) and compilerRt.version $($catalog.compilerRt.version) are not the pinned LLVM $llvmVersion" }
$Flags = @('--target=x86_64-unknown-none', '-ffreestanding', '-fno-builtin', '-nostdlibinc', '-mno-red-zone', '-mcmodel=kernel', '-mgeneral-regs-only', '-O2', '-fno-stack-protector', '-c')
$archive = $catalog.files[0].name
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $BuildDirectory) { throw "The build directory must be new: $BuildDirectory" }
if ((Get-KimiSha256 $Tarball) -cne $catalog.compilerRt.sourceSha256) { throw "The tarball is not the catalog's LLVM $($catalog.compilerRt.version) source: $Tarball" }
$tools = [ordered]@{}
foreach ($name in @('clang', 'llvm-ar', 'llvm-nm', 'llvm-objdump')) {
    $tools[$name] = Get-KimiLlvmToolIdentity (Join-Path $LlvmRoot "bin/$name.exe") $llvmVersion
}
$tar = Join-Path $env:SystemRoot 'System32/tar.exe'
$listing = & $tar -tf $Tarball
if ($LASTEXITCODE -ne 0) { throw "tar cannot list the tarball ($LASTEXITCODE)" }
$top = ($listing[0] -split '/')[0]
if (-not $top -or @($listing | Where-Object { $_ -cne $top -and -not $_.StartsWith("$top/", [StringComparison]::Ordinal) }).Count -ne 0) { throw "The tarball has no one top folder: $($listing[0])" }
$source = Join-Path $BuildDirectory 'source'
$objects = Join-Path $BuildDirectory 'objects'
New-Item -ItemType Directory -Path $source, $objects | Out-Null
& $tar -xf $Tarball -C $source "$top/compiler-rt/lib/builtins" "$top/compiler-rt/LICENSE.TXT"
if ($LASTEXITCODE -ne 0) { throw "tar cannot extract compiler-rt's builtins and licence ($LASTEXITCODE)" }
$builtins = Join-Path $source "$top/compiler-rt/lib/builtins"
$members = @($catalog.sources | ForEach-Object { [IO.Path]::ChangeExtension($_, '.o') })
Push-Location -LiteralPath $builtins
try {
    for ($i = 0; $i -lt $members.Count; $i++) {
        & $tools.clang.path @Flags $catalog.sources[$i] -o (Join-Path $objects $members[$i])
        if ($LASTEXITCODE -ne 0) { throw "clang cannot compile $($catalog.sources[$i]) ($LASTEXITCODE)" }
    }
}
finally { Pop-Location }
Push-Location -LiteralPath $objects
try {
    & $tools['llvm-ar'].path rcD $archive @members
    if ($LASTEXITCODE -ne 0) { throw "llvm-ar failed ($LASTEXITCODE)" }
}
finally { Pop-Location }
$path = Join-Path $objects $archive
function Get-Symbols([string[]] $Arguments) {
    $names = & $tools['llvm-nm'].path @Arguments $path
    if ($LASTEXITCODE -ne 0) { throw "llvm-nm failed ($LASTEXITCODE)" }
    return [Collections.Generic.HashSet[string]]::new([string[]] @($names | Where-Object { $_ -and $_ -notmatch ':$' }), [StringComparer]::Ordinal)
}
$defined = Get-Symbols @('--defined-only', '--extern-only', '-j')
$undefined = Get-Symbols @('--undefined-only', '-j')
$missing = @($undefined | Where-Object { -not $defined.Contains($_) } | Sort-Object -CaseSensitive)
if ($missing) { throw "The archive's members need symbols it does not define: $($missing -join ', ')" }
$provided = [string[]] @($defined)
[Array]::Sort($provided, [StringComparer]::Ordinal)
$disassembly = & $tools['llvm-objdump'].path -d --no-show-raw-insn $path
if ($LASTEXITCODE -ne 0) { throw "llvm-objdump failed ($LASTEXITCODE)" }
$float = @($disassembly | Where-Object { $_ -match '^\s*[0-9a-f]+:\s+(f\w*|\S.*(xmm|%st))' })
if ($float) { throw "A member holds an xmm register or an x87 instruction: $($float[0])" }
$license = Join-Path $source "$top/compiler-rt/LICENSE.TXT"
$forms = @($BuildDirectory, $BuildDirectory.Replace('\', '/'))
$text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($path))
foreach ($form in $forms) { if ($text.IndexOf($form, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "$path holds the build directory's path" } }
$identities = [ordered]@{}
$hash = Get-KimiSha256 $path
$identities[$archive] = @{ sha256 = $hash; catalogMatched = $hash -ceq $catalog.files[0].sha256 }
$hash = Get-KimiSha256 $license
$identities['LICENSE.TXT'] = @{ sha256 = $hash; catalogMatched = $hash -ceq $catalog.compilerRt.licenseSha256 }
$symbolsMatched = ($provided -join ',') -ceq ($catalog.providedSymbols -join ',')
[ordered]@{
    compilerRt = $catalog.compilerRt; tools = $tools; flags = $Flags; sources = $catalog.sources
    undefinedChecked = $undefined.Count; definedSymbols = $provided.Count; providedSymbols = $provided; providedSymbolsMatched = $symbolsMatched
    files = $identities
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'build-compiler-rt.json') -Encoding utf8
$identities.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value.sha256) catalog=$($_.Value.catalogMatched)" }
"providedSymbols catalog=$symbolsMatched"
$unmatched = @($identities.Keys | Where-Object { -not $identities[$_].catalogMatched }) + @(if (-not $symbolsMatched) { 'providedSymbols' })
if ($unmatched) { throw "Not the catalog's, so nothing is copied: $($unmatched -join ', ') (build-compiler-rt.json states the archive's)" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $path -Destination (Join-Path $OutputDirectory $archive) -Force
Copy-Item -LiteralPath $license -Destination (Join-Path $OutputDirectory 'LICENSE-compiler-rt.txt') -Force

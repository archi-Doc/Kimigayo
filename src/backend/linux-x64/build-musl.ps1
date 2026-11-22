# Builds linux-x64's c supply, musl's crt1.o, crti.o, crtn.o and libc.a, from the pinned tarball (Tatara B7b4; development only: the
# products are committed beside profile.json and carried by the compiler, Kimi.csproj). Route M1: WSL's Ubuntu runs musl's own configure
# and make, which call the pinned LLVM's clang.exe and llvm-ar.exe through WSL interop; the two wrappers written beside the extracted
# tree only give a Windows process NUL for /dev/null and llvm-ar its members in a response file (a command line has 32,767 characters).
# Checks: the tarball's SHA-256 is the catalog's sourceSha256 (its signature is checked at the download); every non-weak undefined
# symbol of libc.a's members is defined in libc.a or the crt, except exactly the catalog's compilerRuntimeSymbols (clang's complex
# multiplication helpers, which only cpow, cpowf and cpowl call and no supply defines); c's symbols (the catalog's providedSymbols)
# are defined in libc.a; no product holds the build directory's path. The report (build-musl.json in the build directory) states each product's SHA-256 and
# whether it is the catalog's; two builds in two directories are compared byte for byte (the reproduction, T-M8).
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
$products = @('crt1.o', 'crti.o', 'crtn.o', 'libc.a')
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $BuildDirectory) { throw "The build directory must be new: $BuildDirectory" }
if ((Get-KimiSha256 $Tarball) -cne $catalog.musl.sourceSha256) { throw "The tarball is not the catalog's musl $($catalog.musl.version): $Tarball" }
$tools = [ordered]@{}
foreach ($name in @('clang', 'llvm-ar', 'llvm-ranlib', 'llvm-nm')) {
    $tools[$name] = Get-KimiLlvmToolIdentity (Join-Path $LlvmRoot "bin/$name.exe") $llvmVersion
}
# A Windows path as WSL's drvfs mounts it; the wrappers and make's variables take no quoting, so a path must have none to need.
function ConvertTo-WslPath([string] $Path) {
    if ($Path -notmatch '^([A-Za-z]):\\([^\s''"$`\\]+(\\[^\s''"$`\\]+)*)$') { throw "A path WSL cannot take unquoted: $Path" }
    return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\', '/')
}
New-Item -ItemType Directory -Path $BuildDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& (Join-Path $env:SystemRoot 'System32/tar.exe') -xzf $Tarball -C $BuildDirectory
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
$source = Join-Path $BuildDirectory "musl-$($catalog.musl.version)"
$cc = Join-Path $BuildDirectory 'cc'
$ar = Join-Path $BuildDirectory 'ar'
$lf = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($cc, "#!/bin/sh`nfor a in `"`$@`"; do shift; if [ `"`$a`" = /dev/null ]; then set -- `"`$@`" NUL; else set -- `"`$@`" `"`$a`"; fi; done`nexec $(ConvertTo-WslPath $tools.clang.path) --target=x86_64-linux-musl `"`$@`"`n", $lf)
[IO.File]::WriteAllText($ar, "#!/bin/sh`nmode=`$1; archive=`$2; shift 2; printf '%s\n' `"`$@`" > `"`$archive.rsp`"`n$(ConvertTo-WslPath $tools['llvm-ar'].path) `"`$mode`" `"`$archive`" `"@`$archive.rsp`"; status=`$?; rm -f `"`$archive.rsp`"; exit `$status`n", $lf)
$script = "set -e; chmod +x $(ConvertTo-WslPath $cc) $(ConvertTo-WslPath $ar); ./configure --target=x86_64-linux-musl --disable-shared CC=$(ConvertTo-WslPath $cc) AR=$(ConvertTo-WslPath $ar) RANLIB=$(ConvertTo-WslPath $tools['llvm-ranlib'].path); make -j`$(nproc) lib/crt1.o lib/crti.o lib/crtn.o lib/libc.a; uname -r"
$log = Join-Path $BuildDirectory 'make.log'
wsl.exe -d Ubuntu --cd $source --exec sh -c $script *> $log
if ($LASTEXITCODE -ne 0) { throw "musl's configure or make failed ($LASTEXITCODE): $log" }
foreach ($product in $products) { Copy-Item -LiteralPath (Join-Path $source "lib/$product") -Destination (Join-Path $OutputDirectory $product) -Force }
$paths = $products | ForEach-Object { Join-Path $OutputDirectory $_ }
$nm = $tools['llvm-nm'].path
function Get-Symbols([string[]] $Arguments) {
    $names = & $nm @Arguments
    if ($LASTEXITCODE -ne 0) { throw "llvm-nm failed ($LASTEXITCODE)" }
    return [Collections.Generic.HashSet[string]]::new([string[]] @($names | Where-Object { $_ -and $_ -notmatch ':$' }), [StringComparer]::Ordinal)
}
$defined = Get-Symbols (@('--defined-only', '--extern-only', '-j') + $paths)
$undefined = Get-Symbols @('--undefined-only', '--no-weak', '-j', $paths[3])
$missing = @($undefined | Where-Object { -not $defined.Contains($_) } | Sort-Object -CaseSensitive)
if (($missing -join ',') -cne ($catalog.compilerRuntimeSymbols -join ',')) { throw "libc.a's members need symbols that libc.a and the crt do not define: $($missing -join ', ') (the catalog's: $($catalog.compilerRuntimeSymbols -join ', '))" }
$library = Get-Symbols @('--defined-only', '--extern-only', '-j', $paths[3])
$absent = @($catalog.providedSymbols | Where-Object { -not $library.Contains($_) })
if ($absent) { throw "libc.a does not define c's symbols: $($absent -join ', ')" }
$forms = @($BuildDirectory, $BuildDirectory.Replace('\', '/'), (ConvertTo-WslPath $BuildDirectory))
$identities = [ordered]@{}
foreach ($path in $paths) {
    $text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($path))
    foreach ($form in $forms) { if ($text.IndexOf($form, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "$path holds the build directory's path" } }
    $name = [IO.Path]::GetFileName($path)
    $hash = Get-KimiSha256 $path
    $identities[$name] = @{ sha256 = $hash; catalogMatched = $hash -ceq ($catalog.files | Where-Object name -ceq $name).sha256 }
}
[ordered]@{
    musl = $catalog.musl; tools = $tools; undefinedChecked = $undefined.Count; definedSymbols = $defined.Count; products = $identities
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'build-musl.json') -Encoding utf8
$identities.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value.sha256) catalog=$($_.Value.catalogMatched)" }

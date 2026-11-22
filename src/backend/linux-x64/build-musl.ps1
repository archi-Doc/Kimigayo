# Builds linux-x64's c supply, musl's crt1.o, crti.o, crtn.o and libc.a, from the pinned tarball (Tatara B7b4; development only: the
# products and the tarball's COPYRIGHT are committed beside profile.json and carried by the compiler, Kimi.csproj). Route M1: the WSL
# distribution -Distribution (Ubuntu by default) runs musl's own configure and make, which call the pinned LLVM's clang.exe and
# llvm-ar.exe through WSL interop; the two wrappers written beside the extracted tree only give a Windows process NUL for /dev/null and
# llvm-ar its members in a response file (a command line has 32,767 characters).
# Checks, all before anything is copied to -OutputDirectory: the tarball's SHA-256 is the catalog's sourceSha256 (its signature is
# checked at the download); every non-weak undefined symbol of libc.a's members is defined in libc.a or the crt, except exactly the
# catalog's compilerRuntimeSymbols (clang's complex multiplication helpers, which only cpow, cpowf and cpowl call and no supply
# defines); c's symbols (the catalog's providedSymbols) are defined in libc.a; no product holds the build directory's path; each product
# is its catalog row's SHA-256 and the COPYRIGHT is the catalog's copyrightSha256. The report (build-musl.json in the build directory,
# written before the catalog match) states the tools, the distribution's kernel and make versions and each file's SHA-256 against the
# catalog's, so a new musl's first run fails the match and its report gives the catalog's hashes.
# Reproduction (T-M8) is a procedure of two runs around this script, not the script itself: run it twice with new build directories at
# different depths; both runs passing the catalog match gives byte-equal products, equal to the committed files.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot,
    [Parameter(Mandatory)] [string] $Tarball,
    [Parameter(Mandatory)] [string] $BuildDirectory,
    [string] $OutputDirectory = $PSScriptRoot,
    [string] $Distribution = 'Ubuntu'
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
$kernel = wsl.exe -d $Distribution --exec uname -r
if ($LASTEXITCODE -ne 0) { throw "WSL's $Distribution cannot run uname ($LASTEXITCODE)" }
$make = @(wsl.exe -d $Distribution --exec make --version)[0]
if ($LASTEXITCODE -ne 0) { throw "WSL's $Distribution cannot run make ($LASTEXITCODE)" }
# A Windows path as WSL's drvfs mounts it; the wrappers and make's variables take no quoting, so a path must have none to need.
function ConvertTo-WslPath([string] $Path) {
    if ($Path -notmatch '^([A-Za-z]):\\([^\s''"$`\\]+(\\[^\s''"$`\\]+)*)$') { throw "A path WSL cannot take unquoted: $Path" }
    return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\', '/')
}
New-Item -ItemType Directory -Path $BuildDirectory | Out-Null
& (Join-Path $env:SystemRoot 'System32/tar.exe') -xzf $Tarball -C $BuildDirectory
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
$source = Join-Path $BuildDirectory "musl-$($catalog.musl.version)"
$cc = Join-Path $BuildDirectory 'cc'
$ar = Join-Path $BuildDirectory 'ar'
$lf = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($cc, "#!/bin/sh`nfor a in `"`$@`"; do shift; if [ `"`$a`" = /dev/null ]; then set -- `"`$@`" NUL; else set -- `"`$@`" `"`$a`"; fi; done`nexec $(ConvertTo-WslPath $tools.clang.path) --target=x86_64-linux-musl `"`$@`"`n", $lf)
[IO.File]::WriteAllText($ar, "#!/bin/sh`nmode=`$1; archive=`$2; shift 2; printf '%s\n' `"`$@`" > `"`$archive.rsp`"`n$(ConvertTo-WslPath $tools['llvm-ar'].path) `"`$mode`" `"`$archive`" `"@`$archive.rsp`"; status=`$?; rm -f `"`$archive.rsp`"; exit `$status`n", $lf)
$script = "set -e; chmod +x $(ConvertTo-WslPath $cc) $(ConvertTo-WslPath $ar); ./configure --target=x86_64-linux-musl --disable-shared CC=$(ConvertTo-WslPath $cc) AR=$(ConvertTo-WslPath $ar) RANLIB=$(ConvertTo-WslPath $tools['llvm-ranlib'].path); make -j`$(nproc) lib/crt1.o lib/crti.o lib/crtn.o lib/libc.a"
$log = Join-Path $BuildDirectory 'make.log'
wsl.exe -d $Distribution --cd $source --exec sh -c $script *> $log
if ($LASTEXITCODE -ne 0) { throw "musl's configure or make failed ($LASTEXITCODE): $log" }
$paths = $products | ForEach-Object { Join-Path $source "lib/$_" }
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
$paths += Join-Path $source 'COPYRIGHT'
$hash = Get-KimiSha256 $paths[4]
$identities['COPYRIGHT'] = @{ sha256 = $hash; catalogMatched = $hash -ceq $catalog.musl.copyrightSha256 }
[ordered]@{
    musl = $catalog.musl; tools = $tools; wsl = [ordered]@{ distribution = $Distribution; kernel = "$kernel"; make = $make }
    undefinedChecked = $undefined.Count; definedSymbols = $defined.Count; files = $identities
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'build-musl.json') -Encoding utf8
$identities.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value.sha256) catalog=$($_.Value.catalogMatched)" }
$unmatched = @($identities.Keys | Where-Object { -not $identities[$_].catalogMatched })
if ($unmatched) { throw "Not the catalog's bytes, so nothing is copied: $($unmatched -join ', ') (build-musl.json states their SHA-256)" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($path in $paths) { Copy-Item -LiteralPath $path -Destination (Join-Path $OutputDirectory ([IO.Path]::GetFileName($path))) -Force }

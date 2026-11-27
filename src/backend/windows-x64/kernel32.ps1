# Shared import definition, library check and generation for all PowerShell builders; it reads toolchain.ps1, which its callers load.
# The adopted kernel32 import library, committed beside the catalog (profile.json's kernel32.sha256) and carried by the compiler: the
# one place naming it, which every script linking kernel32 reads (build.ps1 names its candidate after it).
function Get-KimiKernel32 { return Join-Path $PSScriptRoot 'kernel32.lib' }

function Get-KimiKernel32Definition {
    $definition = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'kernel32.def')).Replace("`r`n", "`n").TrimEnd() + "`n"
    $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($definition)))
    if ($hash -cne (Read-KimiWindowsProfile).kernel32.definitionSha256) { throw 'Kernel32 definition SHA-256 mismatch' }
    return $definition
}

# An import library's content, as the tools read it: llvm-dlltool names KERNEL32.dll, and llvm-readobj lists x64 COFF members only that
# define exactly the definition's symbols and their __imp_ forms, each once. Generation's check (New-KimiKernel32Library), so the
# adopted bytes were inspected when setup or an LLVM update reproduced them.
function Test-KimiKernel32Library([string] $Dlltool, [string] $Readobj, [string] $Path) {
    $dll = & $Dlltool -I $Path | Out-String
    if ($LASTEXITCODE -ne 0 -or $dll.Trim() -cne 'KERNEL32.dll') { throw 'Unexpected generated kernel32 DLL name' }
    $inspection = & $Readobj --file-headers $Path | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect generated kernel32 library' }
    $symbols = @((Get-KimiKernel32Definition).Split("`n") | Select-Object -Skip 2 | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $expected = @($symbols) + @($symbols | ForEach-Object { "__imp_$_" })
    $actual = @([regex]::Matches($inspection, '(?m)^Symbol: (\S+)\r?$') | ForEach-Object { $_.Groups[1].Value })
    $formats = @([regex]::Matches($inspection, '(?m)^Format: (\S+)\r?$') | ForEach-Object { $_.Groups[1].Value })
    if ($formats.Count -eq 0 -or @($formats | Where-Object { $_ -cnotin @('COFF-x86-64','COFF-import-file-x86-64') }).Count -ne 0) {
        throw 'Generated kernel32 library has an unexpected architecture'
    }
    if ((($actual | Sort-Object -CaseSensitive) -join ',') -cne (($expected | Sort-Object -CaseSensitive) -join ',')) {
        throw 'Generated kernel32 library has an unexpected import symbol set'
    }
}

function New-KimiKernel32Library([hashtable] $Tools, [string] $OutputPath) {
    $output = [IO.Path]::GetFullPath($OutputPath)
    $staging = $output + '-' + [guid]::NewGuid().ToString('N')
    $dlltool = (Resolve-Path -LiteralPath $Tools['llvm-dlltool']).Path
    $readobj = (Resolve-Path -LiteralPath $Tools['llvm-readobj']).Path
    New-Item -ItemType Directory -Path $staging | Out-Null
    Push-Location -LiteralPath $staging
    try {
        [IO.File]::WriteAllText((Join-Path $staging 'kernel32.def'), (Get-KimiKernel32Definition), [Text.UTF8Encoding]::new($false))
        & $dlltool -m i386:x86-64 -d kernel32.def -l kernel32.lib
        if ($LASTEXITCODE -ne 0) { throw 'Kernel32 import library generation failed' }
        Test-KimiKernel32Library $dlltool $readobj kernel32.lib
        Move-Item -LiteralPath (Join-Path $staging 'kernel32.def') -Destination ([IO.Path]::ChangeExtension($output, '.def')) -Force
        Move-Item -LiteralPath (Join-Path $staging 'kernel32.lib') -Destination $output -Force
        return @{ path = $output; sha256 = Get-KimiSha256 $output; generator = 'llvm-dlltool'; dll = 'KERNEL32.dll'; definitionSha256 = (Read-KimiWindowsProfile).kernel32.definitionSha256 }
    }
    finally {
        Pop-Location
        # Only exact generated files in our fresh staging directory.
        foreach ($name in @('kernel32.def', 'kernel32.lib')) { Remove-Item -LiteralPath (Join-Path $staging $name) -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $staging -Force
    }
}

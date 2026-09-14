# Shared import definition, identity checks and generation for all PowerShell builders.
function Get-KimiKernel32Definition {
    $definition = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'kernel32.def')).Replace("`r`n", "`n").TrimEnd() + "`n"
    $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($definition)))
    if ($hash -cne (Read-KimiWindowsProfile).kernel32.definitionSha256) { throw 'Kernel32 definition SHA-256 mismatch' }
    return $definition
}

function Get-KimiDlltoolIdentity {
    [CmdletBinding()]
    param([string] $ToolPath, [switch] $AllowUnpinnedToolchain)
    if (-not (Test-Path -LiteralPath $ToolPath -PathType Leaf)) { throw 'llvm-dlltool not found' }
    $resolved = (Resolve-Path -LiteralPath $ToolPath).Path
    $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = (Read-KimiWindowsProfile).kernel32.dlltoolSha256
    $matched = $hash -ceq $expected
    if (-not $matched) {
        if (-not $AllowUnpinnedToolchain) { throw 'llvm-dlltool SHA-256 mismatch. Use -AllowUnpinnedToolchain only for exploratory builds.' }
        Write-Warning 'llvm-dlltool SHA-256 mismatch; continuing with an unverified toolchain.'
    }
    return @{ path = $resolved; sha256 = $hash; expectedSha256 = $expected; hashMatched = $matched }
}

function Assert-KimiKernel32Manifest($Entry) {
    if ($Entry.kind -cne 'import' -or $Entry.generator -cne 'llvm-dlltool' -or $Entry.dll -cne 'KERNEL32.dll' -or
        $Entry.definitionSha256 -cne (Read-KimiWindowsProfile).kernel32.definitionSha256 -or $Entry.PSObject.Properties['input']) {
        throw 'Invalid generated kernel32 identity. Re-emit the link manifest; external kernel32 paths are no longer supported.'
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
        $dll = & $dlltool -I kernel32.lib | Out-String
        if ($LASTEXITCODE -ne 0 -or $dll.Trim() -cne 'KERNEL32.dll') { throw 'Unexpected generated kernel32 DLL name' }
        $inspection = & $readobj --file-headers kernel32.lib | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect generated kernel32 library' }
        $symbols = @('GetProcessHeap','HeapAlloc','HeapFree','GetStdHandle','WriteFile','GetLastError','ExitProcess','VirtualAlloc','VirtualProtect','VirtualFree')
        $expected = @($symbols) + @($symbols | ForEach-Object { "__imp_$_" })
        $actual = @([regex]::Matches($inspection, '(?m)^Symbol: (\S+)\r?$') | ForEach-Object { $_.Groups[1].Value })
        $formats = @([regex]::Matches($inspection, '(?m)^Format: (\S+)\r?$') | ForEach-Object { $_.Groups[1].Value })
        if ((($actual | Sort-Object -CaseSensitive) -join ',') -cne (($expected | Sort-Object -CaseSensitive) -join ',') -or
            $formats.Count -eq 0 -or @($formats | Where-Object { $_ -cnotin @('COFF-x86-64','COFF-import-file-x86-64') }).Count -ne 0) {
            throw 'Generated kernel32 library has an unexpected architecture or import symbol set'
        }
        Move-Item -LiteralPath (Join-Path $staging 'kernel32.def') -Destination ([IO.Path]::ChangeExtension($output, '.def')) -Force
        Move-Item -LiteralPath (Join-Path $staging 'kernel32.lib') -Destination $output -Force
        return @{ path = $output; sha256 = (Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant(); generator = 'llvm-dlltool'; dll = 'KERNEL32.dll'; definitionSha256 = (Read-KimiWindowsProfile).kernel32.definitionSha256 }
    }
    finally {
        Pop-Location
        # Only exact generated files in our fresh staging directory.
        foreach ($name in @('kernel32.def', 'kernel32.lib')) { Remove-Item -LiteralPath (Join-Path $staging $name) -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $staging -Force
    }
}

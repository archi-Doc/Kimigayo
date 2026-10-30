[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [string] $ToolchainRoot = '',
    [string] $ResultRoot = (Join-Path $PSScriptRoot '../../../artifacts/verify/library-verification')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path ([IO.Path]::GetFullPath($ResultRoot)) ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$tools = @{}
$identities = @{}
foreach ($name in @('opt', 'llc', 'llvm-nm')) {
    $tools[$name] = Join-Path $ToolchainRoot "$name.exe"
    $identities[$name] = Get-KimiLlvmToolIdentity $tools[$name] (Read-KimiWindowsProfile).llvmVersion
}
function Invoke-Tool([string] $Name, [string[]] $Arguments) {
    & $tools[$Name] @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Library verification failed: $Name" }
}
# Library sources emitted through the CLI, so their IR is MIR code generation's on the pipeline this process selects (KIMI_PIPELINE).
$sources = [ordered]@{
    Empty = ''
    Main = 'public func main(value: i32) -> i32 => value + 1'
    MainAbort = 'public func main() => $abort("must not execute")'
    Functions = "public group Api`n    public func answer() -> i32 => 42`n    public func text() -> string => `"library`""
    Generic = "public group Api`n    public func keep<T>(value: T) -> T => value@move"
    Cleanup = "public struct Value`n    public let value: i32`n    public init(value: i32) => self.value = value`n    drop => Console.writeLine(`"drop`")"
}
$checks = [Collections.Generic.List[object]]::new()
$status = 'FAIL'
try {
    $fixtures = foreach ($name in $sources.Keys) {
        $directory = Join-Path $work "sources/$name"
        $null = New-Item -ItemType Directory -Path $directory
        'OutputKind="Library" Targets={"x86_64-pc-windows-msvc"} LlvmBin="absent toolchain"' | Set-Content -LiteralPath "$directory/$name.kimiproj" -Encoding utf8
        [IO.File]::WriteAllText("$directory/Library.kimi", $sources[$name], [Text.UTF8Encoding]::new($false))
        & dotnet $compiler emit "$directory/$name.kimiproj" 1> "$directory/emit.stdout" 2> "$directory/emit.stderr"
        if ($LASTEXITCODE -ne 0) { throw "Library emit failed: $name" }
        Get-Item -LiteralPath "$directory/bin/x86_64-pc-windows-msvc/$name.ll"
    }
    foreach ($fixture in $fixtures) {
        foreach ($level in @('O0', 'O2')) {
            $ir = $fixture.FullName
            $stem = Join-Path $work ($fixture.BaseName + '.' + $level)
            Invoke-Tool 'opt' @('-passes=verify', '-disable-output', $ir)
            if ($level -eq 'O2') {
                Invoke-Tool 'opt' @('-S', '-passes=default<O2>', $ir, '-o', "$stem.ll")
                $ir = "$stem.ll"
                Invoke-Tool 'opt' @('-passes=verify', '-disable-output', $ir)
            }
            Invoke-Tool 'llc' @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2', '-relocation-model=pic', '-code-model=small', $ir, '-o', "$stem.obj")
            $symbols = Invoke-Tool 'llvm-nm' @('--defined-only', '--format=posix', "$stem.obj") | Out-String
            [IO.File]::WriteAllText("$stem.symbols", $symbols)
            if ($symbols -match '(?m)^__kimi_start\s' -or $symbols -notmatch '(?m)^_fltused\s') { throw 'Library object has an entry or lacks the profile marker' }
            $checks.Add(@{ fixture = $fixture.Name; optimization = $level; irSha256 = (Get-FileHash -LiteralPath $fixture.FullName).Hash; objectSha256 = (Get-FileHash -LiteralPath "$stem.obj").Hash })
        }
    }
    $projectDirectory = Join-Path $work 'project'
    $null = New-Item -ItemType Directory -Path $projectDirectory
    $project = Join-Path $projectDirectory 'Library.kimiproj'
    'OutputKind="Library" Targets={"x86_64-pc-windows-msvc"} LlvmBin="absent toolchain"' | Set-Content -LiteralPath $project -Encoding utf8
    'public func main(value: i32) -> i32 => value + 1' | Set-Content -LiteralPath (Join-Path $projectDirectory 'Library.kimi') -Encoding utf8
    & dotnet $compiler emit $project 1> (Join-Path $work 'emit.stdout') 2> (Join-Path $work 'emit.stderr')
    if ($LASTEXITCODE -ne 0) { throw 'Library emit failed' }
    $manifestPath = Join-Path $projectDirectory 'bin/x86_64-pc-windows-msvc/Library.link.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.outputKind -cne 'Library' -or $null -ne $manifest.entry -or $null -ne $manifest.subsystem) { throw 'Incorrect Library inspection manifest' }
    if ($manifest.irSha256 -cne (Get-FileHash -LiteralPath (Join-Path (Split-Path $manifestPath) $manifest.irFile)).Hash.ToLowerInvariant()) { throw 'Library IR hash mismatch' }
    & dotnet $compiler build $project 1> (Join-Path $work 'build.stdout') 2> (Join-Path $work 'build.stderr')
    # IMPL 20.3.2: a Library build succeeds on its product checks, publishes only a checked record and runs no native tool.
    if ($LASTEXITCODE -ne 0) { throw 'Library build failed' }
    $record = Get-Content -LiteralPath (Join-Path (Split-Path $manifestPath) 'Library.link.build.json') -Raw | ConvertFrom-Json
    if ($record.status -cne 'checked' -or $record.outputKind -cne 'Library') { throw 'Library build must publish only a checked record' }
    & dotnet $compiler run $project 1> (Join-Path $work 'run.stdout') 2> (Join-Path $work 'run.stderr')
    if ($LASTEXITCODE -ne 1) { throw 'Library inspection must not become runnable' }
    if (@(Get-ChildItem -LiteralPath $projectDirectory -Recurse -Filter '*.exe').Count -ne 0) { throw 'Library command published an executable' }
    $status = 'PASS'
}
finally {
    $selectors = @(Get-ChildItem env: | Where-Object Name -In 'KIMI_PIPELINE' | ForEach-Object { "$($_.Name)=$($_.Value)" })
    @{ status = $status; configuration = $Configuration; compilerSha256 = (Get-FileHash -LiteralPath $compiler).Hash; selectors = $selectors; tools = $identities; checks = $checks } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $work 'result.json') -Encoding utf8
}
Write-Output "Passed $($checks.Count) Library LLVM/object checks and emit/build/run boundaries: $work"

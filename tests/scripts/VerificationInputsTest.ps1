# Isolated repository checks for exact-source verification and post-commit matching. No compiler build is needed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/verification-inputs.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $repo ('temp/verification-inputs/' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work -Force
$cases = 0
function Put([string] $path, [string] $text) {
    $full = Join-Path $work $path
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    [IO.File]::WriteAllText($full, $text, [Text.UTF8Encoding]::new($false))
}
function Expect([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:cases++
}
function Reject([scriptblock] $action, [string] $message) {
    try { $null = & $action }
    catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        $script:cases++
        return
    }
    throw "Expected rejection containing '$message'."
}
$null = Invoke-KimiVerificationGit $work @('init', '--quiet')
$null = Invoke-KimiVerificationGit $work @('config', 'user.name', 'Verification regression')
$null = Invoke-KimiVerificationGit $work @('config', 'user.email', 'verification@example.invalid')
$null = Invoke-KimiVerificationGit $work @('config', 'commit.gpgsign', 'false')
Put '.gitattributes' "* text=auto eol=lf`n"
Put '.gitignore' "bin/`nobj/`nartifacts/`n"
Put 'src/App.cs' "class App {}`r`n"
Put 'src/space 日.cs' "class Space {}`n"
Put 'src/App.csproj' "<Project />`n"
Put 'Directory.Build.props' "<Project />`n"
Put 'docs/SPEC.md' "Required behavior`n"
Put 'docs/dev/PLAN.md' "Current position`n"
Put 'draft/Proposals/ongoing.md' "Draft`n"
Put 'src/obj/project.assets.json' '{}'
$null = Invoke-KimiVerificationGit $work @('add', '--all')
$null = Invoke-KimiVerificationGit $work @('commit', '--quiet', '-m', 'Baseline')
$baseline = Get-KimiVerificationInputs $work
Expect ($baseline.files.path -contains 'src/space 日.cs') 'Unicode/spaced paths must be represented.'
Expect ($baseline.files.path -notcontains 'docs/dev/PLAN.md') 'Post-verification bookkeeping must be excluded.'
Expect ($baseline.files.path -notcontains 'draft/Proposals/ongoing.md') 'Independent proposals must be excluded.'
$first = Assert-KimiVerificationCommit $work $baseline
Expect ($first.Length -ge 40) 'Commit must resolve to a full object identity.'
Assert-KimiVerificationInputsStable $baseline (Get-KimiVerificationInputs $work)
$cases++

Put 'src/bin/generated.dll' 'output'
Put 'artifacts/report.json' 'output'
Put 'draft/Proposals/ongoing.md' 'unrelated edit'
Put 'docs/dev/PLAN.md' 'new position'
Assert-KimiVerificationInputsStable $baseline (Get-KimiVerificationInputs $work)
$cases++
Put 'src/App.cs' "class Changed {}`n"
$changed = Get-KimiVerificationInputs $work
Reject { Assert-KimiVerificationInputsStable $baseline $changed } 'src/App.cs'
Reject { Assert-KimiVerificationCommit $work $changed } 'does not match'
Put 'src/App.cs' "class App {}`n"
$normalized = Get-KimiVerificationInputs $work
Reject { Assert-KimiVerificationInputsStable $baseline $normalized } 'src/App.cs'
Expect ((Assert-KimiVerificationCommit $work $normalized) -ceq $first) 'CRLF/LF changes must have distinct byte hashes but the same Git-normalized commit.'
Put 'src/App.cs' "class App {}`r`n"

Put 'src/New.cs' "class New {}`n"
$added = Get-KimiVerificationInputs $work
Expect ($added.files.path -contains 'src/New.cs') 'New untracked source must be captured.'
Reject { Assert-KimiVerificationInputsStable $baseline $added } 'input set differs'
Reject { Assert-KimiVerificationCommit $work $added } 'omits verified input'
$null = Invoke-KimiVerificationGit $work @('add', '--', 'src/New.cs')
$null = Invoke-KimiVerificationGit $work @('commit', '--quiet', '-m', 'New source')
$second = Assert-KimiVerificationCommit $work $added
Expect ($second -cne $first) 'New source must require its containing commit.'
Reject { Assert-KimiVerificationCommit $work $baseline } 'does not match'

Put 'Directory.Build.props' '<Project changed="true" />'
Reject { Assert-KimiVerificationInputsStable $added (Get-KimiVerificationInputs $work) } 'Directory.Build.props'
Put 'Directory.Build.props' "<Project />`n"
Put 'src/obj/project.assets.json' '{"changed":true}'
Reject { Assert-KimiVerificationInputsStable $added (Get-KimiVerificationInputs $work) } 'dependencies changed'
Put 'src/obj/project.assets.json' '{}'
$null = Invoke-KimiVerificationGit $work @('rm', '--', 'src/space 日.cs')
$deleted = Get-KimiVerificationInputs $work
Reject { Assert-KimiVerificationInputsStable $added $deleted } 'input set differs'
Reject { Assert-KimiVerificationCommit $work $deleted } 'does not match'
$null = Invoke-KimiVerificationGit $work @('commit', '--quiet', '-m', 'Deleted source')
$null = Assert-KimiVerificationCommit $work $deleted
$cases++
Write-Output "Passed $cases verification input checks. Isolated repository retained: $work"

# G170 C0 sizes: A1 non-trivia C# tokens (the R4 unit-report / CompilerSizeBudgetTest count), lines, and change counts.
$ErrorActionPreference = 'Stop'
$wt = 'C:\Users\bwff1\repos\archi-Doc\Kimigayo\bin\worktrees\bu-g170-c0'
$bin = Join-Path $wt 'tests\xUnitTest\bin\Release\net10.0'
Add-Type -Path (Join-Path $bin 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $bin 'Microsoft.CodeAnalysis.CSharp.dll')
function Count-Tokens([string] $text) {
    $n = 0
    foreach ($t in [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text).GetRoot().DescendantTokens()) {
        if (-not $t.IsMissing -and $t.RawKind -ne [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::EndOfFileToken) { $n++ }
    }
    return $n
}
$names = $args
$paths = $names | ForEach-Object { "src/Kimi/Compiler/Binding/$_" }
$commits = @(git -C $wt log -n 30 --format=%H -- @paths)
"head $(git -C $wt rev-parse HEAD); last 30 commits touching the set: $($commits[-1].Substring(0,8))..$($commits[0].Substring(0,8)) ($(git -C $wt log -1 --format=%cs $commits[-1])..$(git -C $wt log -1 --format=%cs $commits[0]))"
'| File | Tokens | Lines | Commits (of the 30) | Commits (all history) |'
'| --- | ---: | ---: | ---: | ---: |'
$total = 0
foreach ($p in $paths) {
    $text = [IO.File]::ReadAllText((Join-Path $wt $p))
    $tok = Count-Tokens $text
    $total += $tok
    $lines = ($text.TrimEnd("`n") -split "`n").Count
    $in30 = @($commits | Where-Object { @(git -C $wt diff-tree --no-commit-id --name-only -r $_ -- $p).Count -gt 0 }).Count
    $all = @(git -C $wt log --format=%H -- $p).Count
    '| {0} | {1:N0} | {2:N0} | {3} | {4} |' -f $p.Replace('src/Kimi/Compiler/Binding/', ''), $tok, $lines, $in30, $all
}
"total tokens $total"

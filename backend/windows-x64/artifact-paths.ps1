# Paths in saved diagnostics are descriptive, never inputs to a subsequent build.
function ConvertTo-KimiArtifactText {
    param(
        [Parameter(ValueFromPipeline)] [AllowEmptyString()] [string] $Text,
        [string] $ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
        [string] $UserRoot = [Environment]::GetFolderPath('UserProfile')
    )
    process {
        # Match the longest root first. Support plain paths and JSON/LLVM escaping.
        foreach ($mapping in @(@($ProjectRoot, '/_/project'), @($UserRoot, '/_/user'))) {
            if (-not $mapping[0]) { continue }
            $root = $mapping[0].TrimEnd([char[]]'\/')
            foreach ($form in @($root.Replace('\', '\\'), $root.Replace('\', '/'), $root) | Select-Object -Unique) {
                $pattern = [regex]::Escape($form) + '(?=[\\/\s"'']|$)'
                $replacement = $mapping[1]
                $Text = [regex]::Replace($Text, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement }, 'IgnoreCase')
            }
        }
        $Text
    }
}

function Protect-KimiLlvmPaths([string] $Path) {
    # Do not rewrite program string constants: only LLVM's source identification.
    $text = [IO.File]::ReadAllText($Path)
    $text = [regex]::Replace($text, '(?m)^(?:; ModuleID = .*|source_filename = .*|!\d+ = .*!DIFile\(.*)$',
        [Text.RegularExpressions.MatchEvaluator]{ param($match) ConvertTo-KimiArtifactText $match.Value })
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function Invoke-KimiLlvmOutput([string] $ToolPath, [string[]] $Arguments, [string] $OutputPath) {
    # CodeView can record the -o argument even without full debug information.
    $tool = (Resolve-Path -LiteralPath $ToolPath).Path
    $output = [IO.Path]::GetFullPath($OutputPath)
    Push-Location -LiteralPath ([IO.Path]::GetDirectoryName($output))
    try {
        & $tool @Arguments -o ([IO.Path]::GetFileName($output))
        if ($LASTEXITCODE -ne 0) { throw "$tool failed ($LASTEXITCODE)" }
    }
    finally { Pop-Location }
}

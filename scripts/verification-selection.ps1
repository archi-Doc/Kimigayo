# Shared selector rules for verify.ps1. Discovery must use the freshly built assembly.
function ConvertTo-KimiTestPattern([string] $Pattern, [switch] $Method) {
    if (-not $Pattern -or $Pattern -notmatch '^\*?[\p{L}\p{N}_.+]*\*?$' -or $Pattern -eq '**') {
        throw "Invalid test selector '$Pattern'. Use a class or Class.Method name, with '*' only at the beginning or end."
    }
    if ($Pattern.StartsWith('*')) { return $Pattern }
    $dots = ($Pattern.ToCharArray() | Where-Object { $_ -eq '.' }).Count
    if ($Method -and $dots -eq 0) { throw "Method selector '$Pattern' needs its declaring class (Class.Method)." }
    if ((-not $Method -and $dots -eq 0) -or ($Method -and $dots -eq 1)) { return "XunitTest.$Pattern" }
    return $Pattern
}

function Get-KimiTestAlternatives([string] $Pattern, [switch] $Method) {
    $Pattern
    if ($Method) {
        $separator = $Pattern.LastIndexOf('.')
        if ($separator -ge 0 -and -not $Pattern.Substring(0, $separator).EndsWith('+AllocationTests')) {
            $Pattern.Insert($separator, '+AllocationTests')
        }
    }
    elseif (-not $Pattern.EndsWith('*') -and -not $Pattern.EndsWith('+AllocationTests')) {
        "$Pattern+AllocationTests"
    }
}

function Assert-KimiTestSelections([string[]] $Class, [string[]] $Method, [string[]] $DiscoveredMethods) {
    $classes = @($DiscoveredMethods | ForEach-Object { $_.Substring(0, $_.LastIndexOf('.')) } | Sort-Object -Unique)
    foreach ($selection in $Class) {
        $matched = $false
        foreach ($pattern in (Get-KimiTestAlternatives $selection)) {
            if ($classes -like $pattern) { $matched = $true; break }
        }
        if (-not $matched) { throw "No test class matches '$selection' in the freshly built assembly." }
    }
    foreach ($selection in $Method) {
        $matched = $false
        foreach ($pattern in (Get-KimiTestAlternatives $selection -Method)) {
            if ($DiscoveredMethods -like $pattern) { $matched = $true; break }
        }
        if (-not $matched) { throw "No test method matches '$selection' in the freshly built assembly." }
    }
}

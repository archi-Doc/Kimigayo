# Shared selector and executed-test rules for verify.ps1. Discovery must use the freshly built assembly.
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

# The common compiler checks every Unit test selection includes (docs/dev/VERIFICATION.md, verification scope).
$KimiCommonTestClasses = @('XunitTest.ArchitectureRulesTest')

function Join-KimiCommonTestSelections([string[]] $Selection, [switch] $Method) {
    # Class selections are ORed, method selections are ORed and the two groups intersect, so the common classes
    # join each nonempty group (methods as Class.*) to remain a union with the caller's selection.
    if ($Selection.Count -eq 0) { return }
    $Selection
    foreach ($common in $KimiCommonTestClasses) {
        $pattern = if ($Method) { "$common.*" } else { $common }
        if ($Selection -notcontains $pattern) { $pattern }
    }
}

# The cases a run recorded, the test nodes of its per-test results (-result-xml); a run without a result file recorded none.
function Get-KimiRecordedCases([string] $ResultXml) {
    if (-not (Test-Path -LiteralPath $ResultXml)) { return }
    Select-Xml -LiteralPath $ResultXml -XPath '//test' | ForEach-Object { $_.Node }
}

# The cases a run executed: xUnit reports Explicit tests not requested as Not Run.
function Get-KimiExecutedCases([string] $ResultXml) { Get-KimiRecordedCases $ResultXml | Where-Object result -CNE 'NotRun' }

# The tests a run executed, as Class.Method.
function Get-KimiExecutedTests([string] $ResultXml) {
    Get-KimiExecutedCases $ResultXml | ForEach-Object { "$($_.type).$($_.method)" }
}

# Every class and method selection matches one of the tests, Class.Method: the methods discovered in the freshly built
# assembly before the run, or the tests it executed after it.
function Assert-KimiTestSelections([string[]] $Class, [string[]] $Method, [string[]] $Tests, [string] $Where) {
    $classes = @($Tests | ForEach-Object { $_.Substring(0, $_.LastIndexOf('.')) } | Sort-Object -Unique)
    foreach ($selection in $Class) {
        $matched = $false
        foreach ($pattern in (Get-KimiTestAlternatives $selection)) {
            if ($classes -like $pattern) { $matched = $true; break }
        }
        if (-not $matched) { throw "No test class matches '$selection' $Where." }
    }
    foreach ($selection in $Method) {
        $matched = $false
        foreach ($pattern in (Get-KimiTestAlternatives $selection -Method)) {
            if ($Tests -like $pattern) { $matched = $true; break }
        }
        if (-not $matched) { throw "No test method matches '$selection' $Where." }
    }
}

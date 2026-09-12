$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'artifact-paths.ps1')
$project = 'C:\Users\Example Person\work\Project'
$user = 'C:\Users\Example Person'
foreach ($root in @($project, $project.ToLowerInvariant(), $project.Replace('\', '/'), $project.Replace('\', '\\'))) {
    $result = ConvertTo-KimiArtifactText "File: $root/file.obj" -ProjectRoot $project -UserRoot $user
    if ($result -cne 'File: /_/project/file.obj') { throw "Path normalization failed: $result" }
}
$json = @{ path = "$project\bin\file.obj"; tool = "$user\tools\opt.exe"; sha256 = 'abc123' } | ConvertTo-Json |
    ConvertTo-KimiArtifactText -ProjectRoot $project -UserRoot $user | ConvertFrom-Json
if ($json.path -cne '/_/project\bin\file.obj' -or $json.tool -cne '/_/user\tools\opt.exe' -or $json.sha256 -cne 'abc123') {
    throw 'JSON paths or identity were damaged'
}
$unrelated = ConvertTo-KimiArtifactText "$project-other/file.obj" -ProjectRoot $project -UserRoot ''
if ($unrelated -cne "$project-other/file.obj") { throw 'Matched a partial directory name' }

$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$path = Join-Path $out ('path-test-' + [guid]::NewGuid().ToString('N') + '.ll')
try {
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $escaped = $repo.Replace('\', '\\')
    $constant = '@text = private constant [1 x i8] c"' + $escaped + '"'
    $ir = "; ModuleID = '$repo/test.ll'`nsource_filename = `"$escaped/test.ll`"`n!1 = !DIFile(filename: `"test.c`", directory: `"$escaped`")`n$constant`n"
    [IO.File]::WriteAllText($path, $ir)
    Protect-KimiLlvmPaths $path
    $actual = [IO.File]::ReadAllText($path)
    if ($actual -notlike '*source_filename = "/_/project/test.ll"*' -or
        $actual -notlike '*directory: "/_/project"*' -or -not $actual.Contains($constant)) {
        throw 'LLVM metadata normalization changed program data or left a private path'
    }
    Protect-KimiLlvmPaths $path
    if ([IO.File]::ReadAllText($path) -cne $actual) { throw 'Normalization is not idempotent' }
}
finally {
    Remove-Item -LiteralPath $path -Force
}
Write-Output 'Artifact path tests passed (plain/escaped paths, JSON identities, LLVM metadata and program data).'

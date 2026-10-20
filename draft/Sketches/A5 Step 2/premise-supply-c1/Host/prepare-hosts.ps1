# Filesystem-only preparation. Runtime commands live in the frozen packet.
param([Parameter(Mandatory=$true)][string]$Worktree)
$ErrorActionPreference='Stop'
$wt=(Resolve-Path -LiteralPath $Worktree).Path
$unit=Split-Path $PSScriptRoot
$destination=Join-Path $wt 'temp/c1-kernel-hosts-02'
if(Test-Path -LiteralPath $destination){throw "Preserve existing host directory: $destination"}
New-Item -ItemType Directory -Path $destination | Out-Null
$utf8=[System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $destination 'global.json'),"{`"sdk`":{`"version`":`"11.0.100-rc.2.26504.105`",`"rollForward`":`"disable`",`"allowPrerelease`":true}}`n",$utf8)
[System.IO.File]::WriteAllText((Join-Path $destination 'Directory.Build.props'),"<Project />`n",$utf8)
[System.IO.File]::WriteAllText((Join-Path $destination 'Directory.Build.targets'),"<Project />`n",$utf8)
[System.IO.File]::WriteAllText((Join-Path $destination 'Standalone.NuGet.Config'),"<configuration><packageSources><clear /></packageSources></configuration>`n",$utf8)
foreach($variant in @('before','after')){
    $path=Join-Path $destination $variant
    New-Item -ItemType Directory -Path (Join-Path $path 'source') | Out-Null
    $owner=if($variant -eq 'before'){'Before'}else{'Kernel'}
    foreach($name in @('Cases.cs','Demand.cs','Demand.Completion.cs','Forward.cs','Forward.Completion.cs','Forward.Identity.cs','Model.cs','Terms.cs')){
        Copy-Item -LiteralPath (Join-Path $unit "$owner/$name") -Destination (Join-Path $path "source/$name")
    }
    if($variant -eq 'before'){
        # Root nullable-target-correction-v2: preserve Before; transform its build copy only.
        $copied=Join-Path $path 'source/Demand.Completion.cs'
        $text=[IO.File]::ReadAllText($copied)
        $needle='facts.Error.Contains(target)'
        if([regex]::Matches($text,[regex]::Escape($needle)).Count -ne 1){throw 'Old null-guard transform must have exactly one site'}
        [IO.File]::WriteAllText($copied,$text.Replace($needle,'(target is not null && facts.Error.Contains(target))'),$utf8)
    }
    foreach($name in @('Program.cs','Harness.cs')){
        Copy-Item -LiteralPath (Join-Path $unit "Kernel/$name") -Destination (Join-Path $path "source/$name")
    }
    Copy-Item -LiteralPath (Join-Path $unit 'Before/expectations.json') -Destination $path
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'additional-inputs.json'),(Join-Path $PSScriptRoot 'additional-expectations.json') -Destination $path
    $project=@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>C1Kernel</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <EnableDefaultItems>false</EnableDefaultItems>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    <UseSharedCompilation>false</UseSharedCompilation>
    <UseAppHost>false</UseAppHost>
    <RuntimeFrameworkVersion>10.0.13</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="source/*.cs" />
    <None Include="expectations.json;additional-inputs.json;additional-expectations.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@
    [System.IO.File]::WriteAllText((Join-Path $path 'Kernel.csproj'),$project+"`n",$utf8)
}
Write-Output "Prepared isolated hosts at $destination; no restore/build/run."

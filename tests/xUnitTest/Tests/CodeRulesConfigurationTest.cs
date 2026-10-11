// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace XunitTest;

public class CodeRulesConfigurationTest
{
    [Fact]
    public void RoslynPreservesEveryConfiguredListEntry()
    {
        var errors = new List<Diagnostic>();
        var config = CodeRulesConfiguration.Read(CodeRulesTestHelper.Options(), TestContext.Current.CancellationToken, errors)!;
        Assert.Empty(errors);
        Assert.Contains("Kimi.Compiler.TyCtxt", config.Lists["allow_types.mir_build"]);
        Assert.Contains("Kimi.Compiler.Mir.MirCalleeKind:5", config.Lists["vocabularies"]);
        Assert.Contains("src/Kimi/Compiler/Emission/", config.Lists["scope.density.exclude"]);
        Assert.False(config.Applies(CodeRule.Find("KR3001")!, "src/Kimi/Compiler/Emission/Emission.cs"));
    }

    [Theory]
    [InlineData("stage", "R99")]
    [InlineData("max_parameters", "0")]
    [InlineData("max_parameters", "six")]
    [InlineData("scope.densitty.exclude", "src/Kimi/Compiler/Mir/")]
    [InlineData("catch_boundaries", "src/Kimi/..")]
    public async Task InvalidConfigurationDoesNotProduceCompletion(string option, string value)
    {
        var diagnostics = await CodeRulesTestHelper.Analyze(
            "class C { }", CodeRulesTestHelper.Options("Report", configure: x => x[CodeRulesConfiguration.Prefix + option] = value));
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, x => Assert.Equal("KR9003", x.Id));
    }

    [Fact]
    public async Task ReportCompletionCarriesCurrentAdditionalFileChecksums()
    {
        var diagnostics = await CodeRulesTestHelper.Analyze("class C { }", CodeRulesTestHelper.Options("Report"));
        var complete = Assert.Single(diagnostics);
        Assert.Equal("KR9000", complete.Id);
        Assert.Equal("1", complete.Properties["treeCount"]);
        Assert.Equal("exception,transitional", complete.Properties["annotationKinds"]);
        Assert.Equal("Sha256", complete.Properties["configAlgorithm"]);
        Assert.Equal(64, complete.Properties["configChecksum"]!.Length);
        Assert.Equal(64, complete.Properties["baselineChecksum"]!.Length);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:relative")]
    [InlineData("\\relative")]
    public async Task RepositoryRootMustBeFullyQualified(string root)
    {
        var diagnostics = await CodeRulesTestHelper.Analyze(
            "class C { }", CodeRulesTestHelper.Options("Report", configure: x => x["build_property.KimiRepositoryRoot"] = root));
        Assert.Equal("KR9003", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void BuiltInSeveritiesHaveOneConfigurationOwner()
    {
        var pending = new Stack<string>();
        pending.Push(Repository.Root);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(child) is not ("bin" or "obj" or ".git" or "temp" or "artifacts" or "node_modules" or ".vs"))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Path.GetExtension(file) is not (".props" or ".targets" or ".csproj" or ".globalconfig") && Path.GetFileName(file) != ".editorconfig")
                {
                    continue;
                }

                if (Path.GetRelativePath(Repository.Root, file).Replace('\\', '/') == "src/Kimi/CodeRules.globalconfig")
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                Assert.DoesNotContain("IDE0051", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IDE0052", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IDE0060", content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PathsAndScopeExclusionsAreBoundedByTheRepository()
    {
        var errors = new List<Diagnostic>();
        var config = CodeRulesConfiguration.Read(CodeRulesTestHelper.Options(), TestContext.Current.CancellationToken, errors)!;
        Assert.Empty(errors);
        Assert.Null(config.Relative(Path.Combine(Repository.Root + "-other", "src/Kimi/Test.cs")));
        Assert.Equal(CodeRulesTestHelper.SourcePath, config.Relative(Path.Combine(Repository.Root, CodeRulesTestHelper.SourcePath)));
        Assert.False(config.Applies(CodeRule.Find("KR2001")!, "src/Kimi/Compiler/Analysis/Test.cs"));
        Assert.True(config.Applies(CodeRule.Find("KR1004")!, "src/Kimi/Compiler/Analysis/Test.cs"));
        Assert.False(config.Applies(CodeRule.Find("KR1006")!, CodeRulesTestHelper.SourcePath));
        Assert.True(config.Applies(CodeRule.Find("KR1006")!, "src/Kimi/Compiler/Mir/Build/Test.cs"));
    }

    [Fact]
    public async Task GeneratedTreesDoNotCountAsAnalyzedSource()
    {
        var diagnostics = await CodeRulesTestHelper.Analyze("// <auto-generated>\nclass C { }", CodeRulesTestHelper.Options("Report"));
        Assert.Equal("0", Assert.Single(diagnostics).Properties["treeCount"]);
    }
}

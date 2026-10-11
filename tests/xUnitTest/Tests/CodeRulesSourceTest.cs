// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace XunitTest;

public class CodeRulesSourceTest
{
    [Fact]
    public void PartialTypeAndMemberIdentitiesSurviveFileMoves()
    {
        var compilation = CodeRulesTestHelper.Compilation("partial class C { void M() { } }", "partial class C { void N() { } }");
        var sources = compilation.SyntaxTrees.Select(x => new CodeRulesSource(compilation, x, x.FilePath, default)).ToArray();
        Assert.Equal(
            sources[0].Scope(sources[0].Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single()),
            sources[1].Scope(sources[1].Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single()));
        Assert.Equal("member:M:C.M", sources[0].MemberAnchor(sources[0].Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single()));
    }

    [Fact]
    public void LocalFunctionCollisionSuffixesOnlyApplyToIdenticalPaths()
    {
        var compilation = CodeRulesTestHelper.Compilation("class C { void M() { { void F() { void G() { } } } { void F() { } } void H() { } } }");
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        var anchors = source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(source.MemberAnchor).ToArray();
        Assert.Equal(["local:M:C.M/F#1", "local:M:C.M/F/G", "local:M:C.M/F#2", "local:M:C.M/H"], anchors);
    }

    [Fact]
    public void TopLevelLocalsHaveFileIdentity()
    {
        var compilation = CodeRulesTestHelper.Compilation("void F() { }");
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        Assert.Equal(
            "local:file:" + CodeRulesTestHelper.SourcePath + "/F",
            source.MemberAnchor(source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single()));
    }

    [Theory]
    [InlineData("class C { System.Action a = () => { void F() {} }; System.Action b = () => { void F() {} }; }")]
    [InlineData("class C { System.Action a = () => { void F() {} }, b = () => { void F() {} }; }")]
    public void FieldInitializerLocalFunctionsUseTheirFieldSymbols(string code)
    {
        var compilation = CodeRulesTestHelper.Compilation(code);
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        Assert.Equal(
            new[] { "local:F:C.a/F", "local:F:C.b/F" },
            source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(source.MemberAnchor));
    }

    [Fact]
    public void FieldLocalIdentitySurvivesMovingToAnotherPartialFile()
    {
        var anchors = new List<string?>();
        foreach (var sources in new[]
        {
            new[] { "partial class C { System.Action a = () => { void F() {} }; }", "partial class C {}" },
            new[] { "partial class C {}", "partial class C { System.Action a = () => { void F() {} }; }" },
        })
        {
            var compilation = CodeRulesTestHelper.Compilation(sources);
            var tree = compilation.SyntaxTrees.Single(x => x.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Any());
            var source = new CodeRulesSource(compilation, tree, tree.FilePath, default);
            anchors.Add(source.MemberAnchor(source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single()));
        }

        Assert.Equal(["local:F:C.a/F", "local:F:C.a/F"], anchors);
    }

    [Theory]
    [InlineData("void F() {} class C { void M() { void F() {} } }")]
    [InlineData("class C { void M() { void F() {} } } void F() {}")]
    public void LocalCollisionMapsStopAtOtherMemberOwners(string code)
    {
        var compilation = CodeRulesTestHelper.Compilation(code);
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        var anchors = source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(source.MemberAnchor).ToArray();
        Assert.Contains("local:file:" + CodeRulesTestHelper.SourcePath + "/F", anchors);
        Assert.Contains("local:M:C.M/F", anchors);
    }

    [Theory]
    [InlineData("{ void F() { } } { void F() { } }")]
    [InlineData("if (true) { void F() { } } else { void F() { } }")]
    public void TopLevelLocalCollisionsUseTheWholeFile(string code)
    {
        var compilation = CodeRulesTestHelper.Compilation(code);
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        Assert.Equal(
            new[] { "local:file:" + CodeRulesTestHelper.SourcePath + "/F#1", "local:file:" + CodeRulesTestHelper.SourcePath + "/F#2" },
            source.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(source.MemberAnchor));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HashesUseUtf8TokensAndNormalizedLineEndings(string newline)
    {
        var code = "class C { string M() => @\"é😀" + newline + "x\"; }";
        var compilation = CodeRulesTestHelper.Compilation(code);
        var tree = compilation.SyntaxTrees.Single();
        var source = new CodeRulesSource(compilation, tree, CodeRulesTestHelper.SourcePath, default);
        var method = source.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var canonical = "string M ( ) => @\"é😀\nx\" ;";
        var hash = Encoding.UTF8.GetBytes(canonical).Aggregate(14695981039346656037UL, (value, next) => unchecked((value ^ next) * 1099511628211UL));
        Assert.Equal("node:" + hash.ToString("x16"), source.NodeAnchor(method.DescendantTokens()));
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CompileTimeMatchParseTest
{
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("// independent selection\n")]
    public void AdjacentMatchesSelectIndependently(string separator)
    {
        var compilation = Parse(
            "#match\n    #case true\n        var first = 1\n" + separator +
            "#match\n    #case true\n        var second = 2\n    #case _\n        var excluded = 3");

        AssertValid(compilation);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("first", SelectedFieldName(items[0]));
        Assert.Equal("second", SelectedFieldName(items[1]));
    }

    [Fact]
    public void AdjacentMatchesEachHaveTheirOwnFallback()
    {
        var compilation = Parse("#match\n    #case _\n        var first = 1\n#match\n    #case _\n        var second = 2");

        AssertValid(compilation);
        Assert.Equal(2, compilation.Kotonoha.GeneratedFunction!.Body!.Items.Count);
    }

    [Fact]
    public void BlankLinesAndCommentsInsideMatchPreserveArmOrder()
    {
        var compilation = Parse("""
            #match // selection
                #case false
                    var excluded = 1

                // the same match
                /* still the same match */
                #case true
                    var selected = 2

                #case _
                    var fallback = 3
            """);

        AssertValid(compilation);
        Assert.Equal("selected", SelectedFieldName(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)));
    }

    [Fact]
    public void NestedMatchesHaveIndependentBoundariesAndRoundTrip()
    {
        var compilation = Parse("""
            #match
                #case outerCondition
                    #match
                        #case innerCondition
                            ()
                        #case _
                            ()
                #case _
                    ()
            #match
                #case siblingCondition
                    ()
            """);
        AssertValid(compilation);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        var outer = Assert.IsType<CompileTimeMatchKoto>(items[0]);
        var inner = Assert.IsType<CompileTimeMatchKoto>(Assert.Single(outer.Arms[0].Body.Items));
        Assert.Equal(2, outer.Arms.Count);
        Assert.Equal(2, inner.Arms.Count);
        Assert.Single(Assert.IsType<CompileTimeMatchKoto>(items[1]).Arms);
        Assert.All(outer.ChildNodes, child => Assert.Same(outer, child.Parent));
        Assert.Same(outer.Arms[0].Body, Assert.Single(outer.Arms[0].Body.PendingDirectiveConditions).Scope);
        Assert.Equal(2, compilation.Kotonoha.RootKoto.PendingDirectiveConditions.Count);

        var written = outer.ToString();
        Assert.StartsWith("#match\n", written);
        var restored = Parse(written);
        AssertValid(restored);
        var restoredMatch = Assert.IsType<CompileTimeMatchKoto>(Assert.Single(restored.Kotonoha.GeneratedFunction!.Body!.Items));
        Assert.Equal(written, restoredMatch.ToString());
        Assert.Equal(0, outer.Span.Start);
        Assert.True(inner.Span.End <= outer.Span.End);
    }

    [Theory]
    [InlineData("#case true\n    ()")]
    [InlineData("func f()\n    #case true\n        ()")]
    [InlineData("struct S\n    #case true\n        var value: i32")]
    [InlineData("#match\n    #case true\n        #case true\n            ()")]
    [InlineData("#match\n    #case true\n        ()\n#case _\n    ()")]
    public void CaseOutsideDirectMatchBodyIsRejected(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.CompileTimeCaseOutsideMatch_Kd);

    [Theory]
    [InlineData("#match")]
    [InlineData("#match\n    // no arms")]
    [InlineData("#match\n    ()")]
    [InlineData("#match\n#match\n    #case _\n        ()")]
    public void MatchRequiresAtLeastOneIndentedArm(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.EmptyCompileTimeMatch_Kd);

    [Theory]
    [InlineData("var invalid = 1")]
    [InlineData("#if true")]
    [InlineData("#Tag")]
    [InlineData(";")]
    [InlineData("#match\n        #case _\n            ()")]
    public void DirectMatchItemsMustBeCaseArms(string invalidItem)
    {
        var compilation = Parse($"#match\n    {invalidItem}\n    #case true\n        ()\nvar following = 1");

        AssertDiagnostic(compilation, invalidItem == ";" ? DiagnosticCode.SemicolonNotAllowed_Kd : DiagnosticCode.InvalidCompileTimeMatchItem_Kd);
        Assert.Equal("following", Assert.IsType<FieldKoto>(compilation.Kotonoha.GeneratedFunction!.Body!.Items[^1]).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("#match value\n    #case _\n        ()")]
    [InlineData("#match:\n    #case _\n        ()")]
    [InlineData("#match\n    #case _ extra\n        ()")]
    public void UnexpectedHeaderTokensAreRejected(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.UnexpectedTrailingToken_Kd);

    [Fact]
    public void EachArmRequiresItsOwnIndentedBody()
        => AssertDiagnostic(Parse("#match\n    #case true\n    #case _\n        ()"), DiagnosticCode.EmptyExecutableBlock_Kd);

    [Fact]
    public void EarlyFalseIfExcludesExactlyOneMatch()
    {
        var compilation = Parse("""
            #if false
            #match
                #case unknownCondition
                    var incomplete =
                #case 1
                    var incomplete =
            #match
                #case _
                    var retained = 1
            """);

        AssertValid(compilation);
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
        Assert.Equal("retained", SelectedFieldName(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)));
    }

    [Fact]
    public void DeferredIfControlsTheEntireMatchAsOneItem()
    {
        var compilation = Parse("#if outerCondition\n#match\n    #case innerCondition\n        ()\n    #case _\n        ()\n#match\n    #case _\n        ()");
        AssertValid(compilation);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        var conditional = Assert.IsType<CompileTimeIfKoto>(items[0]);
        var match = Assert.IsType<CompileTimeMatchKoto>(conditional.Target);

        Assert.Equal(2, match.Arms.Count);
        Assert.IsType<CodeBlockKoto>(items[1]);
        Assert.Same(conditional, match.Parent);
        Assert.All(compilation.Kotonoha.RootKoto.PendingDirectiveConditions, obligation => Assert.Same(compilation.Kotonoha.RootKoto, obligation.Scope));
    }

    [Theory]
    [InlineData("#match", DiagnosticCode.EmptyCompileTimeMatch_Kd)]
    [InlineData("#match\n    ()", DiagnosticCode.InvalidCompileTimeMatchItem_Kd)]
    [InlineData("#match\n    #case true", DiagnosticCode.EmptyExecutableBlock_Kd)]
    [InlineData("#match\n    ;\n    #case true\n        ()", DiagnosticCode.SemicolonNotAllowed_Kd)]
    [InlineData("#match value\n    #case _\n        ()", DiagnosticCode.UnexpectedTrailingToken_Kd)]
    public void ExcludedMatchStillValidatesItsSourceStructure(string target, DiagnosticCode diagnostic)
    {
        var compilation = Parse($"#if false\n{target}\nvar following = 1");

        AssertDiagnostic(compilation, diagnostic);
        Assert.Equal("following", Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Fact]
    public void SelectedArmMayBeEmptiedByAnInnerDirective()
    {
        var compilation = Parse("func f()\n    #match\n        #case _\n            #if false\n                ()");

        AssertValid(compilation);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        Assert.Empty(Assert.IsType<CodeBlockKoto>(Assert.Single(function.Body!.Items)).Items);
    }

    [Fact]
    public void ContractConditionsKeepTheContractLookupContext()
    {
        var compilation = Parse("""
            contract C
                #match
                    #case true
                        var count: i32 has get
                    #case unknownCondition
                        var other: i32 has get
            """);

        AssertValid(compilation);
        var contract = Assert.Single(compilation.Kotonoha.RootKoto.NestedDeclarationContainers);
        Assert.Same(contract, Assert.Single(contract.PendingDirectiveConditions).Scope);
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
        Assert.IsType<PropertyKoto>(Assert.Single(Assert.IsType<CodeBlockKoto>(Assert.Single(contract.Members)).Items));
    }

    private static string SelectedFieldName(Koto item)
        => Assert.IsType<FieldKoto>(Assert.Single(Assert.IsType<CodeBlockKoto>(item).Items)).NameKoto.IdentifierName;

    private static void AssertValid(Compilation compilation)
        => Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());

    private static void AssertDiagnostic(Compilation compilation, DiagnosticCode code)
        => Assert.Contains(compilation.Kotonoha.DiagnosticCollection.GetArray(), diagnostic => diagnostic.Entry.Name == code.ToString());

    private static Compilation Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        return compilation;
    }
}

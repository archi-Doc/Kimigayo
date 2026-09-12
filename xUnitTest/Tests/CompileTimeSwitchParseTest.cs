// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CompileTimeSwitchParseTest
{
    [Theory]
    [InlineData("#switch")]
    [InlineData("# switch")]
    public void FirstTrueArmWinsAndRuntimeMatchRetainsItsSyntax(string header)
    {
        var compilation = Parse(header + """

                #case false
                    var excluded = 0
                #case true
                    func choose(flag: bool) -> i32 => match flag
                        true => 1
                        false => 2
                #case true
                    var later = 3
                #case _
                    var fallback = 4
            """);
        AssertValid(compilation);
        var selected = Assert.IsType<CodeBlockKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(selected.Items));
        Assert.IsType<MatchKoto>(function.ExpressionBody);
        Assert.Contains("match flag", function.ToString());
    }

    [Theory]
    [InlineData("#match\n    #case _\n        ()")]
    [InlineData("func f()\n    #match\n        #case _\n            ()")]
    [InlineData("struct S\n    #match\n        #case _\n            let value: i32")]
    public void RemovedDirectiveSpellingIsRejected(string source)
        => Assert.NotEmpty(Parse(source).Kotonoha.DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("let switch = 1")]
    [InlineData("switch true\n    #case _\n        ()")]
    public void SwitchIsReservedAndRequiresTheDirectivePrefix(string source)
        => Assert.NotEmpty(Parse(source).Kotonoha.DiagnosticCollection.GetArray());

    [Fact]
    public void KeywordPrefixDoesNotReserveLongerNames()
    {
        var compilation = Parse("let switchValue = 1\nlet value = switchValue");
        AssertValid(compilation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("// independent selection\n")]
    public void AdjacentSwitchesSelectIndependently(string separator)
    {
        var compilation = Parse(
            "#switch\n    #case true\n        var first = 1\n" + separator +
            "#switch\n    #case true\n        var second = 2\n    #case _\n        var excluded = 3");

        AssertValid(compilation);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("first", SelectedFieldName(items[0]));
        Assert.Equal("second", SelectedFieldName(items[1]));
    }

    [Fact]
    public void AdjacentSwitchesEachHaveTheirOwnFallback()
    {
        var compilation = Parse("#switch\n    #case _\n        var first = 1\n#switch\n    #case _\n        var second = 2");

        AssertValid(compilation);
        Assert.Equal(2, compilation.Kotonoha.GeneratedFunction!.Body!.Items.Count);
    }

    [Fact]
    public void BlankLinesAndCommentsInsideSwitchPreserveArmOrder()
    {
        var compilation = Parse("""
            #switch // selection
                #case false
                    var excluded = 1

                // the same switch
                /* still the same switch */
                #case true
                    var selected = 2

                #case _
                    var fallback = 3
            """);

        AssertValid(compilation);
        Assert.Equal("selected", SelectedFieldName(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)));
    }

    [Fact]
    public void InvalidNestedSwitchesPreserveBoundariesForRecovery()
    {
        var compilation = Parse("""
            #switch
                #case outerCondition
                    #switch
                        #case innerCondition
                            ()
                        #case _
                            ()
                #case _
                    ()
            #switch
                #case siblingCondition
                    ()
            """);
        Assert.Equal(3, compilation.Kotonoha.DiagnosticCollection.GetArray().Length);
        AssertDiagnostic(compilation, DiagnosticCode.UnknownCompileTimeName_Kd);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        var outer = Assert.IsType<CompileTimeSwitchKoto>(items[0]);
        var inner = Assert.IsType<CompileTimeSwitchKoto>(Assert.Single(outer.Arms[0].Body.Items));
        Assert.Equal(2, outer.Arms.Count);
        Assert.Equal(2, inner.Arms.Count);
        Assert.Single(Assert.IsType<CompileTimeSwitchKoto>(items[1]).Arms);
        Assert.All(outer.ChildNodes, child => Assert.Same(outer, child.Parent));

        var written = outer.ToString();
        Assert.StartsWith("#switch\n", written);
        var restored = Parse(written);
        Assert.Equal(2, restored.Kotonoha.DiagnosticCollection.GetArray().Length);
        AssertDiagnostic(restored, DiagnosticCode.UnknownCompileTimeName_Kd);
        var restoredSwitch = Assert.IsType<CompileTimeSwitchKoto>(Assert.Single(restored.Kotonoha.GeneratedFunction!.Body!.Items));
        Assert.Equal(written, restoredSwitch.ToString());
        Assert.Equal(0, outer.Span.Start);
        Assert.True(inner.Span.End <= outer.Span.End);
    }

    [Theory]
    [InlineData("#case true\n    ()")]
    [InlineData("func f()\n    #case true\n        ()")]
    [InlineData("struct S\n    #case true\n        var value: i32")]
    [InlineData("#switch\n    #case true\n        #case true\n            ()")]
    [InlineData("#switch\n    #case true\n        ()\n#case _\n    ()")]
    public void CaseOutsideDirectSwitchBodyIsRejected(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);

    [Theory]
    [InlineData("#switch")]
    [InlineData("#switch\n    // no arms")]
    [InlineData("#switch\n    ()")]
    [InlineData("#switch\n#switch\n    #case _\n        ()")]
    public void SwitchRequiresAtLeastOneIndentedArm(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.EmptyCompileTimeSwitch_Kd);

    [Theory]
    [InlineData("var invalid = 1")]
    [InlineData("#if true")]
    [InlineData("#Tag")]
    [InlineData(";")]
    [InlineData("#switch\n        #case _\n            ()")]
    public void DirectSwitchItemsMustBeCaseArms(string invalidItem)
    {
        var compilation = Parse($"#switch\n    {invalidItem}\n    #case true\n        ()\nvar following = 1");

        AssertDiagnostic(compilation, invalidItem == ";" ? DiagnosticCode.SemicolonNotAllowed_Kd : DiagnosticCode.InvalidCompileTimeSwitchItem_Kd);
        Assert.Equal("following", Assert.IsType<FieldKoto>(compilation.Kotonoha.GeneratedFunction!.Body!.Items[^1]).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("#switch value\n    #case _\n        ()")]
    [InlineData("#switch:\n    #case _\n        ()")]
    [InlineData("#switch\n    #case _ extra\n        ()")]
    public void UnexpectedHeaderTokensAreRejected(string source)
        => AssertDiagnostic(Parse(source), DiagnosticCode.UnexpectedTrailingToken_Kd);

    [Fact]
    public void EachArmRequiresItsOwnIndentedBody()
        => AssertDiagnostic(Parse("#switch\n    #case true\n    #case _\n        ()"), DiagnosticCode.EmptyExecutableBlock_Kd);

    [Fact]
    public void EarlyFalseIfExcludesExactlyOneSwitch()
    {
        var compilation = Parse("""
            #if false
            #switch
                #case unknownCondition
                    var incomplete =
                #case 1
                    var incomplete =
            #switch
                #case _
                    var retained = 1
            """);

        AssertValid(compilation);
        Assert.Equal("retained", SelectedFieldName(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)));
    }

    [Fact]
    public void UnknownIfRejectsTheEntireSwitchAsOneItem()
    {
        var compilation = Parse("#if outerCondition\n#switch\n    #case innerCondition\n        ()\n    #case _\n        ()\n#switch\n    #case _\n        ()");
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        Assert.IsType<CodeBlockKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
    }

    [Theory]
    [InlineData("#switch", DiagnosticCode.EmptyCompileTimeSwitch_Kd)]
    [InlineData("#switch\n    ()", DiagnosticCode.InvalidCompileTimeSwitchItem_Kd)]
    [InlineData("#switch\n    #case true", DiagnosticCode.EmptyExecutableBlock_Kd)]
    [InlineData("#switch\n    ;\n    #case true\n        ()", DiagnosticCode.SemicolonNotAllowed_Kd)]
    [InlineData("#switch value\n    #case _\n        ()", DiagnosticCode.UnexpectedTrailingToken_Kd)]
    public void ExcludedSwitchStillValidatesItsSourceStructure(string target, DiagnosticCode diagnostic)
    {
        var compilation = Parse($"#if false\n{target}\nvar following = 1");

        AssertDiagnostic(compilation, diagnostic);
        Assert.Equal("following", Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Fact]
    public void SelectedArmMayBeEmptiedByAnInnerDirective()
    {
        var compilation = Parse("func f()\n    #switch\n        #case _\n            #if false\n                ()");

        AssertValid(compilation);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        Assert.Empty(Assert.IsType<CodeBlockKoto>(Assert.Single(function.Body!.Items)).Items);
    }

    [Fact]
    public void ContractConditionsRejectUnknownEnvironmentNames()
    {
        var compilation = Parse("""
            contract C
                #switch
                    #case true
                        property count: i32 has get
                    #case unknownCondition
                        property other: i32 has get
            """);

        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        var contract = Assert.Single(compilation.Kotonoha.RootKoto.NestedDeclarationContainers);
        var group = Assert.IsType<CompileTimeSwitchKoto>(Assert.Single(contract.Members));
        Assert.All(group.Arms, arm => Assert.IsType<PropertyKoto>(Assert.Single(arm.Body.Items)));
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

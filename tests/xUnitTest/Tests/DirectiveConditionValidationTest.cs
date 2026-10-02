// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class DirectiveConditionValidationTest
{
    [Theory]
    [InlineData("T is i32")]
    [InlineData("T is not i32")]
    [InlineData("s is ref")]
    [InlineData("T is Copy")]
    [InlineData("T is External.Contracts.Comparable")]
    [InlineData("(s is ref) and (T is i32)")]
    public void TypeConditionsAreRejectedInEveryReachedDirective(string requirement)
    {
        foreach (var condition in new[] { requirement, $"false and ({requirement})", $"true or ({requirement})" })
        {
            foreach (var directive in new[]
            {
                $"#if {condition}\n    ()",
                $"#switch\n    #case {condition}\n        ()\n    #case _\n        ()",
                $"#switch\n    #case true\n        ()\n    #case {condition}\n        ()",
            })
            {
                // Check both top-level and genuine generic-body contexts, including later arms.
                foreach (var source in new[] { directive, "func f<s/T>()\n    " + directive.Replace("\n", "\n    ") })
                {
                    var compilation = Parse(source);
                    Assert.Contains(
                        TestDiagnostics.Of(compilation),
                        x => x.Code == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
                }
            }
        }
    }

    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "windows")]
    [InlineData("x86_64-unknown-linux-gnu", "linux")]
    public void EnvironmentSelectionInsideGenericBodyPreservesConstraints(string target, string selected)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(target));
        var source = """
            func f<T>() -> string
                T is Copy
                #if pointerWidth == 64
                    #switch
                        #case windows
                            return "windows"
                        #case linux
                            return "linux"
                        #case _
                            return "other"
            """;
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);

        Assert.Empty(TestDiagnostics.Of(compilation));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var written = function.ToString();
        Assert.Contains("T is Copy", written);
        Assert.Contains($"return \"{selected}\"", written);
        Assert.DoesNotContain("#if", written);
        Assert.DoesNotContain("#switch", written);
    }

    [Theory]
    [InlineData("false and 1")]
    [InlineData("1 and false")]
    [InlineData("true or 1")]
    [InlineData("1 or true")]
    [InlineData("not (false and 1)")]
    [InlineData("not (true or 1)")]
    [InlineData("false and (true or 1)")]
    [InlineData("true or (false and 1)")]
    [InlineData("missing and 1")]
    [InlineData("missing or 1")]
    [InlineData("(false and 1) == false")]
    public void KnownErrorsOverrideShortCircuitTruth(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar excluded = 1");

        Assert.Contains(
            TestDiagnostics.Of(compilation),
            x => x.Code == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("missing == true")]
    [InlineData("true != missing")]
    [InlineData("false and missing")]
    [InlineData("missing and false")]
    [InlineData("true or missing")]
    [InlineData("missing or true")]
    [InlineData("not (false and missing)")]
    [InlineData("not (true or missing)")]
    [InlineData("(false and missing) == false")]
    public void UnknownNamesAreErrorsEvenWhenTruthIsKnown(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar value = 1");
        var diagnostic = Assert.Single(TestDiagnostics.Of(compilation));
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
        Assert.Empty(compilation.AnalyzeControlFlow().PendingBinding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildConfigurationDoesNotHideUnknownNames(bool debug)
    {
        var compilation = Parse("#if debug and missing\nvar conditional = 1", debug);
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(compilation)).Code);
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("false and missing")]
    [InlineData("true or missing")]
    public void SelectedCaseGroupValidatesLaterArmConditions(string laterCondition)
    {
        var compilation = Parse($"""
            func select()
                #switch
                    #case true
                        return
                    #case {laterCondition}
                        return
                    #case _
                        return
            """);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = function.Body!;
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(compilation)).Code);
        Assert.IsType<CompileTimeSwitchKoto>(Assert.Single(body.Items));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("false and 1")]
    public void LaterCaseOperandErrorIsDiagnosedAfterSelection(string condition)
    {
        var compilation = Parse($"#switch\n    #case true\n        ()\n    #case {condition}\n        ()");

        Assert.Contains(
            TestDiagnostics.Of(compilation),
            x => x.Code == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
    }

    [Fact]
    public void ExcludingAStackedPrefixDoesNotHideAnEarlierError()
    {
        var compilation = Parse("#if missing\n#if false\nvar excluded = 1");
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(compilation)).Code);
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Fact]
    public void NestedScopesDiagnoseUnknownNamesImmediately()
    {
        var compilation = Parse("""
            struct Container<T>
                #if false and outerMissing
                var excluded: i32
                #if true
                    #if false and innerMissing
                    var excluded: i32
                func nested<U>()
                    #if false and functionMissing
                    var excluded = 1
                    ()
            """);
        var diagnostics = TestDiagnostics.Of(compilation);
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), x.Code));
        Assert.Contains(diagnostics, x => x.Message.Contains("outerMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Message.Contains("innerMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Message.Contains("functionMissing", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedTargetsValidateNestedConditionsAndGrammar()
    {
        // Excluded syntax receives the source checks of selected syntax (SPEC 19.5).
        var compilation = Parse("#if false\n    #if nestedMissing\n    var incomplete =");

        var diagnostics = TestDiagnostics.Of(compilation);
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, x => x.Code == nameof(DiagnosticCode.UnknownCompileTimeName_Kd) && x.Message.Contains("nestedMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Code == nameof(DiagnosticCode.MissingSyntax_Kd));
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("#if missing")]
    [InlineData("#if false and missing")]
    [InlineData("#if true or missing")]
    [InlineData("#if T is i32")]
    public void FalseStackedPrefixStillValidatesInnerCondition(string inner)
    {
        var compilation = Parse($"#if false\n{inner}\nvar excluded = 1\nvar retained = 1");
        var diagnostic = Assert.Single(TestDiagnostics.Of(compilation));
        Assert.True(diagnostic.Code is nameof(DiagnosticCode.UnknownCompileTimeName_Kd) or nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd), diagnostic.Code);
        Assert.Equal("retained", Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("#if missing\nvar ignored = 1")]
    [InlineData("#if false\n    #if missing\n    var ignored = 1")]
    [InlineData("#if false\n#if missing\nvar ignored = 1")]
    public void UnselectedSwitchArmsValidateNestedConditions(string nested)
    {
        var source = "#switch\n    #case true\n        ()\n    #case _\n        " + nested.Replace("\n", "\n        ");
        var compilation = Parse(source);
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(compilation)).Code);
    }

    [Theory]
    [InlineData("firstMissing and secondMissing")]
    [InlineData("firstMissing or secondMissing")]
    [InlineData("firstMissing == secondMissing")]
    [InlineData("firstMissing != secondMissing")]
    public void UnknownNamesOnBothSidesAreDiagnosedAtTheirSourceSpans(string condition)
    {
        var source = $"#if {condition}\nvar ignored = 1";
        var diagnostics = TestDiagnostics.Of(Parse(source));
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), x.Code));
        Assert.Contains(diagnostics, x => x.Span.Start == source.IndexOf("firstMissing", StringComparison.Ordinal) && x.Span.Length == "firstMissing".Length);
        Assert.Contains(diagnostics, x => x.Span.Start == source.IndexOf("secondMissing", StringComparison.Ordinal) && x.Span.Length == "secondMissing".Length);
    }

    [Theory]
    [InlineData("var missing = true\n#if missing\nvar ignored = 1")]
    [InlineData("func f<T>()\n    #if T\n        ()")]
    public void SourceDeclarationsCannotSupplyConditionValues(string source)
    {
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(Parse(source))).Code);
    }

    [Fact]
    public void LabeledBlockReportsUnknownNameWithoutChangingControlFlow()
    {
        var compilation = Parse("label work: do\n    #if false and missing\n    var excluded = 1\n    exit to work");
        var labeled = Assert.IsType<LabeledKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = Assert.IsType<DoKoto>(labeled.Target).Body;
        var analysis = compilation.AnalyzeControlFlow();

        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(compilation)).Code);
        Assert.Empty(analysis.Issues);
        Assert.Empty(analysis.PendingBinding);
        Assert.Single(body.Items);
    }

    [Fact]
    public void AppendingSourcesPreservesEachDiagnosticsOriginalDocument()
    {
        var compilation = Compilation.CreateForTest();
        var context = compilation.Kotonoha.CreateCodeContext();
        var first = new SourceDocument("first.kimi", "#if false and firstMissing\nvar excluded = 1");
        var second = new SourceDocument("second.kimi", "#if true or secondMissing\nvar value = 1");
        context.Parse(compilation.Kotonoha.RootKoto, first);
        context.Parse(compilation.Kotonoha.RootKoto, second);

        var diagnostics = TestDiagnostics.Of(compilation);
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, x => x.Path == first.Path && x.Message.Contains("firstMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Path == second.Path && x.Message.Contains("secondMissing", StringComparison.Ordinal));
    }

    [Fact]
    public void SerializationRebuildsImmediateDiagnosticsWithoutDuplication()
    {
        var compilation = Parse("#if false and missing\nvar excluded = 1");
        var bytes = TinyhandSerializer.Serialize(compilation.Kotonoha);
        var target = Compilation.CreateForTest();
        Assert.True(target.Prepare("x86_64-pc-windows-msvc"));
        var restored = new Kotonoha(target);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        restored!.OnDeserialized(target);
        restored.OnDeserialized(target);
        var diagnostic = Assert.Single(TestDiagnostics.Of(restored));
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
        Assert.Null(restored.GeneratedFunction);
    }

    [Theory]
    [InlineData("false and true")]
    [InlineData("true or false")]
    [InlineData("windows and pointerWidth == 64")]
    public void FullyValidatedConditionsDoNotRequireBinding(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar value = 1");

        Assert.Empty(TestDiagnostics.Of(compilation));
        Assert.Empty(compilation.AnalyzeControlFlow().PendingBinding);
    }

    private static Compilation Parse(string source, bool debug = false)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.KimiOptions.Debug = debug;
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        return compilation;
    }
}

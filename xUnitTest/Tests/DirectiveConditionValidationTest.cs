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
                $"#match\n    #case {condition}\n        ()\n    #case _\n        ()",
                $"#match\n    #case true\n        ()\n    #case {condition}\n        ()",
            })
            {
                // Check both top-level and genuine generic-body contexts, including later arms.
                foreach (var source in new[] { directive, "func f<s/T>()\n    " + directive.Replace("\n", "\n    ") })
                {
                    var compilation = Parse(source);
                    Assert.Contains(
                        compilation.Kotonoha.DiagnosticCollection.GetArray(),
                        x => x.Entry.Name == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
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
                    #match
                        #case windows
                            return "windows"
                        #case linux
                            return "linux"
                        #case _
                            return "other"
            """;
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var written = function.ToString();
        Assert.Contains("T is Copy", written);
        Assert.Contains($"return \"{selected}\"", written);
        Assert.DoesNotContain("#if", written);
        Assert.DoesNotContain("#match", written);
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
            compilation.Kotonoha.DiagnosticCollection.GetArray(),
            x => x.Entry.Name == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
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
        var diagnostic = Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), diagnostic.Entry.Name);
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
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
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
                #match
                    #case true
                        return
                    #case {laterCondition}
                        return
                    #case _
                        return
            """);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = function.Body!;
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        Assert.IsType<CompileTimeMatchKoto>(Assert.Single(body.Items));
    }

    [Fact]
    public void LaterCaseOperandErrorIsDiagnosedAfterSelection()
    {
        var compilation = Parse("#match\n    #case true\n        ()\n    #case false and 1\n        ()");

        Assert.Contains(
            compilation.Kotonoha.DiagnosticCollection.GetArray(),
            x => x.Entry.Name == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
    }

    [Fact]
    public void ExcludingAStackedPrefixDoesNotHideAnEarlierError()
    {
        var compilation = Parse("#if missing\n#if false\nvar incomplete =");
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Fact]
    public void NestedScopesDiagnoseUnknownNamesImmediately()
    {
        var compilation = Parse("""
            struct Container<T>
                #if false and outerMissing
                var incomplete:
                #if true
                    #if false and innerMissing
                    var incomplete:
                func nested<U>()
                    #if false and functionMissing
                    var incomplete =
                    ()
            """);
        var diagnostics = compilation.Kotonoha.DiagnosticCollection.GetArray();
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), x.Entry.Name));
        Assert.Contains(diagnostics, x => x.Message.Contains("outerMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Message.Contains("innerMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => x.Message.Contains("functionMissing", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedTargetDoesNotValidateNestedConditions()
    {
        var compilation = Parse("#if false\n    #if nestedMissing\n    var incomplete =");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("#if missing")]
    [InlineData("#if false and missing")]
    [InlineData("#if true or missing")]
    [InlineData("#if T is i32")]
    public void FalseStackedPrefixSkipsInnerCondition(string inner)
    {
        var compilation = Parse($"#if false\n{inner}\nvar incomplete =\nvar retained = 1");
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal("retained", Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("#if missing\nvar ignored = 1", true)]
    [InlineData("#if false\n    #if missing\n    var incomplete =", false)]
    [InlineData("#if false\n#if missing\nvar incomplete =", false)]
    public void UnselectedMatchArmsValidateReachedNestedConditions(string nested, bool error)
    {
        var source = "#match\n    #case true\n        ()\n    #case _\n        " + nested.Replace("\n", "\n        ");
        var compilation = Parse(source);
        var diagnostics = compilation.Kotonoha.DiagnosticCollection.GetArray();
        if (error)
        {
            Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(diagnostics).Entry.Name);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("firstMissing and secondMissing")]
    [InlineData("firstMissing or secondMissing")]
    [InlineData("firstMissing == secondMissing")]
    [InlineData("firstMissing != secondMissing")]
    public void UnknownNamesOnBothSidesAreDiagnosedAtTheirSourceSpans(string condition)
    {
        var source = $"#if {condition}\nvar ignored = 1";
        var diagnostics = Parse(source).Kotonoha.DiagnosticCollection.GetArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), x.Entry.Name));
        Assert.Contains(diagnostics, x => x.Span.Start == source.IndexOf("firstMissing", StringComparison.Ordinal) && x.Span.Length == "firstMissing".Length);
        Assert.Contains(diagnostics, x => x.Span.Start == source.IndexOf("secondMissing", StringComparison.Ordinal) && x.Span.Length == "secondMissing".Length);
    }

    [Theory]
    [InlineData("var missing = true\n#if missing\nvar ignored = 1")]
    [InlineData("func f<T>()\n    #if T\n        ()")]
    public void SourceDeclarationsCannotSupplyConditionValues(string source)
    {
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(Parse(source).Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
    }

    [Fact]
    public void LabeledBlockReportsUnknownNameWithoutChangingControlFlow()
    {
        var compilation = Parse("work: do\n    #if false and missing\n    var incomplete =\n    exit to work");
        var labeled = Assert.IsType<LabeledKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = Assert.IsType<DoKoto>(labeled.Target).Body;
        var analysis = compilation.AnalyzeControlFlow();

        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        Assert.Empty(analysis.Issues);
        Assert.Empty(analysis.PendingBinding);
        Assert.Single(body.Items);
    }

    [Fact]
    public void AppendingSourcesPreservesEachDiagnosticsOriginalDocument()
    {
        var compilation = Compilation.CreateForTest();
        var context = compilation.Kotonoha.CreateCodeContext();
        var first = new SourceDocument("first.kimi", "#if false and firstMissing\nvar incomplete =");
        var second = new SourceDocument("second.kimi", "#if true or secondMissing\nvar value = 1");
        context.Parse(compilation.Kotonoha.RootKoto, first);
        context.Parse(compilation.Kotonoha.RootKoto, second);

        var diagnostics = compilation.Kotonoha.DiagnosticCollection.GetArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, x => ReferenceEquals(first, x.SourceDocument) && x.Message.Contains("firstMissing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, x => ReferenceEquals(second, x.SourceDocument) && x.Message.Contains("secondMissing", StringComparison.Ordinal));
    }

    [Fact]
    public void SerializationRebuildsImmediateDiagnosticsWithoutDuplication()
    {
        var compilation = Parse("#if false and missing\nvar incomplete =");
        var bytes = TinyhandSerializer.Serialize(compilation.Kotonoha);
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        restored!.OnDeserialized(compilation);
        restored.OnDeserialized(compilation);
        var diagnostic = Assert.Single(restored.DiagnosticCollection.GetArray());
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), diagnostic.Entry.Name);
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

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
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

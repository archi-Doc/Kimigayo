// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DictionaryLiteralTest
{
    [Theory]
    [InlineData("let entries = [1: true, 2: false]", "i32", "bool")]
    [InlineData("let key: u8 = 2\nlet value: i64 = 3\nlet entries = [1: 2, key: value]", "u8", "i64")]
    [InlineData("let key: u8 = 2\nlet value: i64 = 3\nlet entries = [key: value, 1: 2]", "u8", "i64")]
    [InlineData("let entries = [\"first\": 1.0, \"second\": 2.0]", "string", "f64")]
    public void IndependentLiteralsInferKeyAndValueTypes(string source, string key, string value)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var declaration = c.Kotonoha.GeneratedFunction!.Body!.ChildNodes.OfType<VariableKoto>().Last();
        var type = declaration.BoundType!;
        Assert.Equal(BoundTypeKind.Dictionary, type.Kind);
        Assert.Same(key == "string" ? BoundType.String : BoundType.Primitives[key], type.Components[0]);
        Assert.Same(BoundType.Primitives[value], type.Components[1]);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let key: u8 = 1\nlet entries = [key: 1, 256: 2]")]
    [InlineData("let entries = [1: true, 2: 3]")]
    [InlineData("let entries = [1: true, \"two\": false]")]
    [InlineData("let entries = [:]")]
    [InlineData("struct Key\n    public init() => ()\nlet entries = [Key.init(): 1]")]
    [InlineData("func f<K>(key: K)\n    let entries = [key: 1]")]
    public void InferenceRequiresFittingAndEqualityEvidence(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, static x => x.Code is nameof(DiagnosticCode.UnsupportedBinding_Kd) or nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let key: i64 = 1\nlet other: u8 = 2\nlet entries = [key: true, other: false]", "other", "u8", "i64")]
    [InlineData("let entries = [1: true, 2: \"wrong\"]", "wrong", "string", "bool")]
    [InlineData("let entries: Dictionary<i32, bool> = [1: \"wrong\"]", "wrong", "string", "bool")]
    public void ConflictingEvidenceExplainsActualAndExpectedTypes(string source, string text, string actual, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Contains(actual, error.Label, StringComparison.Ordinal);
        Assert.Contains(expected, error.Label, StringComparison.Ordinal);
        Assert.False(c.Binding.Result.IsComplete);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(error, Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("let entries = [1: true, (+1): false]")]
    [InlineData("let entries = [1: [1: true], 1: [2: false]]")]
    public void InferredKeysKeepMandatoryDuplicateChecking(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.DuplicateDictionaryKey_Kd), error.Code);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("let entries = [1: [1: true], 2: [2: false]]")]
    [InlineData("func f<K>(key: K)\n    K is Equatable\n    let entries = [key: 1]")]
    [InlineData("func f()\n    let entries: Dictionary<i32, bool> = [1: $abort(\"stop\"), 2: true]")]
    public void NestedAndGenericEvidenceAndNeverValuesBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDictionaryInferenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let key: u8 = 2\nlet entries = [1: [1: true], key: [2: false]]");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Theory]
    [InlineData("let entries = [1: true, 2: \"wrong\"]", "TypeMismatch_Kd")]
    [InlineData("let entries = [1: true, +1: false]", "DuplicateDictionaryKey_Kd")]
    public void InferenceDiagnosticsReachBothOutputAdapters(string source, string code)
    {
        var path = Path.GetFullPath("DictionaryLiteral.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Code, console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Label ?? record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal(record.Display!.Range, sent.Range);
        Assert.Equal(record.Code, sent.Code);
        Assert.Contains(record.Label ?? record.Message, sent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndependentErrorsRemainVisibleWithInvalidLiteralChildren()
    {
        var c = MinimalEmissionTest.Analyze("let entries = [missing: true, 2: false]\nlet wrong: i32 = true");
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Contains(errors, static x => x.Text == "missing");
        Assert.Contains(errors, static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd) && x.Text == "true");
    }

    [Theory]
    [InlineData("i32", "1", "(+1)")]
    [InlineData("i32", "1_000", "1000")]
    [InlineData("i32", "0", "-0")]
    [InlineData("i32", "-2147483648", "(-2147483648)")]
    [InlineData("u128", "340282366920938463463374607431768211455", "340282366920938463463374607431768211455")]
    [InlineData("bool", "true", "(true)")]
    [InlineData("char", "'a'", "'\\u(61)'")]
    [InlineData("string", "\"a\"", "\"\\u(61)\"")]
    [InlineData("()", "()", "(())")]
    public void MandatoryDuplicateKeysFailBinding(string type, string first, string later)
    {
        var source = "let entries: Dictionary<" + type + ", i32> = [" + first + ": 1, " + later + ": 2]";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains("DuplicateDictionaryKey_Kd", MinimalEmissionTest.Describe(c, null));
        var duplicate = Assert.Single(c.Binding.Issues, issue => issue.Code == DiagnosticCode.DuplicateDictionaryKey_Kd);
        Assert.Equal(source.LastIndexOf(later, StringComparison.Ordinal) + (type == "string" ? 1 : 0), duplicate.Node.Span.Start);
    }

    [Fact]
    public void EligibleKeysAreComparedAcrossRuntimeKeysAndInUnreachableCode()
    {
        var c = MinimalEmissionTest.Analyze("func unused(key: i32)\n    if false\n        let entries: Dictionary<i32, i32> = [1: 1, key: 2, (1): 3, +1: 4]");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains("DuplicateDictionaryKey_Kd", MinimalEmissionTest.Describe(c, null));
        Assert.Equal(2, c.Binding.Issues.Count(issue => issue.Code == DiagnosticCode.DuplicateDictionaryKey_Kd));
    }

    [Theory]
    [InlineData("i32", "1", "2")]
    [InlineData("i32", "-1", "1")]
    [InlineData("i32", "1", "1 + 0")]
    [InlineData("i32", "1", "1@i32")]
    [InlineData("i32", "1", "+(1)")]
    [InlineData("i32", "-1", "-(1)")]
    [InlineData("f64", "1.0", "1.0")]
    [InlineData("(i32, i32)", "(1, 2)", "(1, 2)")]
    public void StaticCheckingDoesNotExpandIntoOtherExpressions(string type, string first, string later)
    {
        var c = MinimalEmissionTest.Analyze("let entries: Dictionary<" + type + ", i32> = [" + first + ": 1, " + later + ": 2]");
        Assert.False(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void NamedKeysAndNestedLiteralsDoNotPolluteStaticChecking()
    {
        var c = MinimalEmissionTest.Analyze("let key = 1\nlet entries: Dictionary<i32, Dictionary<i32, i32>> = [key: [1: 2], key: [1: 3], 1: [1: 4]]");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}

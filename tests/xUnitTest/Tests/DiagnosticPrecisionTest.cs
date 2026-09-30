// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DiagnosticPrecisionTest
{
    public static TheoryData<string> MutationNames => [.. DiagnosticCorpus.Mutations.Select(static x => x.Name)];

    [Fact]
    public void IndependentErrorsRemainVisibleAfterAnUnknownIterationSubject()
    {
        const string source = """
            public func main()
                var sum: i32 = 0
                for value in missing
                    sum = sum + value
                let wrong: i32 = true
                absent()
            """;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Issues.Count == 3, string.Join("\n", c.Binding.Issues));
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == Kimi.DiagnosticCode.UnresolvedBinding_Kd));
        Assert.Single(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // DIAGNOSTICS.md rule 3: an implementation limit is Unsupported where it is met; a check that rests on a failed Type
    // (expected Type, candidate signature, generic Name) is derived and published only through that failure.
    [Theory]
    [InlineData("func set(a: uniq/[3 of i32])\n    a[0] = 5\npublic func main() => ()", "UnsupportedBinding_Kd", "a[0] = 5")]
    [InlineData("struct S\n    public let v: [2 of i32]\n    public init() => self.v = [1, 2]\nfunc set(s: uniq/S)\n    s.v[0] = 5\npublic func main() => ()", "InvalidAssignment_Kd", "s.v[0]")]
    [InlineData("func f(w: Weak<i32>) -> i32 => 0\npublic func main() => ()", "UnsupportedBinding_Kd", "Weak")]
    [InlineData("public func main()\n    let r = Kimi.Intrinsics.makeRc(1)", "UnsupportedBinding_Kd", "Kimi.Intrinsics.makeRc")]
    [InlineData("public func main()\n    let r = Kimi.Intrinsics.nothing(1)", "UnresolvedBinding_Kd", "Kimi.Intrinsics.nothing")]
    [InlineData("func f(w: Missing<i32>) -> i32 => 0\npublic func main() => ()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    let w: Option<Missing> = .None", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    var w: Missing = .None\n    w = .Some(1)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing => .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing\n    return .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    public void LimitsAndDependentChecksPublishOneSpecificError(string source, string code, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
    }

    // SPEC 3.4.1: G35's dynamic Array case now executes; keep its original source as an acceptance regression.
    [Fact]
    public void DynamicArrayWriteNoLongerReportsAnImplementationLimit()
    {
        var c = MinimalEmissionTest.Analyze("func set(a: uniq/Array<i32>)\n    a[0] += 5\npublic func main() => ()");
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Emission.Validate(out var error), error);
    }

    // SPEC 23.3.6.2: a mismatch names both Types, a write names its target, each at the smallest syntax that shows it.
    [Theory]
    [InlineData("func f() -> i32\n    return true\npublic func main() => ()", "true", "expected i32, found bool")]
    [InlineData("public func main()\n    let wrong: i32 = true", "true", "expected i32, found bool")]
    [InlineData("public func main()\n    let sum = 1 + \"a\"", "1", "expected string, found integer literal")]
    [InlineData("public func main()\n    if 1 and true\n        ()", "1", "expected bool, found integer literal")]
    [InlineData("public func main()\n    let counter = 1\n    counter += 1", "counter", "counter cannot be written")]
    public void TargetChecksShowTheirLocationAndFacts(string source, string text, string label)
    {
        var error = Assert.Single(PublishedErrors(MinimalEmissionTest.Analyze(source)));
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
        Assert.Equal(error.Code == nameof(Kimi.DiagnosticCode.InvalidAssignment_Kd), error.Advice == "Declare the binding with var to assign it again");
    }

    // SPEC 23.3.6.2: a failed selection counts and relates the candidates it considered.
    [Fact]
    public void AFailedSelectionRelatesItsCandidates()
    {
        var c = MinimalEmissionTest.Analyze("func pick(value: i32) -> i32 => value\nfunc pick(value: string) -> i32 => 0\npublic func main()\n    let x = pick(true)");
        Assert.Single(PublishedErrors(c));
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
        Assert.Equal(nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd), record.Code);
        Assert.Equal("none of 2 candidates applies", record.Label);
        Assert.Equal(["candidate", "candidate"], record.Related!.Select(static x => x.Role));
        Assert.Equal([0, 1], record.Related!.Select(static x => x.Range!.Value.Start.Line));
    }

    // A recovered chained comparison is not type-checked, a control-flow check that repeats Binding's is derived, and a
    // structural control-flow check stays independent of a Binding failure at the same place.
    [Theory]
    [InlineData("public func main()\n    let ok = 1 < 2 < 3\n    let wrong: i32 = true\n    missing()", "ChainedComparison_Kd,TypeMismatch_Kd,UnresolvedBinding_Kd")]
    [InlineData("func f() -> i32\n    let wrong: i32 = true\npublic func main() => ()", "FunctionFallthrough_Kd,TypeMismatch_Kd")]
    [InlineData("public func main() -> Missing\n    return ()", "UnresolvedBinding_Kd")]
    public void EachProblemPublishesOneErrorAcrossPhases(string source, string codes)
        => Assert.Equal(codes.Split(','), PublishedErrors(MinimalEmissionTest.Analyze(source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    // DIAGNOSTICS.md §9.2: the published Errors of a mutation are exactly its expected codes. Every intended problem is
    // reported, including independent ones, nothing depends on another, and no fallback or unexplained derived fact remains.
    [Theory]
    [MemberData(nameof(MutationNames))]
    public void MilestoneFaultsHaveSpecificDiagnosticsWithoutDependentCascades(string name)
    {
        var mutation = DiagnosticCorpus.Mutation(name);
        var c = MinimalEmissionTest.Analyze(DiagnosticCorpus.Apply(mutation));
        var errors = PublishedErrors(c);
        var detail = string.Join("\n", errors.Select(static x => x.ToString()));
        Assert.True(errors.Length != 0, detail);
        Assert.True(errors.All(x => mutation.Expected.Contains(x.Code)), detail);
        Assert.True(mutation.Expected.All(code => errors.Any(x => x.Code == code)), detail);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 23.3.6.4: syntax that failed to parse, including a token the lexer rejected, is the prerequisite of every check of
    // its recovered form, so its syntax Error alone explains it: no unexplained derived record, no repeated token report and no
    // ownership record for a body that the syntax Error left missing.
    [Theory]
    [InlineData("func f() ->\n    return 1\nlet a = f()", "MissingReturnType_Kd")]
    [InlineData("let a = 1\nlet v = a + @", "UnmatchedToken_Kd")]
    [InlineData("let v: i32 = 0x", "InvalidNumericLiteral_Kd")]
    [InlineData("let a = 1\nlet v = a.\nlet w = 2", "IdentifierExpected_Kd")]
    [InlineData("func f() -> i32\n\nfunc g()\n\nlet y = 1", "EmptyExecutableBlock_Kd,EmptyExecutableBlock_Kd")]
    [InlineData("func f() ->\nlet y = 1", "MissingReturnType_Kd")]
    public void RecoveredSyntaxExplainsItsDependents(string source, string codes)
        => Assert.Equal(codes.Split(','), PublishedErrors(MinimalEmissionTest.Analyze(source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    // SPEC 23.3.6.2, 23.3.6.3: a name missing at the end of a line is an insertion point after the dot, not the next line.
    [Fact]
    public void AMissingMemberNameIsAnInsertionPointAfterTheDot()
    {
        const string Source = "let a = 1\nlet v = a.\nlet w = 2";
        var error = Assert.Single(PublishedErrors(MinimalEmissionTest.Analyze(Source)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.IdentifierExpected_Kd), error.Code);
        Assert.Equal(Source.IndexOf("a.\n", StringComparison.Ordinal) + 2, error.Span.Start);
        Assert.Equal(0, error.Span.Length);
    }

    // SPEC 23.3.6.6: the synthesized writes of one interpolation share its span; a failed write is a problem at the value it
    // writes, so two failures have a defined order instead of faulting the check.
    [Fact]
    public void FailedInterpolationWritesAreLocatedAtTheirValues()
    {
        var errors = PublishedErrors(MinimalEmissionTest.Analyze("let a: Option<isize> = Option.None\nlet b: Option<i32> = Option.None\nConsole.writeLine(\"\\(a) \\(b) \\(a)\")"));
        Assert.Equal(3, errors.Length);
        Assert.All(errors, static x => Assert.Equal(nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd), x.Code));
        Assert.Equal(["a", "b", "a"], errors.Select(static x => x.Text));
    }

    // SPEC 4.6.9, 5.3, 23.3.6.4: an index key that fits no conformance, Dictionary key or pointer offset is reported once, with
    // both Types at the key or at the index expression, and a receiver without indexing is a Language error.
    [Theory]
    [InlineData("let a = v[^1]", "NoApplicableOverload_Kd", "v[^1]", null)]
    [InlineData("let n: i32 = 0\nlet a = v[n]", "NoApplicableOverload_Kd", "v[n]", null)]
    [InlineData("let a = v[0..1]", "NotIndexable_Kd", "v[0..1]", null)]
    [InlineData("let x: i32 = 5\nlet a = x[0]", "NotIndexable_Kd", "x[0]", null)]
    [InlineData("var d: Dictionary<isize, string> = [1: \"x\"]\nlet a = d[^1]", "TypeMismatch_Kd", "^1", "expected isize, found Kimi.FromEnd<i32>")]
    [InlineData("func f(p: unsafe/i32) -> i32\n    unsafe\n        return p[0..4]", "TypeMismatch_Kd", "0..4", "expected isize, found Kimi.Range<i32, i32>")]
    public void IndexKeysReportOneExplainedError(string statement, string code, string text, string? label)
    {
        const string View = "struct View\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
            "    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value\nlet v = View.init(value: 7)\n";
        var error = Assert.Single(PublishedErrors(MinimalEmissionTest.Analyze(View + statement)));
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, label is null ? null : error.Label);
    }

    // SPEC 13.5.3: only a numeric conversion changes an owned Core, so converting a position or range is a Type error.
    [Theory]
    [InlineData("let a = (^1)@u8", "(^1)@u8", "expected u8, found Kimi.FromEnd<i32>")]
    [InlineData("let y = ^1\nlet a = y@FromEnd<i64>", "y@FromEnd<i64>", "expected Kimi.FromEnd<i64>, found Kimi.FromEnd<i32>")]
    [InlineData("let n: i32 = 3\nlet a = n@FromEnd<i32>", "n@FromEnd<i32>", "expected Kimi.FromEnd<i32>, found i32")]
    [InlineData("let a = (0..1)@u8", "(0..1)@u8", "expected u8, found Kimi.Range<i32, i32>")]
    public void PositionConversionsAreTypeErrors(string statement, string text, string label)
    {
        var error = Assert.Single(PublishedErrors(MinimalEmissionTest.Analyze(statement)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
    }

    // Publishes every front-end phase as a check does and returns its Errors.
    private static TestDiagnostic[] PublishedErrors(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)];
    }
}

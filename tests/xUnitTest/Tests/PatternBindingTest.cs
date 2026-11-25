// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class PatternBindingTest
{
    private const string Selecting = "func g(o: Option<i32>, c: bool, v: i32) -> i32\n";
    private const string BothMissing = "UnresolvedBinding_Kd missing | UnresolvedBinding_Kd missing2";
    private const string Use = "func h(v: i32) -> i32 => v\nfunc g(o: Option<i32>) -> ()\n";
    private const string OptionMatch = "func (o: Option<i32>) -> i32\n        match o\n            .Some(let n) => return n\n            .None => return 0";
    private const string CaseMatch = "    public let f = func (o: E) -> i32\n        match o\n            .A(let n) => return n\n            .B => return 0\n";

    [Theory]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    Option<i32>.Some(let n,) => n\n    Option<i32>.None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    (::Kimi.Option<i32>.Some(let n)) => n\n    (::Kimi.Option<i32>.None) => 0")]
    [InlineData("enum E\n    A(i32)\n    B\nfunc f(x: E) -> i32 => match x@move\n    E.A(let n) => n\n    E.B => 0")]
    [InlineData("func f(x: (i32, bool)) -> i32 => match x\n    (let n, _) => n")]
    [InlineData("func f(x: ()) -> i32 => match x\n    () => 1")]
    [InlineData("func f(x: bool) -> i32 => match x\n    (true) => 1\n    false => 0")]
    [InlineData("func f(x: string) -> i32 => match x@move\n    \"yes\" => 1\n    (_) => 0")]
    [InlineData("func f(x: char) -> i32 => match x\n    'A' => 1\n    _ => 0")]
    [InlineData("func f(x: Option<Option<i32>>) -> i32 => match x\n    .Some(.Some(let n)) => n\n    .Some(_) => 1\n    .None => 0")]
    [InlineData("func f(x: Option<(i32, bool)>) -> i32 => match x\n    .Some((let n, _)) => n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x@move\n    .Some(var n)\n        n = 2\n        yield n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32\n    return match x\n        .Some(let n)\n            yield n\n        .None\n            yield 0")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => if n > 0 => n else => 0\n    .None => 0")]
    [InlineData("func f(x: Option<i32>)\n    match x\n        .Some(_)\n            ()\n        .None => ()")]
    [InlineData("func f<T>(x: Option<T>) => match x@move\n    .Some(let value) => ()\n    .None => ()")]
    [InlineData("func f(x: ref/i32 during static) => match x\n    let r => ()")]
    [InlineData("func f(x: uniq/i32) => match x\n    _ => ()")]
    [InlineData("func f(x: Option<ref/i32 during static>) => match x\n    .Some(let r) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<uniq/i32 during a>) => match x@move\n    .Some(let r) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<Option<i32>>) -> i32 => match x\n    .Some(let inner) => match inner\n        .Some(let n) => n\n        .None => 0\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let Option) => Option\n    Option<i32>.None => 0")]
    [InlineData("struct S\n    public let f = " + OptionMatch)]
    [InlineData("struct S\n    public let f: (Option<i32>) -> i32 = " + OptionMatch)]
    [InlineData("func g() -> i32\n    let f = " + OptionMatch + "\n    return f(Option.Some(1))")]
    [InlineData("struct S\n    public let f = func (o: Option<i32>) -> i32\n        match o\n            .Some(_) => return 1\n            .None => return 0")]
    [InlineData("struct S\n    public let f = func (o: Option<i32>) -> i32\n        match o\n            .None => return 0\n            _ => return 1")]
    [InlineData("struct S\n" + CaseMatch + "enum E\n    A(i32)\n    B")]
    [InlineData("enum E\n    A(i32)\n    B\nstruct S\n" + CaseMatch)]
    public void SupportedPatternsBindAndCheckFlow(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.All(Matches(c), m => Assert.True(c.Binding.TryGetMatch(m, out _)));
    }

    [Theory]
    [InlineData("func f(x: Option<i32>) => match x\n    .Some => ()\n    _ => ()")]
    [InlineData("func f(x: Option<i32>) => match x\n    .Some(_, _) => ()\n    _ => ()")]
    [InlineData("func f(x: Option<i32>) => match x\n    .Missing => ()\n    _ => ()")]
    [InlineData("func f(x: Option<i32>) => match x\n    Option<string>.Some(_) => ()\n    _ => ()")]
    [InlineData("func f(x: (i32, i32)) => match x\n    (let n, let n) => ()")]
    [InlineData("func f(x: (i32, i32)) => match x\n    (let n,) => ()")]
    [InlineData("func f<T>(x: Option<T>) => match x\n    .Some(1) => ()\n    _ => ()")]
    [InlineData("func f(x: i32) => match x\n    true => ()")]
    [InlineData("func f(x: raw/i32) => match x\n    0 => ()\n    _ => ()")]
    [InlineData("struct Data\nfunc f(x: obj/Data) => match x\n    () => ()\n    _ => ()")]
    public void InvalidPatternsSuppressCoverageCascades(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        var plan = Plan(c);
        Assert.Equal(MatchCoverageState.Invalid, plan.Coverage.State);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
    }

    // SPEC 14.8.4, 15.1.6, 23.1.2 (DECISIONS R4-M179): coverage is proved from the Patterns, never from an acquisition, so a binding whose
    // Copy proof is Error leaves its match's coverage, its NonExhaustiveMatch_Kd, a failed guard's record and an unreachable arm's Warning
    // as over the Copy twin, beside the declaration's failure. Fail-before: the binding made the coverage Invalid.
    [Theory]
    [InlineData(".Some(let v) => ()\n    .None => ()", MatchCoverageState.Exhaustive, "", "")]
    [InlineData(".Some(let v) => ()", MatchCoverageState.NonExhaustive, "NonExhaustiveMatch_Kd", "")]
    [InlineData(".Some(let v) if 1 => ()\n    .None => ()", MatchCoverageState.NonExhaustive, "NonExhaustiveMatch_Kd TypeMismatch_Kd", "")]
    [InlineData(".Some(let v) => ()\n    _ => ()\n    .None => ()", MatchCoverageState.Exhaustive, "", ".None 1")]
    public void ABindingWhoseCopyProofIsErrorKeepsItsMatchCoverage(string arms, MatchCoverageState state, string codes, string warnings)
    {
        var good = Facts("Good", "n: i32 = 0");
        Assert.Equal((state, codes, warnings), (good.Coverage.State, good.Codes, good.Warnings));
        Assert.Equal(good, Facts("Bad", "s: string = \"\""));

        (MatchCoverage Coverage, string Codes, string Warnings) Facts(string type, string field)
        {
            var c = Parse($"struct {type}\n    Self is Copy\n    public var {field}\n    public init() => ()\nfunc f(o: Option<{type}>) => match o@move\n    {arms}");
            c.Bind();
            var issues = c.Binding.Issues.Select(static x => $"{x.Code}").Order(StringComparer.Ordinal).ToList();
            Assert.Equal(type == "Bad", issues.Remove(nameof(DiagnosticCode.UnsatisfiedConstraint_Kd)));
            return (Plan(c).Coverage, string.Join(" ", issues), string.Join(" ", c.Binding.PatternWarnings.Select(static x => $"{x.Pattern} {x.CoveringArm}")));
        }
    }

    [Theory]
    [InlineData("func f(x: ref/i32 during static) => match x\n    0 => ()\n    _ => ()", 1)]
    [InlineData("func f(x: Option<ref/i32 during static>) => match x\n    .Some(0) => ()\n    _ => ()", 1)]
    [InlineData("func f(x: Option<i32>) => match x@ref\n    .Some(_) => ()\n    .None => ()", 1)]
    [InlineData("func f(x: ref/(ref/i32 during static) during static) => match x\n    0 => ()\n    _ => ()", 2)]
    [InlineData("func f(x: Option<uniq/i32 during a>) => match x\n    .Some(0) => ()\n    _ => ()", 1)]
    [InlineData("func f(x: ref/(uniq/i32 during b) during a) => match x\n    0 => ()\n    _ => ()", 2)]
    public void SharedStructuralInspectionHasCompleteCoverage(string source, int layers)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
        Assert.Contains(Plan(c).Positions, x => x.ImplicitFollows == layers);
    }

    [Theory]
    [InlineData("func f(x: Option<i32>) => match x\n    .None => ()", MatchCoverageReason.MissingCase)]
    [InlineData("func f(x: Option<bool>) => match x\n    .Some(true) => ()\n    .Some(false) => ()\n    .None => ()", MatchCoverageReason.WholePayloadRequired)]
    [InlineData("func f(x: (bool, bool)) => match x\n    (true, _) => ()\n    (false, true) => ()\n    (false, false) => ()", MatchCoverageReason.CatchAllRequired)]
    [InlineData("func f(x: i32) => match x\n    0 => ()", MatchCoverageReason.CatchAllRequired)]
    [InlineData("func f(x: bool) => match x\n    true => ()", MatchCoverageReason.BooleanValuesRequired)]
    public void CoverageUsesOnlyTheSpecifiedProofs(string source, MatchCoverageReason reason)
    {
        var path = Path.GetFullPath("match-coverage.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.NonExhaustive, Plan(c).Coverage.State);
        Assert.Equal(reason, Plan(c).Coverage.Reason);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "NonExhaustiveMatch_Kd");
        Assert.Equal(new DiagnosticValue("coverage", DiagnosticValueKind.Enumeration, reason.ToString()), record.Reason![0]);
        var note = reason switch
        {
            MatchCoverageReason.MissingCase => "The Case at declaration index 0 has no arm.",
            MatchCoverageReason.WholePayloadRequired => "The Case at declaration index 0 requires an unguarded whole-payload arm.",
            MatchCoverageReason.BooleanValuesRequired => "Unguarded true and false Patterns or a catch-all are required.",
            _ => "An unguarded whole-position catch-all is required.",
        };
        Assert.Equal(note, record.Note);
        if (reason is MatchCoverageReason.MissingCase or MatchCoverageReason.WholePayloadRequired)
        {
            Assert.Equal(new DiagnosticValue("case", DiagnosticValueKind.Number, "0"), record.Reason[1]);
            Assert.Equal(2, record.Reason.Length);
        }
        else
        {
            Assert.Single(record.Reason);
        }

        Assert.Equal(source.IndexOf("match", StringComparison.Ordinal), record.Span!.Value.Start);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(note, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains(note, sent.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("u8", "-0", true)]
    [InlineData("u8", "-1", false)]
    [InlineData("i8", "-128", true)]
    [InlineData("i8", "128", false)]
    [InlineData("i8", "-129", false)]
    [InlineData("i128", "-170141183460469231731687303715884105728", true)]
    [InlineData("u128", "340282366920938463463374607431768211455", true)]
    [InlineData("isize", "9223372036854775807", true)]
    [InlineData("isize", "9223372036854775808", false)]
    [InlineData("usize", "18446744073709551615", true)]
    public void LiteralFittingPreservesFullMagnitude(string type, string literal, bool valid)
    {
        var c = Parse($"func f(x: {type}) => match x\n    {literal} => ()\n    _ => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void WarningsNormalizeValuesAndReportOnceWithoutInvalidatingBinding()
    {
        var c = Parse("func f(x: i32) => match x\n    0 => ()\n    -0 => ()\n    0x00 => ()\n    _ => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(2, c.Binding.PatternWarnings.Count);
        Assert.All(c.Binding.PatternWarnings, x => Assert.Equal(0, x.CoveringArm));
        c.Binding.ReportDiagnostics();
        c.Binding.ReportDiagnostics();
        Assert.Equal(2, TestDiagnostics.Of(c).Count(x => x.Code == nameof(DiagnosticCode.UnreachablePattern_Kd)));
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    [Fact]
    public void GuardedPatternsDoNotCoverAndGuardTransfersAreVisited()
    {
        var c = Parse("func f(x: bool) => match x\n    true if true => ()\n    false if yield () => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.NonExhaustive, Plan(c).Coverage.State);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        Assert.Contains(CompilationTestHelper.BindingErrors(c), x => x.Message.Contains("target", StringComparison.Ordinal));
    }

    [Fact]
    public void GuardDoesNotPoisonIndependentCoverage()
    {
        var c = Parse("func f(x: Option<i32>) => match x\n    .Some(let n) if n > 0 => ()\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
    }

    [Fact]
    public void PatternAndImmediateBodyShareDeclarationSpace()
    {
        var c = Parse("func f(x: Option<i32>) => match x\n    .Some(let n)\n        let n = 1\n    .None\n        ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void NestedBlocksMayShadowAndOtherArmsCannotSeeBindings()
    {
        var valid = Parse("func f(x: Option<i32>) => match x\n    .Some(let n)\n        if true\n            let n = 1\n        ()\n    .None\n        ()");
        Assert.True(valid.Bind().IsComplete, Describe(valid));
        var invalid = Parse("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => n");
        Assert.False(invalid.Bind().IsComplete);
        Assert.Contains(invalid.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    // SPEC 15.1.6 (F's C8 ruling 24): the binding of a moved Subject is transferred with its responsibility, so it Moves and asks no
    // Copy (it was CopyOrMove, the Unknown Copy of T, which HIR lowered to Move).
    [Fact]
    public void InferredMatchResultAndGenericAcquisitionAreRetained()
    {
        var c = Parse("func f(x: bool)\n    let r = match x\n        true => 1\n        false => 2\nfunc generic<T>(x: Option<T>) => match x@move\n    .Some(let value) => ()\n    .None => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var selections = Matches(c);
        Assert.Equal("i32", selections[0].TypeOf()!.Name);
        Assert.True(c.Binding.TryGetMatch(selections[1], out var plan));
        Assert.Equal(PatternAcquisition.Move, Assert.Single(plan!.Positions, p => p.Kind == BoundPatternKind.Binding).Acquisition);
    }

    [Fact]
    public void ValueOrZeroNowPassesMatchOwnershipVerification()
    {
        var c = Parse("func valueOrZero(value: Option<i32>) -> i32\n    return match value\n        .Some(let n) => n\n        .None => 0");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var result = c.Analyze();
        Assert.True(result.IsVerified);
    }

    [Theory]
    [InlineData("isize", "2147483648", true)]
    [InlineData("isize", "9223372036854775807", true)]
    [InlineData("isize", "9223372036854775808", false)]
    [InlineData("isize", "-9223372036854775808", true)]
    [InlineData("isize", "-9223372036854775809", false)]
    [InlineData("usize", "18446744073709551615", true)]
    [InlineData("usize", "18446744073709551616", false)]
    public void PointerSizedPatternsUseTheProfileWidth(string type, string literal, bool valid)
    {
        var c = Parse($"func f(x: {type}) => match x\n    {literal} => ()\n    _ => ()", "i686-unknown-linux-gnu");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("char", "'A'", "'\\u(41)'")]
    [InlineData("string", "\"A\"", "\"\\u(41)\"")]
    public void EscapedLiteralsHaveTheSameCoverageValue(string type, string first, string second)
    {
        var c = Parse($"func f(x: {type}) => match x@move\n    {first} => ()\n    {second} => ()\n    _ => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(0, Assert.Single(c.Binding.PatternWarnings).CoveringArm);
        Assert.All(KotoTree.Walk(Plan(c).Syntax.Arms[0].Pattern), p => Assert.False(c.Binding.TryGetEnumConstruction(p, out _)));
    }

    [Theory]
    [InlineData("func f(x: Option<bool>) => match x\n    .Some(true) => ()\n    .Some(false) => ()\n    .Some(_) => ()\n    .None => ()", 0)]
    [InlineData("func f(x: Option<i32>) => match x\n    .Some(_) => ()\n    .Some(0) if true => ()\n    .None => ()", 1)]
    [InlineData("func f(x: (i32, bool)) => match x\n    (0, _) => ()\n    (0, true) => ()\n    (_, _) => ()", 1)]
    public void ContainmentUsesOneEarlierUnguardedPattern(string source, int warnings)
    {
        var c = Parse(source);
        c.Bind();
        Assert.Equal(warnings, c.Binding.PatternWarnings.Count);
    }

    [Fact]
    public void WarningDoesNotRemoveLaterBodyFromTypeChecks()
    {
        var c = Parse("func f(x: i32) -> i32 => match x\n    _ => 1\n    0 => \"invalid\"");
        Assert.False(c.Bind().IsComplete);
        Assert.Single(c.Binding.PatternWarnings);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    // SPEC 14.8.3, 23.1.2 (PLAN G259): a failed arm makes no pattern unreachable. A use of a local whose Type its failed match left
    // unknown binds the declaration again, and the match rebuilds its plan each time it is bound, so no arm is counted twice.
    [Theory]
    [InlineData("    return h(k)")]
    [InlineData("    return h(k) + h(k)")]
    [InlineData("    let z = k\n    return z")]
    [InlineData("    let z = (k, k)\n    return z.0")]
    public void ARebindOfAFailedMatchCountsEachArmOnce(string use)
    {
        const string Head = "func h(v: i32) -> i32 => v\nfunc g(c: bool) -> i32\n    let k = match c\n        true => 1\n        false => MISSING\n";
        Assert.Equal("UnresolvedBinding_Kd missing", Records(Head.Replace("MISSING", "missing", StringComparison.Ordinal) + use));
        Assert.Equal(string.Empty, Records(Head.Replace("MISSING", "2", StringComparison.Ordinal) + use));
    }

    // SPEC 23.1.2: a failed pattern or guard is that part's own located failure, the one record; the match, whose own requirements are
    // its coverage and its result join, adds none. A binding declared in a failed position rests on it, so a body's or guard's use of it
    // adds none either (each row's twin replaces `written` by `valid` and has no record).
    [Theory]
    [InlineData("func g(o: Option<i32>) -> i32\n    let k: i32 = match o\n        .Some(let a, let b) => a\n        _ => 0\n    return k", "InvalidPattern_Kd .Some(let a, let b)", "let a, let b", "let a")]
    [InlineData("func g(o: Option<i32>) -> ()\n    match o\n        .Some(let a, let b) if a > 0 => ()\n        _ => ()", "InvalidPattern_Kd .Some(let a, let b)", "let a, let b", "let a")]
    [InlineData(Use + "    match o\n        .Some(let a, let b) => h(a)\n        _ => ()", "InvalidPattern_Kd .Some(let a, let b)", "let a, let b", "let a")]
    [InlineData(Use + "    match o\n        .Missing(let a) => h(a)\n        _ => ()", "UnresolvedBinding_Kd .Missing | InvalidPattern_Kd .Missing(let a)", ".Missing", ".Some")]
    [InlineData("func g(o: Option<i32>) => match o\n    .Some(let x) if x => ()\n    _ => ()", "TypeMismatch_Kd x", "if x =>", "if x > 0 =>")]
    [InlineData("func g(x: i32) => match x\n    true => ()\n    _ => ()", "InvalidPattern_Kd true", "true", "0")]
    [InlineData("func g(o: Option<i32>) => match o\n    .Missing => ()\n    _ => ()", "InvalidPattern_Kd .Missing | UnresolvedBinding_Kd .Missing", ".Missing", ".None")]
    [InlineData("func g(o: Option<i32>) => match o\n    .Missing => ()\n    .None => ()", "InvalidPattern_Kd .Missing | UnresolvedBinding_Kd .Missing", ".Missing", ".Some(_)")]
    [InlineData("func g(o: Option<i32>) => match o\n    Missing.A => ()\n    _ => ()", "InvalidPattern_Kd Missing.A", "Missing.A", "Option<i32>.None")]
    [InlineData("func g(o: Option<i32>) -> i32 => match o\n    .Some(_, _) => 1\n    _ => 2", "InvalidPattern_Kd .Some(_, _)", "(_, _)", "(_)")]
    [InlineData("func g(x: (i32, i32)) => match x\n    (let n, let n) => ()", "DuplicateBinding_Kd let n", "let n)", "let m)")]
    public void AFailedPatternOrGuardIsItsOwnOneRecord(string source, string records, string written, string valid)
    {
        Assert.Equal(records, Records(source));
        Assert.Equal(string.Empty, Records(source.Replace(written, valid, StringComparison.Ordinal)));
    }

    // SPEC 23.1.2 (PLAN G260): a failed Subject, of a match or a try, is the prerequisite of its positions, bindings, coverage and
    // result only. Guards and bodies are bound, so their own failures (`missing2`) are reported, and a use of a binding rests on it
    // through the binding's declaration, as a use of a local rests on a failed initializer. Each row's twin writes `subject` for
    // SUBJECT and `other` for OTHER and reports `twin`.
    [Theory]
    [InlineData(Selecting + "    match SUBJECT\n        _ => OTHER\n    return 0", BothMissing, "o", "()", "")]
    [InlineData(Selecting + "    let k: i32 = match SUBJECT\n        _ => OTHER\n    return k", BothMissing, "o", "1", "")]
    [InlineData(Selecting + "    let k: i32 = match SUBJECT\n        .Some(let x) => x + OTHER\n        .None => 0\n    return k", BothMissing, "o", "1", "")]
    [InlineData(Selecting + "    let k: i32 = match SUBJECT\n        .Some(let x) if x > 0 => x\n        _ if OTHER => 1\n        _ => 0\n    return k", BothMissing, "o", "c", "")]
    [InlineData(Selecting + "    match SUBJECT\n        .Some(let x) => match x\n            0 => ()\n            _ => OTHER\n        .None => ()\n    return 0", BothMissing, "o", "()", "")]
    [InlineData(Selecting + "    let k: i32 = match f(SUBJECT)\n        .Some(let x) => x + OTHER\n        .None => 0\n    return k", BothMissing, "v", "1", "")]
    [InlineData(Selecting + "    match SUBJECT\n        .Some(let x) => OTHER\n    return 0", BothMissing, "o", "()", "NonExhaustiveMatch_Kd match o\n        .Some(let x) => ()")]
    [InlineData(Selecting + "    match SUBJECT\n        .Some(let x) => x.foo()\n        _ => ()\n    return 0", "UnresolvedBinding_Kd missing", "o", "", "UnresolvedBinding_Kd x.foo")]
    [InlineData(Selecting + "    let k: i32 = match SUBJECT\n        .Some(let x) if x.foo() => 1\n        _ => 0\n    return k", "UnresolvedBinding_Kd missing", "o", "", "UnresolvedBinding_Kd x.foo")]
    [InlineData(Selecting + "    match SUBJECT\n        .Some(let x)\n            let w = func () -> i32 => x\n            h(w())\n        _ => ()\n    return 0", "UnresolvedBinding_Kd missing", "o", "", "")]
    [InlineData("func t(v: i32) -> Option<i32>\n    let r = try f(SUBJECT)\n    return .Some(r + OTHER)", BothMissing, "v", "1", "")]
    [InlineData("func t(v: i32) -> Option<i32>\n    let r = try SUBJECT\n    return .Some(r.foo())", "UnresolvedBinding_Kd missing", "f(v)", "", "UnresolvedBinding_Kd r.foo")]
    public void AFailedSubjectLeavesItsGuardsAndBodiesIndependent(string body, string records, string subject, string other, string twin)
    {
        const string Head = "func f(v: i32) -> Option<i32> => .Some(v)\nfunc h(v: i32) -> i32 => v\n";
        Assert.Equal(records, Records(Head + body.Replace("SUBJECT", "missing", StringComparison.Ordinal).Replace("OTHER", "missing2", StringComparison.Ordinal)));
        Assert.Equal(twin, Records(Head + body.Replace("SUBJECT", subject, StringComparison.Ordinal).Replace("OTHER", other, StringComparison.Ordinal)));
    }

    // SPEC 14.8, 14.9.2, 14.9.3, 23.1.2 (D-1163): a match's own requirements are its coverage and its result join, so a failed Subject,
    // Pattern position, guard or arm body is that part's own failure and the match is built with its arms. An Invalid position (a failed
    // Pattern, a Case whose payload Type is not read, a Pattern resting on the failed Subject) is a leaf that tests nothing, so every exit
    // structural completion counts is built (fail-before: InternalInvariant_Kd at the loop or function, a false UninitializedPlace_Kd at
    // a Place the unbuilt exit initialized), and the arms' independent records are reported (fail-before: lostrec, nopelost, gsubjmove,
    // casefailmove lost MovedPlace_Kd, armfit the join's TypeMismatch_Kd). A Case leaf's read payload Patterns keep their own records
    // (casefailsibnope, casefailsiblit); the positions they formed rest on the unread payload, so a late recheck there is not made
    // (caselatesib, caselatesibloop: its InvalidConstraint_Kd read E's failed clause, a dependent record by rule W). Each valid twin has
    // no record; a twin with its own record has it at the same span. PLAN G1321 (Q13, dependent records removed): the plan judges its
    // Subject Type once, at its root position. In min, nouse (InvalidConstraint_Kd 8:13) and matchinvloop (UnsatisfiedConstraint_Kd
    // 8:13) BindMatch refutes it and every arm's Pattern rests on that root (BU-251 D2), so the second arm's record read the failed root
    // directly. In c1t (InvalidConstraint_Kd 11:9), c1tloop and c1tguard (InvalidConstraint_Kd 13:13) the refutation is late
    // (`E<Source>` passes BindMatch's judgment): the second arm keeps its kind and its test (D3's late form), and its record is gone
    // because the late recheck judges only the root; it read E's failed clause, a dependent record as caselatesib's is, and so is
    // caselatesibtwin's InvalidConstraint_Kd 13:12 at its nested payload position (`.A(let n)` of `E<Source>`).
    [Theory]
    [InlineData("nope", "func sum(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) => total += v\n            .Nope => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"\\(sum(.None))\")\n", "InvalidPattern_Kd 6:13 | UnresolvedBinding_Kd 6:13")]
    [InlineData("mismatch", "func drain<I>(iterator: uniq/I) -> i64\n    I is Iterator\n    var total: i64 = 0\n    loop\n        match iterator.next()\n            .Some(let value) => total += value\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "TypeMismatch_Kd 6:42")]
    [InlineData("valueloop", "func f(o: Option<i32>) -> i32\n    var total = 0\n    let r: i32 = loop\n        match o\n            .Some(let v) => total += missing\n            .None => exit 0\n    return r + total\n\npublic func main()\n    Console.writeLine(\"\\(f(.None))\")\n", "UnresolvedBinding_Kd 5:38")]
    [InlineData("min", "func drain<I>(iterator: uniq/I) -> i64\n    I is Iterator\n    I.Iterator.Item is i64\n    var total: i64 = 0\n    loop\n        match iterator.next()\n            .Some(let value) => total += value\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 3:5 | InvalidConstraint_Kd 6:15 | InvalidConstraint_Kd 7:13")]
    [InlineData("nouse", "func drain<I>(iterator: uniq/I) -> i64\n    I is Iterator\n    I.Iterator.Item is i64\n    var total: i64 = 0\n    loop\n        match iterator.next()\n            .Some(let value) => total += 1\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 3:5 | InvalidConstraint_Kd 6:15 | InvalidConstraint_Kd 7:13")]
    [InlineData("subj", "func sum() -> i32\n    var total = 0\n    loop\n        match missing\n            .Some(let v) => total += v\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 4:15")]
    [InlineData("guard", "func sum(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) if missing => total += v\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:29")]
    [InlineData("guardty", "func sum(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) if v => total += v\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "TypeMismatch_Kd 5:29")]
    [InlineData("lostrec", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(a: R) -> i32\n    return match missing\n        .Some(_) => take(a@move) + take(a@move)\n        .None => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 6:18 | MovedPlace_Kd 7:41")]
    [InlineData("nopelost", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(a: R, o: Option<i32>) -> i32\n    return match o\n        .Some(_) => take(a@move) + take(a@move)\n        .Nope => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "MovedPlace_Kd 7:41 | InvalidPattern_Kd 8:9 | UnresolvedBinding_Kd 8:9")]
    [InlineData("armfit", "func f() -> i32\n    let r: i32 = match missing\n        .Some(_) => \"s\"\n        .None => 0\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "TypeMismatch_Kd 2:18 | UnresolvedBinding_Kd 2:24")]
    [InlineData("gsubjloop", "func f() -> i32\n    var total = 0\n    loop\n        match missing\n            .Some(let v) if v > 0 => total += v\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 4:15")]
    [InlineData("gsubjnest", "func f() -> i32\n    var total = 0\n    loop\n        match missing\n            (let a, let b) if a > b => total += a\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 4:15")]
    [InlineData("gsubjmove", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(a: R) -> i32\n    return match missing\n        .Some(_) if take(a@move) > 0 => 1\n        _ => take(a@move)\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 6:18 | MovedPlace_Kd 8:19")]
    [InlineData("valjoin", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        let x: i32 = match o\n            .Some(_) => missing\n            .None => exit\n        total += x\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:25")]
    [InlineData("slotuses", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f() -> i32\n    var total = 0\n    loop\n        match missing\n            .Some(var v)\n                v = R()\n                v.n = 2\n                total += v.n + take(v@move)\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 8:15 | UnresolvedBinding_Kd 10:21")]
    [InlineData("dupbind", "func f(p: (i32, i32)) -> i32\n    var total = 0\n    loop\n        match p\n            (let a, let a) => total += a\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "DuplicateBinding_Kd 5:21")]
    [InlineData("initarm", "func f(o: Option<i32>) -> i32\n    var r: i32\n    match o\n        .Some(let v) => r = v\n        .Nope => r = 0\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InvalidPattern_Kd 5:9 | UnresolvedBinding_Kd 5:9")]
    [InlineData("labeldo", "func f() -> i32\n    let r: i32 = label work: do\n        match missing\n            .Some(_) => exit to work 1\n            .None => exit to work 2\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 3:15")]
    [InlineData("nopebind", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Nope(let x) => total += x\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:13 | InvalidPattern_Kd 5:13")]
    [InlineData("nopeguardbind", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Nope(let x) if x > 0 => total += x\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:13 | InvalidPattern_Kd 5:13")]
    [InlineData("nestnope", "func f(o: Option<Option<i32>>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(.Nope(let x)) => total += x\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:19 | InvalidPattern_Kd 5:19")]
    [InlineData("vjinferann", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        let x: i32 = match o\n            .Some(0) => missing\n            .Some(_) => 1\n            .None => exit\n        total += x\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 5:25")]
    [InlineData("matchinvloop", "struct Box<T>\n    T is i32\nfunc f(value: Option<Box<string>>) -> i32\n    var n = 0\n    loop\n        match value\n            .Some(_) => n += 1\n            .None => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnsatisfiedConstraint_Kd 3:22 | UnsatisfiedConstraint_Kd 7:13")]
    [InlineData("c1t", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nfunc f(value: E<Source>) -> i32\n    return match value\n        .A(let n) => n\n        .B => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5 | InvalidConstraint_Kd 10:9")]
    [InlineData("c1tloop", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nfunc f(value: E<Source>) -> i32\n    var total = 0\n    loop\n        match value\n            .A(let n) => total += n\n            .B => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5 | InvalidConstraint_Kd 12:13")]
    [InlineData("c1tguard", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nfunc f(value: E<Source>) -> i32\n    var total = 0\n    loop\n        match value\n            .A(let n) if n > 0 => total += n\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5 | InvalidConstraint_Kd 12:13")]
    [InlineData("casefailloop", "enum E\n    A(Missing)\n    B(i32)\nfunc g(e: E) -> i32\n    var n = 0\n    loop\n        match e\n            .A(let x) => n += 1\n            .B(_) => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 8:16")]
    [InlineData("casefailsibloop", "enum E\n    A(i32, Missing)\n    B(i32)\nfunc g(e: E) -> i32\n    var total = 0\n    loop\n        match e\n            .A(let n, _) => total += n\n            .B(_) => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:12 | InvalidPattern_Kd 8:23")]
    [InlineData("casefailguard", "enum E\n    A(Missing)\n    B(i32)\nfunc g(e: E) -> i32\n    var n = 0\n    loop\n        match e\n            .A(let x) if x > 0 => n += 1\n            .B(_) => exit\n            _ => n += 2\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 8:16")]
    [InlineData("casefailnest2", "enum E\n    A(Missing)\n    B(i32)\nfunc g(o: Option<E>) -> i32\n    var n = 0\n    loop\n        match o\n            .Some(.A(let x)) => n += 1\n            _ => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 8:22")]
    [InlineData("casefailmove", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nenum E\n    A(Missing)\n    B(i32)\nfunc g(e: E, a: R) -> i32 => match e\n    .A(_) => take(a@move) + take(a@move)\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 6:7 | InvalidPattern_Kd 9:8 | MovedPlace_Kd 9:34")]
    [InlineData("caselatesib", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nenum F\n    C(E<Source>, Missing)\n    D\nfunc f(v: F) -> i32\n    return match v\n        .C(.A(let n), _) => n\n        _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5 | UnresolvedBinding_Kd 9:18 | InvalidPattern_Kd 13:23")]
    [InlineData("caselatesibtwin", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nenum F\n    C(E<Source>, i32)\n    D\nfunc f(v: F) -> i32\n    return match v\n        .C(.A(let n), _) => n\n        _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5")]
    [InlineData("caselatesibloop", "contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nenum F\n    C(E<Source>, Missing)\n    D\nfunc f(v: F) -> i32\n    var total = 0\n    loop\n        match v\n            .C(.A(let n), _) => total += n\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InaccessibleBinding_Kd 4:8 | InaccessibleBinding_Kd 5:5 | UnresolvedBinding_Kd 9:18 | InvalidPattern_Kd 15:27")]
    [InlineData("casefail", "enum E\n    A(Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let x) => 1\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 5:8")]
    [InlineData("casefailwild", "enum E\n    A(Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(_) => 1\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 5:8")]
    [InlineData("casefailref", "enum E {s}\n    A(ref/i32 during missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let r) => r@follow\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "MissingOriginBinding_Kd 2:22 | InvalidPattern_Kd 5:8")]
    [InlineData("casefailsib", "enum E\n    A(i32, Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let n, _) => n\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:12 | InvalidPattern_Kd 5:15")]
    [InlineData("casefailsibnope", "enum E\n    A(Option<i32>, Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(.Nope, _) => 1\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:20 | InvalidPattern_Kd 5:8 | UnresolvedBinding_Kd 5:8 | InvalidPattern_Kd 5:15")]
    [InlineData("casefailsibbind", "enum E\n    A(Option<i32>, Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(.Some(let v), _) => v\n    _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:20 | InvalidPattern_Kd 5:22")]
    [InlineData("casefailsibguard", "enum E\n    A(Option<i32>, Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(.Some(let v), _) if v > 0 => v\n    _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:20 | InvalidPattern_Kd 5:22")]
    [InlineData("casefailsiblit", "enum E\n    A(i32, Missing)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(5000000000, _) => 1\n    _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:12 | InvalidPattern_Kd 5:8 | InvalidPattern_Kd 5:20")]
    [InlineData("casenestleaf", "enum In\n    P(Missing2)\n    Q\nenum Out\n    C(In, Missing)\n    D\nfunc g(v: Out) -> i32 => match v\n    .C(.P(let x), _) => 1\n    _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | UnresolvedBinding_Kd 5:11 | InvalidPattern_Kd 8:11 | InvalidPattern_Kd 8:19")]
    [InlineData("casearitynope", "enum E\n    A(Option<i32>, i32)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(.Nope, _, _) => 1\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "InvalidPattern_Kd 5:5")]
    [InlineData("casefailbyval", "enum E\n    A(Missing)\n    B(string)\nfunc mk() -> E => E.B(\"s\")\nfunc g() -> i32 => match mk()\n    .A(let x) => 1\n    .B(let s) => 2\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:7 | InvalidPattern_Kd 6:8")]
    [InlineData("n5", "struct W\n    public var o: Option<Missing> = .None\n    public init() => ()\nfunc f(w: W) -> i32\n    var n = 0\n    loop\n        match w.o\n            .Some(let v) => n += 1\n            .None => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:26")]
    [InlineData("n5guard", "struct W\n    public var o: Option<Missing> = .None\n    public init() => ()\nfunc f(w: W) -> i32\n    var n = 0\n    loop\n        match w.o\n            .Some(let v) if v > 0 => n += 1\n            .None => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "UnresolvedBinding_Kd 2:26")]
    [InlineData("nopetwin", "func sum(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) => total += v\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"\\(sum(.None))\")\n", "")]
    [InlineData("twin", "func drain<I>(iterator: uniq/I) -> i64\n    I is Iterator\n    I.(Iterator).Item is i64\n    var total: i64 = 0\n    loop\n        match iterator.next()\n            .Some(let value) => total += value\n            .None => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("guardtwin", "func sum(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) if v > 0 => total += v\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("gsubjtwin", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(let v) if v > 0 => total += v\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("valjointwin", "func f(o: Option<i32>) -> i32\n    var total = 0\n    loop\n        let x: i32 = match o\n            .Some(let v) => v\n            .None => exit\n        total += x\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("initarmtwin", "func f(o: Option<i32>) -> i32\n    var r: i32\n    match o\n        .Some(let v) => r = v\n        .None => r = 0\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("tuplenopetwin", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(p: (R, Option<i32>)) -> i32\n    return match p@move\n        (let r, .None) => take(r@move)\n        (let r, _) => take(r@move)\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("dupbindtwin", "func f(p: (i32, i32)) -> i32\n    var total = 0\n    loop\n        match p\n            (let a, let b) => total += a + b\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("labeldotwin", "func f(o: Option<i32>) -> i32\n    let r: i32 = label work: do\n        match o\n            .Some(_) => exit to work 1\n            .None => exit to work 2\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("nestnopetwin", "func f(o: Option<Option<i32>>) -> i32\n    var total = 0\n    loop\n        match o\n            .Some(.Some(let x)) => total += x\n            _ => exit\n    return total\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailtwin", "enum E\n    A(i32)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let x) => 1\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailsibtwin", "enum E\n    A(i32, i32)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let n, _) => n\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailreftwin", "enum E {s}\n    A(ref/i32 during s)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let r) => r@follow\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailnest2twin", "enum E\n    A(i32)\n    B(i32)\nfunc g(o: Option<E>) -> i32\n    var n = 0\n    loop\n        match o\n            .Some(.A(let x)) => n += 1\n            _ => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailsibbindtwin", "enum E\n    A(Option<i32>, i32)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(.Some(let v), _) => v\n    _ => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("casefailbyvaltwin", "enum E\n    A(i32)\n    B(string)\nfunc mk() -> E => E.B(\"s\")\nfunc g() -> i32 => match mk()\n    .A(let x) => 1\n    .B(let s) => 2\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("n5twin", "struct W\n    public var o: Option<i32> = .None\n    public init() => ()\nfunc f(w: W) -> i32\n    var n = 0\n    loop\n        match w.o\n            .Some(let v) => n += 1\n            .None => exit\n    return n\n\npublic func main()\n    Console.writeLine(\"x\")\n", "")]
    [InlineData("lostrectwin", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(a: R, o: Option<i32>) -> i32\n    return match o\n        .Some(_) => take(a@move) + take(a@move)\n        .None => 0\n\npublic func main()\n    Console.writeLine(\"x\")\n", "MovedPlace_Kd 7:41")]
    [InlineData("armfittwin", "func f(o: Option<i32>) -> i32\n    let r: i32 = match o\n        .Some(_) => \"s\"\n        .None => 0\n    return r\n\npublic func main()\n    Console.writeLine(\"x\")\n", "TypeMismatch_Kd 2:18")]
    [InlineData("gsubjmovetwin", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nfunc f(a: R, o: Option<i32>) -> i32\n    return match o\n        .Some(_) if take(a@move) > 0 => 1\n        _ => take(a@move)\n\npublic func main()\n    Console.writeLine(\"x\")\n", "MovedPlace_Kd 8:19")]
    [InlineData("casefailmovetwin", "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\nenum E\n    A(i32)\n    B(i32)\nfunc g(e: E, a: R) -> i32 => match e\n    .A(_) => take(a@move) + take(a@move)\n    .B(let y) => y\n\npublic func main()\n    Console.writeLine(\"x\")\n", "MovedPlace_Kd 9:34")]
    public void AFailedPartOfAMatchIsItsOwnAndItsArmsAreBuilt(string row, string source, string records)
        => Assert.Equal(row + ": " + records, row + ": " + LocatedCodes(source));

    // SPEC 14.8.1, 15.1.6, 14.10.3 (DECISIONS R4-M175): a Pattern binding of a Never payload is no Never form, so it initializes its
    // local from the selected Place, and each read of the local is the Never form (fail-before: UninitializedPlace_Kd at each read); the
    // twin Moves it twice, one MovedPlace_Kd (fail-before: also UninitializedPlace_Kd at the first). A labeled do whose exits both deliver
    // gets no completion from its body's end arrival, as it is no Never form (fail-before: UninitializedPlace_Kd at `result`); the twin's
    // exit of a Never value is assumed delivered and places nothing.
    [Theory]
    [InlineData("func run() -> Result<(), i32>\n    match source()\n        .Ok(let v) => v@move\n        .Err(let e) => return .Err(e)\n", "")]
    [InlineData(
        "func use(x: Never) => ()\nfunc run() -> Result<(), i32>\n    match source()\n        .Ok(let v)\n            use(v)\n            use(v)\n" +
        "        .Err(let e) => return .Err(e)\n    return .Ok(())\n",
        "")]
    [InlineData(
        "func take(x: Never) => ()\nfunc run() -> Result<(), i32>\n    match source()\n        .Ok(let v)\n            take(v@move)\n            take(v@move)\n" +
        "        .Err(let e) => return .Err(e)\n    return .Ok(())\n",
        "MovedPlace_Kd v")]
    [InlineData(
        "func f() -> i32\n    let result = label work: do\n        match (return 1)\n            _ if (exit to work 2) => ()\n            _ => ()\n" +
        "        exit to work 3\n    return result\n",
        "")]
    [InlineData(
        "func f() -> i32\n    let result = label work: do\n        match (return 1)\n            _ if (exit to work 2) => ()\n            _ => ()\n" +
        "        exit to work (return 3)\n    return result\n",
        "UninitializedPlace_Kd result")]
    public void ANeverPayloadBindingInitializesItsLocal(string source, string records)
    {
        var c = MinimalEmissionTest.Analyze("func source() -> Result<Never, i32> => .Err(1)\n" + source, output: OutputKind.Library);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(records, MinimalEmissionTest.Problems(c));
    }

    // SPEC 14.10.3 per part, 15.1.6 (the owner's 「A で進める（推奨）」; DECISIONS R4-M175): a match acquires its whole Subject, so a Case value
    // constructed with a Never payload part, in unreachable code, is UninitializedPlace_Kd at the match whichever part the arm binds
    // (fail-before: accepted). The real-path twins of NeverContinuationTest's per-part rows, a loop's and a do's, read only what their
    // completing exit placed and are accepted.
    [Theory]
    [InlineData("enum E\n    Pair(i64, i64)\n    Other\nfunc f(c: bool, a: i64) -> i64\n    return 9\n    let e = E.Pair(a, (return 1))\n    match e\n        .Pair(let x, _) => return x\n        .Other => return 0\n", "UninitializedPlace_Kd e")]
    [InlineData("enum E\n    Pair(i64, i64)\n    Other\nfunc f(c: bool, a: i64) -> i64\n    return 9\n    let e = E.Pair(a, (return 1))\n    match e\n        .Pair(_, let y) => return y\n        .Other => return 0\n", "UninitializedPlace_Kd e")]
    [InlineData("func f(c: bool, a: i64) -> i64\n    let t = loop\n        if c => exit (a, a)\n        exit (a, (return 1))\n    return t.0\n", "")]
    [InlineData("func f(c: bool, a: i64) -> i64\n    let t = label w: do\n        if c => exit to w (a, a)\n        exit to w (a, (return 1))\n    return t.1\n", "")]
    public void ASubjectWithANeverPartIsAcquiredWhole(string source, string records)
    {
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(records, MinimalEmissionTest.Problems(c));
    }

    [Fact]
    public void SharedAccessBindsAReferenceToTheCopyPayload()
    {
        // SPEC 15.1.6: a Place reached with shared access binds ref/T, also for a Copy T.
        var c = Parse("func f(x: ref/Option<i32> during static) => match x\n    .Some(let n) => n\n    .None => 0");
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        var plan = Plan(c);
        var root = plan.Positions[plan.Arms[0].Pattern];
        Assert.Equal(PatternAccessMode.Shared, root.AccessMode);
        Assert.Equal(1, root.ImplicitFollows);
        var binding = Assert.Single(plan.Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal(PatternAccessMode.Shared, binding.AccessMode);
        Assert.Equal(0, binding.ImplicitFollows);
        Assert.Equal(PatternAcquisition.Borrow, binding.Acquisition);
        Assert.Equal(SemanticsKind.Ref, binding.BodySymbol!.Type!.Semantics);
        Assert.Same(BoundType.I32, binding.BodySymbol.Type.Components[0]);
        Assert.Equal(BindingState.Resolved, plan.Syntax.Arms[0].Body.StateOf());
    }

    // SPEC 15.1.6 (F's C8 ruling 24): a part of a moved Subject is transferred, so the stored reference Moves (it was Copied: the same
    // bits) and keeps its Origin.
    [Fact]
    public void StoredReferenceBindingsRetainOriginsAndOwnedAccess()
    {
        var c = Parse("enum View {a}\n    Some(ref/i32 during a)\nfunc f(x: View)\n    origin x.a == static\n    match x@move\n        .Some(let r) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var binding = Assert.Single(Plan(c).Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal(PatternAccessMode.Owned, binding.AccessMode);
        Assert.Equal(0, binding.ImplicitFollows);
        Assert.Equal(PatternAcquisition.Move, binding.Acquisition);
        Assert.NotNull(binding.MatchedType?.Origin);
        Assert.Same(binding.MatchedType, binding.BodySymbol!.Type);
        Assert.Equal(OriginKind.Static, binding.MatchedType!.Origin!.Kind);
    }

    [Fact]
    public void ReplacingPatternRebuildsScopeAndRetiresRemovedMatch()
    {
        var c = Parse("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0");
        Assert.True(c.Bind().IsComplete);
        var plan = Plan(c);
        var match = plan.Syntax;
        var oldPattern = match.Arms[0].Pattern;
        var replacement = Parse("func g(x: Option<i32>) => match x\n    .Some(let m) => ()");
        var newPattern = Matches(replacement)[0].Arms[0].Pattern;
        ParseTestHelper.Detach(newPattern, new CodeBlockKoto(newPattern.CodeContext));
        Assert.True(KotoHelper.Replace(match, oldPattern, newPattern));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.UnresolvedBinding_Kd && ReferenceEquals(i.Node, match.Arms[0].Body));
        Assert.DoesNotContain(plan.Positions, p => ReferenceEquals(p.Source, oldPattern));
        var newName = Assert.Single(KotoTree.Walk(newPattern).OfType<IdentifierNameKoto>(), n => n.IdentifierName == "m");
        // Reusing an old Pattern child must not resurrect its old arm scope or Symbol.
        var oldName = Assert.Single(KotoTree.Walk(oldPattern).OfType<IdentifierNameKoto>(), n => n.IdentifierName == "n");
        ParseTestHelper.Detach(oldName, new CodeBlockKoto(oldName.CodeContext));
        Assert.True(KotoHelper.Replace(newName.Parent!, newName, oldName));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("n", Assert.Single(plan.Positions, p => p.Kind == BoundPatternKind.Binding).BodySymbol!.Name);
        var function = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), f => !f.IsGenerated);
        var empty = Assert.Single(KotoTree.Walk(Parse("func g() => ()").Kotonoha.RootKoto).OfType<FunctionKoto>(), f => !f.IsGenerated);
        ParseTestHelper.Detach(empty, new CodeBlockKoto(empty.CodeContext));
        Assert.True(KotoHelper.Replace(function.Parent!, function, empty));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.TryGetMatch(match, out _));
        Assert.False(plan.IsCurrent);
        Assert.Empty(plan.Positions);
    }

    [Theory]
    [InlineData("func f(x: bool) => match x\n    (_) => ()", MatchCoverageState.Exhaustive)]
    [InlineData("func f(x: bool) => match x\n    (_) if true => ()", MatchCoverageState.NonExhaustive)]
    public void CoverageHonorsGroupingAndGuards(string source, MatchCoverageState expected)
    {
        var c = Parse(source);
        c.Bind();
        Assert.Equal(expected, Plan(c).Coverage.State);
    }

    [Fact]
    public void GuardReturnTargetsFunctionAndChecksUnsafeOperations()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true if return 1 => 2\n    true => 4\n    false => 3");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.IsType<FunctionKoto>(HirTables.PlanOf(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ReturnKoto>().Single()).Plan);
        Assert.Equal(HirCompletion.Diverges, HirTables.CompletionOf(Matches(c)[0].Arms[0].Guard!));
        var unsafeGuard = Parse("func f(x: bool, p: raw/bool) => match x\n    _ if *p => ()");
        unsafeGuard.Bind();
        Assert.Contains(unsafeGuard.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        unsafeGuard.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(unsafeGuard), x => x.Code == nameof(DiagnosticCode.UnsafeBlockRequired_Kd) && x.Text == "*p");
    }

    // The HIR publishes the guard's exit to the do and neither the match nor the do completing; the MIR's verdict on the program is
    // ANeverPayloadBindingInitializesItsLocal's F3 row (DECISIONS R4-M175).
    [Fact]
    public void AbruptSubjectDoesNotPropagateGuardTransfers()
    {
        var c = Parse("func f() -> i32\n    let result = label work: do\n        match (return 1)\n            _ if (exit to work 2) => ()\n            _ => ()\n        exit to work 3\n    return result");
        Assert.True(c.Bind().IsComplete, Describe(c));
        HirTables.Publish(c);
        var match = Matches(c)[0];
        var guardExit = Assert.IsType<ExitKoto>(KotoHelper.UnwrapParentheses(match.Arms[0].Guard!));
        var target = Assert.IsType<DoKoto>(HirTables.PlanOf(guardExit).Plan);
        Assert.Equal(HirCompletion.Diverges, HirTables.CompletionOf(match));
        Assert.Equal(HirCompletion.Diverges, HirTables.CompletionOf(target));
    }

    [Fact]
    public void ResultRequiringBlockMustYieldAndLetPatternIsInitialized()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true\n        ()\n    false => 0");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.TypeMismatch_Kd);
        var immutable = Parse("func f(x: i32) => match x@move\n    let n\n        n = 2");
        Assert.False(immutable.Bind().IsComplete);
        Assert.Contains(immutable.Binding.Issues, i => i.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    [Theory]
    [InlineData("i32", "x", PatternAcquisition.Copy)]
    [InlineData("string", "x@move", PatternAcquisition.Move)]
    [InlineData("ref/i32 during static", "x", PatternAcquisition.Copy)]
    [InlineData("uniq/i32", "x", PatternAcquisition.Move)]
    [InlineData("uniq/i32", "x@move", PatternAcquisition.Move)]
    public void OwnedBindingAcquisitionUsesTheCompleteStoredType(string type, string subject, PatternAcquisition acquisition)
    {
        // SPEC 15.1.6: a bare Place is shared-borrowed and its whole binding Copies that reference; the owned
        // subject needs x@move. A bare exclusive borrow value is Reborrowed exclusively, and a transferred one is
        // acquired as it is, so its whole binding transfers the uniq reference.
        var c = Parse($"func f(x: {type}) => match {subject}\n    let value => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var binding = Assert.Single(Plan(c).Positions);
        Assert.Equal(acquisition, binding.Acquisition);
        Assert.Equal(PatternAccessMode.Owned, binding.AccessMode);
        Assert.Equal(0, binding.ImplicitFollows);
    }

    // SPEC 15.1.6, 3.5.2 (F's C8 ruling 27, C-520 C3): the one binding still asking Copy, a whole-Subject binding of a Subject borrowed in
    // place or of a reference-typed temporary, has a reference layer's Type, whose Copy its Semantics decides: a bare Place `o`, a type
    // parameter's `t`, an object view and a raw pointer are read through `ref/` or are Copy (Copy); a stored `uniq/` is reborrowed and a
    // temporary `uniq/` moved (Move). All valid; the rows pin the domain ruling 27 leaves (behaviour unchanged: no fail-first).
    [Theory]
    [InlineData("func f(o: Option<i32>) -> ()\n    match o\n        let w => ()\n", PatternAcquisition.Copy)]
    [InlineData("func f(u: uniq/Option<i32>) -> ()\n    match u\n        let x => ()\n", PatternAcquisition.Move)]
    [InlineData("func f<T>(t: T) -> ()\n    match t\n        let w => ()\n", PatternAcquisition.Copy)]
    [InlineData("struct Node\n    var v: i32\nfunc f(n: objref/Node) -> ()\n    match n\n        let w => ()\n", PatternAcquisition.Copy)]
    [InlineData("func f(p: raw/i32) -> ()\n    match p\n        let w => ()\n", PatternAcquisition.Copy)]
    [InlineData("struct S\n    var v: i32\nfunc pick(a: uniq/S) -> uniq/S => a\nfunc f(s: uniq/S) -> ()\n    match pick(s)\n        let x => ()\n", PatternAcquisition.Move)]
    public void AWholeSubjectBindingOfABorrowedSubjectFollowsItsReferenceLayer(string source, PatternAcquisition acquisition)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var binding = Assert.Single(Plan(c).Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal((PatternAccessMode.Owned, false), (binding.AccessMode, binding.OwnedPart));
        Assert.Equal(acquisition, binding.Acquisition);
    }

    // SPEC 15.1.6, 8.14, 8.15.2 (G642, as F's C8 ruling 24 amends it for a transferred Subject): an owned part is bound by its complete
    // stored Type in each Semantics case; `value`, `(s/T, i32)`, a part of the temporary Subject, is transferred with its responsibility,
    // so it Moves in every case (owner or ref; owner or uniq) and asks no Copy (it Copied where every case Copies, else CopyOrMove). Both
    // programs are valid. A part that is itself a pair layer is no owned part (its mode is never ByValue), so it stays a borrow.
    [Theory]
    [InlineData("owner or ref", PatternAcquisition.Move)]
    [InlineData("owner or uniq", PatternAcquisition.Move)]
    public void AnOwnedPairPartOfATemporarySubjectMoves(string admitted, PatternAcquisition acquisition)
    {
        var c = Parse($"func f<s/T>(x: s/T) -> ()\n    s is {admitted}\n    T is Copy\n    match ((x@move, 1), 2)\n        (let value, _) => ()\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var binding = Assert.Single(Plan(c).Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal(PatternAccessMode.Owned, binding.AccessMode);
        Assert.Equal(acquisition, binding.Acquisition);
    }

    // SPEC 6.5.2 (G381): a stored Property's inferred Closure matches Cases, which are prepared before the headers that bind it (the Field
    // rows above, with their typed and local twins). A group's stored Property is static storage, a located limit for a Closure with or
    // without its written Type, and that limit is its one record (it was beside a false InvalidPattern_Kd, then a fault).
    [Theory]
    [InlineData("")]
    [InlineData(": (Option<i32>) -> i32")]
    public void AGroupClosureMatchingCasesKeepsItsStorageLimit(string written)
    {
        var record = Assert.Single(RawPointerSyntaxTest.Records("public group G\n    public let f" + written + " = " + OptionMatch));
        Assert.StartsWith("Unsupported_Kd: let f", record, StringComparison.Ordinal);
    }

    private static Compilation Parse(string source, string target = "x86_64-pc-windows-msvc")
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(TestDiagnostics.Of(c).Length == 0, string.Join("\n", TestDiagnostics.Of(c).Select(x => x.ToString())));
        return c;
    }

    private static MatchKoto[] Matches(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>().ToArray();

    private static BoundMatch Plan(Compilation c)
    {
        Assert.True(c.Binding.TryGetMatch(Assert.Single(Matches(c)), out var plan));
        return plan!;
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));

    // The records a Final Binding publishes for a program, as code and source text in result order.
    private static string Records(string source)
    {
        var c = Parse(source);
        c.Bind();
        c.Binding.ReportDiagnostics();
        return string.Join(" | ", TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Text}"));
    }

    // The records of an Application program through the shared check entry, as code and location (line:column) in result order.
    private static string LocatedCodes(string source)
        => string.Join(" | ", DiagnosticCorpus.LocatedRecords(source).Select(static x => string.Join(' ', x.Split(' ').Take(2))));

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void ProvisionalWarningsAreNotPublished()
        {
            var c = Parse("func f(x: i32) => match x\n    _ => ()\n    0 => ()");
            c.Binding.Bind(BindingMode.Provisional);
            c.Binding.ReportDiagnostics();
            Assert.Empty(TestDiagnostics.Of(c));
            Assert.True(c.Bind().IsComplete);
            c.Binding.ReportDiagnostics();
            c.Binding.ReportDiagnostics();
            Assert.Single(TestDiagnostics.Of(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(32)]
        [InlineData(128)]
        public void WarmBindingAndFlowReusePatternStorage(int count)
        {
            var source = string.Join('\n', Enumerable.Range(0, count).Select(i => $"func matchExample{i}(x: Option<(i32, bool)>) -> i32 => match x\n    .Some((let n, _)) => n\n    .None => 0"));
            var c = Parse(source);
            Assert.True(c.Bind().IsComplete, Describe(c));
            var match = Matches(c)[0];
            Assert.True(c.Binding.TryGetMatch(match, out var plan));
            var positions = plan!.Positions;
            var symbol = positions.Single(x => x.Kind == BoundPatternKind.Binding).BodySymbol;
            var valid = c.Analyze().IsVerified;
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                valid &= c.Analyze().IsVerified;
            }));
            Assert.True(valid, MinimalEmissionTest.Describe(c));
            Assert.True(c.Binding.Result.IsComplete, Describe(c));
            Assert.True(c.Binding.TryGetMatch(match, out var rebound));
            Assert.Same(plan, rebound);
            Assert.Same(positions, rebound!.Positions);
            Assert.Same(symbol, rebound.Positions.Single(x => x.Kind == BoundPatternKind.Binding).BodySymbol);
        }
    }
}

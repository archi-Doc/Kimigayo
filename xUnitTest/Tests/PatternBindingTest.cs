// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class PatternBindingTest
{
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
    public void SupportedPatternsBindAndCheckFlow(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.PendingBinding);
        Assert.True(flow.Issues.Count == 0, string.Join("\n", flow.Issues.Select(x => x.Message)));
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
    [InlineData("func f(x: unsafe/i32) => match x\n    0 => ()\n    _ => ()")]
    [InlineData("struct Data\nfunc f(x: obj/Data) => match x\n    () => ()\n    _ => ()")]
    public void InvalidPatternsSuppressCoverageCascades(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        var plan = Plan(c);
        Assert.Equal(MatchCoverageState.Invalid, plan.Coverage.State);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.DoesNotContain(flow.Issues, x => x.Message.Contains("exhaustive", StringComparison.Ordinal));
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
        Assert.Contains(Plan(c).Positions, x => x.ImplicitDerefs == layers);
    }

    [Theory]
    [InlineData("func f(x: Option<i32>) => match x\n    .None => ()", MatchCoverageReason.MissingCase)]
    [InlineData("func f(x: Option<bool>) => match x\n    .Some(true) => ()\n    .Some(false) => ()\n    .None => ()", MatchCoverageReason.WholePayloadRequired)]
    [InlineData("func f(x: (bool, bool)) => match x\n    (true, _) => ()\n    (false, true) => ()\n    (false, false) => ()", MatchCoverageReason.CatchAllRequired)]
    [InlineData("func f(x: i32) => match x\n    0 => ()", MatchCoverageReason.CatchAllRequired)]
    [InlineData("func f(x: bool) => match x\n    true => ()", MatchCoverageReason.BooleanValuesRequired)]
    public void CoverageUsesOnlyTheSpecifiedProofs(string source, MatchCoverageReason reason)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.NonExhaustive, Plan(c).Coverage.State);
        Assert.Equal(reason, Plan(c).Coverage.Reason);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
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
        Assert.Equal(2, c.Kotonoha.DiagnosticCollection.GetArray().Count(x => x.Entry.Name == nameof(DiagnosticCode.UnreachablePattern_Kd)));
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    [Fact]
    public void GuardedPatternsDoNotCoverAndGuardTransfersAreVisited()
    {
        var c = Parse("func f(x: bool) => match x\n    true if true => ()\n    false if yield () => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.NonExhaustive, Plan(c).Coverage.State);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        Assert.Contains(flow.Issues, x => x.Message.Contains("target", StringComparison.Ordinal));
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

    [Fact]
    public void InferredMatchResultAndGenericAcquisitionAreRetained()
    {
        var c = Parse("func f(x: bool)\n    let r = match x\n        true => 1\n        false => 2\nfunc generic<T>(x: Option<T>) => match x@move\n    .Some(let value) => ()\n    .None => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var selections = Matches(c);
        Assert.Equal("i32", selections[0].BoundType!.Name);
        Assert.True(c.Binding.TryGetMatch(selections[1], out var plan));
        Assert.Equal(PatternAcquisition.CopyOrMove, Assert.Single(plan!.Positions, p => p.Kind == BoundPatternKind.Binding).Acquisition);
    }

    [Fact]
    public void ValueOrZeroNowPassesMatchOwnershipVerification()
    {
        var c = Parse("func valueOrZero(value: Option<i32>) -> i32\n    return match value\n        .Some(let n) => n\n        .None => 0");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var result = c.Ownership.Analyze();
        Assert.True(result.IsVerified);
        Assert.Equal(0, result.UnsupportedCount);
    }

    [Theory]
    [InlineData("isize", "2147483647", true)]
    [InlineData("isize", "2147483648", false)]
    [InlineData("isize", "-2147483648", true)]
    [InlineData("isize", "-2147483649", false)]
    [InlineData("usize", "4294967295", true)]
    [InlineData("usize", "4294967296", false)]
    public void PointerSizedPatternsUsePreparedTarget(string type, string literal, bool valid)
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
        Assert.All(Walk(Plan(c).Syntax.Arms[0].Pattern), p => Assert.False(c.Binding.TryGetEnumConstruction(p, out _)));
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

    [Fact]
    public void ProvisionalWarningsAreNotPublished()
    {
        var c = Parse("func f(x: i32) => match x\n    _ => ()\n    0 => ()");
        c.Binding.Bind(BindingMode.Provisional);
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportDiagnostics();
        Assert.Single(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
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
        Assert.Equal(1, root.ImplicitDerefs);
        var binding = Assert.Single(plan.Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal(PatternAccessMode.Shared, binding.AccessMode);
        Assert.Equal(0, binding.ImplicitDerefs);
        Assert.Equal(PatternAcquisition.Borrow, binding.Acquisition);
        Assert.Equal(SemanticsKind.Ref, binding.BodySymbol!.Type!.Semantics);
        Assert.Same(BoundType.I32, binding.BodySymbol.Type.Components[0]);
        Assert.Equal(BindingState.Resolved, plan.Syntax.Arms[0].Body.BindingState);
    }

    [Fact]
    public void StoredReferenceBindingsRetainOriginsAndOwnedAccess()
    {
        var c = Parse("enum View {a}\n    Some(ref/i32 during a)\nfunc f(x: View)\n    origin x.a == static\n    match x@move\n        .Some(let r) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var binding = Assert.Single(Plan(c).Positions, p => p.Kind == BoundPatternKind.Binding);
        Assert.Equal(PatternAccessMode.Owned, binding.AccessMode);
        Assert.Equal(0, binding.ImplicitDerefs);
        Assert.Equal(PatternAcquisition.Copy, binding.Acquisition);
        Assert.NotNull(binding.MatchedType.Origin);
        Assert.Same(binding.MatchedType, binding.BodySymbol!.Type);
        Assert.Equal(OriginKind.Static, binding.MatchedType.Origin!.Kind);
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
        Assert.True(KotoHelper.Replace(match, oldPattern, newPattern));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.UnresolvedBinding_Kd && ReferenceEquals(i.Node, match.Arms[0].Body));
        Assert.DoesNotContain(plan.Positions, p => ReferenceEquals(p.Source, oldPattern));
        var newName = Assert.Single(Walk(newPattern).OfType<IdentifierNameKoto>(), n => n.IdentifierName == "m");
        // Reusing an old Pattern child must not resurrect its old arm scope or Symbol.
        var oldName = Assert.Single(Walk(oldPattern).OfType<IdentifierNameKoto>(), n => n.IdentifierName == "n");
        Assert.True(KotoHelper.Replace(newName.Parent!, newName, oldName));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("n", Assert.Single(plan.Positions, p => p.Kind == BoundPatternKind.Binding).BodySymbol!.Name);
        var function = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), f => !f.IsGenerated);
        var empty = Assert.Single(Walk(Parse("func g() => ()").Kotonoha.RootKoto).OfType<FunctionKoto>(), f => !f.IsGenerated);
        Assert.True(KotoHelper.Replace(function.Parent!, function, empty));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Binding.TryGetMatch(match, out _));
        Assert.False(plan.IsCurrent);
        Assert.Empty(plan.Positions);
    }

    [Theory]
    [InlineData("func f(x: bool) => match x\n    (_) => ()", MatchCoverageState.Exhaustive)]
    [InlineData("func f(x: bool) => match x\n    (_) if true => ()", MatchCoverageState.NonExhaustive)]
    public void SyntaxCoverageHonorsGroupingAndGuards(string source, MatchCoverageState expected)
    {
        var c = Parse(source);
        var flow = c.AnalyzeControlFlow();
        var match = Matches(c)[0];
        Assert.Equal(expected == MatchCoverageState.NonExhaustive, flow.Issues.Any(i => i.Message.Contains("exhaustive", StringComparison.Ordinal)));
        Assert.DoesNotContain(match.Arms[0].Guard!, flow.PendingBinding);
    }

    [Fact]
    public void GuardReturnTargetsFunctionAndSyntaxChecksUnsafeOperations()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true if return 1 => 2\n    false => 3");
        c.Bind();
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.IsType<FunctionKoto>(Assert.Single(flow.Targets, t => t.Key is ReturnKoto).Value);
        Assert.DoesNotContain(Matches(c)[0].Arms[0].Guard!, flow.PendingBinding);
        var unsafeGuard = Parse("func f(x: bool, p: unsafe/bool) => match x\n    _ if *p => ()");
        var syntaxFlow = unsafeGuard.AnalyzeControlFlow();
        Assert.Contains(syntaxFlow.Issues, i => i.Message.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(syntaxFlow.Issues, i => i.Message.Contains("exhaustive", StringComparison.Ordinal));
    }

    [Fact]
    public void AbruptSubjectDoesNotPropagateGuardTransfers()
    {
        var c = Parse("func f() -> i32\n    let result = work: do\n        match (return 1)\n            _ if (exit to work: 2) => ()\n            _ => ()\n        exit to work: 3\n    return result");
        c.Bind();
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        var match = Matches(c)[0];
        var guardExit = Assert.IsType<ExitKoto>(KotoHelper.UnwrapParentheses(match.Arms[0].Guard!));
        var target = Assert.IsType<DoKoto>(flow.Targets[guardExit]);
        Assert.False(flow.Nodes[match].CanCompleteNormally);
        Assert.False(flow.Nodes[target].CanCompleteNormally);
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
        Assert.Equal(0, binding.ImplicitDerefs);
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
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.PendingBinding);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            flow.Reanalyze(c.Kotonoha.RootKoto);
        }));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Binding.TryGetMatch(match, out var rebound));
        Assert.Same(plan, rebound);
        Assert.Same(positions, rebound!.Positions);
        Assert.Same(symbol, rebound.Positions.Single(x => x.Kind == BoundPatternKind.Binding).BodySymbol);
    }

    private static Compilation Parse(string source, string target = "x86_64-pc-windows-msvc")
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.ToString("source"))));
        return c;
    }

    private static MatchKoto[] Matches(Compilation c) => Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>().ToArray();

    private static BoundMatch Plan(Compilation c)
    {
        Assert.True(c.Binding.TryGetMatch(Assert.Single(Matches(c)), out var plan));
        return plan!;
    }

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}

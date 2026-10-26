// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class EnumBindingTest
{
    [Theory]
    [InlineData("let x = Message.Quit")]
    [InlineData("let x = Message.Write(\"text\")")]
    [InlineData("let x: Message = .Quit")]
    [InlineData("let x: Message = .Write(\"text\")")]
    [InlineData("func f() -> Message => .Write(\"text\")")]
    [InlineData("let x: Option<i32> = .Some(1)")]
    [InlineData("let x: Option<i32> = .None")]
    [InlineData("let x = Option<i32>.Some(1)")]
    [InlineData("let x = Option.Some(1)")]
    [InlineData("let x: Result<i32, string> = .Ok(1)")]
    [InlineData("let x: Result<i32, string> = .Err(\"text\")")]
    [InlineData("let x: ::Kimi.Option<i32> = ::Kimi.Option<i32>.Some(1)")]
    [InlineData("let x: Option<Option<i32>> = .Some(.Some(1))")]
    [InlineData("enum P<T>\n    Pair(T, (T, T))\nfunc f(a: i64)\n    let x = P.Pair(1, (a, a))")]
    [InlineData("enum P<T>\n    Pair(T, (T, T))\nfunc f(a: i64)\n    let y: P<i64> = P.Pair(1, (a, a))")]
    public void ConstructsQualifiedAndExpectedCases(string source)
    {
        var c = CompilationTestHelper.ParseSuccess("enum Message\n    Quit\n    Write(string)\n" + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Contains(KotoTree.Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
    }

    [Theory]
    [InlineData("let x = .None")]
    [InlineData("let x: Option<i32> = .Some")]
    [InlineData("let x: Option<i32> = .None()")]
    [InlineData("let x: Option<i32> = .Some()")]
    [InlineData("let x: Option<i32> = .Some(1, 2)")]
    [InlineData("let x: Option<i32> = .Some(value: 1)")]
    [InlineData("let x: Option<i32> = .Some(\"text\")")]
    [InlineData("let x: Option<i8> = .Some(128)")]
    [InlineData("let x: Option<i32> = Option<string>.Some(\"text\")")]
    [InlineData("let x = Option.None")]
    [InlineData("let x = Option<i32>.Some")]
    [InlineData("let x = Option<i32>.None()")]
    [InlineData("let x: Option<i32> = .Missing")]
    [InlineData("let x: i32 = .Some(1)")]
    [InlineData("var v: Option<i32>\nlet x = v.None")]
    [InlineData("var v: Option<i32>\nlet x = v.Some(1)")]
    public void RejectsInvalidConstruction(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
        foreach (var expression in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>())
        {
            Assert.False(c.Binding.TryGetEnumConstruction(expression, out _));
        }
    }

    [Fact]
    public void PayloadOperationsKeepOrderAndAcquisition()
    {
        var c = CompilationTestHelper.ParseSuccess("enum Pair<T>\n    Pair(T, string)\nfunc f<T>(a: T, b: string) -> Pair<T> => Pair<T>.Pair(a@move, b@move)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var invocation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.True(c.Binding.TryGetEnumConstruction(invocation, out var plan));
        Assert.Null(invocation.CallOf());
        Assert.Equal(0, plan!.Case.Ordinal);
        Assert.Equal(AcquisitionKind.CopyOrMove, plan.Acquisitions[0]);
        Assert.Equal(AcquisitionKind.Move, plan.Acquisitions[1]);
        for (var i = 0; i < 2; i++)
        {
            Assert.Same(invocation.ArgumentNodes[i], plan.PayloadOperations[i].Source);
            Assert.Equal(i, plan.PayloadOperations[i].ParameterIndex);
        }
    }

    [Fact]
    public void CoreOptionAndResultUseConditionalCopy()
    {
        // SPEC 22.1: Option is Copy when T is Copy, Result when T and E are Copy.
        var c = CompilationTestHelper.ParseSuccess("var a: Option<i32>\nvar b: Option<string>\nvar r: Result<i32, i32>\nvar s: Result<i32, string>\nvar t: Result<string, i32>");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().ToArray();
        ConstraintProof[] expected = [ConstraintProof.Proven, ConstraintProof.Refuted, ConstraintProof.Proven, ConstraintProof.Refuted, ConstraintProof.Refuted];
        Assert.Equal(expected, variables.Select(x => c.Binding.ProveCopy(x.TypeOf()!, x)));
    }

    [Theory]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\n    None\nfunc f<T>(x: ref/T) -> V<T>{result}\n    origin result.a == x\n    return V<T>.Some(x)")]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\n    None\nfunc f<T>(x: ref/T)\n    let v = V<T>.Some(x)")]
    [InlineData("func f<T>(x: ref/T) -> Option<ref/T during x> => .Some(x)")]
    [InlineData("func f<T>(x: uniq/T) -> Option<uniq/T during x> => .Some(x)")]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\nfunc f()\n    var n = 1\n    let v = V<i32>.Some(n)")]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\n    None\nfunc f<T>(x: ref/T) -> V<T>{result}\n    origin result.a == x\n    return .None")]
    public void PreservesPayloadOriginContracts(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).First(x => c.Binding.TryGetEnumConstruction(x, out _));
        Assert.True(c.Binding.TryGetEnumConstruction(use, out var plan));
        Assert.True(plan!.Type.OriginArguments.Count != 0 || plan.Type.Components[0].Origin is not null);
    }

    [Theory]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\n    None\nlet v = V<i32>.None")]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\nfunc f<T>(x: ref/T) -> V<T>{result}\n    origin result.a == static\n    return .Some(x)")]
    [InlineData("enum V\n    Some(ref/i32)")]
    [InlineData("enum E\n    A\n    A(i32)")]
    [InlineData("enum E\n    A\n    func A() => ()")]
    [InlineData("enum E\n    func A() => ()\n    A")]
    [InlineData("struct Hidden\npublic enum E\n    A(Hidden)")]
    [InlineData("enum E\n    A\nenum E\n    B")]
    [InlineData("enum E<T>\n    A(T)\nenum E<T>\n    B")]
    [InlineData("enum E<T>\n    T is Copy\n    A(T)\nlet x = E.A(\"text\")")]
    [InlineData("enum E<T>\n    A(T)\n    public func f() => ()\nE.f()")]
    public void RejectsInvalidDeclarationsAndOriginContracts(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
    }

    // SPEC 6.5.2, 3.5: a bare Non-Copy or Copy-unproven Place never Moves into a payload.
    [Theory]
    [InlineData("enum Pair<T>\n    Pair(T, string)\nfunc f<T>(a: T, b: string) -> Pair<T> => Pair<T>.Pair(a, b@move)")]
    [InlineData("enum Pair<T>\n    Pair(T, string)\nfunc f<T>(a: T, b: string) -> Pair<T> => Pair<T>.Pair(a@move, b)")]
    [InlineData("enum Message\n    Write(string)\nlet text = \"text\"\nlet x = Message.Write(text)")]
    public void BarePayloadPlacesRequireATransfer(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TransferRequired_Kd);
    }

    // SPEC 6.5.2, 3.5, 8.14 (PLAN G252, DECISIONS BU-45): a payload Place is acquired by its bare acquisition, so a pair layer Copies or
    // Reborrows by case, and only a case without a plan (an owner whose target is not proven Copy, the exclusive case through a shared
    // path) fails. Each row: Binding's errors.
    [Theory]
    [InlineData("value: s/T", "s is owner or uniq\n    T is Copy", "s/T", "value", "")]
    [InlineData("value: s/T", "s is ref or uniq", "s/T", "value", "")]
    [InlineData("value: s/T", "s is owner or uniq", "s/T", "value", "TransferRequired_Kd value")]
    [InlineData("x: ref/(s/T during a) during b", "s is ref or uniq", "s/T during a", "x@follow", "TransferRequired_Kd x@follow")]
    public void APairPayloadIsAcquiredByCase(string parameters, string constraints, string payloadType, string payload, string errors)
    {
        var source = $"func f<s/T>({parameters}) -> ()\n    {constraints}\n    let o: Option<{payloadType}> = .Some({payload})\n    _ = o@move\n";
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    [Theory]
    [InlineData("enum E<T>\n    Both(T, T)\nvar b: i8\nlet x = E.Both(1, b)")]
    [InlineData("enum E<T>\n    Both(T, T)\nvar b: i8\nlet x = E.Both(b, 1)")]
    [InlineData("enum E<T>\n    T is Copy\n    A(T)\nlet x = E.A(1)")]
    [InlineData("enum E\n    A\n    public func f() -> i32 => 1\nlet x = E.f()")]
    [InlineData("enum Option<T>\n    Mine(T)\nlet x = Option<i32>.Mine(1)\nlet y: ::Kimi.Option<i32> = .Some(1)")]
    public void SharesInferenceConstraintsAndMemberLookup(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void RebindInvalidatesConstructionAfterCaseCollision()
    {
        var c = CompilationTestHelper.ParseSuccess("enum E\n    A(i32)\nlet x = E.A(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.True(c.Binding.TryGetEnumConstruction(call, out var plan));
        var declaration = (EnumKoto)plan!.Case.Owner.Declaration;
        c.Kotonoha.CreateCodeContext().Parse(declaration, "func A() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(call, out _));
    }

    // SPEC 10.5.1, 6.5.2 (owner decision (1)): a contextual Case argument waits: it resolves only in the selected candidate's expected
    // enum, and is completed there without committing a loser; an unfitted literal, an aggregate literal's elements and nested Case
    // syntax take the payload Type, so a Tuple of literals fits as its literals do.
    [Theory]
    [InlineData("func f(x: Option<i32>) => ()\nf(.Some(1))", true)]
    [InlineData("func f(x: Option<i32>) => ()\nf(.None)", true)]
    [InlineData("func f(x: Option<Option<i8>>) => ()\nf(.Some(.Some(127)))", true)]
    [InlineData("func f(x: Option<i8>) => ()\nf(.Some(128))", false)]
    [InlineData("func f(x: Option<i32>) => ()\nfunc f(x: Option<i8>) => ()\nf(.None)", false)]
    [InlineData("func f<T>(x: Option<T>, y: T) => ()\nf(.Some(1), 2)", true)]
    [InlineData("func f<T>(x: T) => ()\nfunc f(x: Option<i32>) => ()\nf(.None)", false)]
    [InlineData("func f(x: Option<(i64, i64)>) => ()\nf(.Some((1, 2)))", true)]
    [InlineData("func f(x: Option<(i8, i64)>) => ()\nf(.Some((128, 2)))", false)]
    [InlineData("func f(x: Option<Option<(i64, i64)>>) => ()\nf(.Some(.Some((1, 2))))", true)]
    [InlineData("func f(x: Option<(i64, Option<i8>)>) => ()\nf(.Some((1, .None)))", true)]
    [InlineData("func f(x: Option<[2 of i64]>) => ()\nf(.Some([1, 2]))", true)]
    public void AContextualCaseArgumentWaitsForTheSelectedCandidate(string source, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.Equal(valid, c.Bind().IsComplete);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
        Assert.Equal(valid, call.CallOf() is not null);
        if (!valid)
        {
            Assert.DoesNotContain(KotoTree.Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
        }
    }

    // SPEC 10.5.1, 10.1 (owner decision (1)): a contextual Case is never probed per candidate, so candidates that differ only in its payload
    // Type are ambiguous even where only one payload Type would fit.
    [Theory]
    [InlineData("func f(x: Option<i8>) => ()\nfunc f(x: Option<i32>) => ()\nf(.Some(128))", "f(.Some(128))")]
    [InlineData("func f(x: Option<i32>) => ()\nfunc f(x: Option<i8>) => ()\nf(.Some(128))", "f(.Some(128))")]
    [InlineData("func f(x: Option<(i8, i8)>) => ()\nfunc f(x: Option<(i32, i32)>) => ()\nf(.Some((128, 1)))", "f(.Some((128, 1)))")]
    public void AContextualCaseIsNotProbedPerCandidate(string source, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.AmbiguousBinding_Kd), at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // SPEC 6.5.2 item 2, 10.8.1, 10.5.1: a Case construction takes the expected Type's slots first, then its payloads' evidence by kind,
    // element by element, then the Literal defaults; under the argument probe a construction with an unbound slot waits for its call.
    // An anonymous payload's written header is evidence at a callable position (SPEC 10.5.4), and a waiting callable at an F binds it and
    // is completed once every other slot is bound (SPEC 10.5).
    [Theory]
    [InlineData("enum Two<T>\n    Both(T, (T) -> i64)\nfunc f(a: i64)\n    let x = Two.Both(a, func (v) => v)")]
    [InlineData("enum Two<T>\n    Both(T, (T) -> i64)\nfunc f(a: i64)\n    let x = Two.Both(a, func (v: i64) -> i64 => v)")]
    [InlineData("enum F<T>\n    Pair(T, (T, (T) -> T))\nfunc f()\n    let x = F.Pair(1@i32, (2, func (v) => v))")]
    [InlineData("enum H<T>\n    Make((T) -> T)\nfunc f()\n    let x = H.Make(func (v: i64) -> i64 => v)")]
    [InlineData("enum F<T>\n    Pair(T, (T, (T) -> T))\nfunc takeF(value: F<i64>) => ()\nfunc f() => takeF(F.Pair(1, (2, func (v) => v)))")]
    [InlineData("func f()\n    let x = Option.Some(func (v: i64) -> i64 => v)")]
    [InlineData("enum W<T>\n    Wrap(T, i64)\nfunc f()\n    let x = W.Wrap(func (v: i64) -> i64 => v, 1)")]
    [InlineData("func inc(v: i64) -> i64 => v + 1\nfunc f()\n    let x = Option.Some(inc)")]
    [InlineData("func takeF(value: Option<(i64) -> i64>) => ()\nfunc f() => takeF(Option.Some(func (v) => v))")]
    [InlineData("enum Two<T>\n    Both(T, T)\nfunc f(w: (i64, i64)) -> Two<(i64, i64)> => Two.Both((1, 2), w)")]
    [InlineData("enum Two<T>\n    Both(T, T)\nfunc f(a: i64) -> Two<(i64, i64)> => Two.Both((1, 2), (a, a))")]
    [InlineData("enum P<T>\n    Pair(T, (T, T))\nfunc f(a: i64) -> P<i64> => P.Pair(1, (1, a))")]
    [InlineData("enum P<T>\n    Pair(T, (T, T))\nfunc identity<T>(value: T) -> T => value@move\nfunc f(a: (i64, i64)) -> P<i64> => P.Pair(identity(1), a)")]
    [InlineData("enum F<T>\n    Pair(T, (T, (T) -> T))\nfunc f() -> F<i32> => F.Pair(1, (2, func (v: i32) -> i32 => v))")]
    [InlineData("enum Q<T>\n    Items(T, [2 of T])\nfunc f(a: i64) -> Q<i64> => Q.Items(1, [a, a])")]
    [InlineData("func takeOpt(value: Option<i64>) => ()\nfunc f() => takeOpt(Option.Some(1))")]
    [InlineData("func takeOpt(value: Option<i64>) => ()\nfunc f() => takeOpt(Option.None)")]
    [InlineData("func f()\n    var values: Array<Option<i64>> = []\n    values@uniq.append(Option.Some(1))")]
    [InlineData("func identity<T>(value: T) -> T => value@move\nfunc f()\n    let o: Option<i64> = Option.Some(identity(1))")]
    public void ACaseConstructionSolvesItsSlotsFromItsPayloads(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Contains(KotoTree.Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
    }

    // SPEC 6.5.2, 10.8.1, 23.1: a payload that does not fit its decided payload Type is the construction's one Type mismatch, settled,
    // typed or Literal alike, as a call's misfit is the call's; a slot no evidence or default binds is the construction's own record, on
    // which its waiting payloads depend; a waiting callable's own failure is its record (SPEC 10.5).
    [Theory]
    [InlineData("enum P<T>\n    Pair(T, (T, T))\nfunc f(a: i64)\n    let x = P.Pair(1, [a, a])\n", nameof(DiagnosticCode.TypeMismatch_Kd), "P.Pair(1, [a, a])")]
    [InlineData("func f()\n    let x: Option<i64> = Option.Some(true)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Option.Some(true)")]
    [InlineData("func f() -> Option<i64> => .Some(true)\n", nameof(DiagnosticCode.TypeMismatch_Kd), ".Some(true)")]
    [InlineData("enum Two<T>\n    Both(T, T)\nfunc f() -> Two<i64> => Two.Both(\"a\", \"b\")\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Two.Both(\"a\", \"b\")")]
    [InlineData("func takeOpt(value: Option<i64>) => ()\nfunc f() => takeOpt(.Some(true))\n", nameof(DiagnosticCode.TypeMismatch_Kd), ".Some(true)")]
    [InlineData("enum Two<T>\n    Both(T, T)\nfunc f(b: bool)\n    let x: Two<i64> = Two.Both(b, 1)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Two.Both(b, 1)")]
    [InlineData("enum Two<T>\n    Both(T, T)\nfunc f() => Two.Both(1, 2.0)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Two.Both(1, 2.0)")]
    [InlineData("enum Two<T>\n    Both(T, (T) -> i64)\nfunc f(a: i64)\n    let x = Two.Both(a, func (v: bool) -> i64 => 1)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Two.Both(a, func (v: bool) -> i64 => 1)")]
    [InlineData("func identity<T>(value: T) -> T => value@move\nfunc f()\n    let x = Option.Some(identity)\n", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "identity")]
    [InlineData("func f()\n    let x = Option.Some(func (v) => v)\n", nameof(DiagnosticCode.UnresolvedBinding_Kd), "v")]
    [InlineData("func identity<T>(value: T) -> T => value@move\nfunc f()\n    let o = Option.Some(identity(1))\n", nameof(DiagnosticCode.UnresolvedBinding_Kd), "Option.Some(identity(1))")]
    [InlineData("func takeGeneric<T>(value: Option<T>) => ()\nfunc f() => takeGeneric(Option.Some(1))\n", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "takeGeneric(Option.Some(1))")]
    public void ACaseConstructionReportsItsOwnFailure(string source, string code, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source + "public func main() -> () => ()\n").Diagnostics);
        Assert.Equal((code, at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // SPEC 10.8, 3.8 (DECISIONS BU-2, R11): a Never element is no evidence, while a Never component of a typed part's Type is.
    [Theory]
    [InlineData("let x = P.Pair(1, ((return 1), a))")]
    [InlineData("let x = P.Pair(1, (a, (return 1)))")]
    public void ANeverElementOfAPayloadIsNoEvidence(string statement)
    {
        var c = CompilationTestHelper.ParseSuccess("enum P<T>\n    Pair(T, (T, T))\nfunc f(a: i64) -> i32\n    " + statement + "\n    return 0\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var construction = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(x => BoundEnumConstruction.Of(x)).Single(x => x is not null)!;
        Assert.Equal("i64", construction.Type.Components[0].Name);
    }

    [Fact]
    public void BindingConstructionDoesNotCertifyOwnership()
    {
        var c = CompilationTestHelper.ParseSuccess("let x: Option<i32> = .Some(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Analysis.IsVerified);
        Assert.True(c.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("    let value: i32")]
    [InlineData("    property value: i32 { get }")]
    [InlineData("    init() => ()")]
    [InlineData("    drop() => ()")]
    [InlineData("    struct Nested")]
    public void ForbiddenEnumMembersCannotPassParsingAndBinding(string member)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "enum E\n    A\n" + member);
        Assert.True(TestDiagnostics.Of(c).Length != 0 || !c.Bind().IsComplete);
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node} ({x.Node.GetType().Name}, parent {x.Node.Parent?.GetType().Name})"));

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmEnumBindingReusesIdentitiesAndPlanStorage()
        {
            var c = CompilationTestHelper.ParseSuccess("enum E<T>\n    Both(T, string)\n" + string.Join('\n', Enumerable.Range(0, 128).Select(i => $"let v{i} = E<i32>.Both({i}, \"x\")")));
            Assert.True(c.Bind().IsComplete, Describe(c));
            var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
            Assert.True(c.Binding.TryGetEnumConstruction(call, out var plan));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
            Assert.True(c.Binding.TryGetEnumConstruction(call, out var rebound));
            Assert.Same(plan, rebound);
        }

        [Fact]
        public void WarmPayloadEvidenceAndWaitingConstructionsAllocateNothing()
        {
            var c = CompilationTestHelper.ParseSuccess(
                "enum P<T>\n    Pair(T, (T, T))\nfunc takeOpt(value: Option<i64>) => ()\nfunc f(a: i64)\n    let x = P.Pair(1, (1, a))\n    takeOpt(Option.Some(1))");
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        }

        [Fact]
        public void WarmContextualConstructionPreservesBorrowOperations()
        {
            var c = CompilationTestHelper.ParseSuccess("func take(x: Option<ref/i32 during static>) => ()\nfunc f(x: ref/i32 during static)\n    take(.Some(x))");
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        }
    }
}

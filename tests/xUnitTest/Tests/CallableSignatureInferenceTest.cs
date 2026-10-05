// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class CallableSignatureInferenceTest(ITestOutputHelper output)
{
    private const string Len = "func len(n: ref/i32) -> i32 => n@follow\n";

    [Theory]
    [InlineData("Item", "func answer() -> i32 => 42\nlet value = consume(answer)")]
    [InlineData("Closure", "let answer = func [] () -> i32 => 42\nlet value = consume(answer)")]
    [InlineData("Common", "let answer: () -> i32 = func [] () -> i32 => 42\nlet value = consume(answer@move)")]
    public void IndependentlyTypedCallableResultsInferOuterSlots(string name, string expression)
    {
        var source = "func consume<T, F>(action: F) -> T\n    F is Callable<owner, () -> T>\n    return action@move()\n" + expression + "\nrequire value == 42 else => $abort(\"inference\")";
        ScalarEmissionTest.EmitFixture("CallableSignature" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Shared", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nrequire invoke(answer) == 42 else => $abort(\"shared\")")]
    [InlineData("Exclusive", "func invoke<T, F>(f: uniq/F) -> T\n    F is Callable<uniq, () -> T>\n    return f()\nvar f = answer\nrequire invoke(f@uniq) == 42 else => $abort(\"exclusive\")")]
    [InlineData("Input", "func input<T, F>(f: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 42\nfunc identity(value: i64) -> i32 => 42\nrequire input(identity) == 42 else => $abort(\"input\")")]
    [InlineData("Forward", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nfunc forward<G>(f: ref/G) -> i32\n    G is Callable<() -> i32>\n    return invoke(f)\nrequire forward(answer) == 42 else => $abort(\"forward\")")]
    [InlineData("Tuple", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nfunc pair() -> (i32, bool) => (42, true)\nlet result = invoke(pair)\nrequire result.0 == 42 and result.1 else => $abort(\"tuple\")")]
    public void SignaturesInferThroughEachReceiverAndGenericForwarding(string name, string body)
        => ScalarEmissionTest.EmitFixture("CallableSignature" + name, "func answer() -> i32 => 42\n" + body, string.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignatureEvidencePrecedesLiteralDefaultsInEitherOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, f: ref/F" : "f: ref/F, value: T";
        var invocation = reversed ? "accept(0, answer)" : "accept(answer, 0)";
        var source = "func answer() -> i64 => 42\nfunc need(value: i64) => require value == 42 else => $abort(\"type\")\nfunc accept<T, F>(" + parameters + ") -> T\n    F is Callable<() -> T>\n    return f()\nlet result = " + invocation + "\nneed(result)";
        ScalarEmissionTest.EmitFixture("CallableSignatureLiteral" + reversed, source, string.Empty);
    }

    [Fact]
    public void AnInferredNonCopyResultKeepsItsDestructionResponsibility()
    {
        const string Source = """
            struct Item
                drop => Console.writeLine("drop")
            func make() -> Item => Item.init()
            func consume<T, F>(action: F) -> T
                F is Callable<owner, () -> T>
                return action@move()
            let result = consume(make)
            Console.writeLine("alive")
            """;
        ScalarEmissionTest.EmitFixture("CallableSignatureNonCopy", Source, "alive\ndrop\n");
    }

    [Fact]
    public void ContainerArgumentsAreSubstitutedBeforeSignatureMatching()
    {
        const string Source = """
            struct Box<T>
                public func apply<R, F>(self: ref/Self, f: ref/F, value: T) -> R
                    F is Callable<(T) -> R>
                    return f(value@move)
            func widen(value: i32) -> i64 => value@i64
            func need(value: i64) => require value == 42 else => $abort("container")
            let box = Box<i32>.init()
            let result = box.apply(widen, 42)
            need(result)
            func flag(value: bool) -> i32 => if value => 7 else => 0
            let other = Box<bool>.init()
            let small = other.apply(flag, true)
            require small == 7 else => $abort("other instance")
            """;
        ScalarEmissionTest.EmitFixture("CallableSignatureContainer", Source, string.Empty);
    }

    [Fact]
    public void AFixedContainerInputCannotBeInferredFromTheCallable()
    {
        const string Source = """
            struct Box<T>
                public func accept<R, F>(self: ref/Self, f: ref/F)
                    F is Callable<(T) -> R>
                    return
            func flag(value: bool) -> i64 => 42
            let box = Box<i32>.init()
            box.accept(flag)
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains("(bool) -> i64", error.Note, StringComparison.Ordinal);
        Assert.Contains("(i32) -> R", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASignatureCanInferNeverWithoutExecutingItsCallable(bool common)
    {
        var source = "func stop() -> Never => $abort(\"unreachable\")\n" +
            (common ? "func accept<T>(f: () -> T) => ()\n" : "func accept<T, F>(f: ref/F)\n    F is Callable<() -> T>\n    return\n") +
            "accept(stop)\nConsole.writeLine(\"alive\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "accept");
        Assert.Same(BoundType.Never, call.BoundCall!.TypeArguments[0]);
        ScalarEmissionTest.EmitFixture("CallableSignatureNever" + common, source, "alive\n");
    }

    [Fact]
    public void SignatureMatchingDoesNotUseNeverSubtypingToHideConflictingEvidence()
    {
        const string Source = "func stop() -> Never => $abort(\"stop\")\nfunc accept<T, F>(f: ref/F, value: T)\n    F is Callable<() -> T>\n    return\nlet number: i32 = 7\naccept(stop, number)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains("() -> Never", error.Note, StringComparison.Ordinal);
        Assert.Contains("() -> i32", error.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealNeverExpressionStillSuppliesNoTypeEvidence()
    {
        const string Source = "func stop() -> Never => $abort(\"stop\")\nfunc keep<T>(first: T, second: T) -> T => first@move\nlet result = keep(7, stop())";
        var column = Source.IndexOf("$abort", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture("CallableSignatureNeverExpression", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_ABORT: stop\n");
    }

    [Fact]
    public void AnIndependentReferenceSuppliesEvidenceBeforeCommonFunctionErasure()
    {
        const string Source = "func answer() -> i64 => 42\nfunc make<T>(f: () -> T) -> T => f()\nfunc need(value: i64) => require value == 42 else => $abort(\"signature\")\nlet result = make(answer)\nneed(result)";
        ScalarEmissionTest.EmitFixture("CallableSignatureCommonReference", Source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingOrdinaryEvidenceRejectsInEitherArgumentOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, f: ref/F" : "f: ref/F, value: T";
        var invocation = reversed ? "accept(true, answer)" : "accept(answer, true)";
        var source = "func answer() -> i32 => 42\nfunc accept<T, F>(" + parameters + ")\n    F is Callable<() -> T>\n    return\n" + invocation;
        var path = Path.GetFullPath("callable-signature.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(invocation, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("none of 1 candidates applies", error.Label);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal("candidates", fact.Name);
        Assert.Equal("1", fact.Value);
        Assert.Equal("candidate", Assert.Single(error.Related!).Role);
        Assert.Contains("known call signature is () -> i32", error.Note, StringComparison.Ordinal);
        Assert.Contains("requires () -> bool", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Advice);
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(invocation, console.Text, StringComparison.Ordinal);
        Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void AnAnonymousWaitingBodyDoesNotInferItsOuterResult()
    {
        const string Source = "func consume<T, F>(action: F) -> T\n    F is Callable<owner, () -> T>\n    return action@move()\nlet value = consume(func [] () => 42)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("accept(missing, true)", "UnresolvedBinding_Kd")]
    [InlineData("accept(answer, true)\nlet independent: i32 = true", "NoApplicableOverload_Kd,TypeMismatch_Kd")]
    public void MissingPrerequisitesAndIndependentErrorsKeepTheirCauses(string body, string codes)
    {
        var source = "func answer() -> i32 => 42\nfunc accept<T, F>(f: ref/F, value: T)\n    F is Callable<() -> T>\n    return\n" + body;
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(codes.Split(','), TestDiagnostics.Of(c).Select(x => x.Code));
    }

    // SPEC 10.8: a known call signature over its own Origins supplies a slot's structure and Semantics. A per-call Origin never solves
    // the slot's Origin: it becomes an open region, a local region of the call that other evidence or an expected result fills (SPEC
    // 15.3.6), and the argument's per-call Origin is instantiated to it under SPEC 10.7.
    [Theory]
    [InlineData("ItemRefT", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    return 7\nrequire run(len) == 7 else => $abort(\"item\")")]
    [InlineData("ClosureRefT", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    return 7\nlet f = func (n: ref/i32) => n@follow\nrequire run(f@ref) == 7 else => $abort(\"closure\")")]
    [InlineData("CommonRefT", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    return 7\nlet f: (ref/i32) -> i32 = len\nrequire run(f@ref) == 7 else => $abort(\"common\")")]
    [InlineData("OpenItem", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\nrequire run(len) == 7 else => $abort(\"open item\")")]
    [InlineData("OpenCommon", "func runOnly<T>(action: (T) -> i32) -> i32 => 7\nrequire runOnly(len) == 7 else => $abort(\"open common\")")]
    [InlineData("OpenClosure", "func runOnly<T>(action: (T) -> i32) -> i32 => 7\nlet f = func (n: ref/i32) => n@follow\nrequire runOnly(f) == 7 else => $abort(\"open closure\")")]
    [InlineData("OpenResult", "func runOnly<T>(action: (T) -> i32) -> Option<T> => .None\nlet r = runOnly(len)\nlet s: Option<ref/i32> = r\nmatch s\n    .Some(let v) => $abort(\"open result\")\n    .None => ()")]
    [InlineData("FixedValue", "func keep<T>(value: T, action: (T) -> i32) -> i32 => action(value@move)\nlet x: i32 = 4\nrequire keep(x@ref, len) == 4 else => $abort(\"fixed value\")")]
    [InlineData("FixedClosure", "func keep<T>(value: T, action: (T) -> i32) -> i32 => action(value@move)\nlet x: i32 = 4\nlet f = func (n: ref/i32) => n@follow\nrequire keep(x@ref, f) == 4 else => $abort(\"fixed closure\")")]
    [InlineData("FixedCallable", "func keep<T, F>(value: T, action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return action(value@move)\nlet x: i32 = 4\nrequire keep(x@ref, len) == 4 else => $abort(\"fixed callable\")")]
    [InlineData("FixedHeader", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> ()>\n    return 7\nrequire run(func (n: ref/i32) => ()) == 7 else => $abort(\"header\")")]
    [InlineData("ExpectedResult", "func runOnly<T>(action: (T) -> i32) -> Option<T> => .None\nfunc get(x: ref/i32) -> Option<ref/i32 during x> => runOnly(len)\nlet a: i32 = 1\nmatch get(a@ref)\n    .Some(let v) => $abort(\"expected\")\n    .None => ()")]
    public void OriginBearingSignaturesInferSlots(string name, string body)
        => ScalarEmissionTest.EmitFixture("OriginSignatureEvidence" + name, Len + body, string.Empty);

    // SPEC 10.7, 15.3.6: a universal Origin of an Item that none of its inputs mentions, such as the result-only `s` of `constant() ->
    // ref/i32 during s`, is a call-time Origin that each call instantiates, so it is instantiated to the slot's open region and the
    // erasure and the Callable proof hold. `make(constant)` was accepted before the Item's Origins became open regions, and then was
    // NoApplicableOverload_Kd.
    [Theory]
    [InlineData("Erased", "func make<T>(f: () -> T) -> i32 => 7\nlet r = make(constant)")]
    [InlineData("Callable", "func make<T, F>(f: ref/F) -> i32\n    F is Callable<() -> T>\n    return 7\nlet r = make(constant)")]
    [InlineData("Input", "func make<T>(f: (ref/i32) -> T) -> i32 => 7\nlet r = make(constantOf)")]
    [InlineData("InputCallable", "func make<T, F>(f: ref/F) -> i32\n    F is Callable<(ref/i32) -> T>\n    return 7\nlet r = make(constantOf)")]
    public void AResultOnlyUniversalIsInstantiatedToTheOpenRegion(string name, string body)
        => ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceResultOnly" + name, "func constant() -> ref/i32 during s => $abort(\"never\")\nfunc constantOf(n: ref/i32) -> ref/i32 during s => $abort(\"never\")\n" + body + "\nrequire r == 7 else => $abort(\"result only\")", string.Empty);

    // SPEC 10.8: no argument fixes a slot first by traversal order; the value's Origin fills the open region in either order, so the
    // result keeps the Loan of x and a later write is a Loan conflict.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedEvidenceWinsInEitherOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, action: (T) -> i32" : "action: (T) -> i32, value: T";
        var arguments = reversed ? "x@ref, len" : "len, x@ref";
        var body = $"func keep<T>({parameters}) -> T => value@move\nvar x: i32 = 4\nlet r = keep({arguments})\n";
        var c = MinimalEmissionTest.Analyze(Len + body + "require r@follow == 4 else => $abort(\"order\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.BoundCall?.Target.Name == "keep");
        var slot = call.BoundCall!.TypeArguments[0]!;
        var borrow = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), static x => x.ToString() == "x@ref");
        Assert.Same(borrow.BoundType!.Origin, slot.Origin);
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceOrder" + reversed, Len + body + "require r@follow == 4 else => $abort(\"order\")", string.Empty);

        var source = "func len(n: ref/i32) -> i32 => n@follow\n" + body.Replace("var x", "    var x", StringComparison.Ordinal).Replace("let r", "    let r", StringComparison.Ordinal);
        source = source.Replace($"func keep<T>({parameters}) -> T => value@move\n", $"func keep<T>({parameters}) -> T => value@move\npublic func main() -> ()\n", StringComparison.Ordinal) + "    x = 5\n    Console.writeLine(\"\\(r@follow)\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "x = 5"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.8: the per-call Origins of a known call signature are no evidence for the callee's own Origins either, so `x` is solved
    // from the borrow in either argument order; `len` first gave NoApplicableOverload_Kd.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKnownSignatureIsNoEvidenceForTheCalleesOrigins(bool reversed)
    {
        var declaration = reversed ? "func apply(f: (ref/i32 during x) -> i32, x: ref/i32) -> i32 => f(x)\n" : "func apply(x: ref/i32, f: (ref/i32 during x) -> i32) -> i32 => f(x)\n";
        var call = reversed ? "apply(len, n@ref)" : "apply(n@ref, len)";
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceCallee" + reversed, Len + declaration + "let n: i32 = 2\nrequire " + call + " == 2 else => $abort(\"callee\")", string.Empty);
    }

    // SPEC 10.8, 15.3.6: with no other evidence, the slot keeps an open region that no Loan supports and that is never displayed.
    [Fact]
    public void APerCallOriginLeavesTheSlotOriginOpen()
    {
        var c = MinimalEmissionTest.Analyze(Len + "func runOnly<T>(action: (T) -> i32) -> Option<T> => .None\nlet r = runOnly(len)\nlet again = runOnly(len)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(static x => x.BoundCall?.Target.Name == "runOnly").ToArray();
        Assert.Equal(2, calls.Length);
        var first = calls[0].BoundCall!.TypeArguments[0]!;
        var second = calls[1].BoundCall!.TypeArguments[0]!;
        Assert.Equal((BoundTypeKind.Semantics, SemanticsKind.Ref), (first.Kind, first.Semantics));
        Assert.Equal((OriginKind.Inference, true), (first.Origin!.Kind, first.Origin.Open));
        Assert.NotSame(first.Origin, second.Origin); // Each call site has its own local region.
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));

        // SPEC 15.3.7, IMPL 21.3: Origin differences alone never duplicate generated code.
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ir, @"define internal \S+ @__kimi_generic_entry\d+\("));
    }

    // SPEC 15.6.5: an open region holds no Loans, so a later borrow fitted into it would lose its Loan. Such a fit is never accepted:
    // the assignment is rejected, and a call whose argument would store into it is not applicable.
    [Theory]
    [InlineData("    var z = runOnly(len)\n    z = .Some(y@ref)\n", nameof(DiagnosticCode.TypeMismatch_Kd), ".Some(y@ref)")]
    [InlineData("    let z = keepSome(len, .Some(y@ref))\n", nameof(DiagnosticCode.NoApplicableOverload_Kd), "keepSome(len, .Some(y@ref))")]
    public void AnOpenRegionTakesNoLaterBorrow(string body, string code, string text)
    {
        var source = "func runOnly<T>(action: (T) -> i32) -> Option<T> => .None\nfunc keepSome<T>(action: (T) -> i32, value: Option<T>) -> Option<T> => value@move\n" +
            "func len(n: ref/i32) -> i32 => n@follow\npublic func main() -> ()\n    var y: i32 = 3\n" + body + "    y = 4\n    match z\n        .Some(let v) => Console.writeLine(\"\\(v@follow)\")\n        .None => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.8, 15.3.6: when only the argument's per-call Origin would satisfy the slot, the Function value or Callable proof fails
    // under the call-time instantiation, since that Origin cannot outlive the call. Interim record until MissingOriginBinding_Kd.
    [Theory]
    [InlineData("func make<T>(action: (ref/i32) -> T) -> i32 => 7\nlet m = make(pick)", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func make<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/i32) -> T>\n    return 7\nlet m = make(pick)", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func make<T>(action: (ref/i32) -> T) -> i32 => 7\nlet m = make(func (n: ref/i32) => n)", nameof(DiagnosticCode.UnprovenConstraint_Kd))]
    public void OnlyAPerCallOriginWouldSatisfyTheSlot(string body, string code)
    {
        var c = MinimalEmissionTest.Analyze("func pick(n: ref/i32) -> ref/i32 => n\n" + body);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    // SPEC 8.6, 7.6.4: a closure result over its hidden environment receiver satisfies no Callable signature and no erasure, so it
    // never solves a slot; otherwise `s` would outlive the environment that `c@move` destroys. A captured shared reference keeps its
    // external Origin, so it is evidence, and the result keeps the Loan of x.
    [Theory]
    [InlineData("func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\n", "    let s = make(c@ref)\n    let d = c@move\n")]
    [InlineData("func make<T, F>(f: F) -> T\n    F is Callable<() -> T>\n    return f()\n", "    let s = make(c@move)\n")]
    [InlineData("func make<T>(f: () -> T) -> T => f()\n", "    let s = make(c@move)\n")]
    public void AnEnvironmentResultIsNoSlotEvidence(string declaration, string body)
    {
        var source = declaration + "public func main() -> ()\n    let values: Array<i32> = [11, 22]\n    let c = func [values@move] () => values@ref\n" + body + "    Console.writeLine(\"\\(s[0])\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), body.Trim().Split('\n')[0][8..]), (error.Code, Text(source, error.Span)));

        var valid = "func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\npublic func main() -> ()\n    var x: i32 = 5\n    let r = x@ref\n    let c = func [r] () => r\n    let s = make(c@ref)\n    x = 6\n    Console.writeLine(\"\\(s@follow)\")\n";
        var conflict = Assert.Single(DiagnosticCorpus.Check(valid).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "x = 6"), (conflict.Code, Text(valid, conflict.Span)));
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceCapturedReference", "func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nlet x: i32 = 5\nlet r = x@ref\nlet c = func [r] () => r\nlet s = make(c@ref)\nrequire s@follow == 5 else => $abort(\"captured\")", string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOriginSignatureEvidenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Len + "func keep<T>(value: T, action: (T) -> i32) -> i32 => action(value@move)\nfunc runOnly<T>(action: (T) -> i32) -> i32 => 7\nlet x: i32 = 4\nrequire keep(x@ref, len) == 4 and runOnly(len) == 7 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSignatureInferenceAndEmissionAllocateNothing()
    {
        const string Source = "func answer() -> i32 => 42\nfunc invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nrequire invoke(answer) == 42 else => $abort(\"warm\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}

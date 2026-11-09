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

    private const string WrapDeclarations = "func identity<T>(value: T) -> T => value@move\nfunc wrap<T>(v: T) -> Option<T> => .Some(v@move)\nfunc wrap2<T>(v: (T, i32)) -> Option<T> => .None\nfunc twice(v: i64) -> i64 => v\nfunc show(value: i32) -> () => ()\nfunc show<T>(value: T) -> () => ()\nfunc k() -> i32\n";

    private const string NeverDeclarations = "func stopAt(x: i32) -> Never => $abort(\"stop\")\nfunc stopB(x: bool) -> Never => $abort(\"stop\")\nfunc h(f: (i32) -> i32) -> i32 => f(1)\nfunc cb<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(1)\n";

    private const string ItemDeclarations = "func twice(v: i64) -> i64 => v\nfunc g<T>(x: T) -> i32 => 0\nfunc k(c1: bool, c2: bool) -> i32\n";

    // A closure whose result borrows its hidden environment receiver (SPEC 8.10, 7.7.4).
    private const string EnvironmentClosure = "public func main() -> ()\n    let values: Array<i32> = [11, 22]\n    let c = func [values@move] () => values@ref\n";

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallOf()?.Target.Name == "accept");
        Assert.Same(BoundType.Never, call.CallOf()!.TypeArguments[0]);
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
        Assert.False(c.Generate(TextWriter.Null));
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
        Assert.False(c.Generate(TextWriter.Null));
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

    // A universal bounded by an input is not result-only: this separate signature inference boundary remains located.
    [Theory]
    [InlineData("    let r = makeOf(bounded)\n    Console.writeLine(\"\\(r@follow)\")\n", nameof(DiagnosticCode.NoApplicableOverload_Kd), "makeOf(bounded)")]
    public void AResultOnlyInstantiationKeepsItsLimits(string body, string code, string text)
    {
        var source = "func constant() -> ref/i32 during s => $abort(\"never\")\nfunc bounded(n: ref/i32) -> ref/i32 during s\n    origin n outlives s\n    return n\n" +
            "func make<T>(f: () -> T) -> T => f()\nfunc makeOf<T>(f: (ref/i32) -> T) -> T => $abort(\"never\")\npublic func main() -> ()\n" + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text), (error.Code, Text(source, error.Span)));
    }

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.CallOf()?.Target.Name == "keep");
        var slot = call.CallOf()!.TypeArguments[0]!;
        var borrow = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), static x => x.ToString() == "x@ref");
        Assert.Same(borrow.TypeOf()!.Origin, slot.Origin);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "runOnly").ToArray();
        Assert.Equal(2, calls.Length);
        var first = calls[0].CallOf()!.TypeArguments[0]!;
        var second = calls[1].CallOf()!.TypeArguments[0]!;
        Assert.Equal((BoundTypeKind.Semantics, SemanticsKind.Ref), (first.Kind, first.Semantics));
        Assert.Equal((OriginKind.Inference, true), (first.Origin!.Kind, first.Origin.Open));
        Assert.NotSame(first.Origin, second.Origin); // Each call site has its own local region.
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));

        // SPEC 15.3.7, IMPL 21.3: Origin differences alone never duplicate generated code: both calls call the one instance.
        var startup = GeneratedIr.Parse(CompilationTestHelper.WriteIr(c)).Startup;
        var entries = startup.Instructions.Where(static x => x.Callee?.Signature == "internal void (ptr, ptr)").ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Same(entries[0].Callee, entries[1].Callee);
    }

    // Local-region inference keeps the actual Loan inserted after a result-only call or at a generic argument.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOpenRegionRetainsLaterBorrows(bool argument)
    {
        const string Declarations = "func runOnly<T>(action: (T) -> i32) -> Option<T> => .None\nfunc keepSome<T>(action: (T) -> i32, value: Option<T>) -> Option<T> => value@move\nfunc len(n: ref/i32) -> i32 => n@follow\n";
        var body = argument ? "    let z = keepSome(len, .Some(y@ref))\n" : "    var z = runOnly(len)\n    z = .Some(y@ref)\n";
        var prefix = Declarations + "public func main() -> ()\n    var y: i32 = 3\n" + body;
        const string Use = "    match z\n        .Some(let v) => require v@follow == 3 else => $abort(\"value\")\n        .None => $abort(\"none\")\n";
        var source = prefix + "    y = 4\n" + Use;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "y = 4"), (error.Code, Text(source, error.Span)));
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceLaterBorrow" + argument, prefix + Use + "    y = 4\n", string.Empty);
    }

    [Fact]
    public void AResultOnlyInstantiationAllowsLocalReassignment()
    {
        const string Source = "func constant() -> ref/i32 during s => $abort(\"never\")\nfunc make<T>(f: () -> T) -> T => f()\npublic func main()\n    var r = make(constant)\n    let local: i32 = 3\n    r = local@ref\n    require r@follow == 3 else => $abort(\"value\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // SPEC 15.3.6, 10.8: when only the argument's own per-call Origin would satisfy the slot (the Function value or Callable proof holds
    // once that Origin stands in for the slot's open region, and fails otherwise), the candidate stays applicable and the selected call
    // is MissingOriginBinding_Kd at the call, whose Reason names the argument and the slot, with the argument related. It was
    // NoApplicableOverload_Kd. A Function Item value and a concrete Closure value give the same record.
    [Theory]
    [InlineData("func make<T>(action: (ref/i32) -> T) -> i32 => 7\n", "make(pick)", "pick")]
    [InlineData("func make<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/i32) -> T>\n    return 7\n", "make(pick)", "pick")]
    [InlineData("func make<T>(action: (ref/i32) -> T) -> i32 => 7\n", "make(f)", "f")]
    [InlineData("func make<T>(action: (ref/i32) -> T) -> i32 => 7\n", "make(g)", "g")]
    public void OnlyAPerCallOriginWouldSatisfyTheSlot(string declaration, string call, string argument)
    {
        var source = "func pick(n: ref/i32) -> ref/i32 => n\n" + declaration + "public func main() -> ()\n    let f = pick\n    let g = func (n: ref/i32) => n\n    let m = " + call + "\n    Console.writeLine(\"\\(m)\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.MissingOriginBinding_Kd), call, $"only a per-call Origin of {argument} would satisfy T"), (error.Code, Text(source, error.Span), error.Label));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("argument", argument), (related.Role, Text(source, related.Span)));
    }

    // SPEC 10.8, 10.6: a header that omits its result supplies no evidence for T, which only the waiting body could fix; that is the
    // inference-boundary record at the call, relating the anonymous function, not a per-call Origin.
    [Fact]
    public void AnOmittedHeaderResultLeavesTheSlotUnbound()
    {
        var c = MinimalEmissionTest.Analyze("func make<T>(action: (ref/i32) -> T) -> i32 => 7\nlet m = make(func (n: ref/i32) => n)");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), "make(func (n: ref/i32) => n)"), (error.Code, error.Text));
    }

    // SPEC 15.3.6: other Constraints still decide applicability, so a slot that the stand-in fails another Constraint for stays
    // inapplicable; a signature whose input also holds the slot, or whose result needs no per-call Origin, satisfies it.
    [Fact]
    public void APerCallStandInDecidesNothingElse()
    {
        var source = "func pick(n: ref/i32) -> ref/i32 => n\nfunc owned<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/i32) -> T>\n    T is Owned\n    return 7\npublic func main() -> ()\n    let c = owned(pick)\n";
        var owned = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), "owned(pick)"), (owned.Code, Text(source, owned.Span)));
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidencePerCallValid", Len + "func pick(n: ref/i32) -> ref/i32 => n\nfunc same<T>(action: (T) -> T) -> i32 => 7\nfunc make<T>(action: (ref/i32) -> T) -> i32 => 7\nrequire same(pick) + make(len) == 14 else => $abort(\"valid\")", string.Empty);
    }

    // SPEC 10.7, 10.8: a required input over a fixed Origin, such as the callee's `x` of `(ref/i32 during x) -> T`, instantiates the
    // argument's per-call input to that Origin, which then solves the slot. It is no per-call Origin, so nothing is reported, in either
    // argument order and for a Function Item, a Function Item value, a Closure value and a Callable proof alike (these were a Language
    // MissingOriginBinding_Kd claiming that only a per-call Origin of pick would satisfy T).
    [Theory]
    [InlineData("Value", "func make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nlet r = make(n@ref, pick)")]
    [InlineData("Reversed", "func make<T>(action: (ref/i32 during x) -> T, v: ref/i32 during x) -> T => action(v)\nlet r = make(pick, n@ref)")]
    [InlineData("Item", "func make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nlet f = pick\nlet r = make(n@ref, f)")]
    [InlineData("Closure", "func make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nlet c = func (k: ref/i32) => k\nlet r = make(n@ref, c)")]
    [InlineData("Callable", "func make<T, U, F>(v: T, f: ref/F) -> U\n    F is Callable<(T) -> U>\n    return f(v@move)\nlet r = make(n@ref, pick)")]
    [InlineData("Returned", "func make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nfunc g(p: ref/i32) -> ref/i32 during p => make(p, pick)\nlet r = g(n@ref)")]
    public void AFixedRequiredInputInstantiatesThePerCallInput(string name, string body)
        => ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceFixedInput" + name, "func pick(n: ref/i32) -> ref/i32 => n\nlet n: i32 = 6\n" + body + "\nrequire r@follow == 6 else => $abort(\"fixed input\")", string.Empty);

    // A per-call input beside the fixed one still needs a per-call Origin, and an instantiation never lets the result escape.
    [Fact]
    public void AFixedRequiredInputDecidesNothingElse()
    {
        var mixed = "func make<T>(v: ref/i32 during x, action: (ref/i32 during x, ref/i32) -> T) -> i32 => 1\nfunc both(a: ref/i32, b: ref/i32) -> ref/i32 => a\npublic func main() -> ()\n    let n: i32 = 6\n    let r = make(n@ref, both)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(mixed).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.MissingOriginBinding_Kd), "make(n@ref, both)", "only a per-call Origin of both would satisfy T"), (error.Code, Text(mixed, error.Span), error.Label));

        var escape = "func pick(n: ref/i32) -> ref/i32 => n\nfunc make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nfunc g() -> ref/i32 during static\n    let local: i32 = 6\n    return make(local@ref, pick)\n" +
            "func h(p: ref/i32, q: ref/i32) -> ref/i32 during q => make(p, pick)\npublic func main() -> ()\n    Console.writeLine(\"\\(g()@follow)\")\n";
        var rejected = DiagnosticCorpus.Check(escape);
        Assert.False(rejected.Accepted);
        Assert.Equal(["make(local@ref, pick)", "make(p, pick)"], rejected.Diagnostics.Select(x => Text(escape, x.Span)));
    }

    // SPEC 23.1: the record and its related argument reach the CLI, the language server and JSON.
    [Fact]
    public void EveryOutputCarriesThePerCallOriginRecord()
    {
        var source = "func pick(n: ref/i32) -> ref/i32 => n\nfunc make<T>(action: (ref/i32) -> T) -> i32 => 7\npublic func main() -> ()\n    let m = make(pick)\n    Console.writeLine(\"\\(m)\")\n";
        var check = DiagnosticCorpus.Check(source);
        var record = Assert.Single(check.Diagnostics);
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("MissingOriginBinding_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("only a per-call Origin of pick would satisfy T", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = argument: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var withRelated = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal(record.Related![0].Range, Assert.Single(withRelated.RelatedInformation!).Location.Range);

        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("only a per-call Origin of pick would satisfy T", json, StringComparison.Ordinal);
        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 8.10, 7.7.4: a closure result over its hidden environment receiver satisfies no Callable signature and no erasure, so it
    // never solves a slot; otherwise `s` would outlive the environment that `c@move` destroys. A captured shared reference keeps its
    // external Origin, so it is evidence, and the result keeps the Loan of x.
    [Theory]
    [InlineData("func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\n", "    let s = make(c@ref)\n    let d = c@move\n")]
    [InlineData("func make<T, F>(f: F) -> T\n    F is Callable<() -> T>\n    return f()\n", "    let s = make(c@move)\n")]
    [InlineData("func make<T>(f: () -> T) -> T => f()\n", "    let s = make(c@move)\n")]
    public void AnEnvironmentResultIsNoSlotEvidence(string declaration, string body)
    {
        var source = declaration + EnvironmentClosure + body + "    Console.writeLine(\"\\(s[0])\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        var call = body.Trim().Split('\n')[0][8..];
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), call), (error.Code, Text(source, error.Span)));

        // DECISIONS BU-86: the record carries the judgment that left the candidate pending, the argument and the slot.
        Assert.Equal("The result of c over its environment is no evidence for T", error.Note);
        var argument = Assert.Single(error.Related!);
        Assert.Equal(("argument", call[5..^1]), (argument.Role, Text(source, argument.Span)));

        var valid = "func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\npublic func main() -> ()\n    var x: i32 = 5\n    let r = x@ref\n    let c = func [r] () => r\n    let s = make(c@ref)\n    x = 6\n    Console.writeLine(\"\\(s@follow)\")\n";
        var conflict = Assert.Single(DiagnosticCorpus.Check(valid).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "x = 6"), (conflict.Code, Text(valid, conflict.Span)));
        ScalarEmissionTest.EmitFixture("OriginSignatureEvidenceCapturedReference", "func make<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nlet x: i32 = 5\nlet r = x@ref\nlet c = func [r] () => r\nlet s = make(c@ref)\nrequire s@follow == 5 else => $abort(\"captured\")", string.Empty);
    }

    // DECISIONS BU-86: a result whose callee Origin no argument substitutes (`.None` is no evidence for s) no longer leaves the candidate
    // Pending: s is a call region (SPEC 15.6.4 step 1; DECISIONS BU-104's twelfth-amendment paragraph), so the record is the Callable
    // Constraint's own, with the call's Note and the Constraint related, and still carries no environment judgment.
    [Fact]
    public void AnUnsubstitutedResultOriginCarriesNoEnvironmentJudgment()
    {
        var source = "func make<T, F>(f: ref/F, o: Option<ref/i32 during s>) -> Option<ref/i32 during s>\n    F is Callable<() -> T>\n    return o@move\npublic func main() -> ()\n    let values: Array<i32> = [11, 22]\n    let c = func [values@move] () => values@ref\n    let r = make(c@ref, .None)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(
            (nameof(DiagnosticCode.UnprovenConstraint_Kd), "make(c@ref, .None)", "Call make requires F is Callable<() -> T>; this condition cannot be proven under the supplied bindings"),
            (error.Code, Text(source, error.Span), error.Note));
        var constraint = Assert.Single(error.Related!);
        Assert.Equal(("constraint", "F is Callable<() -> T>"), (constraint.Role, Text(source, constraint.Span)));
    }

    // DECISIONS BU-86 (2026-11-03): the environment judgment decides only for a slot that no evidence binds (SPEC 10.8). Once another
    // argument (`n: i32`) or the expected result binds T, the closure's result part is matched by no inference; the Callable Constraint
    // at F judges it and refutes it (SPEC 8.10), so the candidate is inapplicable whatever position stays open (U's Literal).
    [Theory]
    [InlineData("func make<T, U, F>(f: ref/F, t: T, u: U) -> T\n    F is Callable<() -> T>\n    return t@move\n", "    let n: i32 = 1\n    let r = make(c@ref, n, 2)\n")]
    [InlineData("func make<T, U, F>(f: ref/F, u: U) -> T\n    F is Callable<() -> T>\n    return $abort(\"x\")\n", "    let r: i32 = make(c@ref, 2)\n")]
    public void AnEnvironmentResultDecidesOnlyAnUnsolvedSlot(string declaration, string body)
    {
        var source = declaration + EnvironmentClosure + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        var call = body[(body.LastIndexOf("= ", StringComparison.Ordinal) + 2)..^1];
        Assert.Equal(
            (nameof(DiagnosticCode.NoApplicableOverload_Kd), call, "make requires F is Callable<() -> T>; this condition is refuted for closure () -> ref/Array<i32>"),
            (error.Code, Text(source, error.Span), error.Note));
    }

    // SPEC 10.8, 8.10: with T bound by a reference to another place, the Callable proof decides; only its Origin part is Unknown, so the
    // record is that proof's own, at the argument.
    [Fact]
    public void ABoundSlotsReferenceIsJudgedByTheCallableProof()
    {
        var source = "func make<T, U, F>(f: ref/F, t: T, u: U) -> T\n    F is Callable<() -> T>\n    return t@move\n" + EnvironmentClosure +
            "    let w: Array<i32> = [1]\n    let n = w@ref\n    let r = make(c@ref, n, 2)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "c@ref"), (error.Code, Text(source, error.Span)));
        Assert.StartsWith("F is Callable<() -> T> is not proven", error.Note, StringComparison.Ordinal);
    }

    // SPEC 10.8, 10.1 steps 4-5: when another argument or the expected result binds T, the environment result is no evidence and the
    // candidate is judged by its proof: refuted (SPEC 8.10), so the overload applies (rows 1-2), or proven for a closure whose result is
    // not over its environment (row 3).
    [Theory]
    [InlineData("func make<T, U, F>(f: ref/F, t: T, u: U) -> i32\n    F is Callable<() -> T>\n    return 1\nfunc make<F>(f: ref/F, t: i32, u: i64) -> i32\n    return 2\n" + EnvironmentClosure, "    let n: i32 = 1\n    let r = make(c@ref, n, 2)\n")]
    [InlineData("func make<T, U, F>(f: ref/F, u: U) -> T\n    F is Callable<() -> T>\n    return $abort(\"x\")\nfunc make<F>(f: ref/F, u: i64) -> i32\n    return 2\n" + EnvironmentClosure, "    let r: i32 = make(c@ref, 2)\n")]
    [InlineData("func make<T, U, F>(f: ref/F, t: T, u: U) -> T\n    F is Callable<() -> T>\n    return t@move\npublic func main() -> ()\n    let c = func [] () => 5\n", "    let n: i32 = 1\n    let r = make(c@ref, n, 2)\n")]
    public void ABoundEnvironmentSlotLeavesTheCandidateToItsProof(string declarations, string body)
        => Assert.Empty(DiagnosticCorpus.Check(declarations + body).Diagnostics);

    // SPEC 10.8 (traversal order never chooses a solution; DECISIONS BU-86): the environment judgment that decides is that of the first
    // unsolved slot in slot order, whatever the order of the arguments. T is bound by n, U only by e's environment result, so each order
    // twin, with or without an overload, is pending on U.
    [Theory]
    [InlineData("f: ref/F, g: ref/G", "c@ref, e@ref", false)]
    [InlineData("g: ref/G, f: ref/F", "e@ref, c@ref", false)]
    [InlineData("f: ref/F, g: ref/G", "c@ref, e@ref", true)]
    [InlineData("g: ref/G, f: ref/F", "e@ref, c@ref", true)]
    public void TheEnvironmentJudgmentIsChosenBySlotOrder(string closures, string arguments, bool overload)
    {
        var source = "func make<T, U, V, F, G>(" + closures + ", t: T, v: V) -> i32\n    F is Callable<() -> T>\n    G is Callable<() -> U>\n    return 1\n" +
            (overload ? "func make<F, G>(" + closures + ", t: i32, v: i64) -> i32\n    return 2\n" : string.Empty) + EnvironmentClosure +
            "    let w: Array<i32> = [1]\n    let e = func [w@move] () => w@ref\n    let n: i32 = 1\n    let r = make(" + arguments + ", n, 2)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(
            (nameof(DiagnosticCode.UnprovenConstraint_Kd), "make(" + arguments + ", n, 2)", "The result of e over its environment is no evidence for U"),
            (error.Code, Text(source, error.Span), error.Note));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOriginSignatureEvidenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Len + "func keep<T>(value: T, action: (T) -> i32) -> i32 => action(value@move)\nfunc runOnly<T>(action: (T) -> i32) -> i32 => 7\nlet x: i32 = 4\nrequire keep(x@ref, len) == 4 and runOnly(len) == 7 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // The fixed-input instantiation and the per-call stand-in rechecks run only when a fit or a Callable proof fails as is; a warm rebind
    // of programs that take them, including a losing overload whose Callable proof needs the stand-in, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFixedInputInstantiationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func pick(n: ref/i32) -> ref/i32 => n\nfunc make<T>(v: ref/i32 during x, action: (ref/i32 during x) -> T) -> T => action(v)\nfunc make2<T, U, F>(v: T, f: ref/F) -> U\n    F is Callable<(T) -> U>\n    return f(v@move)\n" +
            "func rank<F>(f: ref/F, k: i64) -> i32\n    F is Callable<(i32) -> i32>\n    return 1\nfunc rank<F>(f: ref/F, k: i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    return 2\nlet n: i32 = 6\n" +
            "require make(n@ref, pick)@follow + make2(n@ref, pick)@follow + rank(pick, 1) == 14 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
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
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // A row is (unit, form, program, records). Its verdict comes from SPEC, never from a build: records is empty for a valid program, which
    // also generates, and otherwise lists each expected Error as `Code: source line` (see CompilationTestHelper.AssertRecordsAtLines). unit
    // guard: the row runs today and must keep passing. Any other unit is the plan's unit that makes the row pass: the row is Explicit
    // until that unit lands and removes the marker. `dotnet xUnitTest.dll -class <class> -explicit only` runs those.
    [Theory]
    // SPEC 10.5.3, 10.5.1, 10.8 (PLAN G152): a reference that needs S waits, and the expected result gives its position S; without S it is an error. One
    // that does not need S is independently typable (g152i).
    [InlineData("I2", "g152o", WrapDeclarations + "    let o: Option<(i64) -> i64> = wrap(identity)\n    return 0\n", "", Explicit = true)]
    [InlineData("I2", "g152ov", WrapDeclarations + "    let o: Option<(i32) -> ()> = wrap(show)\n    return 0\n", "", Explicit = true)]
    [InlineData("guard", "g152t", WrapDeclarations + "    let o: Option<(i64) -> i64> = wrap2((identity, 1))\n    return 0\n", "")]
    [InlineData("guard", "g152on", WrapDeclarations + "    let o = wrap(identity)\n    return 0\n", "*: let o = wrap(identity) | ...")]
    [InlineData("guard", "g152tn", WrapDeclarations + "    let o = wrap2((identity, 1))\n    return 0\n", "*: let o = wrap2((identity, 1)) | ...")]
    [InlineData("guard", "g152bad", WrapDeclarations + "    let o: Option<(i64) -> bool> = wrap(identity)\n    return 0\n", "*: let o: Option<(i64) -> bool> = wrap(identity) | ...")]
    [InlineData("guard", "g152i", WrapDeclarations + "    let o = wrap(twice)\n    return 0\n", "")]

    // SPEC 10.7 (PLAN G159 (3)): an implementation result R fits Q when R <: Q or R is Never, for erasure and Callable alike; it does not bridge a
    // parameter (n3b...) and a common Function Type relates only by subtyping (n3suba; SPEC 3.8's `let` form is NeverContinuationTest's).
    [InlineData("guard", "n3item", NeverDeclarations + "func k() -> i32\n    let f: (i32) -> i32 = stopAt\n    return 0\n", "")]
    [InlineData("I3", "n3arg", NeverDeclarations + "func k() -> i32\n    return h(stopAt)\n", "", Explicit = true)]
    [InlineData("I3", "n3cl", NeverDeclarations + "func k() -> i32\n    let f: (i32) -> i32 = func [] (x: i32) -> Never => $abort(\"no\")\n    return 0\n", "", Explicit = true)]
    [InlineData("I3", "n3cla", NeverDeclarations + "func k() -> i32\n    return h(func [] (x: i32) -> Never => $abort(\"no\"))\n", "", Explicit = true)]
    [InlineData("I3", "n3cb", NeverDeclarations + "func k() -> i32\n    return cb(stopAt)\n", "", Explicit = true)]
    [InlineData("I3", "n3cbv", NeverDeclarations + "func k() -> i32\n    let c = func [] (x: i32) -> Never => $abort(\"no\")\n    return cb(c)\n", "", Explicit = true)]
    [InlineData("I3", "n3cbl", NeverDeclarations + "func k() -> i32\n    return cb(func [] (x: i32) -> Never => $abort(\"no\"))\n", "", Explicit = true)]
    [InlineData("guard", "n3b", NeverDeclarations + "func k() -> i32\n    return h(stopB)\n", "*: return h(stopB) | ...")]
    [InlineData("guard", "n3bl", NeverDeclarations + "func k() -> i32\n    let f: (i32) -> i32 = stopB\n    return 0\n", "*: let f: (i32) -> i32 = stopB | ...")]
    [InlineData("guard", "n3bc", NeverDeclarations + "func k() -> i32\n    let f: (i32) -> i32 = func [] (x: bool) -> Never => $abort(\"no\")\n    return 0\n", "*: let f: (i32) -> i32 = func [] (x: bool) -> Never => $abort(\"no\") | ...")]
    [InlineData("guard", "n3cbb", NeverDeclarations + "func k() -> i32\n    return cb(stopB)\n", "*: return cb(stopB) | ...")]
    [InlineData("guard", "n3suba", NeverDeclarations + "func f(handler: (i32) -> Never) -> i32\n    return h(handler@move)\n", "*: return h(handler@move) | ...")]
    public void AReferencesAndANeverResultsFitFollowSignatureCompatibility(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 10.8, 10.2, 7.7.4 (PLAN G356): an explicit Type argument fixes the parameter, so an Item at it is erased, alone and through a join; its
    // result-position forms it1 and it2 wait for PLAN's SPEC reading.
    [InlineData("I3", "gt1", ItemDeclarations + "    return g<(i64) -> i64>(twice)\n", "", Explicit = true)]
    [InlineData("I3", "gt2", ItemDeclarations + "    return g<(i64) -> i64>(if c1 => twice else => twice)\n", "", Explicit = true)]
    [InlineData("I3", "gt3", ItemDeclarations + "    return g<(i64) -> i64>(if c1 => twice else if c2 => func [] (v: i64) -> i64 => v else => func [] (v: i64) -> i64 => v)\n", "", Explicit = true)]
    [InlineData("guard", "gt4", ItemDeclarations + "    return g<(i64) -> i64>(func [] (v: i64) -> i64 => v)\n", "")]
    [InlineData("guard", "gt1n", ItemDeclarations + "    return g<(i64) -> bool>(twice)\n", "*: return g<(i64) -> bool>(twice) | ...")]
    [InlineData("guard", "gt2n", ItemDeclarations + "    return g<(i64) -> bool>(if c1 => twice else => twice)\n", "*: return g<(i64) -> bool>(if c1 => twice else => twice) | ...")]
    public void AnItemIsErasedAtASlotAnExplicitTypeArgumentFixes(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}

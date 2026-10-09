// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 10.5: a generic function reference binds its own slots from the fixed expected call signature, and its Function Item
// keeps those bound arguments (SPEC 7.6.4); calls and erasure enter the instance of the bound arguments.
public class GenericFunctionReferenceTest
{
    private const string Identity = "func identity<T>(value: T) -> T => value@move\n";
    private const string Show = "func show(value: i32) -> () => Console.writeLine(\"plain\")\nfunc show<T>(value: T) -> () => Console.writeLine(\"generic\")\n";
    private const string Invoke = "func invoke<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> ()>\n    action(1)\n";
    private const string Apply = "func apply<F>(action: ref/F, value: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return action(value)\n";
    private const string FirstOf = "func firstOf<T>(a: T, b: ref/i32) -> T => a@move\n";
    private const string Pick = "func pick<T>(a: T, b: T) -> T => a@move\n";
    private const string ResultOnly = "func g<T>(a: ref/i32, b: ref/i32) -> T => $abort(\"never\")\n";
    private const string ApplyTwo = "func apply<F>(action: ref/F, a: ref/i32, b: ref/i32) -> ref/i32 during (a and b)\n    F is Callable<(ref/i32, ref/i32) -> ref/i32>\n    return action(a, b)\n";
    private const string Counter = "struct Counter\n    var value: i32\n\n    public init(value: i32)\n        self.value = value\n\n    public func pick<T>(self, other: T) -> T => other@move\n";
    private const string Tagged = "func tagged<T>(a: T, s: string) -> T => a@move\n";
    private const string OwnedBox = "struct Box\n    var value: string\n\n    public init(value: string)\n        self.value = value@move\n\n    public func pick<T>(self: Self, other: T) -> T => other@move\n";

    [Theory]
    [InlineData("Erased", Identity + "let b: (i32) -> i32 = identity\nrequire b(2) == 2 else => $abort(\"erased\")", "")]
    [InlineData("Returned", Identity + "func make() -> (i32) -> i32 => identity\nlet f = make()\nrequire f(3) == 3 else => $abort(\"returned\")", "")]
    [InlineData("PreferNongeneric", Show + Invoke + "let a: (i32) -> () = show\na(1)\ninvoke(show)", "plain\nplain\n")]
    [InlineData("OnlyGeneric", Show + "let s: (bool) -> () = show\ns(true)", "generic\n")]
    [InlineData("PerCallOrigin", "func inspect<T>(value: ref/T) -> () => Console.writeLine(\"inspect\")\nlet c: (ref/i32) -> () = inspect\nlet n: i32 = 1\nc(n@ref)", "inspect\n")]
    [InlineData("CallableItem", Identity + Apply + "require apply(identity, 5) == 5 else => $abort(\"callable\")", "")]
    [InlineData("FunctionParameter", Identity + "func run(action: (i32) -> i32, value: i32) -> i32 => action(value)\nrequire run(identity, 6) == 6 else => $abort(\"parameter\")", "")]
    [InlineData("GenericBody", Identity + "func twice<U>(value: U) -> U\n    U is Copy\n    U is Owned\n    let f: (U) -> U = identity\n    return f(f(value))\nrequire twice(7) == 7 else => $abort(\"body\")", "")]
    [InlineData("NonCopyInstance", Identity + "let f: (string) -> string = identity\nlet text = f(\"text\")\nConsole.writeLine(text)", "text\n")]
    [InlineData("Constrained", "func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet f: (i32) -> (i32, i32) = pair\nlet p = f(4)\nrequire p.0 + p.1 == 8 else => $abort(\"constrained\")", "")]
    // SPEC 10.6: a per-call slot's reference wrapped in an anonymous function runs; each call of the anonymous function binds T.
    [InlineData("PerCallWrapped", "struct Node\n    public let v: i32\n    public init(v: i32)\n        self.v = v\n" + Identity + "let h: (ref/Node) -> ref/Node = func (value) => identity(value)\nlet n = Node.init(v: 4)\nrequire h(n@ref).v == 4 else => $abort(\"wrapped\")", "")]
    [InlineData("PerCallWrappedCallable", "func apply<F>(action: ref/F, x: ref/i32) -> ref/i32 during x\n    F is Callable<(ref/i32) -> ref/i32>\n    return action(x)\n" + Identity + "let x: i32 = 5\nrequire apply(func (value) => identity(value), x@ref)@follow == 5 else => $abort(\"callable\")", "")]
    [InlineData("PerCallWrappedNamed", "func both<T>(first: T ! count => second: i32) -> T => first@move\nlet g: (ref/i32, i32) -> ref/i32 = func (first, second) => both(first, count: second)\nlet n: i32 = 6\nrequire g(n@ref, 1)@follow == 6 else => $abort(\"named\")", "")]
    [InlineData("PerCallWrappedExclusive", "func second<T>(count: i32, value: T) -> T => value@move\nlet s: (i32, uniq/i32) -> uniq/i32 = func (count, value) => second(count, value)\nvar n: i32 = 7\nrequire s(1, n@uniq)@follow == 7 else => $abort(\"exclusive\")", "")]
    [InlineData("PerCallWrappedTwoInputs", FirstOf + "let f: (ref/i32, ref/i32) -> ref/i32 = func (a, b) => firstOf(a, b)\nlet x: i32 = 1\nlet y: i32 = 2\nrequire f(x@ref, y@ref)@follow == 1 else => $abort(\"two\")", "")]
    [InlineData("PerCallWrappedMember", Counter + "let f: (ref/Counter, ref/i32) -> ref/i32 = func (p1, other) => Counter.pick(p1, other)\nlet c = Counter.init(value: 1)\nlet n: i32 = 4\nrequire f(c@ref, n@ref)@follow == 4 else => $abort(\"member\")", "")]
    [InlineData("PerCallWrappedMeet", Pick + "let f: (ref/i32, ref/i32) -> ref/i32 = func (a, b) => pick(a, b)\nlet x: i32 = 1\nlet y: i32 = 2\nrequire f(x@ref, y@ref)@follow == 1 else => $abort(\"meet\")", "")]
    [InlineData("PerCallWrappedShadowed", "func identity<T>(identity: T) -> T => identity@move\nlet h: (ref/i32) -> ref/i32 = func (p1) => identity(p1)\nlet n: i32 = 8\nrequire h(n@ref)@follow == 8 else => $abort(\"shadowed\")", "")]
    [InlineData("PerCallWrappedCallableTwoInputs", ApplyTwo + FirstOf + "let x: i32 = 5\nrequire apply(func (a, b) => firstOf(a, b), x@ref, x@ref)@follow == 5 else => $abort(\"callable\")", "")]
    [InlineData("PerCallWrappedMoved", Tagged + "let f: (ref/i32, string) -> ref/i32 = func (a, s) => tagged(a, s@move)\nlet n: i32 = 2\nrequire f(n@ref, \"x\")@follow == 2 else => $abort(\"moved\")", "")]
    [InlineData("PerCallWrappedOwnedSelf", OwnedBox + "let f: (Box, ref/i32) -> ref/i32 = func (p1, other) => Box.pick(p1@move, other)\nlet n: i32 = 4\nrequire f(Box.init(value: \"b\"), n@ref)@follow == 4 else => $abort(\"self\")", "")]
    // SPEC 15.4.4: the Origin that a written Type argument omits is solved from S by local inference when S fixes it.
    [InlineData("OmittedWrittenOrigin", Identity + "let f: (ref/i32 during static) -> ref/i32 during static = identity<ref/i32>\nConsole.writeLine(\"ok\")", "ok\n")]
    // SPEC 10.5, 10.8: a slot bound to a Function Type holds no per-call Origin of S, since that Function Type binds its own inputs;
    // a written Function Type argument and the one in S are two spellings of one normalized Type.
    [InlineData("NestedFunctionSlot", "func first(v: ref/i32) -> ref/i32 => v\nfunc call<T>(f: T, x: ref/i32) -> i32\n    T is Callable<(ref/i32) -> ref/i32>\n    return f(x)@follow\nlet h: ((ref/i32) -> ref/i32, ref/i32) -> i32 = call\nlet k: ((ref/i32) -> ref/i32, ref/i32) -> i32 = call<(ref/i32) -> ref/i32>\nlet n: i32 = 5\nrequire h(first, n@ref) + k(first, n@ref) == 10 else => $abort(\"nested\")", "")]
    [InlineData("NestedFunctionSlotPlain", "func first(v: ref/i32) -> ref/i32 => v\nfunc call<T>(f: T) -> () => Console.writeLine(\"called\")\nlet h: ((ref/i32) -> ref/i32) -> () = call\nlet k: ((ref/i32) -> ref/i32) -> () = call<(ref/i32) -> ref/i32>\nh(first)\nk(first)", "called\ncalled\n")]
    public void GenericReferencesEnterTheirBoundInstance(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("GenericReference" + name, source, stdout);
    }

    [Fact]
    public void TheItemRecordsItsBoundArguments()
    {
        var c = MinimalEmissionTest.Analyze(Identity + Apply + "let r = apply(identity, 5)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "apply");
        var item = call.BoundCall!.TypeArguments[0]!;
        Assert.Equal(BoundTypeKind.FunctionItem, item.Kind);
        Assert.Same(BoundType.I32, Assert.Single(item.Components));
        Assert.Equal("function item identity<i32>", Binding.DiagnosticTypeName(item));
    }

    [Theory]
    [InlineData(Identity + "let g: (ref/i32) -> ref/i64 = identity")] // The structure fails before any Origin is judged.
    [InlineData(Identity + "let f: (i32, i32) -> i32 = identity")] // Arity differs.
    public void InapplicableGenericReferencesAreTypeMismatches(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.True(error.Text is "identity" or "pair" or "keep", error.ToString() + " " + error.Text); // Located at the reference.
        Assert.DoesNotContain("per-call", error.Note, StringComparison.Ordinal);
        Assert.Contains("Type parameters are bound from the expected signature", error.Note, StringComparison.Ordinal); // Owned failures: ErasureOwnedDiagnosticTest.
    }

    // SPEC 10.6, 15.3.6: a slot that only a per-call Origin of the fixed expected call signature would satisfy is one
    // MissingOriginBinding_Kd at the reference, showing the slot and that parameter.
    [Theory]
    [InlineData("Initializer", "struct Node\n    public let v: i32\n    public init(v: i32)\n        self.v = v\n" + Identity + "let h: (ref/Node) -> ref/Node = identity", "identity", "1st", "ref/Node")]
    [InlineData("Return", Identity + "func make() -> (ref/i32) -> ref/i32\n    return identity", "identity", "1st", "ref/i32")]
    [InlineData("Callable", "func apply<F>(action: ref/F, x: ref/i32) -> ref/i32 during x\n    F is Callable<(ref/i32) -> ref/i32>\n    return action(x)\n" + Identity + "let x: i32 = 5\nlet r = apply(identity, x@ref)", "identity", "1st", "ref/i32")]
    [InlineData("Assignment", "func first<T>(v: ref/T) -> ref/T => v\n" + Identity + "var g: (ref/i32) -> ref/i32 = first\ng = identity", "identity", "1st", "ref/i32")]
    [InlineData("SecondParameter", "func second<T>(count: i32, value: T) -> T => value@move\nlet s: (i32, uniq/i32) -> uniq/i32 = second", "second", "2nd", "uniq/i32")]
    [InlineData("Named", "func both<T>(first: T ! count => second: i32) -> T => first@move\nlet g: (ref/i32, i32) -> ref/i32 = both", "both", "1st", "ref/i32")]
    // The elided result of S is the meet of both inputs, which the slot bound from the 1st parameter fits.
    [InlineData("TwoInputs", FirstOf + "let f: (ref/i32, ref/i32) -> ref/i32 = firstOf", "firstOf", "1st", "ref/i32")]
    [InlineData("Member", Counter + "let f: (ref/Counter, ref/i32) -> ref/i32 = Counter.pick", "Counter.pick", "2nd", "ref/i32")]
    [InlineData("CallableTwoInputs", ApplyTwo + FirstOf + "let x: i32 = 5\nlet r = apply(firstOf, x@ref, x@ref)", "firstOf", "1st", "ref/i32")]
    // A written Type argument's omitted Origin is solved by local inference, which only the per-call Origin would satisfy.
    [InlineData("Explicit", Identity + "let f: (ref/i32) -> ref/i32 = identity<ref/i32>", "identity<ref/i32>", "1st", "ref/i32")]
    // A parameter named like the reference would shadow it inside the wrapper, so it is renamed.
    [InlineData("Shadowed", "func identity<T>(identity: T) -> T => identity@move\nlet h: (ref/i32) -> ref/i32 = identity", "identity", "1st", "ref/i32")]
    // A bare Place never moves (SPEC 3.5), so the wrapper passes a by-value parameter of S that is not Copy, an owned self too, with @move.
    [InlineData("Moved", Tagged + "let f: (ref/i32, string) -> ref/i32 = tagged", "tagged", "1st", "ref/i32")]
    [InlineData("OwnedSelf", OwnedBox + "let f: (Box, ref/i32) -> ref/i32 = Box.pick", "Box.pick", "2nd", "ref/i32")]
    public void APerCallOriginLeavesTheReferenceSlotUnsolved(string name, string source, string reference, string ordinal, string parameter)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var all = TestDiagnostics.Of(c);
        Assert.DoesNotContain(all, x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        var error = Assert.Single(all, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), error.Code);
        Assert.Equal(reference, error.Text);
        Assert.Equal($"only the {ordinal} parameter's per-call Origin would satisfy Type parameter 'T'", error.Label);
        Assert.Contains($"its {ordinal} parameter {parameter} at each call", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(new[] { "declaration", "parameter" }, record.Related!.Select(static x => x.Role).Order(StringComparer.Ordinal));
        var document = c.Diagnostics.FindDocument(path)!;
        var shown = record.Related!.Single(static x => x.Role == "parameter").Span!.Value;
        Assert.Equal(parameter, document.SourceText.Substring(shown.Start, shown.Length)); // The written parameter of S.
        if (name != "Initializer")
        {
            return;
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("MissingOriginBinding_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("only the 1st parameter's per-call Origin would satisfy Type parameter 'T'", console.Text, StringComparison.Ordinal);
        var identity = Kimi.Checking.SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(Kimi.Lsp.WorkspaceCheck.Place(new(Kimi.Checking.CheckOutcome.Completed, false, Kimi.Checking.TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    // SPEC 10.6, 15.3.6: a slot that only the meet of several parameters' per-call Origins would satisfy names them all and relates each
    // written parameter of S. The Callable form was accepted with that meet in the bound argument before (2026-10-05).
    [Theory]
    [InlineData("Meet", Pick + "let f: (ref/i32, ref/i32) -> ref/i32 = pick", "pick")]
    [InlineData("ResultOnly", ResultOnly + "let h: (ref/i32, ref/i32) -> ref/i32 = g", "g")]
    [InlineData("CallableResultOnly", ApplyTwo + ResultOnly + "let x: i32 = 5\nlet r = apply(g, x@ref, x@ref)", "g")]
    public void PerCallOriginsOfSeveralParametersAreNamedTogether(string name, string source, string reference)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), error.Code);
        Assert.Equal(reference, error.Text);
        Assert.Equal("only the per-call Origins of the 1st and 2nd parameters would satisfy Type parameter 'T'", error.Label);
        Assert.Contains("binds the Origins of its 1st parameter ref/i32 and 2nd parameter ref/i32 at each call", error.Note, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var record = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(new[] { "declaration", "parameter", "parameter" }, record.Related!.Select(static x => x.Role).Order(StringComparer.Ordinal));
        var text = c.Diagnostics.FindDocument(path)!.SourceText;
        var spans = record.Related!.Where(static x => x.Role == "parameter").Select(static x => x.Span!.Value).ToArray();
        Assert.All(spans, x => Assert.Equal("ref/i32", text.Substring(x.Start, x.Length))); // The written parameters of S, in order.
        Assert.True(spans[0].Start < spans[1].Start, name);
    }

    // SPEC 10.5: failed slot binding keeps TypeMismatch_Kd and names the structural or Origin conflict.
    // A reached constraint failure is tested separately with its actual obligation.
    [Theory]
    [InlineData(Identity + "let h: (ref/i32) -> ref/i32 during static = identity", "identity", "its parameters and result bind Type parameter 'T' to Types that differ only in their Origins")]
    [InlineData(Identity + "func outer(x: ref/i32) -> i32\n    let h: (ref/i32) -> ref/i32 during x = identity\n    return 0", "identity", "its parameters and result bind Type parameter 'T' to Types that differ only in their Origins")]
    [InlineData(Identity + "let f: (ref/i32) -> ref/i32 = identity<ref/i32 during static>", "identity<ref/i32 during static>", "the written Type arguments and the signature give Type parameter 'T' Types that differ only in their Origins")]
    [InlineData(FirstOf + "let f: (ref/i32, ref/i64) -> ref/i32 = firstOf", "firstOf", "no binding of them fits its structure")]
    public void ReferenceSlotFailuresNameTheirCause(string source, string reference, string cause)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(reference, error.Text);
        Assert.StartsWith("The generic function's Type parameters are bound from the expected signature without adaptations; " + cause, error.Note, StringComparison.Ordinal);
    }

    // SPEC 10.5, 15.6.5: an input Origin of a function enclosing the reference is fixed in its body, so a slot may hold it; at a common
    // Function Type the Item is then judged by the Owned condition (SPEC 7.6.4, 15.2.3), which a premise can prove. A written Type
    // argument whose Origin is omitted takes that Origin from S by local inference (SPEC 15.4.4), so it reaches the same condition.
    [Theory]
    [InlineData("make")]
    [InlineData("make<ref/i32>")]
    public void AnEnclosingInputOriginIsAFixedSlotBinding(string reference)
    {
        const string Make = "func make<T>(value: T) -> T => value@move\n";
        var c = MinimalEmissionTest.Analyze(Make + "func outer(x: ref/i32) -> i32\n    let h: (ref/i32 during x) -> ref/i32 during x = " + reference + "\n    return h(x)@follow");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
        Assert.Equal(reference, error.Text);
        Assert.Contains("Owned bound generic arguments", error.Note, StringComparison.Ordinal);

        var proven = MinimalEmissionTest.Analyze(Make + "func outer(x: ref/i32) -> i32\n    origin x outlives static\n    let h: (ref/i32 during x) -> ref/i32 during x = " + reference + "\n    return h(x)@follow\nConsole.writeLine(\"ok\")");
        Assert.True(proven.Binding.Result.IsComplete && proven.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(proven, null));
        Assert.Contains("define", CompilationTestHelper.WriteIr(proven), StringComparison.Ordinal); // Generation accepts what check accepts.
    }

    // SPEC 10.6: a reference whose written Type arguments are `ref/i32` with an omitted Origin and `i64` for a slot (`M`) that only a
    // written argument binds is one MissingOriginBinding_Kd with no repair candidate. The same written arguments at a signature without
    // per-call Origins are a value (ExplicitTypeArgumentsSelectTheReference, Phantom).
    [Fact]
    public void APerCallSlotOfAnExplicitReferenceIsReportedAtTheReference()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze("func tag<T, M>(a: T) -> T => a@move\nlet f: (ref/i32) -> ref/i32 = tag<ref/i32, i64>", path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), error.Code);
        Assert.Equal("tag<ref/i32, i64>", error.Text);
        Assert.Equal("only the 1st parameter's per-call Origin would satisfy Type parameter 'T'", error.Label);
        Assert.Contains("its 1st parameter ref/i32 at each call", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(new[] { "declaration", "parameter" }, record.Related!.Select(static x => x.Role).Order(StringComparer.Ordinal));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("only the 1st parameter's per-call Origin would satisfy Type parameter 'T'", console.Text, StringComparison.Ordinal);
    }

    // SPEC 10.8: two spellings of one Function Type bind one slot, and a result-only per-call slot wrapped in an anonymous function is accepted.
    [Theory]
    [InlineData("func keep<T>(f: T) -> T => f@move\nlet m: ((ref/i32) -> ref/i32) -> (ref/i32) -> ref/i32 = keep")]
    [InlineData(ResultOnly + "let h: (ref/i32, ref/i32) -> ref/i32 = func (a, b) => g(a, b)")]
    [InlineData(ApplyTwo + ResultOnly + "let x: i32 = 5\nlet r = apply(func (a, b) => g(a, b), x@ref, x@ref)")]
    public void ReferenceSlotCounterpartsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 10.6: the record's cause is its own; an independent error stays visible and a use of the annotated binding adds none.
    [Fact]
    public void APerCallSlotKeepsIndependentProblemsVisible()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "let h: (ref/i32) -> ref/i32 = identity\nlet n: i32 = 4\nlet m = h(n@ref)\nlet z: i32 = missing\nConsole.writeLine(\"\\(m@follow)\")");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code} {x.Text}").ToArray();
        Assert.Equal(new[] { $"{nameof(DiagnosticCode.MissingOriginBinding_Kd)} identity", $"{nameof(DiagnosticCode.UnresolvedBinding_Kd)} missing" }, errors);
    }

    // SPEC 10.5: explicit Type arguments narrow the candidates to those that take them; one remaining candidate is a value.
    [Theory]
    [InlineData("Value", Show + "let d = show<i32>\nd(3)", "generic\n")]
    [InlineData("Expected", Show + "let a: (i32) -> () = show<i32>\na(4)", "generic\n")]
    [InlineData("Callable", Identity + Apply + "require apply(identity<i32>, 5) == 5 else => $abort(\"callable\")", "")]
    [InlineData("String", Identity + "let f = identity<string>\nConsole.writeLine(f(\"text\"))", "text\n")]
    [InlineData("Qualified", "group Tools\n    public func echo<T>(value: T) -> T => value@move\nlet g = Tools.echo<i32>\nrequire g(6) == 6 else => $abort(\"qualified\")", "")]
    [InlineData("Parenthesized", Identity + "let h: (bool) -> bool = (identity<bool>)\nrequire h(true) else => $abort(\"parenthesized\")", "")]
    // SPEC 10.5, 10.8: such a reference needs no fixed call signature, so its Item signature is evidence for the outer call.
    [InlineData("OpenResult", Identity + "func run<R, F>(action: ref/F) -> R\n    F is Callable<(i32) -> R>\n    return action(1)\nlet r = run(identity<i32>)\nrequire r == 1 else => $abort(\"open\")", "")]
    [InlineData("OuterFilter", Identity + "func run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return 1\nfunc run<F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(i64) -> i64>\n    return 2\nrequire run(identity<i64>) == 2 else => $abort(\"filter\")", "")]
    [InlineData("OriginInput", "func first<T>(value: ref/T) -> ref/T => value\nfunc apply<F>(action: ref/F, x: ref/i32) -> ref/i32 during x\n    F is Callable<(ref/i32) -> ref/i32>\n    return action(x)\nlet x: i32 = 7\nrequire apply(first<i32>, x@ref)@follow == 7 else => $abort(\"origin\")", "")]
    [InlineData("Phantom", "func tag<T, M>(a: T) -> T => a@move\nlet f: (i32) -> i32 = tag<i32, i64>\nrequire f(3) == 3 else => $abort(\"phantom\")", "")]
    public void ExplicitTypeArgumentsSelectTheReference(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("GenericReferenceExplicit" + name, source, stdout);
    }

    [Fact]
    public void AnOverloadSetWithoutASignatureIsAmbiguous()
    {
        var c = MinimalEmissionTest.Analyze(Show + "let e = show");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
        Assert.Equal("show", error.Text);
        Assert.Contains("without a fixed expected call signature", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let f = identity")]
    [InlineData("let o: Option<i32> = .None\nlet f = (identity)")]
    public void AGenericReferenceWithoutASignatureLeavesItsSlotUnbound(string body)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(Identity + body, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.UnboundTypeArgument_Kd), error.Code);
        Assert.True(error.Text is "identity" or "(identity)", error.Text);
        Assert.Contains("'T'", error.Label, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Generic parameter 'T' is not bound", console.Text, StringComparison.Ordinal);
        var identity = Kimi.Checking.SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(Kimi.Lsp.WorkspaceCheck.Place(new(Kimi.Checking.CheckOutcome.Completed, false, Kimi.Checking.TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData(Identity + "let g = identity<i32, bool>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData(Show + "let u: (i32) -> () = show<i32, bool>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet p = pair<string>", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd))]
    public void ExplicitArgumentsThatNoCandidateTakesAreRejected(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericReferenceSelectionAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.GenericFunctionReference);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 10.6: a warm rebind of a per-call slot failure, including one found through an Origin conflict, and of a Type mismatch whose
    // Note names an Origin conflict, reuses the candidate buffers, the scratch bindings and the fact tables.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Identity + "let h: (ref/i32) -> ref/i32 = identity\nlet n: i32 = 1\nlet m = h(n@ref)", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    [InlineData(FirstOf + "let f: (ref/i32, ref/i32) -> ref/i32 = firstOf", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    [InlineData(Pick + "let f: (ref/i32, ref/i32) -> ref/i32 = pick", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    [InlineData(Identity + "let h: (ref/i32) -> ref/i32 during static = identity", nameof(DiagnosticCode.TypeMismatch_Kd))]
    [InlineData(Tagged + "let f: (ref/i32, string) -> ref/i32 = tagged", nameof(DiagnosticCode.MissingOriginBinding_Kd))] // The @move proof.
    [InlineData("func tag<T, M>(a: T) -> T => a@move\nlet f: (ref/i32) -> ref/i32 = tag<ref/i32, i64>", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    [InlineData("func firstOf<T>(a: T, b: ref/i32) -> T\n    T is Owned\n    return a@move\nlet f: (ref/i32, ref/i32) -> ref/i32 = firstOf", nameof(DiagnosticCode.UnprovenConstraint_Kd))]
    public void WarmPerCallSlotFailureAllocatesNothing(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var complete = false;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete |= c.Bind().IsComplete));
        Assert.False(complete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error).Code);
    }

    // SPEC 15.4.4: a warm rebind that solves a written Type argument's omitted Origin from S reuses the local's inference record.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOmittedWrittenOriginAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "let f: (ref/i32 during static) -> ref/i32 during static = identity<ref/i32>");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete);
    }
}

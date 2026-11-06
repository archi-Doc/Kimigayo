// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 10.5, 10.7, 7.7.4, 15.2.3: a call's applicability uses only the signature part of an erasure; the Shared receiver and the Owned
// environment of a Closure value are judged after selection, at the argument, and a failure there never selects another candidate.
public class ErasureAfterSelectionTest
{
    private const string Call = "func call(action: (i32) -> i32, x: i32) -> i32 => action(x)\nfunc run<T>(action: (T) -> i32, x: T) -> i32 => action(x@move)\n";

    private const string ValueH = "func k(h: ((i32) -> i32, i32) -> i32) -> i32\n";

    private const string Copied = "    let y: i32 = 2\n    let f = func [y] (n: i32) => n + y\n";

    private const string FieldS = "struct S\n    public var h: ((i32) -> i32, i32) -> i32\nfunc k(s: ref/S) -> i32\n";

    private const string ActEnum = "enum Act\n    Run((i32) -> i32)\nfunc k() -> Act\n";

    private const string ActRun = "enum Act\n    Run((i32) -> i32)\nfunc k() -> i32\n";

    private const string NonCopy = "    let s = \"abc\"\n    let f = func [s@move] (n: i32) => n + 1\n";

    private const string FnK = "func k(g: (i32) -> i32) -> i32\n    return 0\n";

    private const string GenericK = "func k<T>(g: (T) -> T, x: T) -> T\n    return x@move\n";

    [Theory]
    [InlineData("    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let r = call(f, 1)\n", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "y@ref")]
    [InlineData("    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let r = run(f, 2)\n", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "y@ref")]
    [InlineData("    var total = 0\n    var f = func [var total] (n: i32) -> i32\n        total = total + n\n        return total\n    let r = call(f, 1)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Exclusive call")]
    [InlineData("    var total = 0\n    var f = func [var total] (n: i32) -> i32\n        total = total + n\n        return total\n    let r = run(f, 2)\n", nameof(DiagnosticCode.TypeMismatch_Kd), "Exclusive call")]
    public void AStoredClosureIsJudgedAtTheArgumentAfterSelection(string body, string code, string fact)
    {
        var source = Call + "public func main() -> ()\n" + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, "f"), (error.Code, Text(source, error.Span)));
        Assert.Contains(fact, error.Note, StringComparison.Ordinal);
    }

    // SPEC 15.2.3: a capture of a generic Type that is not proven Owned is Unknown, so the record is UnprovenConstraint_Kd at the argument.
    [Fact]
    public void AnUnprovenEnvironmentIsTheUnprovenConstraintRecord()
    {
        var source = Call + "func g<T>(v: T) -> i32\n    let f = func [v@move] (n: i32) => n\n    return call(f@move, 1)\npublic func main() -> ()\n    let r = g(1)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "f@move"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.7: an erasure and a concrete acquisition are incomparable, and an Owned or receiver failure cannot reopen selection, so the
    // erasure candidate stays applicable beside the F candidate and the call is ambiguous (both selected the F candidate; the second one
    // printed 1). The Note names that incomparability.
    [Theory]
    [InlineData("F is Callable<(i32) -> i32>\n    return action(x)", "    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n")]
    [InlineData("F is Callable<owner, (i32) -> i32>\n    return action@move(x)", "    var total = 0\n    var f = func [var total] (n: i32) -> i32\n        total = total + n\n        return total\n")]
    public void AnErasureConditionNeverMakesTheErasureCandidateInapplicable(string clause, string body)
    {
        var source = "func call(action: (i32) -> i32, x: i32) -> i32 => action(x)\nfunc call<F>(action: F, x: i32) -> i32\n    " + clause + "\npublic func main() -> ()\n" + body + "    let r = call(f@move, 1)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.AmbiguousBinding_Kd), "call(f@move, 1)"), (error.Code, Text(source, error.Span)));
        Assert.Contains("incomparable (SPEC 10.2.2, 10.7)", error.Note, StringComparison.Ordinal);
    }

    // SPEC 23.1.2: each failing erasure argument is its own problem, so every one is reported, and a converting one is not.
    [Fact]
    public void EveryFailingErasureArgumentIsReported()
    {
        var source = "func call3(a: (i32) -> i32, b: (i32) -> i32, c: (i32) -> i32) -> i32 => a(1) + b(2) + c(3)\npublic func main() -> ()\n    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let h = func [y] (n: i32) => n + y\n    let g = func [y@ref] (n: i32) => n * y@follow\n    let r = call3(f, h, g)\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([(nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "f"), (nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "g")], errors.Select(x => (x.Code, Text(source, x.Span))));
    }

    // SPEC 7.7.4: a stored closure with a Shared receiver and an Owned environment converts at both parameters, and the calls run.
    [Fact]
    public void AnOwnedSharedClosureConverts()
        => ScalarEmissionTest.EmitFixture("ErasureAfterSelectionValid", Call + "let y: i32 = 2\nlet f = func [y] (n: i32) => n + y\nrequire call(f, 1) == 3 and run(f, 2) == 4 else => $abort(\"valid\")", string.Empty);

    // SPEC 7.7.4, 3.5, 12.5.2 (G341, rule E, DECISIONS BU-83): an erasure acquires its source ordinarily, by bare acquisition of the
    // source's own Type, so a stored Copy Closure converts at a value call's Function parameter as at a named call's (en_ok), whatever
    // the callee: a parameter (ev_ok, ev_nocap, ev_implicit, ev_twice), a `ref/` parameter (ev_refh), a local of a Function Type
    // (ev_local), a Function Item local (ev_localitem), a Field (ev_field, ev_fieldp) or a Closure (ev_clos). A Non-Copy source (ev_nc)
    // and a value already of the erased Type (ev_erased) still need `@move`.
    [Theory]
    [InlineData("ev_ok", ValueH + Copied + "    return h(f, 1)\n", "")]
    [InlineData("ev_nocap", ValueH + "    let f = func [] (n: i32) => n + 1\n    return h(f, 1)\n", "")]
    [InlineData("ev_implicit", ValueH + "    let y: i32 = 2\n    let f = func (n: i32) => n + y\n    return h(f, 1)\n", "")]
    [InlineData("ev_twice", ValueH + Copied + "    return h(f, 1) + h(f, 2)\n", "")]
    [InlineData("ev_refh", "func k(h: ref/(((i32) -> i32, i32) -> i32)) -> i32\n" + Copied + "    return h(f, 1)\n", "")]
    [InlineData("ev_local", Call + "func k() -> i32\n    let h: ((i32) -> i32, i32) -> i32 = call\n" + Copied + "    return h(f, 1)\n", "")]
    [InlineData("ev_localitem", Call + "func k() -> i32\n    let h = call\n" + Copied + "    return h(f, 1)\n", "")]
    [InlineData("ev_field", FieldS + Copied + "    return s.h(f, 1)\n", "")]
    [InlineData("ev_fieldp", FieldS + Copied + "    return (s.h)(f, 1)\n", "")]
    [InlineData("ev_clos", "func k() -> i32\n    let g = func [] (a: (i32) -> i32, x: i32) -> i32 => a(x)\n" + Copied + "    return g(f, 1)\n", "")]
    [InlineData("en_ok", Call + "func k() -> i32\n" + Copied + "    return call(f, 1)\n", "")]
    [InlineData("ev_nc", ValueH + "    let s = \"abc\"\n    let f = func [s@move] (n: i32) => n + 1\n    return h(f, 1)\n", "TransferRequired_Kd f")]
    [InlineData("ev_erased", ValueH + "    let y: i32 = 2\n    let f: (i32) -> i32 = func [y] (n: i32) => n + y\n    return h(f, 1)\n", "TransferRequired_Kd f")]
    public void AStoredCopyClosureErasesAtAValueCallAsAtANamedCall(string form, string source, string records)
        => AssertRecords(form, source, records);

    // SPEC 7.7.4, 3.5, 6.5.2 (G341, rule E, DECISIONS BU-83): a Case payload at a Function Type acquires its erased source by the
    // source's own Type too, contextual (ec_ok) or qualified (ec_q); a Non-Copy source still needs `@move` (ec_nc).
    [Theory]
    [InlineData("ec_ok", ActEnum + Copied + "    return .Run(f)\n", "")]
    [InlineData("ec_q", ActEnum + Copied + "    return Act.Run(f)\n", "")]
    [InlineData("ec_nc", ActEnum + "    let s = \"abc\"\n    let f = func [s@move] (n: i32) => n + 1\n    return .Run(f)\n", "TransferRequired_Kd f")]
    public void AStoredCopyClosureErasesAtACasePayload(string form, string source, string records)
        => AssertRecords(form, source, records);

    // SPEC 7.7.4, 3.5, 23.1.2 (G341, rule E, DECISIONS BU-83): Binding judges an erased source's bare acquisition once, at the source,
    // so a Non-Copy source's record stands beside an independent one (q_v_nc_berr, r_geninst_nc), inside a payload's failing argument
    // (p_typemis) and at each use, with no Move of the source (r_twice_nc).
    [Theory]
    [InlineData("q_v_nc_berr", ValueH + NonCopy + "    return h(f, 1)\nfunc z() -> i32\n    return \"x\"\n", "TransferRequired_Kd f; TypeMismatch_Kd \"x\"")]
    [InlineData("r_geninst_nc", "func k<T>(h: ((i32) -> i32, i32) -> i32, t: T) -> i32\n" + NonCopy + "    return h(f, 1)\nfunc m(h: ((i32) -> i32, i32) -> i32) -> i32\n    return k(h, 1)\n", "TransferRequired_Kd f; TransferRequired_Kd h")]
    [InlineData("p_typemis", "enum Act\n    Run((i32) -> i32)\nfunc k(h: ((i32) -> i32, i32) -> i32) -> Act\n" + NonCopy + "    return .Run(h(f, 1) + 0)\n", "TransferRequired_Kd f")]
    [InlineData("r_twice_nc", ValueH + NonCopy + "    return h(f, 1) + h(f, 2)\n", "TransferRequired_Kd f; TransferRequired_Kd f")]
    public void ANonCopyErasedSourceIsBindingsRecordAtEachUse(string form, string source, string records)
        => AssertRecords(form, source, records);

    // SPEC 7.7.4, 3.5 (G341, rule E, DECISIONS BU-83): a payload's erasure Copies a Copy source, which stays usable afterwards
    // (p_useafter_ok, p_useafter2_ok, p_gen_useafter_ok), in a second payload (p_twice_ok) or at a value call (p_then_v_ok); the
    // payload slot's Move takes only the fresh erased value. A Non-Copy source still needs `@move` at each payload (p_twice_nc).
    [Theory]
    [InlineData("p_useafter_ok", ActRun + Copied + "    let a = Act.Run(f)\n    return f(1)\n", "")]
    [InlineData("p_useafter2_ok", ActRun + Copied + "    let a: Act = .Run(f)\n    return f(1)\n", "")]
    [InlineData("p_twice_ok", ActRun + Copied + "    let a = Act.Run(f)\n    let b = Act.Run(f)\n    return 1\n", "")]
    [InlineData("p_then_v_ok", "enum Act\n    Run((i32) -> i32)\n" + ValueH + Copied + "    let a = Act.Run(f)\n    return h(f, 1)\n", "")]
    [InlineData("p_gen_useafter_ok", "enum Act<T>\n    Run((T) -> T)\nfunc k() -> i32\n" + Copied + "    let a: Act<i32> = .Run(f)\n    return f(1)\n", "")]
    [InlineData("p_twice_nc", ActRun + NonCopy + "    let a = Act.Run(f)\n    let b = Act.Run(f)\n    return 1\n", "TransferRequired_Kd f; TransferRequired_Kd f")]
    public void APayloadErasureLeavesACopySourceUsable(string form, string source, string records)
        => AssertRecords(form, source, records);

    // SPEC 7.7.4, 12.4.1, 10.5.1 (G386; DECISIONS BU-83's rule E): a parenthesized anonymous function at a Function parameter is the
    // unparenthesized argument, erased once (HirFormsTest.AnErasureIsAdjustedOnceAtTheNodeBindingErased holds the other forms), also at a
    // parameter holding Origins and in a callee that calls it; an invalid one keeps its record: a header that does not fit
    // (NoApplicableOverload_Kd, at the call), an environment that is not Owned (UnprovenConstraint_Kd, at the grouping's operand, the
    // value the erasure converts; DECISIONS BU-107).
    [Theory]
    [InlineData("g386", "func k(q: ref/i32, r: ref/i32, g: (Option<ref/(ref/i32 during q) during r>) -> i32) -> i32\n    return 0\nfunc run(a: ref/i32, b: ref/i32) -> i32\n    return k(a, b, (func [] (o) => 0))\n", "")]
    [InlineData("w6", "func k(g: (i32) -> i32) -> i32\n    return g(1)\nfunc run() -> i32\n    return k((func [] (o) => o))\n", "")]
    [InlineData("i386a", FnK + "func run() -> i32\n    return k((func [] (o: bool) => 0))\n", "NoApplicableOverload_Kd k((func [] (o: bool) => 0))")]
    [InlineData("a7", FnK + "func run() -> i32\n    return k(((func [] (o: bool) => 0)))\n", "NoApplicableOverload_Kd k(((func [] (o: bool) => 0)))")]
    [InlineData("a13", GenericK + "func run() -> i32\n    return k((func [] (o: bool) => o), 1)\n", "NoApplicableOverload_Kd k((func [] (o: bool) => o), 1)")]
    [InlineData("i386c", FnK + "func run(x: ref/i32) -> i32\n    return k((func [x] (o) => x@follow))\n", "UnprovenConstraint_Kd func [x] (o) => x@follow")]
    [InlineData("a8", FnK + "func run(x: ref/i32) -> i32\n    return k(((func [x] (o) => x@follow)))\n", "UnprovenConstraint_Kd func [x] (o) => x@follow")]
    public void AGroupedCallableArgumentIsErasedOnce(string form, string source, string records)
        => AssertRecords(form, source, records);

    // SPEC 7.7.4, 15.6.4, 5.7 (the owner's O-28 (a), DECISIONS BU-116): a concrete Closure whose result has a raw layer without an
    // Origin, also as a Tuple element (e04) or an Option payload (e06), retains its call receiver by rule A, so its result contract
    // depends on the receiver and it is not erased: at an initialization (t09; e02, the result the fixed expected signature gives; e03,
    // with no capture), a call argument (e01) or a return (e05), the Type mismatch whose Note names the dependence. A result stating an
    // Origin (w01, w06), a Function Item (w02), a result with no raw layer (w03) and a raw layer the call writes through a parameter,
    // which 7.7.4 does not name (w05), still convert.
    [Theory]
    [InlineData("t09", "func f(p: raw/i32) -> raw/i32\n    let g: () -> raw/i32 = func () -> raw/i32 => p\n    return g()\n", "TypeMismatch_Kd func () -> raw/i32 => p")]
    [InlineData("e01", "func run(g: () -> raw/i32) -> i32 => 0\nfunc f(p: raw/i32) -> i32\n    return run(func () -> raw/i32 => p)\n", "TypeMismatch_Kd func () -> raw/i32 => p")]
    [InlineData("e02", "func f(p: raw/i32) -> raw/i32\n    let g: () -> raw/i32 = func () => p\n    return g()\n", "TypeMismatch_Kd func () => p")]
    [InlineData("e03", "func f(p: raw/i32) -> raw/i32\n    let g: (raw/i32) -> raw/i32 = func (q: raw/i32) -> raw/i32 => q\n    return g(p)\n", "TypeMismatch_Kd func (q: raw/i32) -> raw/i32 => q")]
    [InlineData("e04", "func f(p: raw/i32) -> i32\n    let g: () -> (i32, raw/i32) = func () -> (i32, raw/i32) => (1, p)\n    return g().0\n", "TypeMismatch_Kd func () -> (i32, raw/i32) => (1, p)")]
    [InlineData("e05", "func mk(p: raw/i32) -> (() -> raw/i32)\n    return func () -> raw/i32 => p\n", "TypeMismatch_Kd func () -> raw/i32 => p")]
    [InlineData("e06", "func f(p: raw/i32) -> raw/i32?\n    let g: () -> raw/i32? = func () -> raw/i32? => .Some(p)\n    return g()\n", "TypeMismatch_Kd func () -> raw/i32? => .Some(p)")]
    [InlineData("w01", "func f() -> raw/i32 during static\n    let g: () -> raw/i32 during static = func () -> raw/i32 during static => null\n    return g()\n", "")]
    [InlineData("w02", "func h(q: raw/i32) -> raw/i32 => q\nfunc f(p: raw/i32) -> raw/i32\n    let g: (raw/i32) -> raw/i32 = h\n    return g(p)\n", "")]
    [InlineData("w03", "func f(n: i32) -> i32\n    let g: () -> i32 = func () -> i32 => n\n    return g()\n", "")]
    [InlineData("w05", "func f(p: raw/i32)\n    let g: (uniq/(raw/i32)) -> () = func (s: uniq/(raw/i32)) => s@follow = p\n    var t: raw/i32 = null\n    g(t@uniq)\n", "")]
    [InlineData("w06", "func f() -> raw/i32\n    let g: () -> raw/i32 = func () -> raw/i32 during static => null\n    return g()\n", "")]
    public void AClosureWhoseResultRetainsItsReceiverIsNotErased(string form, string source, string records)
        => Assert.All(AssertRecords(form, source, records), x => Assert.Contains("result retains its call receiver", x.Note, StringComparison.Ordinal));

    // The program's records, each `Code text` in report order ("; " between them), with a startup body.
    private static CheckDiagnostic[] AssertRecords(string form, string source, string records)
    {
        source += "public func main() -> () => ()\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        var shown = string.Join("; ", diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}"));
        Assert.True(shown == records, $"{form}: expected [{records}], found [{shown}]");
        return diagnostics;
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}

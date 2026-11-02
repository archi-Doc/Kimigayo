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
    {
        source += "public func main() -> () => ()\n";
        var shown = string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {Text(source, x.Span)}"));
        Assert.True(shown == records, $"{form}: expected [{records}], found [{shown}]");
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}

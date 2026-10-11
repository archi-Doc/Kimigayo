// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 10.5, 10.7, 7.6.4, 15.2.3: a call's applicability uses only the signature part of an erasure; the Shared receiver and the Owned
// environment of a Closure value are judged after selection, at the argument, and a failure there never selects another candidate.
public class ErasureAfterSelectionTest
{
    private const string Call = "func call(action: (i32) -> i32, x: i32) -> i32 => action(x)\nfunc run<T>(action: (T) -> i32, x: T) -> i32 => action(x@move)\n";

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
        Assert.Contains("incomparable (SPEC 10.2.1, 10.7)", error.Note, StringComparison.Ordinal);
    }

    // SPEC 23.3.6.4: each failing erasure argument is its own problem, so every one is reported, and a converting one is not.
    [Fact]
    public void EveryFailingErasureArgumentIsReported()
    {
        var source = "func call3(a: (i32) -> i32, b: (i32) -> i32, c: (i32) -> i32) -> i32 => a(1) + b(2) + c(3)\npublic func main() -> ()\n    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let h = func [y] (n: i32) => n + y\n    let g = func [y@ref] (n: i32) => n * y@follow\n    let r = call3(f, h, g)\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([(nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "f"), (nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "g")], errors.Select(x => (x.Code, Text(source, x.Span))));
    }

    // SPEC 7.6.4: a stored closure with a Shared receiver and an Owned environment converts at both parameters, and the calls run.
    [Fact]
    public void AnOwnedSharedClosureConverts()
        => ScalarEmissionTest.EmitFixture("ErasureAfterSelectionValid", Call + "let y: i32 = 2\nlet f = func [y] (n: i32) => n + y\nrequire call(f, 1) == 3 and run(f, 2) == 4 else => $abort(\"valid\")", string.Empty);

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}

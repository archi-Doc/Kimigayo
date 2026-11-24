// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionReferenceConditionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExpectedFunctionTypesKeepTheReferencedConditions(bool block, bool virtualSlot)
    {
        foreach (var type in new[] { "string", "i32" })
        {
            var path = Path.GetFullPath("reference-condition.kimi");
            var source = Program(block, virtualSlot, type);
            var c = MinimalEmissionTest.Analyze(source, path);
            Assert.Equal(type == "i32", c.Binding.Result.IsComplete);
            if (type == "i32")
            {
                ScalarEmissionTest.EmitFixture($"FunctionReferenceCondition{block}{virtualSlot}", source, string.Empty);
                continue;
            }

            c.Binding.ReportDiagnostics();
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize();
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal("UnsatisfiedConstraint_Kd", error.Code);
            Assert.Contains(error.Reason!, x => x.Name == "subject" && x.Value == "string");
            Assert.Contains(error.Reason!, x => x.Name == "constraint" && x.Value == "T is Copy");
            Assert.Single(error.Related!, x => x.Role == "constraint");
            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(result, string.Empty);
            Assert.Contains("Copy", console.Text, StringComparison.Ordinal);
            output.WriteLine(console.Text);
            var identity = SourceIdentity.FromPath(path);
            foreach (var related in new[] { false, true })
            {
                var shown = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
                Assert.Contains("Copy", shown.Message, StringComparison.Ordinal);
                Assert.Contains("string", shown.Message, StringComparison.Ordinal);
                output.WriteLine(shown.Message);
            }
        }
    }

    // SPEC 10.4 step 3, 8.3: the best fitting reference, here the non-generic one, is selected even when its condition is unknown, and the
    // generic one never takes its place. (SPEC 3.8: the reference fits `((i32) -> i32) -> i32` exactly; `(i32) -> Never` would fit none.)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownConditionsNeverSelectAnotherReference(bool overload)
    {
        var source = "struct Box<T>\n    public func choose(value: (i32) -> i32) -> i32\n        T is Copy\n        return 1\n" +
            (overload ? "    public func choose<U>(value: U) -> i32 => 2\n" : string.Empty) +
            "func use<T>()\n    T is Owned\n    let f: ((i32) -> i32) -> i32 = Box<T>.choose\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Contains("Copy", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func keep<T>(value: T) -> T\n    T is Owned\n    return value@move\nlet h: (ref/i32) -> ref/i32 = keep", "T is Owned", "UnprovenConstraint_Kd")]
    [InlineData("func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet f: (string) -> (string, string) = pair", "T is Copy", "UnsatisfiedConstraint_Kd")]
    // Owned needs a static Origin proof; an unproven condition also precedes the met binding's Origin conflict.
    [InlineData("func firstOf<T>(a: T, b: ref/i32) -> T\n    T is Owned\n    return a@move\nlet f: (ref/i32, ref/i32) -> ref/i32 = firstOf", "T is Owned", "UnprovenConstraint_Kd")]
    // A composite clause names the subject its operands share.
    [InlineData("func need<T>(value: T)\n    T is not Copy\n    ()\nlet f: (i32) -> () = need", "T is not Copy", "UnsatisfiedConstraint_Kd")]
    [InlineData("func need<T>(value: T)\n    T is string or bool\n    ()\nlet f = need<i32>", "T is string or bool", "UnsatisfiedConstraint_Kd")]
    [InlineData("func need<T>(value: ref/T)\n    T is Copy or Owned\n    ()\nfunc outer<T>(value: ref/T)\n    _ = need<T>\n()", "T is Copy or Owned", "UnprovenConstraint_Kd")]
    public void FailedGenericConditionsKeepTheirConcreteObligation(string source, string clause, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, error.Code);
        Assert.Contains(clause, error.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("per-call", error.Note, StringComparison.Ordinal);
    }

    // SPEC 23.1.2 (rule W), 7.7.4, 10.7: a callee whose own declared signature failed where it is written (8.3: `Box<string>`, with
    // `T is i32` refuted) is a failed input of a reference that needs a Function Type: the reference rests on that record and adds none,
    // never an internal error (triage row 9: InternalInvariant_Kd at an annotated `let`), whatever the states of its written Types when
    // the reference binds. Each published error as Code@line:column, in source order. A reference erased at a call argument keeps the
    // record of the call's trial, which folds the callee's verdict into its proof (7:17; PLAN G375's site, not this rule's). With an
    // overload the selection runs first, as a call's does (10.1): a valid candidate that wins keeps its own records (7.6: an unsafe one
    // taken as a value, 9:30), and only when none applies does the reference rest on the failed one. It rests only on a candidate whose
    // own check read that signature: one that fails step 1 (written Type arguments: no candidate, 10.5.3) or the arity check reads
    // none, so the reference keeps its own record (8:27, 7:24). A failed part is a wildcard and every other part and clause is still
    // judged, whatever its position (no order, 10.7): a mismatch at a part that did not fail (7:33 before or after the failed part, 7:32
    // under `g<bool>`, 8:33 in a group) or a refuted clause of the callee's own (11:33, `U is Marker` for bool) keeps the reference's
    // record. Controls: the reference without a Function Type (its Item's own records, unchanged), a valid callee (none), a valid callee
    // under a failed annotation (the annotation's record and its dependent mismatch, unchanged).
    [Theory]
    [InlineData("() -> Box<string>", "", "    let f: () -> Box<string> = g\n    return 0", "UnsatisfiedConstraint_Kd@4:6 | UnsatisfiedConstraint_Kd@4:13 | UnsatisfiedConstraint_Kd@7:18")]
    [InlineData("() -> Box<string>", "func take(f: () -> Box<string>) -> i32 => 0", "    return take(g)", "UnsatisfiedConstraint_Kd@4:6 | UnsatisfiedConstraint_Kd@4:13 | UnsatisfiedConstraint_Kd@5:20 | UnsatisfiedConstraint_Kd@7:17")]
    [InlineData("() -> Box<string>", "", "    let f: () -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:6 | UnsatisfiedConstraint_Kd@4:13")]
    [InlineData("() -> Box<string>", "", "    let f = g\n    return 0", "UnsatisfiedConstraint_Kd@4:6 | UnsatisfiedConstraint_Kd@4:13 | UnsatisfiedConstraint_Kd@7:13")]
    [InlineData("(x: Box<string>) -> i32", "/// - safety: none.\nunsafe func g(x: string) -> i32 => 0\n", "    let f: (string) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:11 | DirectCallFunctionValue_Kd@9:30")]
    [InlineData("(x: Box<string>) -> i32", "func g(x: bool, y: bool) -> i32 => 0\n", "    let f: (i32) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:11")]
    [InlineData("(x: Box<string>) -> i32", "func g<U>(x: U) -> i32 => 0\n", "    let f: (i32) -> i32 = g<bool>\n    return 0", "UnsatisfiedConstraint_Kd@4:11 | NoApplicableOverload_Kd@8:27")]
    [InlineData("(x: Box<string>) -> i32", "", "    let f: () -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:11 | TypeMismatch_Kd@7:24")]
    [InlineData("(x: i32, y: Box<string>) -> i32", "", "    let f: (bool, i32) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:19 | TypeMismatch_Kd@7:33")]
    [InlineData("(x: Box<string>, y: i32) -> i32", "", "    let f: (i32, bool) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:11 | TypeMismatch_Kd@7:33")]
    [InlineData("<U>(x: U, y: Box<string>) -> i32", "", "    let f: (i32, i32) -> i32 = g<bool>\n    return 0", "UnsatisfiedConstraint_Kd@4:20 | TypeMismatch_Kd@7:32")]
    [InlineData("(x: i32, y: Box<string>) -> i32", "func g(x: bool) -> i32 => 0\n", "    let f: (bool, i32) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@4:19 | NoApplicableOverload_Kd@8:33")]
    [InlineData("_unused() -> i32", "contract Marker\nfunc g<U>(x: U, y: Box<string>) -> i32\n    U is Marker\n    return 0\n", "    let f: (bool, i32) -> i32 = g\n    return 0", "UnsatisfiedConstraint_Kd@6:20 | UnsatisfiedConstraint_Kd@11:33")]
    [InlineData("() -> Box<i32>", "", "    let f: () -> Box<i32> = g\n    return 0", "")]
    [InlineData("() -> Box<i32>", "", "    let f: () -> Box<string> = g\n    return 0", "UnsatisfiedConstraint_Kd@7:18 | TypeMismatch_Kd@7:32")]
    public void AFailedSignatureIsTheReferencesFailedInput(string signature, string declaration, string body, string expected)
    {
        var source = $"struct Box<T>\n    T is i32\n\nfunc g{signature} => $abort(\"no\")\n{declaration}\nfunc host() -> i32\n{body}\n\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(expected.Length == 0, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error).OrderBy(static x => x.Span.Start);
        Assert.Equal(expected, string.Join(" | ", errors.Select(x => $"{x.Code}@{LineColumn(source, x.Span.Start)}")));
    }

    [Fact]
    public void UnknownOwnSlotConditionsBlockReferenceSelection()
    {
        var c = MinimalEmissionTest.Analyze("func pick<T>(value: T) -> i32\n    T is Copy\n    return 1\nfunc use<U>(value: U)\n    U is Owned\n    let f: (U) -> i32 = pick\n()");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Contains("T is Copy", error.Note, StringComparison.Ordinal);
        Assert.Contains("U", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("i32")]
    public void RefutedCandidatesDoNotPreventAnApplicableReference(string type)
    {
        var source = "func fail(value: i32) -> Never => $abort(\"not called\")\nstruct Box<T>\n    public func choose(value: (i32) -> i32) -> i32\n        T is Copy\n        return 1\n    public func choose(value: (i32) -> Never) -> i32 => 2\n" +
            $"let f: ((i32) -> Never) -> i32 = Box<{type}>.choose\nlet value: (i32) -> Never = fail\nrequire f(value@move) == 2 else => $abort(\"choice\")";
        ScalarEmissionTest.EmitFixture("FunctionReferenceConditionChoice" + type, source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ConditionChecksReuseBindingStorage()
    {
        var c = MinimalEmissionTest.Analyze(Program(true, true, "i32"));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid);
    }

    private static string Program(bool block, bool virtualSlot, string type)
    {
        var receiver = virtualSlot ? "self: objref/Self" : string.Empty;
        var function = $"public {(virtualSlot ? "virtual " : string.Empty)}func read({receiver}) -> i32";
        var member = block ? $"    Self is Marker when T is Copy\n        {function} => 1\n"
            : $"    {function}\n        T is Copy\n        return 1\n";
        var inputs = virtualSlot ? $"objref/Box<{type}>" : string.Empty;
        var call = virtualSlot ? $"let owner = Box<{type}>.init()@obj\nrequire action(owner@objref) == 1 else => $abort(\"reference\")" : "require action() == 1 else => $abort(\"reference\")";
        return $"contract Marker\n{(virtualSlot ? "open " : string.Empty)}struct Box<T>\n    public init() => ()\n" + member + $"let action: ({inputs}) -> i32 = Box<{type}>.read\n" + call;
    }

    // The 1-based line:column of an offset in an ASCII source.
    private static string LineColumn(string source, int offset)
    {
        var start = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        return $"{source.AsSpan(0, offset).Count('\n') + 1}:{offset - start + 1}";
    }
}

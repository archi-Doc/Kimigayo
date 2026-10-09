// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2, 7.6.3, 8.6, 23.3.6.4: a capture list and a closure argument explain their own failures at the written entry or with the
// receivers involved, and the checks that rest on a rejected capture report nothing more.
public class CaptureDiagnosticTest
{
    private const string Inspect = "func inspect(text: ref/string) -> i32 => 3\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n    public func twice(value: ref/i32 during a) -> i32 => value@follow * 2\n";

    private const string Apply = "func apply<F>(f: uniq/F) -> string\n    F is Callable<uniq, () -> string>\n    return f()\n";

    private const string Run = "func run<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return f()\n";

    private const string Count = "let start = 0\nvar count = func [var start] () -> i32\n    start += 1\n    return start\n";

    private const string Text = "let start = 0\nvar text = func [var start] () -> string\n    start += 1\n    return \"a\"\n";

    // SPEC 7.6.2: a capture name that a later entry, or a parameter, repeats is one DuplicateBinding_Kd at the later name, with the
    // entry it repeats related; it was located at the whole closure. A later call of the closure rests on it.
    [Theory]
    [InlineData("let x: i32 = 1\nlet f = func [x] (x: i32) => x\nrequire f(2) == 2 else => $abort(\"f\")", "func [x] (", "The capture entry x and the parameter x would both declare x in the anonymous function's body; a parameter cannot repeat a capture name (SPEC 7.6.2)", "Rename the parameter, or remove the capture entry x if the body needs only the argument")]
    [InlineData("let x: i32 = 1\nlet g = func [x, x] () => x\nrequire g() == 1 else => $abort(\"g\")", "func [x, ", "The capture list names x twice; each entry declares its own environment binding, so a name is captured once (SPEC 7.6.2)", "Remove the repeated entry x")]
    public void ARepeatedCaptureNameIsLocatedAtTheLaterName(string source, string later, string note, string advice)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.DuplicateBinding_Kd), error.Code);
        Assert.Equal(new SourceSpan(source.IndexOf(later, StringComparison.Ordinal) + later.Length, 1), error.Span);
        Assert.Equal("'x' is declared again", error.Label);
        Assert.Equal(note, error.Note);
        Assert.Equal(advice, error.Advice);
        var related = Assert.Single(error.Related!);
        Assert.Equal(("declaration", "capture entry", source.IndexOf("func [", StringComparison.Ordinal) + 6), (related.Role, related.Label, related.Span!.Value.Start));
    }

    // The repeated capture reaches the command rendering and both language-server placements at the later entry.
    [Fact]
    public void ARepeatedCaptureNameIsPublished()
    {
        const string Source = "let x: i32 = 1\nlet g = func [x, x] () => x\nrequire g() == 1 else => $abort(\"g\")";
        var path = Path.GetFullPath("capture-repeat.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("The capture list names x twice", console.Text, StringComparison.Ordinal);
        Assert.Contains("capture entry", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((error.Code, error.Display!.Range), (sent.Code, sent.Range));
        }
    }

    // Each repeated name of one list is its own record, in source order.
    [Fact]
    public void EveryRepeatedCaptureNameIsReported()
    {
        const string Source = "let x: i32 = 1\nlet h = func [x, x] (x: i32) => x";
        var output = DiagnosticCorpus.Check(Source);
        var entry = Source.IndexOf("[x, x]", StringComparison.Ordinal);
        Assert.Equal(
            [(nameof(DiagnosticCode.DuplicateBinding_Kd), entry + 4), (nameof(DiagnosticCode.DuplicateBinding_Kd), Source.IndexOf("(x:", StringComparison.Ordinal) + 1)],
            output.Diagnostics.Select(x => (x.Code, x.Span!.Value.Start)).ToArray());
        Assert.All(output.Diagnostics, x => Assert.Equal(entry + 1, Assert.Single(x.Related!).Span!.Value.Start));
    }

    // SPEC 7.6.2, 23.3.6.4: the other entries of a list that repeats a name are still bound, so an entry that fails on its own is
    // reported beside the repeat exactly as in a list without one (the repeat returned before them and hid it): an unresolved name at
    // the closure, a bare Non-Copy entry and an exclusive slot borrow of a let binding at the entry. A list whose other entries are
    // valid reports only the repeat, and a list without a repeat reports nothing.
    [Theory]
    [InlineData("let x: i32 = 1\nlet h = func [x, w, x] () => x\nrequire h() == 1 else => $abort(\"h\")", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "func [x, w, x] () => x", "[x, w, ")]
    [InlineData("let h = func [zz, zz] () => 1\nrequire h() == 1 else => $abort(\"h\")", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "func [zz, zz] () => 1", "[zz, ")]
    [InlineData("let x: i32 = 1\nlet text = \"abc\"\nlet h = func [text, x, text] () => x\nrequire h() == 1 else => $abort(\"h\")", nameof(DiagnosticCode.TransferRequired_Kd), "[text", "[text, x, ")]
    [InlineData("let x: i32 = 1\nlet h = func [x@uniq, x] () => 1\nrequire h() == 1 else => $abort(\"h\")", nameof(DiagnosticCode.InvalidAssignment_Kd), "[x@uniq", "[x@uniq, ")]
    public void ARepeatKeepsTheOtherEntriesChecked(string source, string code, string entry, string beforeRepeat)
    {
        var output = DiagnosticCorpus.Check(source);
        var located = entry[0] == '[' ? (Start: source.IndexOf(entry, StringComparison.Ordinal) + 1, Text: entry[1..]) : (Start: source.IndexOf(entry, StringComparison.Ordinal), Text: entry);
        var repeat = source.IndexOf(beforeRepeat, StringComparison.Ordinal) + beforeRepeat.Length;
        var name = source[repeat..source.IndexOf(']', repeat)];
        Assert.Equal(
            [(code, located.Start, located.Text), (nameof(DiagnosticCode.DuplicateBinding_Kd), repeat, name)],
            output.Diagnostics.Select(x => (x.Code, x.Span!.Value.Start, source.Substring(x.Span!.Value.Start, x.Span!.Value.Length))).ToArray());
        Assert.Equal($"'{name}' is declared again", output.Diagnostics[1].Label);
        var alone = Assert.Single(DiagnosticCorpus.Check(source.Remove(repeat - 2, name.Length + 2)).Diagnostics);
        Assert.Equal((code, located.Start, output.Diagnostics[0].Note), (alone.Code, alone.Span!.Value.Start, alone.Note));
        Assert.Empty(DiagnosticCorpus.Check("let x: i32 = 1\nlet y: i32 = 2\nlet h = func [x, y] () => x + y\nrequire h() == 3 else => $abort(\"h\")").Diagnostics);
    }

    // The entry's record and the repeat reach the command rendering and both language-server placements as two records.
    [Fact]
    public void ARepeatAndAnotherEntryArePublished()
    {
        const string Source = "let x: i32 = 1\nlet h = func [x, w, x] () => x\nrequire h() == 1 else => $abort(\"h\")";
        var path = Path.GetFullPath("capture-repeat-entry.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([nameof(DiagnosticCode.InvalidCaptureBinding_Kd), nameof(DiagnosticCode.DuplicateBinding_Kd)], result.Diagnostics.Select(static x => x.Code).ToArray());
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("InvalidCaptureBinding_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("The capture list names x twice", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity].ToArray();
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal((result.Diagnostics[i].Code, result.Diagnostics[i].Display!.Range), (sent[i].Code, sent[i].Range));
            }
        }
    }

    // A closure whose body failed is bound again where it is called; its own capture entries are not repeats of themselves (it was a
    // DuplicateBinding_Kd at the closure beside the body's located limit).
    [Fact]
    public void AClosureBoundAgainKeepsItsCaptures()
    {
        const string Source = Holder + "func run(n: ref/i32) -> i32\n    let f = func [n] () => Holder.twice(n)\n    return f()\nlet n = 3\nrequire run(n@ref) == 6 else => $abort(\"run\")";
        var output = DiagnosticCorpus.Check(Source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), "Holder"), (error.Code, Source.Substring(error.Span!.Value.Start, error.Span!.Value.Length)));
    }

    // SPEC 7.6.2, 23.3.6.4: an omitted capture list captures only by Copy, so the rejected capture of a Non-Copy binding is read as
    // that Copy and moves nothing; a later use of the binding reports only its own problems (it was a MovedPlace_Kd cascade), while an
    // independent Move still is.
    [Theory]
    [InlineData("", new[] { nameof(DiagnosticCode.TransferRequired_Kd) })]
    [InlineData("    let moved = text@move\n    Console.writeLine(moved)\n", new[] { nameof(DiagnosticCode.TransferRequired_Kd), nameof(DiagnosticCode.MovedPlace_Kd) })]
    public void ARejectedOmittedCaptureMovesNothing(string between, string[] codes)
    {
        var source = Inspect + "public func main() -> ()\n    let text = \"abc\"\n    let outer = func () -> i32\n        let inner = func [text@move] () => inspect(text@ref)\n        return inner()\n    Console.writeLine(\"\\(outer@move())\")\n" + between + "    Console.writeLine(text)\n";
        var output = DiagnosticCorpus.Check(source);
        Assert.Equal(codes, output.Diagnostics.Select(static x => x.Code).ToArray());
        Assert.Equal("The omitted capture list captures text only by Copy; it never infers a Move, a borrow or a Reborrow", output.Diagnostics[0].Note);
        if (codes.Length == 2)
        {
            Assert.Equal(source.LastIndexOf("(text)", StringComparison.Ordinal) + 1, output.Diagnostics[1].Span!.Value.Start);
        }
    }

    // SPEC 7.6.3, 8.6: a closure argument whose minimum call receiver its Callable Constraint does not permit names both receivers in
    // the rejected candidate's label and the Note of NoApplicableOverload_Kd, with Advice that names the parameter form; before, the
    // record named only the count.
    [Theory]
    [InlineData(Apply + "let text = \"abc\"\nvar take = func [text@move] () => text@move\nConsole.writeLine(apply(take@uniq))", "apply(take@uniq)", "apply: closure requires owner; Callable requires uniq", "The closure argument of f needs a Consuming call, and the Callable Constraint of F permits Shared and Exclusive calls only (SPEC 7.6.3, 8.6)", "Declare f as F by value, with F is Callable<owner, ...>, and pass the closure with @move, or change the closure so that a Shared or Exclusive call suffices")]
    [InlineData(Run + Count + "Console.writeLine(\"\\(run(count@ref))\")", "run(count@ref)", "run: closure requires uniq; Callable requires ref", "The closure argument of f needs an Exclusive call, and the Callable Constraint of F permits Shared calls only (SPEC 7.6.3, 8.6)", "Declare f as uniq/F, with F is Callable<uniq, ...>, and pass the closure with @uniq, or change the closure so that a Shared call suffices")]
    public void AClosureReceiverMismatchNamesBothReceivers(string source, string text, string label, string note, string advice)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), text), (error.Code, source.Substring(error.Span!.Value.Start, error.Span!.Value.Length)));
        Assert.Equal(label, Assert.Single(error.Related!).Label);
        Assert.Equal($"{label}. {note}", error.Note);
        Assert.Equal(advice, error.Advice);
    }

    // The receivers are named only for a candidate that applies once the closure's receiver is permitted. A candidate that fails for
    // another reason keeps its own label: a missing argument, an argument of another Type, or a call signature, whose Note stays the
    // signature's (the receiver Note displaced it). A common Function parameter has no Callable Constraint (its conversion condition is
    // SPEC 7.6.4), so no Callable receiver is claimed for it (it named one).
    [Theory]
    [InlineData(Run + "func run<F>(f: ref/F, n: i32) -> i32\n    F is Callable<() -> i32>\n    return f() + n\n" + Count + "Console.writeLine(\"\\(run(count@ref))\")", "run(count@ref)", new[] { "run: closure requires uniq; Callable requires ref", "run" }, "run: closure requires uniq; Callable requires ref. The closure argument of f needs an Exclusive call, and the Callable Constraint of F permits Shared calls only (SPEC 7.6.3, 8.6)", "Declare f as uniq/F, with F is Callable<uniq, ...>, and pass the closure with @uniq, or change the closure so that a Shared call suffices")]
    [InlineData("func run<F>(f: ref/F, n: i32) -> i32\n    F is Callable<() -> i32>\n    return f() + n\n" + Count + "Console.writeLine(\"\\(run(count@ref, true))\")", "run(count@ref, true)", new[] { "run" }, null, null)]
    [InlineData(Run + "func run<F>(f: ref/F, n: i32) -> i32\n    F is Callable<() -> i32>\n    return f() + n\n" + Text + "Console.writeLine(\"\\(run(text@ref))\")", "run(text@ref)", new[] { "run: callable signature is () -> string; requires () -> i32", "run: callable signature is () -> string; requires () -> i32" }, "The argument's known call signature is () -> string; the candidate requires () -> i32 from the supplied Type evidence", null)]
    public void AClosureReceiverIsNamedOnlyWhereItIsTheFailure(string source, string text, string[] labels, string? note, string? advice)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), text), (error.Code, source.Substring(error.Span!.Value.Start, error.Span!.Value.Length)));
        Assert.Equal(labels, error.Related!.Select(static x => x.Label).ToArray());
        Assert.Equal(note, error.Note);
        Assert.Equal(advice, error.Advice);
    }

    // SPEC 10.5, 7.6.4: erasure receiver conditions are checked at the argument after selection, without a Callable Constraint.
    [Fact]
    public void ACommonFunctionReceiverFailureNamesTheConvertedArgument()
    {
        const string Source = "func pass(f: () -> i32) -> i32 => f()\n" + Count + "Console.writeLine(\"\\(pass(count@move))\")";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("count@move", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains("Exclusive call", error.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("Callable Constraint", error.Note!, StringComparison.Ordinal);
    }

    // The receivers reach the command rendering and both language-server placements.
    [Fact]
    public void AClosureReceiverMismatchIsPublished()
    {
        const string Source = Run + "let start = 0\nvar count = func [var start] () -> i32\n    start += 1\n    return start\nConsole.writeLine(\"\\(run(count@ref))\")";
        var path = Path.GetFullPath("capture-receiver.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("run: closure requires uniq; Callable requires ref", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains("closure requires uniq; Callable requires ref", sent.Message, StringComparison.Ordinal);
        }
    }

    // The counterparts run: a moved capture forwarded through an explicit list, a Consuming closure passed by value to Callable<owner,
    // ...>, and an Exclusive closure passed with @uniq to Callable<uniq, ...>.
    [Theory]
    [InlineData("Forwarded", Inspect + "let text = \"abc\"\nlet outer = func [text@move] () -> i32\n    let inner = func [text@move] () => inspect(text@ref)\n    return inner@move()\nrequire outer@move() == 3 else => $abort(\"outer\")", "")]
    [InlineData("Receivers", "func apply<F>(f: F) -> string\n    F is Callable<owner, () -> string>\n    return f@move()\nfunc count<F>(f: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    return f()\nlet text = \"abc\"\nlet take = func [text@move] () => text@move\nConsole.writeLine(apply(take@move))\nlet start = 0\nvar step = func [var start] () -> i32\n    start += 1\n    return start\nlet first = count(step@uniq)\nlet second = count(step@uniq)\nrequire first == 1 and second == 2 else => $abort(\"count\")", "abc\n")]
    public void PermittedCapturesAndReceiversRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("CaptureDiagnostic" + name, source, stdout);
}

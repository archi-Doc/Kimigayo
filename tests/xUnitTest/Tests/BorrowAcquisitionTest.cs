// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 7.7.2, 8.14: a bare acquisition of a Place storing an exclusive reference Reborrows it, as a same-Type
// annotation would; capture entries initialize environment bindings the same way; generic bodies use finite plans.
public class BorrowAcquisitionTest
{
    private const string Bump = "func bump(target: uniq/i32) => target@follow += 1\n";

    private const string View = "struct P\n    public var x: i32 = 0\n    public init() => ()\nstruct H2 {a}\n    public var o: objuniq/P during a\n";

    private const string Link = "struct Holder2 {a}\n    public var link: uniq/i32 during a\n";

    [Theory]
    [InlineData("Initializer", "var number: i32 = 1\nlet r = number@uniq\nlet first = r\nfirst@follow += 1\nlet second: uniq/i32 = r\nsecond@follow += 1\nr@follow += 1\nrequire number == 4 else => $abort(\"initializer\")")]
    [InlineData("Discard", "var number: i32 = 1\nlet r = number@uniq\n_ = r\nr@follow += 1\nlet moved = r@move\nmoved@follow += 1\nrequire number == 3 else => $abort(\"discard\")")]
    [InlineData("Repeated", Bump + "var number: i32 = 1\nlet r = number@uniq\nbump(r)\nlet s = r\nbump(s)\nbump(r)\nrequire number == 4 else => $abort(\"repeated\")")]
    [InlineData("TupleElement", Bump + "var number: i32 = 1\nlet r = number@uniq\nlet pair = (r, 1)\nbump(pair.0)\nrequire number == 2 else => $abort(\"tuple\")")]
    [InlineData("FixedArrayElement", Bump + "var a: i32 = 1\nvar b: i32 = 5\nvar refs: [2 of uniq/i32] = [a@uniq, b@uniq]\nlet first = refs[0]\nbump(first)\nlet second: uniq/i32 = refs[1]\nbump(second)\nbump(refs[1])\n_ = refs[0]\nrequire a == 2 and b == 7 else => $abort(\"array\")")]
    [InlineData("Field", Bump + "struct Holder {source}\n    public var link: uniq/i32 during source\n    public init(link: uniq/i32 during source)\n        self.link = link@move\nfunc touch(holder: uniq/Holder)\n    let link = holder.link\n    bump(link)\n    bump(holder.link)\nvar number: i32 = 1\nvar holder = Holder.init(number@uniq)\ntouch(holder@uniq)\nrequire number == 3 else => $abort(\"field\")")]
    [InlineData("ObjectHandle", "struct Counter\n    var value: i32 = 0\n    public func add(self: uniq/Self, n: i32) => self.value = self.value + n\n    public func read(self: ref/Self) -> i32 => self.value\nvar owner = Kimi.Intrinsics.makeObj(Counter.init())\ndo\n    let target = owner@objuniq\n    let second = target\n    second.add(2)\n    target.add(3)\nrequire owner.read() == 5 else => $abort(\"objuniq\")")]
    [InlineData("CaptureReborrow", "var number: i32 = 1\nlet r = number@uniq\nlet view: ref/i32 = r\nlet reader = func [view] () -> i32 => view@follow\nlet snapshot = reader()\nvar bump = func [r] () => r@follow += 1\nbump()\nbump()\nrequire snapshot == 1 and number == 3 else => $abort(\"reborrow\")")]
    [InlineData("CaptureMove", "var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@move] () => r@follow += 1\nbump()\nbump()\nrequire number == 3 else => $abort(\"move\")")]
    [InlineData("CaptureSharedSlot", "var number: i32 = 1\nlet reader = func [number@ref] () -> i32 => number@follow\nrequire reader() == 1 else => $abort(\"ref\")\nnumber += 1\nrequire number == 2 else => $abort(\"after\")")]
    [InlineData("CaptureExclusiveSlot", "var number: i32 = 1\nvar bump = func [number@uniq] () -> ()\n    number@follow += 1\nbump()\nbump()\nrequire number == 3 else => $abort(\"uniq\")")]
    [InlineData("GenericPlan", "func count<s/T>(value: s/T) -> i32\n    s is owner or uniq\n    T is Copy\n    var total = 0\n    for i in 0..3\n        let local = value\n        _ = local\n        total += i\n    _ = value\n    return total\nfunc pass<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    let local = value\n    return local@move\nvar number: i32 = 1\nrequire count(number) == 3 and count(number@uniq) == 3 else => $abort(\"count\")\nlet copied = pass(number)\nlet reborrowed = pass(number@uniq)\nreborrowed@follow += 1\nrequire copied == 1 and number == 2 else => $abort(\"pass\")")]
    public void EmitsBareReborrows(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowAcquisition" + name, source, string.Empty);

    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@uniq] () => r@follow@follow += 1\nbump()", nameof(BindingFailure.InvalidAssignment))]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet bump = func [r] () => r@follow += 1\nbump()", nameof(BindingFailure.InvalidAssignment))]
    [InlineData("let text = \"owned\"\nlet copy = func [text] () => ()", nameof(BindingFailure.TransferRequired))]
    public void RejectsInvalidCaptureEntries(string source, string failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf().ToString() == failure);
    }

    // DIAGNOSTICS.md §10: a rejected entry is located at the entry and names the initialization it stands for; an omitted
    // list names the capture; an Exclusive call of a let closure names the callee.
    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r@uniq] () => r@follow@follow += 1\nbump()", "InvalidAssignment_Kd", "r@uniq", "r cannot be written", "slot of the let binding r", false)]
    [InlineData("let text = \"owned\"\nlet keep = func [text] () => ()\nkeep()", "TransferRequired_Kd", "text", "text cannot be read as a Copy value", "let text = text", true)]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet bump = func [r] () => r@follow += 1\nbump()", "InvalidAssignment_Kd", "bump", "bump cannot be written", "Exclusive", false)]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func () => r@follow += 1\nbump()", "TransferRequired_Kd", "func () => r@follow += 1", "r cannot be read as a Copy value", "omitted capture list", false)]
    [InlineData("let text = \"owned\"\nlet keep = func () => Console.writeLine(text)\nkeep()", "TransferRequired_Kd", "func () => Console.writeLine(text)", "text cannot be read as a Copy value", "omitted capture list", false)]
    public void ExplainsRejectedCaptures(string source, string code, string text, string label, string note, bool candidates)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
        Assert.Contains(note, error.Note);
        if (candidates)
        {
            // SPEC 23.1.3: a bare entry offers the transfer and the borrow as candidates at the entry.
            Assert.Equal(["Repair.Transfer", "Repair.Borrow"], error.Repairs!.Select(static x => x.Kind));
            Assert.Contains("[text@move]", UnnecessaryUnsafeBlockTest.Apply(source, error.Repairs![0].Edits), StringComparison.Ordinal);
            Assert.Contains("[text@ref]", UnnecessaryUnsafeBlockTest.Apply(source, error.Repairs[1].Edits), StringComparison.Ordinal);
            Assert.Equal([Kimi.Diagnostics.RepairCondition.Take], error.Repairs[0].Verified);
            Assert.Empty(error.Repairs[1].Verified);
        }
    }

    [Fact]
    public void ACaptureEntryExplainsOnlyItsOwnFailure()
    {
        // The closure already failed for its duplicate parameter, so the rejected entry records nothing: the duplicate is not
        // reported at the capture entry as if it were the entry's problem.
        const string Source = "let n: i32 = 1\nlet f = func [n@uniq] (a: i32, a: i32) -> i32 => a";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(Kimi.DiagnosticCode.DuplicateBinding_Kd), error.Code);
        Assert.NotEqual("n@uniq", error.Text);
    }

    [Theory]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nlet first = r\nr@follow += 1\nfirst@follow += 1")]
    [InlineData("var number: i32 = 1\nlet r = number@uniq\nvar bump = func [r] () => r@follow += 1\nr@follow += 1\nbump()")]
    [InlineData("var number: i32 = 1\nlet reader = func [number@ref] () -> i32 => number@follow\nnumber += 1\n_ = reader()")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value\n    _ = value\n    _ = local\nvar number: i32 = 1\nf(number)")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value@move\n    let b = local\n    _ = local\n    _ = b\nvar number: i32 = 1\nf(number)")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let local = value\n    let b = local\n    _ = value\n    _ = b\nvar number: i32 = 1\nf(number)")]
    public void RejectsUsesDuringReborrowLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
    }

    // SPEC 3.5 (R6-check H1, DECISIONS BU-84): a bare acquisition without a plan is Binding's TransferRequired_Kd at each such Place.
    [Theory]
    [InlineData("func f<T>(value: T) -> ()\n    let local = value\n    _ = local", "value|local")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    let local = value\n    _ = local", "value|local")]
    [InlineData("let text = \"owned\"\nlet copy = text", "text")]
    public void RejectsBareAcquisitionWithoutPlan(string source, string places)
        => Assert.Equal(
            places.Split('|').Select(static x => (DiagnosticCode.TransferRequired_Kd, x)),
            MinimalEmissionTest.Analyze(source).Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));

    // SPEC 3.5.1, 8.14, 8.15.2 (PLAN G262, DECISIONS BU-45): a bare argument is acquired by its bare acquisition, so a pair layer without
    // a plan in some case (its exclusive case through a shared path, or an owner whose target is not proven Copy) is Binding's
    // TransferRequired_Kd at the Place; the acquiring binder's own layers on the path are exclusive in that case, so an exclusive path, an
    // own-binder path and a direct pair keep their plans. Each row: Binding's errors.
    [Theory]
    [InlineData("s/T", "x: ref/(s/T during a) during b", "s is ref or uniq", "x@follow", "TransferRequired_Kd x@follow")]
    [InlineData("s/T, r/U", "x: r/(s/T during a) during b", "s is ref or uniq\n    r is ref or uniq", "x@follow", "TransferRequired_Kd x@follow")]
    [InlineData("s/T", "x: s/T", "s is owner or uniq", "x", "TransferRequired_Kd x")]
    [InlineData("s/T", "x: s/(s/T during a) during b", "s is ref or uniq", "x@follow", "")]
    [InlineData("s/T", "x: uniq/(s/T during a) during b", "s is ref or uniq", "x@follow", "")]
    [InlineData("s/T", "x: s/T", "s is ref or uniq", "x", "")]
    public void ABareArgumentIsAcquiredByItsPlan(string generics, string parameters, string constraints, string argument, string errors)
    {
        var source = $"func take<U>(y: U)\n    return\nfunc f<{generics}>({parameters})\n    {constraints}\n    take({argument})\n";
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 23.1.2, 15.6.1 (PLAN G262): a payload's acquisition and its fit are independent, so a bare Place without a plan (`x@follow`
    // through `ref` in the uniq case, an owned `q` at `uniq/i32`) whose Origin also misfits has both records, each twin one of them; a
    // misfit only in Origins is the relation at the payload, a structural one the construction's mismatch with no acquisition record.
    // A value call's are AValueCallsAcquisitionAndRelationsAreEachRecorded's. Each row: Binding's errors.
    [Theory]
    [InlineData("x: ref/(s/T during a) during b", "s is ref or uniq", "let o: Option<s/T> = .Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow")]
    [InlineData("x: ref/(s/T during a) during b", "s is ref", "let o: Option<s/T> = .Some(x@follow)\n    _ = o@move", "UnprovenOriginRelation_Kd x@follow")]
    [InlineData("x: ref/(s/T during a) during b", "s is ref or uniq", "let o = Option<s/T during b>.Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i32 during r> = .Some(q)\n    _ = o@move", "ExclusiveBorrowRequired_Kd q; UnsatisfiedOriginRelation_Kd q")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i32 during r> = .Some(q@uniq)\n    _ = o@move", "UnsatisfiedOriginRelation_Kd q@uniq")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i64> = .Some(q)\n    _ = o@move", "TypeMismatch_Kd .Some(q)")]
    public void APayloadsAcquisitionAndFitAreEachRecorded(string parameters, string constraints, string use, string errors)
    {
        var source = $"func f<s/T>({parameters}) -> ()\n    {constraints}\n    {use}\n";
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 3.8, 10, 23.1.2 (PLAN G421, DECISIONS BU-96): a value call selects no callee, so a failed acquisition of its callee or of an
    // argument (a bare Place without `@move` or `@uniq`, in some or every Semantics case) stops none of its other judgments: each argument's
    // Origin relation, A5's record, stays beside the acquisition record, before or after it, and the program stays rejected; a twin with
    // the spelling has the relation alone, one without a relation problem the acquisition alone. A named function call is no value call: a
    // bare Place's Copy proof is its candidate's applicability (SPEC 10.1 step 5), so the relations judged after selection rest on it.
    // Each row: the published records in order; an accepted program has none.
    [Theory]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32, g: (s/T, ref/i32 during static) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow, p)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow; UnprovenOriginRelation_Kd p")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32, g: (ref/i32 during static, s/T) -> ()) -> ()\n    s is ref or uniq\n    g(p, x@follow)\n", "UnprovenOriginRelation_Kd p; TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, y: ref/(s/T during a) during b, g: (s/T, s/T) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow, y@follow)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow; UnprovenOriginRelation_Kd y@follow")]
    [InlineData("func f(x: string, p: ref/i32, g: (string, ref/i32 during static) -> ()) -> ()\n    g(x, p)\n", "TransferRequired_Kd x; UnprovenOriginRelation_Kd p")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32) -> ()\n    s is ref or uniq\n    let h = func (v: s/T, w: ref/i32 during static) => ()\n    h(x@follow, p)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd x@follow; UnprovenOriginRelation_Kd p")]
    [InlineData("func f(x: i32, p: ref/i32, g: (uniq/i32, ref/i32 during static) -> ()) -> ()\n    var y: i32 = x\n    g(y, p)\n", "ExclusiveBorrowRequired_Kd y; UnprovenOriginRelation_Kd p")]
    [InlineData("func f(p: ref/i32) -> ()\n    let t: string = \"a\"\n    let c = func [t@move] (w: ref/i32 during static) -> ()\n        let u = t@move\n        return\n    c(p)\n", "TransferRequired_Kd c; UnprovenOriginRelation_Kd p")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T) -> ()) -> ()\n    s is ref\n    g(x@follow)\n", "UnprovenOriginRelation_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T during a) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T during a) -> ()) -> ()\n    s is ref\n    g(x@follow)\n", "")]
    [InlineData("func f<F>(x: string, p: ref/i32, g: F) -> ()\n    F is Callable<(string, ref/i32) -> ()>\n    g(x, p)\n", "TransferRequired_Kd x")]
    [InlineData("func g<s/T>(y: s/T) -> ()\n    s is ref or uniq\n    return\nfunc f<s/T>(x: ref/(s/T during a) during b) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func take<s/T>(v: s/T, w: ref/i32 during static) -> ()\n    s is ref or uniq\n    return\nfunc f<s/T>(x: ref/(s/T during a) during b, p: ref/i32) -> ()\n    s is ref or uniq\n    take(x@follow, p)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func take(v: string, w: ref/i32 during static) -> () => ()\nfunc f(x: string, p: ref/i32) -> ()\n    take(x, p)\n", "TransferRequired_Kd x")]
    public void AValueCallsAcquisitionAndRelationsAreEachRecorded(string body, string records)
    {
        var source = body + "public func main() => ()\n";
        var check = DiagnosticCorpus.Check(source);
        Assert.Equal(records, string.Join("; ", check.Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}")));
        Assert.Equal(records.Length == 0, check.Accepted);
    }

    // SPEC 6.5.2, 3.5, 8.15.2 (R6-check (b2)'s acceptance move): a payload Place is acquired by its bare acquisition, so `s/(uniq/i32)`, whose
    // owner case Reborrows its target, is no TransferRequired_Kd (before (b2) the owner case had no plan).
    [Fact]
    public void APayloadOfAnOwnerCaseOverAnExclusiveTargetIsAcquiredByCase()
    {
        var source = "func pass<s/T>(value: s/(uniq/i32 during a)) -> ()\n    s is owner or uniq\n    let o = Option<s/(uniq/i32 during a)>.Some(value)\n" +
            "    _ = o@move\npublic func main() => ()\n";
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
    }

    // SPEC 3.5.1, 10.2.2 (PLAN G232): at a fixed expected Type that it fits, a bare Place storing an exclusive reference, `uniq/T` or
    // `objuniq/T`, Reborrows as at a bare acquisition position, contained in the layers reaching it (DECISIONS BU-27): the leak is the
    // Origin relation's, the twin with the relation is accepted, at a `return` and an annotated `let`. Through a shared layer it has no
    // Reborrow (TransferRequired_Kd), and a pair layer keeps its by-case plan. Each row: the records.
    [Theory]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> objuniq/P during a\n    origin s outlives a\n    return h.o\n", "")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> objuniq/P during a\n    return h.o\n", "UnprovenOriginRelation_Kd h.o")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> ()\n    origin s outlives a\n    let x: objuniq/P during a = h.o\n    x.x = 3\n", "")]
    [InlineData(Link + "func f(h: uniq/(Holder2 during a) during s) -> uniq/i32 during a\n    origin s outlives a\n    return h.link\n", "")]
    [InlineData(Link + "func f(h: uniq/(Holder2 during a) during s) -> uniq/i32 during a\n    return h.link\n", "UnprovenOriginRelation_Kd h.link")]
    [InlineData(View + "func f(h: ref/(H2 during a) during s) -> objuniq/P during a\n    origin s outlives a\n    return h.o\n", "TransferRequired_Kd h.o")]
    [InlineData("func f<s/T>(x: s/T) -> s/T\n    s is ref or uniq\n    return x\n", "")]
    public void AFittingFixedExpectedTypeReborrowsAStoredExclusiveReference(string body, string records)
    {
        var source = body + "public func main() => ()\n";
        Assert.Equal(records, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}")));
    }
}

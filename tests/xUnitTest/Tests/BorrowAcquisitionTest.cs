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

    // A `confined` witness whose body calls a Closure `g` (SPEC 8.11.4).
    private const string Work = "contract Work\n    func run(self: ref/Self) -> i32\n        effect confined\nstruct W\n    Self is Work\n    public init() => ()\n" +
        "    public func run(self: ref/Self) -> i32\n        let g = func () -> i32 => 2\n";

    // R7-E4 E4a-3's preludes: a struct with an exclusive method and an exclusive input; a getter of it; an object handle's exclusive
    // borrow; a Node whose object borrows an object pair layer adapts to (PLAN G762).
    private const string Lent = "struct H\n    public var n: i32\n    public init()\n        self.n = 0\n    public func bump(self: uniq/Self) -> () => self.n = 1\n" +
        "    public func absorb(self: ref/Self, other: uniq/H) -> () => ()\nfunc take(x: uniq/i32) -> () => ()\n";

    private const string Getter = "struct G\n    public init() => ()\n    public computed p: H\n        get() -> H => H.init()\n";

    private const string Lender = "struct P\n    public var x: i32 = 0\n    public init() => ()\nfunc t(x: objuniq/P) -> () => ()\n";

    private const string Layer = "struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func peek(self: objref/Self) -> i32 => self.n\n" +
        "func take(x: objref/Node) -> i32 => x.n\nfunc f<s/T>(m: s/T, y: s/Node) -> i32\n    s is objref or objuniq\n";

    // A group whose stored property is a static root (its own Unsupported_Kd stands in every row).
    private const string Totals = "group Totals\n    public var count: i32 = 0\n";

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
    // TransferRequired_Kd at the Place, as a stored `uniq` reached through `ref` is (SPEC 3.5.1, PLAN G262); the acquiring binder's own
    // layers on the path are exclusive in that case, so an exclusive path, an own-binder path and a direct pair keep their plans. Each
    // row: Binding's errors.
    [Theory]
    [InlineData("s/T", "x: ref/(s/T during a) during b", "s is ref or uniq", "x@follow", "TransferRequired_Kd x@follow")]
    [InlineData("s/T", "x: ref/(uniq/i32 during a) during b", "s is ref", "x@follow", "TransferRequired_Kd x@follow")]
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

    // SPEC 3.5.1, 8.15.2 (DECISIONS BU-45): the pair layer's Reborrow through an exclusive path, and through the acquiring binder's own
    // layers into a payload, keeps its plan in every case, so check accepts the program. G254a (PLAN G254; Q17 class 1; SPEC 15.6.3): the
    // payload's Reborrow is contained in `b` too (WF gives `a outlives b`), so its valid payload is `Option<s/T during b>`; the former
    // `Option<s/T during a>` needs `b outlives a` and is APayloadsAcquisitionAndFitAreEachRecorded's row, as its concrete twin.
    [Theory]
    [InlineData("x: uniq/(s/T during a) during b", "let y = x@follow\n    _ = y@move")]
    [InlineData("x: s/(s/T during a) during b", "let o = Option<s/T during b>.Some(x@follow)\n    _ = o@move")]
    public void APairLayersReborrowThroughExclusiveLayersIsAccepted(string parameters, string use)
        => Assert.Empty(DiagnosticCorpus.Check($"func f<s/T>({parameters})\n    s is ref or uniq\n    {use}\npublic func main() => ()\n").Diagnostics);

    // SPEC 23.1.2, 15.6.1, 7.3.3 (R7-M95 E4a-4, superseding PLAN G262's reading): a payload's refused acquisition (`x@follow` through `ref`
    // in the uniq case, a stored `uniq/i32` through `ref`, an owned `q` at `uniq/i32`) fails it, its lent Type read for structure only, so
    // a misfit of its Origin, which reads that Type, records nothing (rule W); a misfit only in Origins is the relation at the payload, a
    // structural one the construction's mismatch with no acquisition record. A value call's are
    // AValueCallsAcquisitionAndRelationsAreEachRecorded's. Each row: the published Errors (R7-O1 H: the relation is A5's record, Binding's
    // twin gone).
    [Theory]
    [InlineData("x: ref/(s/T during a) during b", "s is ref or uniq", "let o: Option<s/T> = .Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow")]
    [InlineData("x: ref/(uniq/i32 during a) during b, y: ref/i32 during c", "s is ref", "let o: Option<uniq/i32 during c> = .Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow")]
    [InlineData("x: ref/(uniq/i32 during a) during b", "s is ref", "let o = Option<uniq/i32 during a>.Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow")]
    [InlineData("x: ref/(s/T during a) during b", "s is ref", "let o: Option<s/T> = .Some(x@follow)\n    _ = o@move", "UnprovenOriginRelation_Kd x@follow")]
    [InlineData("x: ref/(s/T during a) during b", "s is ref or uniq", "let o = Option<s/T during b>.Some(x@follow)\n    _ = o@move", "TransferRequired_Kd x@follow")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i32 during r> = .Some(q)\n    _ = o@move", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i32 during r> = .Some(q@uniq)\n    _ = o@move", "UnsatisfiedOriginRelation_Kd q@uniq")]
    [InlineData("r: ref/i32", "s is ref", "var q = 1\n    let o: Option<uniq/i64> = .Some(q)\n    _ = o@move", "TypeMismatch_Kd .Some(q)")]
    [InlineData("x: s/(s/T during a) during b", "s is ref or uniq", "let o = Option<s/T during a>.Some(x@follow)\n    _ = o@move", "UnprovenOriginRelation_Kd x@follow")]
    [InlineData("x: uniq/(uniq/T during a) during b", "s is ref", "let o = Option<uniq/T during a>.Some(x@follow)\n    _ = o@move", "UnprovenOriginRelation_Kd x@follow")]
    public void APayloadsAcquisitionAndFitAreEachRecorded(string parameters, string constraints, string use, string errors)
    {
        var source = $"func f<s/T>({parameters}) -> ()\n    {constraints}\n    {use}\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.PublishedErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 10.1 step 5, 23.1.2 (PLAN G453): a construction of an Origin-carrying structure is a named call, so a bare Place without its
    // spelling (`x@follow` through `ref`, an owned `q` at `uniq/i32`) is its acquisition record only when it is the candidate's one
    // obstacle, as at a plain function; beside a mismatching `true` the selection fails. Each row: Binding's errors.
    [Theory]
    [InlineData("func f(x: ref/(uniq/i32 during a) during b) -> ()\n    let h = Holder3.init(x@follow, true)\n", "NoApplicableOverload_Kd Holder3.init(x@follow, true)")]
    [InlineData("func f(x: ref/(uniq/i32 during a) during b) -> ()\n    let h = Holder3.init(x@follow, 1)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func f() -> ()\n    var q: i32 = 1\n    let h = Holder3.init(q, true)\n", "NoApplicableOverload_Kd Holder3.init(q, true)")]
    [InlineData("func f() -> ()\n    var q: i32 = 1\n    let h = Holder3.init(q, 1)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func f() -> ()\n    var q: i32 = 1\n    let h = Holder3.init(q@uniq, 1)\n", "")]
    public void AConstructionsAcquisitionIsRecordedOnlyAsItsOneObstacle(string body, string errors)
    {
        var source = "struct Holder3 {a}\n    public let v: uniq/i32 during a\n    public init(v: uniq/i32 during a, flag: i32) => self.v = v@move\n" + body;
        var c = CompilationTestHelper.ParseSuccess(source);
        c.Bind();
        Assert.Equal(errors, string.Join("; ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}")));
    }

    // SPEC 3.8, 10, 23.1.2 (PLAN G421, DECISIONS BU-96): a value call selects no callee, so an argument's failed acquisition (a bare Place
    // without `@move` or `@uniq`, in some or every Semantics case) stops none of its other judgments: every other argument's Origin
    // relation, A5's record, stays beside the acquisition record, before or after it, and the program stays rejected; the refused part, its
    // lent Type read for structure only, takes no operation, so its own relation records nothing (R7-M95 E4a-4, superseding BU-96 in part);
    // a twin with the spelling has the relation alone, one without a relation problem the acquisition alone. A failed callee (a Consuming
    // call's missing `c@move`, a payload callee through a reference) stops the call, so its own record rests on no argument. A named
    // function or method call is no value call: a bare Place's Copy proof is its candidate's applicability (SPEC 10.1 step 5), so the
    // relations judged after selection rest on it. Rule W (DECISIONS BU-79; PLAN G421 (ii), G494 unit 2): a failed or misfitting part,
    // before or after the others, one or two, at a Function-typed parameter or a Closure value, is a wildcard: every other part is still
    // fitted and its relation recorded (ab15 `g(p, undefinedName)`). Each row: the published records in order; an accepted program has
    // none.
    [Theory]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func f(x: ref/(uniq/i32 during a) during b, y: ref/i32 during c, g: (uniq/i32 during c) -> ()) -> ()\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32, g: (s/T, ref/i32 during static) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow, p)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd p")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32, g: (ref/i32 during static, s/T) -> ()) -> ()\n    s is ref or uniq\n    g(p, x@follow)\n", "UnprovenOriginRelation_Kd p; TransferRequired_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, y: ref/(s/T during a) during b, g: (s/T, s/T) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow, y@follow)\n", "TransferRequired_Kd x@follow; TransferRequired_Kd y@follow")]
    [InlineData("func f(x: string, p: ref/i32, g: (string, ref/i32 during static) -> ()) -> ()\n    g(x, p)\n", "TransferRequired_Kd x; UnprovenOriginRelation_Kd p")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, p: ref/i32) -> ()\n    s is ref or uniq\n    let h = func (v: s/T, w: ref/i32 during static) => ()\n    h(x@follow, p)\n", "TransferRequired_Kd x@follow; UnprovenOriginRelation_Kd p")]
    [InlineData("func f(x: i32, p: ref/i32, g: (uniq/i32, ref/i32 during static) -> ()) -> ()\n    var y: i32 = x\n    g(y, p)\n", "ExclusiveBorrowRequired_Kd y; UnprovenOriginRelation_Kd p")]
    [InlineData("func f(p: ref/i32) -> ()\n    let t: string = \"a\"\n    let c = func [t@move] (w: ref/i32 during static) -> ()\n        let u = t@move\n        return\n    c(p)\n", "TransferRequired_Kd c")]
    [InlineData("group State\n    public var counter: i32 = 0\nfunc call(r: ref/(rc/((ref/i32) -> ()))) -> () => r@follow(State.counter)\n", "Unsupported_Kd var counter: i32 = 0; Unsupported_Kd r@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T) -> ()) -> ()\n    s is ref\n    g(x@follow)\n", "UnprovenOriginRelation_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T during a) -> ()) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func f<s/T>(x: ref/(s/T during a) during b, g: (s/T during a) -> ()) -> ()\n    s is ref\n    g(x@follow)\n", "")]
    [InlineData("func f<F>(x: string, p: ref/i32, g: F) -> ()\n    F is Callable<(string, ref/i32) -> ()>\n    g(x, p)\n", "TransferRequired_Kd x")]
    [InlineData("func g<s/T>(y: s/T) -> ()\n    s is ref or uniq\n    return\nfunc f<s/T>(x: ref/(s/T during a) during b) -> ()\n    s is ref or uniq\n    g(x@follow)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func take<s/T>(v: s/T, w: ref/i32 during static) -> ()\n    s is ref or uniq\n    return\nfunc f<s/T>(x: ref/(s/T during a) during b, p: ref/i32) -> ()\n    s is ref or uniq\n    take(x@follow, p)\n", "TransferRequired_Kd x@follow")]
    [InlineData("struct H\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func take<s/T>(self: ref/Self, v: s/T, w: ref/i32 during static) -> ()\n        s is ref or uniq\n        return\nfunc f<s/T>(h: ref/H, x: ref/(s/T during a) during b, p: ref/i32) -> ()\n    s is ref or uniq\n    h.take(x@follow, p)\n", "TransferRequired_Kd x@follow")]
    [InlineData("func take(v: string, w: ref/i32 during static) -> () => ()\nfunc f(x: string, p: ref/i32) -> ()\n    take(x, p)\n", "TransferRequired_Kd x")]
    [InlineData("func f(x: string, p: ref/i32, g: (ref/i32 during static, string) -> ()) -> ()\n    g(p, undefinedName)\n", "UnprovenOriginRelation_Kd p; UnresolvedBinding_Kd undefinedName")]
    [InlineData("func f(p: ref/i32, g: (string, ref/i32 during static) -> ()) -> ()\n    g(undefinedName, p)\n", "UnresolvedBinding_Kd undefinedName; UnprovenOriginRelation_Kd p")]
    [InlineData("func f(p: ref/i32, g: (string, ref/i32 during static, string) -> ()) -> ()\n    g(undefinedName, p, 1 + true)\n", "UnresolvedBinding_Kd undefinedName; UnprovenOriginRelation_Kd p; NonNumericOperand_Kd 1 + true")]
    [InlineData("func f(g: (string, string) -> ()) -> ()\n    g(undefinedName, 5)\n", "UnresolvedBinding_Kd undefinedName; TypeMismatch_Kd 5")]
    [InlineData("func f(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32 during static, y: string) -> i32 => 5\n    return c(p, undefinedName)\n", "UnprovenOriginRelation_Kd p; UnresolvedBinding_Kd undefinedName")]
    public void AValueCallsAcquisitionAndRelationsAreEachRecorded(string body, string records)
    {
        var source = body + "public func main() => ()\n";
        var check = DiagnosticCorpus.Check(source);
        Assert.Equal(records, string.Join("; ", check.Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}")));
        Assert.Equal(records.Length == 0, check.Accepted);
    }

    // SPEC 7.7.3, 7.3.3, 3.4.3, 8.10, 13.5.5 (PLAN G1120, D-888): a value call acquires its callee like a method receiver, on the path
    // that continues below every ref and uniq layer to the Callable (a Closure, a Function value, a function item, a Callable F) or to a
    // handle's View Target: through any number of layers, also in a `confined` witness, with arguments and a result over its input;
    // exclusively only where every layer and the path grant exclusive capability (`uniq/ref/F` and `ref/uniq/F` supply no Exclusive
    // call). A Shared call reads an `obj` or `rc` payload through the layers; an Exclusive one through them is the located limit. A raw
    // pointer is no layer the path follows, so `p()` calls nothing. A result naming the call receiver through the layers keeps the Loan
    // of the Callable (SPEC 15.8.2, 15.6.3), so moving it while the result is used is a conflict. Fail-before (dev 7979e3a56):
    // NotCallable_Kd or Unsupported_Kd on the valid forms, `ur()` and `p()` accepted (InternalInvariant_Kd at build), that Move accepted;
    // on 11974eb35, Unsupported_Kd at `ro`, `uo` and `ur`. Each row: the published records in order; an accepted program has none.
    [Theory]
    [InlineData("func f() -> i32\n    let g = func () -> i32 => 2\n    let r = g@ref\n    let rr = r@ref\n    let rrr = rr@ref\n    return rrr()\n", "")]
    [InlineData("func f() -> i32\n    let g = func (x: ref/i32) -> ref/i32 => x\n    let r = g@ref\n    let rr = r@ref\n    let v: i32 = 5\n    let got = rr(v@ref)\n    return got\n", "")]
    [InlineData("func f() -> i32\n    let count: i32 = 0\n    var next = func [var count] () -> i32\n        count += 1\n        return count\n    var u = next@uniq\n    var uu = u@uniq\n    let uuu = uu@uniq\n    return uuu()\n", "")]
    [InlineData("func f(g: (i32) -> i32) -> i32\n    let r = g@ref\n    let rr = r@ref\n    return rr(4)\n", "")]
    [InlineData("func helper(a: i32, b: i32) -> i32 => a + b\nfunc f() -> i32\n    let h = helper\n    let r = h@ref\n    let rr = r@ref\n    let rrr = rr@ref\n    return rrr(2, 3)\n", "")]
    [InlineData("func f<F>(g: ref/F) -> i32\n    F is Callable<() -> i32>\n    let r = g@ref\n    let rr = r@ref\n    return rr()\n", "")]
    [InlineData("func f<F>(g: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    var u = g\n    let uu = u@uniq\n    return uu()\n", "")]
    [InlineData(Work + "        let r = g@ref\n        let rr = r@ref\n        return rr()\n", "")]
    [InlineData(Work + "        let r = g@ref\n        let rr = r@ref\n        let rrr = rr@ref\n        return rrr()\n", "")]
    [InlineData("func f() -> i32\n    let count: i32 = 0\n    var next = func [var count] () -> i32\n        count += 1\n        return count\n    var r = next@ref\n    let ur = r@uniq\n    return ur()\n", "InvalidAssignment_Kd ur")]
    [InlineData("func f<F>(g: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    let r = g@ref\n    return r()\n", "InvalidAssignment_Kd r")]
    [InlineData("func f() -> i32\n    var g = func () -> i32 => 2\n    let p = g@raw\n    return p()\n", "NotCallable_Kd p()")]
    [InlineData("func f() -> i32\n    let o = Kimi.Intrinsics.makeObj(func () -> i32 => 2)\n    let ro = o@ref\n    return ro()\n", "")]
    [InlineData("func f() -> i32\n    var o = Kimi.Intrinsics.makeObj(func () -> i32 => 2)\n    let uo = o@uniq\n    return uo()\n", "")]
    [InlineData("func f() -> i32\n    var r = Kimi.Intrinsics.makeRc(func () -> i32 => 2)\n    let ur = r@uniq\n    return ur()\n", "")]
    [InlineData("func f() -> i32\n    let count: i32 = 0\n    var next = func [var count] () -> i32\n        count += 1\n        return count\n    var o = Kimi.Intrinsics.makeObj(next@move)\n    let uo = o@uniq\n    return uo()\n", "Unsupported_Kd uo")]
    [InlineData("func f() -> i32\n    let n: i32 = 7\n    let g = func [n] () => n@ref\n    let r = g@ref\n    let rr = r@ref\n    let v = rr()\n    let moved = g@move\n    return v\n", "ComparisonLoanConflict_Kd g")]
    public void AValueCalleeIsAcquiredThroughEveryReferenceLayer(string body, string records)
        => this.AValueCallsAcquisitionAndRelationsAreEachRecorded(body, records);

    // SPEC 7.3.3, 15.1.5, 15.6.2, 10.2 (PLAN G453, R7-E4 E4a-3; each row labelled from SPEC before its run): an implicit exclusive
    // acquisition the lending rule denies, a Receiver Expression's (7.3.3: as if the table's operation were written on it) or a bare
    // argument's at `uniq/U` or `objuniq/U`, a payload's or a value call's input, records what its written twin records, at the Place
    // (a `let` root or an owned parameter InvalidAssignment_Kd, a shared layer on the path or below the receiver SharedPathAccess_Kd),
    // not NoApplicableOverload_Kd; a lendable Place without its spelling keeps ExclusiveBorrowRequired_Kd. A construction receiver's
    // Field (6.3.4, valid) is BU-15's limit located at the Place, while a bare argument's missing `@uniq` stays its error (ruling 12 B1),
    // and an object pair layer at a fixed object borrow is PLAN G762's limit, located at the Place. The named remainder keeps
    // NoApplicableOverload_Kd: a pair-layer receiver (13.5.5) and a getter result (11.5). Each row: the published records in order; an
    // accepted program has none.
    [Theory]
    [InlineData(Lent + "func k() -> ()\n    var p = H.init()\n    p.bump()\n", "")]
    [InlineData(Lent + "func k() -> ()\n    let p = H.init()\n    p.bump()\n", "InvalidAssignment_Kd p")]
    [InlineData(Lent + "func k() -> ()\n    let p = H.init()\n    p@uniq.bump()\n", "InvalidAssignment_Kd p")]
    [InlineData(Lent + "func k(p: H) -> ()\n    p.bump()\n", "InvalidAssignment_Kd p")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    h.bump()\n", "SharedPathAccess_Kd h")]
    [InlineData(Lent + "func k(h: uniq/H) -> ()\n    h.bump()\n", "")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    h@follow.bump()\n", "SharedPathAccess_Kd h@follow")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    h@follow@uniq.bump()\n", "SharedPathAccess_Kd h@follow")]
    [InlineData(Lent + "func k(x: ref/(uniq/H during a) during b) -> ()\n    origin a outlives b\n    x.bump()\n", "SharedPathAccess_Kd x")]
    [InlineData(Lent + "func k(x: uniq/(ref/H during a) during b) -> ()\n    origin a outlives b\n    x.bump()\n", "SharedPathAccess_Kd x")]
    [InlineData(Lent + "struct W\n    public var h: H\n    public init()\n        self.h = H.init()\n        self.h.bump()\n", "Unsupported_Kd self.h")]
    [InlineData(Lent + "struct W\n    public var h: H\n    public init()\n        self.h = H.init()\n        self.h@uniq.bump()\n", "Unsupported_Kd self.h@uniq")]
    [InlineData(Lent + "func k<s/T>(x: s/H) -> ()\n    s is ref or uniq\n    x.bump()\n", "NoApplicableOverload_Kd x.bump()")]
    [InlineData(Lent + Getter + "func k(g: G) -> ()\n    g.p.bump()\n", "NoApplicableOverload_Kd g.p.bump()")]
    [InlineData(Lent + Getter + "func k(g: G) -> ()\n    g.p@uniq.bump()\n", "InvalidAssignment_Kd g.p")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    take(h.n)\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    take(h.n@uniq)\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k() -> ()\n    let q: i32 = 1\n    take(q)\n", "InvalidAssignment_Kd q")]
    [InlineData(Lent + "func k() -> ()\n    var q: i32 = 1\n    take(q)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k() -> ()\n    var q: i32 = 1\n    take(q@uniq)\n", "")]
    [InlineData(Lent + "func g<T>(x: uniq/T) -> () => ()\nfunc k(h: ref/H) -> ()\n    g(h.n)\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func use(o: Option<uniq/i32 during a>) -> () => ()\nfunc k(h: ref/H) -> ()\n    use(.Some(h.n))\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k(h: ref/H, f: (uniq/i32) -> ()) -> ()\n    f(h.n)\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "struct W\n    public var h: H\n    public init()\n        self.h = H.init()\n        let k = H.init()\n        k.absorb(self.h)\n", "ExclusiveBorrowRequired_Kd self.h")]
    [InlineData(Lent + "struct W\n    public var h: H\n    public init()\n        self.h = H.init()\n        let k = H.init()\n        k.absorb(self.h@uniq)\n", "Unsupported_Kd self.h@uniq")]
    [InlineData(Lender + "func k(o: obj/P) -> ()\n    t(o)\n", "InvalidAssignment_Kd o")]
    [InlineData(Lender + "func k(o: obj/P) -> ()\n    t(o@objuniq)\n", "InvalidAssignment_Kd o")]
    [InlineData(Lender + "func k(o: obj/P) -> ()\n    var v = o@move\n    t(v)\n", "ExclusiveBorrowRequired_Kd v")]
    [InlineData(Layer + "    let v = take(y)\n    return 0\n", "Unsupported_Kd y")]
    [InlineData(Layer + "    let v = y.peek()\n    return 0\n", "Unsupported_Kd y")]
    public void ADeniedImplicitAcquisitionRecordsItsWrittenTwinsFailureAtThePlace(string body, string records)
        => this.AValueCallsAcquisitionAndRelationsAreEachRecorded(body, records);

    // SPEC 10.2, 7.3.3, 15.1.5, 15.6.1, 23.1.2 (R7-M95 E4a-4, PLAN G486): a fixed destination (an annotated `let` or `var`, an
    // assignment, a Tuple part, a `return` or an expression body, a branch, a default argument, a payload, a value call's argument)
    // defers its implicit acquisition, and a refused one fails the Place with what its written twin records: a lendable Place without its
    // spelling ExclusiveBorrowRequired_Kd, a `let` root, an owned parameter or a capture by value InvalidAssignment_Kd, a shared layer
    // SharedPathAccess_Kd, a stored exclusive reference through a shared layer TransferRequired_Kd, an object pair layer PLAN G762's
    // limit. The Place delivers no Type: its lent Type is read for structure only, so the operation's limits (P42's static root) and the
    // Origin relations reading it record nothing, while a later use of the binding takes no record and an independent failure keeps its
    // own (rule W). A structural misfit stays the value's TypeMismatch_Kd. Each row: the published records in order.
    [Theory]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func k() -> ()\n    let q: i32 = 1\n    let y: uniq/i32 = q\n", "InvalidAssignment_Kd q")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    let y: uniq/i32 = h.n\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lender + "func k(o: obj/P) -> ()\n    let y: objuniq/P = o\n", "InvalidAssignment_Kd o")]
    [InlineData(Lender + "func k(o: obj/P) -> ()\n    var v = o@move\n    let y: objuniq/P = v\n", "ExclusiveBorrowRequired_Kd v")]
    [InlineData(Lent + "func k() -> ()\n    var h = H.init()\n    let y: uniq/i32 = h.n\n", "ExclusiveBorrowRequired_Kd h.n")]
    [InlineData(Lent + "func k() -> ()\n    let h = H.init()\n    let y: uniq/i32 = h.n\n", "InvalidAssignment_Kd h.n")]
    [InlineData("func k() -> ()\n    let xs: [2 of i32] = [1, 2]\n    let y: uniq/i32 = xs[0]\n", "InvalidAssignment_Kd xs[0]")]
    [InlineData(Lent + "func k(x: uniq/(ref/H during a) during b) -> ()\n    origin a outlives b\n    let y: uniq/i32 = x.n\n", "SharedPathAccess_Kd x.n")]
    [InlineData("func k(xs: ref/Array<i32>) -> ()\n    let y: uniq/i32 = xs[0]\n", "SharedPathAccess_Kd xs[0]")]
    [InlineData("func k() -> ()\n    var xs: Array<i32> = [1, 2]\n    let y: uniq/i32 = xs[0]\n", "ExclusiveBorrowRequired_Kd xs[0]")]
    [InlineData("func k() -> ()\n    var d: Dictionary<i64, i32> = [:]\n    let y: uniq/i32 = d[1]\n", "ExclusiveBorrowRequired_Kd d[1]")]
    [InlineData("func k() -> ()\n    let xs: Array<i32> = [1, 2]\n    let y: uniq/i32 = xs[0]\n", "InvalidAssignment_Kd xs[0]")]
    [InlineData("func k(d: ref/Dictionary<i64, i32>) -> ()\n    let y: uniq/i32 = d[1]\n", "SharedPathAccess_Kd d[1]")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let t: (uniq/i32, i32) = (q, 1)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    var w: i32 = 2\n    var y: uniq/i32 = w@uniq\n    y = q\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k(h: ref/H) -> ()\n    var w: i32 = 1\n    var y: uniq/i32 = w@uniq\n    y = h.n\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k(h: uniq/H) -> uniq/i32 during h\n    return h.n\n", "ExclusiveBorrowRequired_Kd h.n")]
    [InlineData(Lent + "func k(h: ref/H) -> uniq/i32 during h\n    return h.n\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k(h: uniq/H) -> uniq/H during h\n    return h@follow\n", "ExclusiveBorrowRequired_Kd h@follow")]
    [InlineData(Lent + "func k(h: uniq/H) -> uniq/i32 during h => h.n\n", "ExclusiveBorrowRequired_Kd h.n")]
    [InlineData(Lent + "func k(h: ref/H) -> uniq/i32 during h => h.n\n", "SharedPathAccess_Kd h.n")]
    [InlineData(Lent + "func k(h: uniq/H, c: bool) -> uniq/i32 during h\n    return if c => h.n@uniq else => h.n\n", "ExclusiveBorrowRequired_Kd h.n")]
    [InlineData("func k(c: bool) -> ()\n    var q: i32 = 1\n    var w: i32 = 2\n    let y: uniq/i32 = if c => w@uniq else => q\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Totals + "func f(x: uniq/i32 = Totals.count) -> () => ()\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData("func f(a: i32, b: uniq/i32 = a) -> () => ()\n", "InvalidAssignment_Kd a")]
    [InlineData(Lent + "func f(h: ref/H, x: uniq/i32 = h.n) -> () => ()\n", "SharedPathAccess_Kd h.n")]
    [InlineData("struct C\n    public var n: i32\n    public init()\n        self.n = 0\n        let y: uniq/i32 = self.n\n", "ExclusiveBorrowRequired_Kd self.n")]
    [InlineData("func k() -> ()\n    var c: i32 = 1\n    let f = func [var c] () -> ()\n        let y: uniq/i32 = c\n", "ExclusiveBorrowRequired_Kd c")]
    [InlineData("func k() -> ()\n    let c: i32 = 1\n    let f = func [c] () -> ()\n        let y: uniq/i32 = c\n", "InvalidAssignment_Kd c")]
    [InlineData(Totals + "func k() -> ()\n    let y: uniq/i32 = Totals.count\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData(Layer + "    let v: objref/Node = y\n    return 0\n", "Unsupported_Kd y")]
    [InlineData(Layer + "    let t: (objref/Node, i32) = (y, 1)\n    return 0\n", "Unsupported_Kd y")]
    [InlineData("struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc f<s/T>(m: s/T, y: s/Node during a) -> objref/Node during a\n    s is objref or objuniq\n    return y\n", "Unsupported_Kd y")]
    [InlineData("struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc f<s/T>(m: s/T, y: s/Node during a) -> objref/Node during a\n    s is objref\n    return y\n", "Unsupported_Kd y")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    let z: uniq/i32 = q\n", "ExclusiveBorrowRequired_Kd q; ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    take(y)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    let z: uniq/i32 = q@uniq\n    take(y)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    let b: bool = 1\n", "ExclusiveBorrowRequired_Kd q; TypeMismatch_Kd 1")]
    [InlineData(Lent + "func k() -> ()\n    var h = H.init()\n    let y: uniq/i32 = h.n\n    h.bump()\n    take(y)\n", "ExclusiveBorrowRequired_Kd h.n")]
    [InlineData(Lent + "func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    q = 2\n    take(y)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i32 = q\n    let z: uniq/i64 = q\n", "ExclusiveBorrowRequired_Kd q; TypeMismatch_Kd q")]
    [InlineData(Lent + "func k(h: uniq/H) -> uniq/i32 during h\n    var q: i32 = 1\n    return q\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k(h: uniq/H) -> Option<uniq/i32 during h>\n    var q: i32 = 1\n    return .Some(q)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Totals + "func k() -> ()\n    let o: Option<uniq/i32> = .Some(Totals.count)\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData(Totals + Lent + "func k() -> ()\n    take(Totals.count)\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData(Totals + "func take3(o: Option<uniq/i32 during a>) -> () => ()\nfunc k() -> ()\n    take3(.Some(Totals.count))\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData(Totals + "func take(x: uniq/i32) -> () => ()\nfunc k() -> ()\n    let f: (uniq/i32) -> () = take\n    f(Totals.count)\n", "Unsupported_Kd var count: i32 = 0; ExclusiveBorrowRequired_Kd Totals.count")]
    [InlineData("func k(h: ref/(uniq/i32 during a) during b) -> ()\n    origin a outlives b\n    let y: uniq/i32 = h@follow\n", "TransferRequired_Kd h@follow")]
    [InlineData("func k(h: ref/(uniq/i32 during a) during b) -> ()\n    origin a outlives b\n    _ = h@follow\n", "TransferRequired_Kd h@follow")]
    [InlineData("func k(h: ref/(uniq/i32 during a) during b) -> ()\n    origin a outlives b\n    let t = (h@follow, 1)\n", "TransferRequired_Kd h@follow")]
    [InlineData(Lender + "func k(h: ref/(objuniq/P during a) during b) -> ()\n    origin a outlives b\n    let y = h@follow\n", "TransferRequired_Kd h@follow")]
    [InlineData("func k() -> ()\n    var q: i32 = 1\n    let y: uniq/i64 = q\n", "TypeMismatch_Kd q")]
    [InlineData("func take3(o: Option<uniq/i32 during a>) -> () => ()\nfunc k() -> ()\n    var q: i32 = 1\n    take3(.Some(q))\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("func take2(t: (uniq/i32 during a, i32)) -> () => ()\nfunc k() -> ()\n    var q: i32 = 1\n    take2((q, 1))\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData(Lent + "func k() -> ()\n    let f: (uniq/i32) -> () = take\n    var q: i32 = 1\n    f(q)\n", "ExclusiveBorrowRequired_Kd q")]
    [InlineData("struct H\n    public var n: i32\n    public init()\n        self.n = 0\n    public func get(self: uniq/Self) -> uniq/i32 during self => self.n@uniq\nfunc k(h: uniq/H, r: ref/H) -> uniq/i32 during h\n    return r.get()\n", "SharedPathAccess_Kd r")]
    [InlineData("struct H\n    public var n: i32\n    public init()\n        self.n = 0\n    public func get(self: uniq/Self) -> uniq/i32 during self => self.n@uniq\nfunc k(h: uniq/H) -> uniq/i32 during h\n    let x = H.init()\n    return x.get()\n", "InvalidAssignment_Kd x")]
    public void ARefusedAcquisitionAtAFixedDestinationFailsThePlace(string body, string records)
        => this.AValueCallsAcquisitionAndRelationsAreEachRecorded(body, records);

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
    // Origin relation's, the twin with the relation is accepted, at a `return`, an annotated `let` and a payload. Through a shared layer it
    // has no Reborrow (TransferRequired_Kd at the Place, SPEC 3.5.1), and a pair layer keeps its by-case plan. Each row: the records.
    [Theory]
    [InlineData("func f(x: uniq/(uniq/i32 during a) during b) -> Option<uniq/i32 during a>\n    origin b outlives a\n    return .Some(x@follow)\n", "")]
    [InlineData(View + "func f(h: ref/(H2 during a) during s) -> Option<objuniq/P during a>\n    origin s outlives a\n    return .Some(h.o)\n", "TransferRequired_Kd h.o")]
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

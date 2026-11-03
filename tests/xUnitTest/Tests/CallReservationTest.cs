// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class CallReservationTest(ITestOutputHelper output)
{
    private const string Cell = "struct Cell\n    public var value: i32 = 1\n    public func set(self: uniq/Self, n: i32) => self.value = n\nfunc set(c: uniq/Cell, n: i32) => c.value = n\nfunc read(c: ref/Cell) -> i32 => c.value\n";
    private const string ObjCell = "struct ObjCell\n    public var value: i32 = 1\n    public func put(self: objuniq/Self, n: i32)\n        let p = self@follow@uniq\n        p.value = n\n";
    private const string Both = "func both(c: uniq/Cell, other: uniq/Cell) => ()\n";
    private const string Change = "func change(c: uniq/Cell) -> i32\n    c.value = 2\n    return 3\n";
    private const string Counter = "struct Counter\n    public var value: i32 = 1\n    public func add(self: uniq/Self, n: i32) => self.value += n\n    public func take(self: uniq/Self) -> i32\n        let n = self.value\n        self.value = 0\n        return n\n";
    private const string Drain = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\nstruct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public init(inner: J) => self.inner = inner@move\n    public func take(self: uniq/Self) -> Option<J.Item> => self.inner.next()\n    public func reset(self: uniq/Self, fresh: J) => self.inner = fresh@move\n";
    private const string Keeper = "struct Store\n    public var value: i32 = 1\n    public func lend(self: uniq/Self during source) -> Lent during source => Lent.init(self)\nstruct Lent {source}\n    var store: uniq/Store during source\n    public init(store: uniq/Store during source) => self.store = store\nstruct Keeper {source}\n    var inner: Lent during source\n    public init(inner: Lent during source) => self.inner = inner@move\n    public func reset(self: uniq/Self, fresh: Lent during source) => self.inner = fresh@move\n    public func peek(self: ref/Self) -> i32 => 1\n";
    private const string ReservationConflict = "This operation conflicts with an exclusive call reservation; only shared inspection is permitted during preparation";
    private const string LoanConflict = "This operation conflicts with an active loan";
    private const string Retaining = " (value retaining the conflicting loan)"; // A6's `Store` witness: a `loan` at the holder's declaration.
    private const string ReservationLabel = "conflicting exclusive call reservation";
    private const string BorrowLabel = "borrow that created the loan";
    private const string LaterUseLabel = "later use of the value retaining the conflicting loan";

    [Theory]
    [InlineData("Explicit", "var c = Cell.init()\nset(c@uniq, c.value + 1)")]
    [InlineData("Typed", "var c = Cell.init()\nset((c@uniq/Cell), read(c))")]
    [InlineData("Method", "var c = Cell.init()\nc@uniq.set(read(c))")]
    [InlineData("Reborrow", "var c = Cell.init()\nlet u = c@uniq\nset(u@follow@uniq, read(u))")]
    [InlineData("Parameter", "func run(c: uniq/Cell) => set(c, read(c))\nvar c = Cell.init()\nrun(c@uniq)")]
    [InlineData("OldShared", "var c = Cell.init()\nlet r = c@ref\nset(c@uniq, read(r))")]
    [InlineData("Named", "var c = Cell.init()\nset(c: c@uniq, n: read(c))")]
    [InlineData("NamedOrder", "func change(c: uniq/Cell) -> i32\n    c.value = 4\n    return c.value\nvar c = Cell.init()\nset(n: change(c@uniq), c: c@uniq)\nrequire c.value == 4 else => $abort(\"order\")")]
    [InlineData("Replace", "var p: i32 = 1\nKimi.Intrinsics.replace(p@uniq, with: p + 1)")]
    [InlineData("ExplicitExchange", "var p: i32 = 1\nlet old = Kimi.Intrinsics.exchange(p@uniq, with: p + 1)")]
    [InlineData("Default", "func next(c: uniq/Cell, n: i32 = c.value + 1) => c.value = n\nvar c = Cell.init()\nnext(c@uniq)\nrequire c.value == 2 else => $abort(\"default\")")]
    [InlineData("DefaultReceiver", "struct Bump\n    public var value: i32 = 1\n    public func next(self: uniq/Self, n: i32 = self.value + 1) => self.value = n\nvar c = Bump.init()\nc@uniq.next()\nrequire c.value == 2 else => $abort(\"receiver default\")")]
    [InlineData("Payload", "var o = Kimi.Intrinsics.makeObj(Cell.init())\nset(o@follow@uniq, read(o@follow@ref))")]
    [InlineData("PayloadMethod", "var o = Kimi.Intrinsics.makeObj(Cell.init())\no.set(read(o@follow@ref))")]
    [InlineData("ReplaceBorrowed", "func update(c: uniq/Cell) => Kimi.Intrinsics.replace(c, with: Cell.init())\nvar c = Cell.init()\nupdate(c@uniq)")]
    [InlineData("Disjoint", "func two(a: uniq/Cell, b: uniq/Cell)\n    a.value = 2\n    b.value = 3\nvar pair = (Cell.init(), Cell.init())\ntwo(pair.0@uniq, pair.1@uniq)")]
    [InlineData("Caught", "var c = Cell.init()\nset(c@uniq, (label scope: do => exit to scope read(c)))")]
    [InlineData("Return", "func run() -> i32\n    var c = Cell.init()\n    defer => c.value = 7\n    set(c@uniq, do => return read(c))\n    return 0\nrequire run() == 1 else => $abort(\"return\")")]
    [InlineData("Generic", "func run<T>(c: uniq/Cell, x: ref/T) => set(c@follow@uniq, read(c))\nvar c = Cell.init()\nlet x = true\nrun(c@uniq, x)")]
    [InlineData("Constructor", "struct S\n    public init(c: uniq/Cell, n: i32) => c.value = n\nvar c = Cell.init()\nlet s = S.init(c@uniq, read(c))")]
    [InlineData("Indirect", "let concrete = func (c: uniq/Cell, n: i32) => c.value = n\nlet f: (uniq/Cell, i32) -> () = concrete\nvar c = Cell.init()\nf(c@uniq, read(c) + 1)\nrequire c.value == 2 else => $abort(\"indirect\")")]
    [InlineData("Concrete", "let f = func (c: uniq/Cell, n: i32) => c.value = n\nvar c = Cell.init()\nf(c@uniq, read(c) + 1)\nrequire c.value == 2 else => $abort(\"concrete\")")]
    [InlineData("Deferred", "func run(flag: bool)\n    var c = Cell.init()\n    defer => set(c@uniq, read(c) + 1)\n    if flag => return\nrun(true)\nrun(false)")]
    [InlineData("GenericArgument", "func use<T>(c: uniq/T, n: i32) => ()\nvar c = Cell.init()\nuse(c@uniq, read(c))")]
    [InlineData("ExplicitCallable", "func inspect<T>(value: ref/T) -> i32 => 2\nlet n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nrequire (f@uniq)(inspect(f)) == 3 else => $abort(\"callable\")")]
    [InlineData("ImplicitCallable", "func inspect<T>(value: ref/T) -> i32 => 2\nlet n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nrequire f@uniq(inspect(f)) == 3 else => $abort(\"callable\")")]
    [InlineData("DisjointReborrow", "func two(a: uniq/Cell, b: uniq/Cell)\n    a.value = 2\n    b.value = 3\nvar pair = (Cell.init(), Cell.init())\nlet u = pair@uniq\ntwo(u.0@uniq, u.1@uniq)")]
    [InlineData("MultipleDefaults", "func next(c: uniq/Cell, n: i32 = c.value, m: i32 = c.value + n) => c.value = m\nvar c = Cell.init()\nnext(c@uniq)\nnext(c@uniq)\nrequire c.value == 4 else => $abort(\"defaults\")")]
    [InlineData("Object", "func put(o: objuniq/Cell, n: i32) => set(o@follow@uniq, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o@objuniq, read(o@follow@ref) + 1)\nrequire read(o@follow@ref) == 2 else => $abort(\"object\")")]
    [InlineData("ObjectReborrow", "func put(o: objuniq/Cell, n: i32) => set(o@follow@uniq, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq\nput(u@objuniq, read(u@follow@ref) + 1)\nrequire read(u@follow@ref) == 2 else => $abort(\"object reborrow\")")]
    [InlineData("ObjectShared", "func peek(o: objref/Cell) -> i32 => read(o@follow@ref)\nfunc put(o: objuniq/Cell, n: i32) => set(o@follow@uniq, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o@objuniq, peek(o@objref) + 1)")]
    [InlineData("ObjectTyped", "func put(o: objuniq/Cell, n: i32) => set(o@follow@uniq, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput((o@objuniq/Cell), read(o@follow@ref) + 1)")]
    [InlineData("ObjectReceiver", "struct ObjCell\n    public var value: i32 = 1\n    public func put(self: objuniq/Self, n: i32)\n        let p = self@follow@uniq\n        p.value = n\nvar o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet n = (o@follow@ref).value\no@objuniq.put(n + 1)\nrequire (o@follow@ref).value == 2 else => $abort(\"object receiver\")")]
    [InlineData("LoopTransfer", "var c = Cell.init()\nvar n: i32 = 0\nwhile n < 2\n    n += 1\n    set(c@uniq, (if n == 1 => continue else => read(c)))\nrequire n == 2 else => $abort(\"continue\")")]
    [InlineData("ImplicitReceiver", "var c = Cell.init()\nc.set(read(c) + 1)\nrequire c.value == 2 else => $abort(\"implicit receiver\")")]
    public void AcceptsPreparedReads(string name, string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Analysis.IsVerified, name + "\n" + string.Join('\n', c.Problems));
        Assert.True(c.Generate(TextWriter.Null), name + ": " + MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("CallReservation" + name, Cell + body, string.Empty);
    }

    // SPEC 15.1.5, 7.3: an owned Place is never lent exclusively at an argument position without
    // @uniq/@objuniq; only a Receiver Expression is acquired implicitly.
    [Theory]
    [InlineData("var c = Cell.init()\nset(c, c.value + 1)")]
    [InlineData("func run(c: uniq/Cell) => set(c, read(c))\nvar c = Cell.init()\nrun(c)")]
    [InlineData("var p: i32 = 1\nKimi.Intrinsics.replace(p, with: p + 1)")]
    [InlineData("func put(o: objuniq/Cell, n: i32) => set(o@follow@uniq, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o, read(o@follow@ref) + 1)")]
    public void RejectsImplicitExclusiveLending(string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ExclusiveBorrowRequired_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("var c = Cell.init()\nlet r = c@uniq\nset(r, c.value)")]
    [InlineData("func both(c: uniq/Cell, other: ref/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@ref)")]
    [InlineData("func both(c: uniq/Cell, other: uniq/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@uniq)")]
    [InlineData("var c = Cell.init()\nlet r = c@ref\nset(c@uniq, read(r))\nlet n = r.value")]
    [InlineData("func change(c: uniq/Cell) -> i32\n    c.value = 2\n    return 3\nvar c = Cell.init()\nset(c@uniq, change(c@uniq))")]
    [InlineData("func same(c: uniq/Cell) -> uniq/Cell during c => c\nvar c = Cell.init()\nset(same(c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((if true => c@uniq else => c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((do => c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((match true\n    true => c@uniq\n    false => c@uniq), read(c))")]
    [InlineData("func both(other: ref/Cell, c: uniq/Cell) => ()\nvar c = Cell.init()\nboth(c@ref, c@uniq)")]
    [InlineData("let f = func (c: uniq/Cell, r: ref/Cell) => ()\nvar c = Cell.init()\nf(c@uniq, c@ref)")]
    [InlineData("var c = Cell.init()\nlet r = c@ref\nlet f = func [r] () => read(r)\nset(c@uniq, f())\nlet n = f()")]
    [InlineData("var p: i32 = 1\nKimi.Intrinsics.swap(p@uniq, p@uniq)")]
    [InlineData("var text = \"owned\"\nKimi.Intrinsics.exchange(text@uniq, with: text@move)")]
    [InlineData("func both(o: objuniq/Cell, r: objref/Cell) => ()\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nboth(o@objuniq, o@objref)")]
    [InlineData("func put(o: objuniq/Cell, n: i32) => ()\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq\nput(u@objuniq, read(o@follow@ref))")]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    c.value = 2\n    exit to scope 3))")]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    defer => c.value = 2\n    exit to scope 3))")]
    public void RejectsConflictsAndBoundaries(string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.False(c.Analysis.IsVerified, body);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericValueReservationUsesTheReferenceValueAbi()
    {
        var source = Cell + "func use<T>(c: T, n: i32) => require n == 1 else => $abort(\"read\")\nvar c = Cell.init()\nuse(c@uniq, read(c))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Analysis.IsVerified);
        NativeAllocationAudit.WriteFixture("CallReservationGenericReference", source, 0, 0, 0);
    }

    [Fact]
    public void AbortDoesNotActivateOrUnwind()
        => ScalarEmissionTest.EmitFixture("CallReservationAbort", Cell + "func run()\n    var c = Cell.init()\n    defer => Console.writeLine(\"unexpected cleanup\")\n    set(c@uniq, $abort(\"stop\"))\nrun()", string.Empty, 1, "Hello.kimi:9:17: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("func both(c: uniq/Cell, r: ref/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@ref)", true)]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    c.value = 2\n    exit to scope 3))", false)]
    public void DiagnosticsIdentifyPreparationOrActivation(string body, bool activation)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.Contains(c.AnalyzeLegacyOwnership().Issues, x => x.Reservation >= 0 && x.Activation == activation);
    }

    // SPEC 15.6.7: an exclusive input whose target is still lent to a live exclusive Reborrow is one conflict. The reservation
    // does not conflict with another reservation; the call's exclusive acquisition cannot activate while the Reborrow lives. Since the
    // MIR decides acceptance (H5, DECISIONS R5-M185) the record is A5's Loan conflict at the input (H3b T46: CallActivationConflict_Kd at
    // the call before), relating the Reborrow and its later use, without the Note naming the implicit acquisition (T49, until A6).
    [Theory]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\ntarget.set(3)\nsecond.set(2)", "target", "target.set(3)", "borrow 8:25 target | loan 10:1 second")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second = target\ntarget.set(3)\nsecond.set(2)", "target", "target.set(3)", "borrow 8:14 target | loan 10:1 second")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second = target@follow@uniq\ntarget.set(3)\nsecond.set(2)", "target", "target.set(3)", "borrow 8:14 target@follow@uniq | loan 10:1 second")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nset(target, 3)\nsecond.set(2)", "target", "target, 3)", "borrow 8:25 target | loan 10:1 second")]
    [InlineData("var c = Cell.init()\nlet second = c@uniq\nc.set(3)\nsecond.set(2)", "c", "c.set(3)", "borrow 7:14 c@uniq | loan 9:1 second")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target\ntarget.put(3)\nsecond.put(2)", "target", "target.put(3)", "borrow 13:14 target | loan 15:1 second")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target@objuniq\ntarget.put(3)\nsecond.put(2)", "target", "target.put(3)", "borrow 13:14 target@objuniq | loan 15:1 second")]
    [InlineData("var x: i32 = 1\nlet target = x@uniq\nlet second: uniq/i32 = target\nKimi.Intrinsics.replace(target, with: 3)\nKimi.Intrinsics.replace(second, with: 2)", "target", "target, with: 3)", "borrow 8:24 target | loan 10:25 second")]
    [InlineData("func run(flag: bool)\n    var c = Cell.init()\n    let target = c@uniq\n    let second: uniq/Cell = target\n    defer => second.set(2)\n    defer => target.set(3)\n    if flag => return\nrun(true)", "target", "target.set(3)", "borrow 9:29 target | loan 10:14 second")]
    public void ALiveReborrowConflictsOnceAtTheInput(string body, string input, string at, string related)
        => this.AssertOneRecord(Cell + body, "ComparisonLoanConflict_Kd", input, LoanConflict, related, at);

    // SPEC 15.6.7: when the Reborrow's last use is a later argument, the reserved input meets the active Loan during preparation
    // and the Loan has ended at activation. The input conflicts with that Loan, not with a call reservation. The record relates the
    // Reborrow and its later use, without the implicit acquisition's Note (H5; H3b T49, until A6 publishes it).
    [Theory]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\ntarget.set(read(second))", "target", "target.set(read", "borrow 8:25 target | loan 9:17 second")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nset(target, read(second))", "target", "target, read", "borrow 8:25 target | loan 9:18 second")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target\ntarget.put((second@follow@ref).value)", "target", "target.put(", "borrow 13:14 target | loan 14:13 second@follow@ref")]
    public void AReborrowEndingDuringPreparationConflictsAtTheInput(string body, string input, string at, string related)
        => this.AssertOneRecord(Cell + body, "ComparisonLoanConflict_Kd", input, LoanConflict, related, at);

    // The valid counterparts: the Reborrow's last use precedes the call, or the retained Loan is the call's own input.
    [Theory]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nsecond.set(2)\ntarget.set(3)")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second = target@follow@uniq\nsecond.set(2)\ntarget.set(read(target))")]
    [InlineData("var c = Cell.init()\nlet second = c@uniq\nsecond.set(2)\nc.set(read(c))")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nsecond.set(read(second))\ntarget.set(3)")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target\nsecond.put(2)\ntarget.put(3)")]
    public void AReborrowEndingBeforeTheCallIsAccepted(string body)
        => AssertAccepted(Cell + body);

    // SPEC 15.6.7: an implicit receiver's target is located once at its lending point, also where its Place is read before
    // the Borrow (an Array or a Dictionary), and a reservation does not reserve the Loans its target retains: in
    // `drain.reset(values.iterateUniq())`, `values` meets the Loan `drain` keeps, not the reservation of `drain`. A Loan still
    // live at activation is one conflict. Since the MIR decides acceptance (H5, DECISIONS R5-M185) it is A5's Loan conflict at the
    // receiver (H3b T46: CallActivationConflict_Kd at the call before), relating the receiver's earlier Borrow and the later use of a
    // value retaining its Loan (`first` keeps the item it took from `drain`), without the implicit acquisition's Note (T49, until A6).
    [Theory]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\nvalues.append(4)\nlet n = it.next()", "values", "values.append(4)", "borrow 7:10 values | loan 9:9 it")]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\n(values).append(4)\nlet n = it.next()", "(values)", "(values).append(4)", "borrow 7:10 values | loan 9:9 it")]
    [InlineData("var table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\ntable.clear()\nlet n = it.next()", "table", "table.clear()", "borrow 7:10 table | loan 9:9 it")]
    [InlineData(Drain + "public func main()\n    var values = [1, 2, 3]\n    var drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let first = drain.take()\n    drain.reset(values.iterateUniq())\n    let second = drain.take()\n    match first@move\n        .Some(let item) => item@follow += 1\n        .None => ()", "values", "values.iterateUniq())\n    let second", "borrow 19:52 values | loan 23:11 first")]
    [InlineData(Drain + "var values = [1, 2, 3]\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\ndrain.reset(values.iterateUniq())\nlet second = drain.take()", "values", "values.iterateUniq())\nlet second", "borrow 18:48 values | loan 20:14 drain")]
    [InlineData(Drain + "func replace<J>(drain: uniq/Drain<J>, fresh: J)\n    J is Iterator\n    drain.reset(fresh@move)\nvar values = [1, 2, 3]\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\nreplace(drain@uniq, values.iterateUniq())\nlet second = drain.take()", "values", "values.iterateUniq())\nlet second", "borrow 21:48 values | loan 23:14 drain")]
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nkeeper.reset(s.lend())\nlet n = keeper.peek()", "s", "s.lend())\nlet n", "borrow 18:26 s | loan 20:9 keeper")]
    // `keeper` has no later use, so the value retaining the Loan is related at its declaration (DIAGNOSTICS A6's `Store` witness; the
    // U-T2 amendment of DECISIONS R4-M57); H3b T46: CallActivationConflict_Kd at `s.lend()` before.
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nkeeper.reset(s.lend())\nlet z = 1", "s", "s.lend())\nlet z", "borrow 18:26 s | loan 18:1 var keeper = Keeper.init(s.lend())" + Retaining)]
    public void AnImplicitReceiverMeetingARetainedLoanConflictsOnceAtTheInput(string body, string input, string at, string related)
        => this.AssertOneRecord(Cell + body, "ComparisonLoanConflict_Kd", input, LoanConflict, related, at);

    // SPEC 15.6.7: when the Loan's last use is a later argument, the receiver meets it during preparation only. The record at the input
    // relates the receiver's earlier Borrow and the Loan's later use, without the implicit acquisition's Note (H5; H3b T49, until A6).
    [Theory]
    [InlineData("func consume<I>(it: I) -> i32 => 4\nvar values = [1, 2, 3]\nvar it = values.iterateUniq()\nvalues.append(consume(it@move))", "values", "values.append", "borrow 8:10 values | loan 9:23 it")]
    [InlineData("func consume<I>(it: I) -> i32 => 4\nvar table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\ntable.insertOrReplace(consume(it@move), 1)", "table", "table.insert", "borrow 8:10 table | loan 9:31 it")]
    public void AnImplicitReceiverMeetingALoanEndingDuringPreparationConflictsAtTheInput(string body, string input, string at, string related)
        => this.AssertOneRecord(Cell + body, "ComparisonLoanConflict_Kd", input, LoanConflict, related, at);

    // The valid counterparts: every holder of the Loan is last used before the receiver is acquired again.
    [Theory]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\nlet n = it.next()\nvalues.append(4)")]
    [InlineData("var table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\nlet n = it.next()\ntable.clear()")]
    [InlineData(Drain + "public func main()\n    var values = [1, 2, 3]\n    var drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let first = drain.take()\n    match first@move\n        .Some(let item) => item@follow += 1\n        .None => ()\n    var again = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let second = again.take()")]
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nlet n = keeper.peek()\nvar again = Keeper.init(s.lend())\nlet m = again.peek()")]
    public void AReceiverLoanEndingBeforeTheCallIsAccepted(string body)
        => AssertAccepted(Cell + body);

    // Independent conflicts at implicit receivers keep one record each (A5's Loan conflicts at the receivers since H5; H3b T46).
    [Fact]
    public void IndependentReceiverConflictsKeepTheirOwnRecords()
    {
        var source = Cell + Drain + "var values = [1, 2, 3]\nvar other = [4, 5]\nvar it = other.iterateUniq()\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\ndrain.reset(values.iterateUniq())\nother.append(6)\nlet n = it.next()\nlet m = drain.take()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.ReportAnalysis();
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static x => Assert.Equal("ComparisonLoanConflict_Kd", x.Code));
        Assert.Equal([source.IndexOf("values.iterateUniq())\nother", StringComparison.Ordinal), source.IndexOf("other.append(6)", StringComparison.Ordinal)], errors.Select(static x => x.Span.Start).Order());
    }

    // SPEC 15.6.7: two overlapping exclusive inputs are one problem. Since the MIR decides acceptance (H5, DECISIONS R5-M185) it is A5's
    // one record at the call whose activation meets the overlap (H3b T44: at the later input before), relating the later Borrow, the
    // later use of the value that retains it (the call) and the reservation it meets, without the implicit acquisitions' Note (T49,
    // until A6).
    [Theory]
    [InlineData(Both + "var c = Cell.init()\nboth(c@uniq, c@uniq)", "both(c@uniq, c@uniq)", "borrow 8:14 c@uniq | loan 8:1 both(c@uniq, c@uniq) | reservation 8:6 c@uniq")]
    [InlineData("var p: i32 = 1\nKimi.Intrinsics.swap(p@uniq, p@uniq)", "Kimi.Intrinsics.swap(p@uniq, p@uniq)", "borrow 7:30 p@uniq | loan 7:1 Kimi.Intrinsics.swap(p@uniq, p@uniq) | reservation 7:22 p@uniq")]
    [InlineData(Change + "var c = Cell.init()\nset(c@uniq, change(c@uniq))", "change(c@uniq)", "borrow 10:5 c@uniq | loan 10:1 set(c@uniq, change(c@uniq)) | reservation 10:20 c@uniq")]
    [InlineData(Change + "var c = Cell.init()\nc.set(change(c@uniq))", "change(c@uniq)", "borrow 10:1 c | loan 10:1 c.set(change(c@uniq)) | reservation 10:14 c@uniq")]
    [InlineData(Counter + "var k = Counter.init()\nk.add(k.take())", "k.take()", "borrow 14:1 k | loan 14:1 k.add(k.take()) | reservation 14:7 k")]
    [InlineData("var values = [1, 2, 3]\nvalues.append(values.remove(0))", "values.remove(0)", "borrow 7:1 values | loan 7:1 values.append(values.remove(0)) | reservation 7:15 values")]
    [InlineData("struct Holder\n    public var items: Array<i32>\n    public init() => self.items = [1, 2, 3]\nvar holder = Holder.init()\nholder.items.append(holder.items.remove(0))", "holder.items.remove(0)", "borrow 10:1 holder.items | loan 10:1 holder.items.append(holder.items.remove(0)) | reservation 10:21 holder.items")]
    [InlineData(Both + "var c = Cell.init()\nlet u = c@uniq\nboth(u, u)", "both(u, u)", "borrow 9:9 u | loan 9:1 both(u, u) | reservation 9:6 u")]
    [InlineData("let n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nlet r = f(f(1))", "f(1)", "borrow 8:9 f | loan 8:9 f(f(1)) | reservation 8:11 f")]
    [InlineData(Both + "func run(flag: bool)\n    var c = Cell.init()\n    defer => both(c@uniq, c@uniq)\n    if flag => return\nrun(true)", "both(c@uniq, c@uniq)", "borrow 9:27 c@uniq | loan 9:14 both(c@uniq, c@uniq) | reservation 9:19 c@uniq")]
    public void OverlappingExclusiveInputsConflictOnceAtTheCall(string body, string call, string related)
        => this.AssertOneRecord(Cell + body, "CallReservationConflict_Kd", call, ReservationConflict, related);

    // SPEC 15.6.7: a write or Move during preparation, here in an argument that catches its own transfer, conflicts with the
    // reservation once, at the operation. Since the MIR decides acceptance (H5, DECISIONS R5-M185) A5's record relates the reserved
    // Borrow and, since the reserved argument's next use, the call, lies past the label block, that argument's declaration as the value
    // retaining the Loan (DIAGNOSTICS A6's `Store` witness; the U-T2 amendment of DECISIONS R4-M57), without a `reservation` related or
    // the implicit acquisition's Note (H3b T49, until A6; the Move below, which no block separates from its call, relates the call).
    [Theory]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    c.value = 2\n    exit to scope 3\n))", "c.value = 2", "borrow 7:5 c@uniq | loan 7:5 c@uniq" + Retaining)]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    defer => c.value = 2\n    exit to scope 3\n))", "c.value = 2", "borrow 7:5 c@uniq | loan 7:5 c@uniq" + Retaining)]
    [InlineData("func run()\n    var c = Cell.init()\n    set(c@uniq, (label scope: do\n        c.value = 2\n        exit to scope 3\n    ))\nrun()", "c.value = 2", "borrow 8:9 c@uniq | loan 8:9 c@uniq" + Retaining)]
    [InlineData("var c = Cell.init()\nc.set((label scope: do\n    c.value = 2\n    exit to scope 3\n))", "c.value = 2", "borrow 7:1 c | loan 7:1 c" + Retaining)]
    [InlineData("var pair = (Cell.init(), Cell.init())\nset(pair.0@uniq, (label scope: do\n    pair.0.value = 2\n    exit to scope 3\n))", "pair.0.value = 2", "borrow 7:5 pair.0@uniq | loan 7:5 pair.0@uniq" + Retaining)]
    public void AnOperationDuringPreparationConflictsOnceWithTheReservation(string body, string operation, string related)
        => this.AssertOneRecord(Cell + body, "CallReservationConflict_Kd", operation, ReservationConflict, related);

    // SPEC 15.6.7: a Move of the reserved target during preparation is one reservation conflict at the moved operand, relating the
    // reserved Borrow and the later use of its reservation, the call, without a `reservation` related (H5; H3b T49, until A6).
    [Fact]
    public void AMoveDuringPreparationConflictsOnceWithTheReservation()
        => this.AssertOneRecord(Cell + "var text = \"owned\"\nKimi.Intrinsics.exchange(text@uniq, with: text@move)", "CallReservationConflict_Kd", "text", ReservationConflict, "borrow 7:26 text@uniq | loan 7:1 Kimi.Intrinsics.exchange(text@uniq, with: text@move)", "text@move");

    // The valid counterparts: distinct targets, a result kept in a separate local, and accesses before or beside the reservation.
    [Theory]
    [InlineData(Both + "var a = Cell.init()\nvar b = Cell.init()\nboth(a@uniq, b@uniq)")]
    [InlineData("var p: i32 = 1\nvar q: i32 = 2\nKimi.Intrinsics.swap(p@uniq, q@uniq)")]
    [InlineData(Change + "var a = Cell.init()\nvar b = Cell.init()\nset(a@uniq, change(b@uniq))")]
    [InlineData(Change + "var c = Cell.init()\nlet n = change(c@uniq)\nset(c@uniq, n)")]
    [InlineData(Counter + "var k = Counter.init()\nlet n = k.take()\nk.add(n)")]
    [InlineData("let n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nlet a = f(1)\nlet r = f(a)")]
    [InlineData("var c = Cell.init()\nc.value = 2\nset(c@uniq, 3)")]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    let n = c.value\n    exit to scope n\n))")]
    public void SeparateOrSharedInputsAreAccepted(string body)
        => AssertAccepted(Cell + body);

    // SPEC 15.6.2, 15.6.7: a write during preparation to a path disjoint from the reservation is valid. The old element-write check
    // refused it with one Unsupported_Kd at the written path; since the MIR decides acceptance (H5, DECISIONS R5-M185) the program is
    // accepted (1a, the owner 2026-11-03): check publishes no record, and its Library form (every root, K11) generates `run`.
    [Fact]
    public void AWriteDisjointFromTheReservationIsAccepted()
    {
        Assert.Empty(DiagnosticCorpus.Check(Cell + "var pair = (Cell.init(), Cell.init())\nset(pair.0@uniq, (label scope: do\n    pair.1.value = 2\n    exit to scope 3\n))\n").Diagnostics);
        var library = Cell + "func run()\n    var pair = (Cell.init(), Cell.init())\n    set(pair.0@uniq, (label scope: do\n        pair.1.value = 2\n        exit to scope 3\n    ))\n";
        Assert.Contains("run", MinimalEmissionTest.GeneratedBodies(library, OutputKind.Library, accepted: true));
    }

    // SPEC 15.6.7: a write of a reference that a pending reservation's receiver was reached through is an error at the write:
    // rewriting, during an argument, a local reference outside the last loaded reference of the receiver's route (`cur.0.takeWith(…)`
    // with `cur = b@follow@uniq` in the argument), concrete and generic, is CallReservationConflict_Kd at the write
    // (MirBorrowsTest.AWriteOfAReservationsRouteReachesIt). The old analysis accepted it and its generated code used the receiver located
    // before the argument, a recorded difference (the owner's (b), 2026-10-22; DECISIONS R4-M106, PLAN G217); since the MIR decides
    // acceptance (H5, DECISIONS R5-M185) check rejects these programs and generation refuses them (1r).
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ARouteReferenceRewrittenInAnArgumentConflictsWithTheReservation(bool generic, bool clear)
    {
        var declarations = generic
            ? "contract Take\n    associate Item\n    func takeWith(self: uniq/Self, f: i32) -> Option<Self.Item>\n    func clear(self: uniq/Self) -> ()\n" +
                "func inspect<T>(value: ref/T) => ()\nfunc f<S>(a: uniq/(uniq/S during s, i32), b: uniq/(uniq/S during s, i32)) -> ()\n    S is Take\n"
            : "struct Inner\n    public var v: i32 = 0\n    public init() => ()\n    public func takeWith(self: uniq/Self, f: i32) -> i32 => self.v + f\n" +
                "    public func clear(self: uniq/Self) => self.v = 0\nfunc inspect<T>(value: ref/T) => ()\nfunc f(a: uniq/(uniq/Inner during s, i32), b: uniq/(uniq/Inner during s, i32)) -> ()\n";
        var source = declarations + "    var cur = a@follow@uniq\n    let first = cur.0.takeWith(label w: do\n        cur = b@follow@uniq\n        exit to w 0\n    )\n" +
            (clear ? "    a.0.clear()\n" : string.Empty) + "    inspect(first@ref)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        var write = source.IndexOf("cur = b@follow@uniq", StringComparison.Ordinal);
        var problem = Assert.Single(c.BuildMir().Problems.All.ToArray(), static x => x.Subject.Module == 0);
        Assert.Equal((DiagnosticCode.CallReservationConflict_Kd, write), (problem.Code, c.SyntaxOf(problem.Subject).Span.Start));
        Assert.False(c.Generate(TextWriter.Null));
    }

    // Two independent overlaps keep one record each, at the call each overlap's activation meets (H5; H3b T44).
    [Fact]
    public void IndependentOverlapsKeepTheirOwnRecords()
    {
        var source = Cell + Both + Change + "var c = Cell.init()\nvar d = Cell.init()\nboth(c@uniq, c@uniq)\nset(d@uniq, change(d@uniq))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.ReportAnalysis();
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static x => Assert.Equal("CallReservationConflict_Kd", x.Code));
        Assert.Equal([source.IndexOf("both(c@uniq", StringComparison.Ordinal), source.IndexOf("change(d@uniq)", StringComparison.Ordinal)], errors.Select(static x => x.Span.Start).Order());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmReservationAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Cell + "var c = Cell.init()\nset(c@uniq, read(c))");
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));

        var success = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.Generate(TextWriter.Null);
        });
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("var o = Kimi.Intrinsics.makeObj(Cell.init())\nlet r = o@objref\nlet u = r@objuniq")]
    [InlineData("let o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq")]
    [InlineData("var c = Cell.init()\nlet u = c@objuniq/Cell")]
    [InlineData("func bad(c: uniq/Cell, n: i32 = (label scope: do\n    c.value = 2\n    exit to scope 3)) => ()\nvar c = Cell.init()\nbad(c)")]
    [InlineData("func bad(c: uniq/Cell, r: ref/Cell = c@ref) => ()\nvar c = Cell.init()\nbad(c)")]
    public void RejectsPermissionAndDefaultEscapes(string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        Assert.False(c.Generate(TextWriter.Null));
    }

    private static void AssertAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Analysis.IsVerified, string.Join('\n', c.Problems));
        c.ReportAnalysis();
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    private static string Text(string source, SourceSpan? span) => source.Substring(span!.Value.Start, span.Value.Length);

    // A related location's label in parentheses where it is not its role's usual one (A6's `Use` witness for `loan`), else nothing.
    private static string OtherLabel(DiagnosticRelated related)
    {
        var usual = related.Role switch
        {
            "borrow" => BorrowLabel,
            "loan" => LaterUseLabel,
            "reservation" => ReservationLabel,
            _ => string.Empty,
        };
        return related.Label == usual ? string.Empty : $" ({related.Label})";
    }

    // The MIR's one record of a rejected program (H5, DECISIONS R5-M185), not generated: one published Error with `code` and `message`
    // at `text`, starting where `at` (by default `text`) first occurs, with no Note and the related locations `related` lists in order,
    // each "role line:column text" (1-based, in the whole source) with its role's label, or followed by "(label)" where A6 gives the
    // role another one (`Retaining`), in the console and in both language-server placements.
    private void AssertOneRecord(string source, string code, string text, string message, string related, string? at = null)
    {
        var path = Path.GetFullPath("reservation-conflict.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.False(c.Accepts);
        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal((code, DiagnosticSeverity.Error, message, (string?)null), (error.Code, error.Severity, error.Message, error.Note));
        Assert.Equal((source.IndexOf(at ?? text, StringComparison.Ordinal), text), (error.Span!.Value.Start, Text(source, error.Span)));
        Assert.Equal(related, string.Join(" | ", error.Related!.Select(x => $"{x.Role} {x.Range!.Value.Start.Line + 1}:{x.Range.Value.Start.Character + 1} {Text(source, x.Span)}{OtherLabel(x)}")));
        var labels = error.Related!.Select(static x => x.Label!).ToArray();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(code, console.Text, StringComparison.Ordinal);
        Assert.All(labels, x => Assert.Contains(x, console.Text, StringComparison.Ordinal));
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var placement in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, placement)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(message, sent.Message, StringComparison.Ordinal);
            if (placement)
            {
                Assert.Equal(error.Related!.Select(static x => x.Range), sent.RelatedInformation!.Select(static x => (SourceRange?)x.Location.Range));
            }
            else
            {
                Assert.All(labels, x => Assert.Contains(x, sent.Message, StringComparison.Ordinal));
            }
        }

        Assert.False(c.Generate(TextWriter.Null));
    }
}

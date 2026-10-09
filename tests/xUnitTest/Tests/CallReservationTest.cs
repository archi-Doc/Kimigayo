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
    private const string Drain = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\nstruct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public init(inner: J) => self.inner = inner@move\n    public func take(self: uniq/Self) -> Option<J.Item> => self.inner.next()\n    public func reset(self: uniq/Self, fresh: J) => self.inner = fresh@move\n";
    private const string Keeper = "struct Store\n    public var value: i32 = 1\n    public func lend(self: uniq/Self during source) -> Lent during source => Lent.init(self)\nstruct Lent {source}\n    var store: uniq/Store during source\n    public init(store: uniq/Store during source) => self.store = store\nstruct Keeper {source}\n    var inner: Lent during source\n    public init(inner: Lent during source) => self.inner = inner@move\n    public func reset(self: uniq/Self, fresh: Lent during source) => self.inner = fresh@move\n    public func peek(self: ref/Self) -> i32 => 1\n";
    private const string ActivationConflict = "An exclusive call reservation cannot activate while a conflicting argument or retained loan remains live";
    private const string ReservationConflict = "This operation conflicts with an exclusive call reservation; only shared inspection is permitted during preparation";
    private const string RetainedLoanLabel = "value retaining the conflicting loan";
    private const string ReservationLabel = "conflicting exclusive call reservation";

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
    [InlineData("DisjointWrite", "var pair = (Cell.init(), Cell.init())\nset(pair.0@uniq, (label scope: do\n    pair.1.value = 2\n    exit to scope 3\n))\nrequire pair.0.value == 3 and pair.1.value == 2 else => $abort(\"disjoint\")")]
    [InlineData("FieldReceiverRead", "struct Holder\n    public var items: Array<i32>\n    public init() => self.items = [1, 2, 3]\n    public func add(self: uniq/Self, n: i32) => self.items.insert(self.items.length, n)\nvar holder = Holder.init()\nholder.items.insert(holder.items.length, 4)\nholder.add(5)\nrequire holder.items.length == 5 else => $abort(\"insert\")")]
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
        Assert.True(c.Ownership.Result.IsVerified, name + "\n" + string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), name + ": " + error);
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
        Assert.False(c.Emission.Validate(out _));
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
    [InlineData("func both(p: uniq/(Cell, Cell), n: i32) => ()\nvar pair = (Cell.init(), Cell.init())\nboth(pair@uniq, (label scope: do\n    pair.1.value = 2\n    exit to scope 3\n))")]
    public void RejectsConflictsAndBoundaries(string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.False(c.Ownership.Result.IsVerified, body);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("head")]
    [InlineData("cycle")]
    [InlineData("place")]
    [InlineData("mode")]
    public void RejectsCorruptedReservationPlans(string mutation)
    {
        var c = MinimalEmissionTest.Analyze(Cell + "var c = Cell.init()\nset(c@uniq, read(c))");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.CallReservations.Count != 0);
        var reservation = body.CallReservations[0];
        switch (mutation)
        {
            case "activation":
                body.CallReservations[0] = reservation with { Activation = reservation.Borrow };
                break;
            case "head":
                body.OperationStorage[reservation.Activation] = body.Operations[reservation.Activation] with { Reservation = -1 };
                break;
            case "cycle":
                body.CallReservations[0] = reservation with { Next = 0 };
                break;
            case "place":
                body.CallReservations[0] = reservation with { Place = 0 };
                break;
            case "mode":
                body.OperationStorage[reservation.Borrow] = body.Operations[reservation.Borrow] with { LoanMode = LoanRequirement.Ref };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericValueReservationUsesTheReferenceValueAbi()
    {
        var source = Cell + "func use<T>(c: T, n: i32) => require n == 1 else => $abort(\"read\")\nvar c = Cell.init()\nuse(c@uniq, read(c))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
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
        Assert.Contains(c.Ownership.Issues, x => x.Reservation >= 0 && x.Activation == activation);
    }

    // SPEC 15.6.7: an exclusive input whose target is still lent to a live exclusive Reborrow is one conflict. The reservation
    // does not conflict with another reservation; the call's exclusive acquisition cannot activate while the Reborrow lives.
    [Theory]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\ntarget.set(3)\nsecond.set(2)", "target.set(3)", "`target` implicitly reborrowed exclusively as the receiver of `set`")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second = target\ntarget.set(3)\nsecond.set(2)", "target.set(3)", "`target` implicitly reborrowed exclusively as the receiver of `set`")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second = target@follow@uniq\ntarget.set(3)\nsecond.set(2)", "target.set(3)", "`target` implicitly reborrowed exclusively as the receiver of `set`")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nset(target, 3)\nsecond.set(2)", "set(target, 3)", "`target` implicitly reborrowed exclusively for parameter `c` of `set`")]
    [InlineData("var c = Cell.init()\nlet second = c@uniq\nc.set(3)\nsecond.set(2)", "c.set(3)", "`c` implicitly borrowed exclusively as the receiver of `set`")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target\ntarget.put(3)\nsecond.put(2)", "target.put(3)", "`target` implicitly reborrowed exclusively as the receiver of `put`")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target@objuniq\ntarget.put(3)\nsecond.put(2)", "target.put(3)", "`target` implicitly reborrowed exclusively as the receiver of `put`")]
    [InlineData("var x: i32 = 1\nlet target = x@uniq\nlet second: uniq/i32 = target\nKimi.Intrinsics.replace(target, with: 3)\nKimi.Intrinsics.replace(second, with: 2)", "Kimi.Intrinsics.replace(target, with: 3)", "`target` implicitly reborrowed exclusively for parameter `target` of `replace`")]
    [InlineData("func run(flag: bool)\n    var c = Cell.init()\n    let target = c@uniq\n    let second: uniq/Cell = target\n    defer => second.set(2)\n    defer => target.set(3)\n    if flag => return\nrun(true)", "target.set(3)", "`target` implicitly reborrowed exclusively as the receiver of `set`")]
    public void ALiveReborrowConflictsOnceAtActivation(string body, string call, string note)
        => this.AssertOneConflict(Cell + body, "CallActivationConflict_Kd", call, ActivationConflict, note);

    // SPEC 15.6.7: when the Reborrow's last use is a later argument, the reserved input meets the active Loan during preparation
    // and the Loan has ended at activation. The input conflicts with that Loan, not with a call reservation.
    [Theory]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\ntarget.set(read(second))", "target", "`target` implicitly reborrowed exclusively as the receiver of `set`")]
    [InlineData("var c = Cell.init()\nlet target = c@uniq\nlet second: uniq/Cell = target\nset(target, read(second))", "target", "`target` implicitly reborrowed exclusively for parameter `c` of `set`")]
    [InlineData(ObjCell + "var o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet target = o@objuniq\nlet second = target\ntarget.put((second@follow@ref).value)", "target", "`target` implicitly reborrowed exclusively as the receiver of `put`")]
    public void AReborrowEndingDuringPreparationConflictsAtTheInput(string body, string input, string note)
        => this.AssertOneConflict(Cell + body, "ComparisonLoanConflict_Kd", input, "This operation conflicts with an active loan", note);

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
    // live at activation is one conflict, which the activation states once with the value retaining the Loan.
    [Theory]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\nvalues.append(4)\nlet n = it.next()", "values.append(4)", null, "var it", "`values` implicitly borrowed exclusively as the receiver of `append`")]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\n(values).append(4)\nlet n = it.next()", "(values).append(4)", null, "var it", "`(values)` implicitly borrowed exclusively as the receiver of `append`")]
    [InlineData("var table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\ntable.clear()\nlet n = it.next()", "table.clear()", null, "var it", "`table` implicitly borrowed exclusively as the receiver of `clear`")]
    [InlineData(Drain + "public func main()\n    var values = [1, 2, 3]\n    var drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let first = drain.take()\n    drain.reset(values.iterateUniq())\n    let second = drain.take()\n    match first@move\n        .Some(let item) => item@follow += 1\n        .None => ()", "values.iterateUniq()", "values.iterateUniq())\n    let second", "var drain", "`values` implicitly borrowed exclusively as the receiver of `iterateUniq`")]
    [InlineData(Drain + "var values = [1, 2, 3]\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\ndrain.reset(values.iterateUniq())\nlet second = drain.take()", "values.iterateUniq()", "values.iterateUniq())\nlet second", "var drain", "`values` implicitly borrowed exclusively as the receiver of `iterateUniq`")]
    [InlineData(Drain + "func replace<J>(drain: uniq/Drain<J>, fresh: J)\n    J is Iterator\n    drain.reset(fresh@move)\nvar values = [1, 2, 3]\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\nreplace(drain@uniq, values.iterateUniq())\nlet second = drain.take()", "values.iterateUniq()", "values.iterateUniq())\nlet second", "var drain", "`values` implicitly borrowed exclusively as the receiver of `iterateUniq`")]
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nkeeper.reset(s.lend())\nlet n = keeper.peek()", "s.lend()", "s.lend())\nlet n", "var keeper", "`s` implicitly borrowed exclusively as the receiver of `lend`")]
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nkeeper.reset(s.lend())\nlet z = 1", "s.lend()", "s.lend())\nlet z", "var keeper", "`s` implicitly borrowed exclusively as the receiver of `lend`")]
    public void AnImplicitReceiverMeetingARetainedLoanConflictsOnceAtActivation(string body, string call, string? at, string holder, string note)
        => this.AssertOneConflict(Cell + body, "CallActivationConflict_Kd", call, ActivationConflict, note, related: holder, at: at);

    // SPEC 15.6.7: when the Loan's last use is a later argument, the receiver meets it during preparation only; the record at the
    // input keeps the Note naming the implicit acquisition.
    [Theory]
    [InlineData("func consume<I>(it: I) -> i32 => 4\nvar values = [1, 2, 3]\nvar it = values.iterateUniq()\nvalues.append(consume(it@move))", "values", "values.append", "var it", "`values` implicitly borrowed exclusively as the receiver of `append`")]
    [InlineData("func consume<I>(it: I) -> i32 => 4\nvar table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\ntable.insertOrReplace(consume(it@move), 1)", "table", "table.insert", "var it", "`table` implicitly borrowed exclusively as the receiver of `insertOrReplace`")]
    public void AnImplicitReceiverMeetingALoanEndingDuringPreparationConflictsAtTheInput(string body, string input, string at, string holder, string note)
        => this.AssertOneConflict(Cell + body, "ComparisonLoanConflict_Kd", input, "This operation conflicts with an active loan", note, related: holder, at: at);

    // The valid counterparts: every holder of the Loan is last used before the receiver is acquired again.
    [Theory]
    [InlineData("var values = [1, 2, 3]\nvar it = values.iterateUniq()\nlet n = it.next()\nvalues.append(4)")]
    [InlineData("var table: Dictionary<i32, i32> = [:]\nvar it = table.iterateUniq()\nlet n = it.next()\ntable.clear()")]
    [InlineData(Drain + "public func main()\n    var values = [1, 2, 3]\n    var drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let first = drain.take()\n    match first@move\n        .Some(let item) => item@follow += 1\n        .None => ()\n    var again = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let second = again.take()")]
    [InlineData(Keeper + "var s = Store.init()\nvar keeper = Keeper.init(s.lend())\nlet n = keeper.peek()\nvar again = Keeper.init(s.lend())\nlet m = again.peek()")]
    public void AReceiverLoanEndingBeforeTheCallIsAccepted(string body)
        => AssertAccepted(Cell + body);

    // Independent conflicts at implicit receivers keep one record each.
    [Fact]
    public void IndependentReceiverConflictsKeepTheirOwnRecords()
    {
        var source = Cell + Drain + "var values = [1, 2, 3]\nvar other = [4, 5]\nvar it = other.iterateUniq()\nvar drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\ndrain.reset(values.iterateUniq())\nother.append(6)\nlet n = it.next()\nlet m = drain.take()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.Ownership.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static x => Assert.Equal("CallActivationConflict_Kd", x.Code));
        Assert.Equal([source.IndexOf("values.iterateUniq())\nother", StringComparison.Ordinal), source.IndexOf("other.append(6)", StringComparison.Ordinal)], errors.Select(static x => x.Span.Start).Order());
    }

    // SPEC 15.6.7: two overlapping exclusive inputs are one problem. The later input conflicts with the earlier reservation during
    // preparation, so its record shows both lending points; the activation that the overlap prevents is not reported again. An
    // implicit acquisition is named in the Note (both when two implicit acquisitions overlap).
    [Theory]
    [InlineData(Both + "var c = Cell.init()\nboth(c@uniq, c@uniq)", "c@uniq)", "c", "c@uniq, ", null)]
    [InlineData("var p: i32 = 1\nKimi.Intrinsics.swap(p@uniq, p@uniq)", "p@uniq)", "p", "p@uniq, ", null)]
    [InlineData(Change + "var c = Cell.init()\nset(c@uniq, change(c@uniq))", "c@uniq))", "c", "c@uniq, ", null)]
    [InlineData(Change + "var c = Cell.init()\nc.set(change(c@uniq))", "c@uniq))", "c", "c.set", "`c` implicitly borrowed exclusively as the receiver of `set`")]
    [InlineData(Counter + "var k = Counter.init()\nk.add(k.take())", "k.take", "k", "k.add", "`k` implicitly borrowed exclusively as the receiver of `take`; `k` implicitly borrowed exclusively as the receiver of `add`")]
    [InlineData("var values = [1, 2, 3]\nvalues.append(values.remove(0))", "values.remove", "values", "values.append", "`values` implicitly borrowed exclusively as the receiver of `remove`; `values` implicitly borrowed exclusively as the receiver of `append`")]
    [InlineData("struct Holder\n    public var items: Array<i32>\n    public init() => self.items = [1, 2, 3]\nvar holder = Holder.init()\nholder.items.append(holder.items.remove(0))", "holder.items.remove", "holder.items", "holder.items.append", "`holder.items` implicitly borrowed exclusively as the receiver of `remove`; `holder.items` implicitly borrowed exclusively as the receiver of `append`")]
    [InlineData(Both + "var c = Cell.init()\nlet u = c@uniq\nboth(u, u)", "u)", "u", "u, u)", "`u` implicitly reborrowed exclusively for parameter `other` of `both`; `u` implicitly reborrowed exclusively for parameter `c` of `both`")]
    [InlineData("let n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nlet r = f(f(1))", "f(1)", "f", "f(f", "`f` implicitly borrowed exclusively as the receiver of its call")]
    [InlineData(Both + "func run(flag: bool)\n    var c = Cell.init()\n    defer => both(c@uniq, c@uniq)\n    if flag => return\nrun(true)", "c@uniq)", "c", "c@uniq, ", null)]
    public void OverlappingExclusiveInputsConflictOnceAtTheLaterInput(string body, string at, string input, string reservedAt, string? note)
        => this.AssertOneConflict(Cell + body, "CallReservationConflict_Kd", input, ReservationConflict, note, "reservation", input, ReservationLabel, at, reservedAt);

    // SPEC 15.6.7: a write or Move during preparation, here in an argument that catches its own transfer, conflicts with the
    // reservation once, at the operation, relating the reserved lending point.
    [Theory]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    c.value = 2\n    exit to scope 3\n))", "c.value = 2", "c.value = 2", "c", "c@uniq", null)]
    [InlineData("var c = Cell.init()\nset(c@uniq, (label scope: do\n    defer => c.value = 2\n    exit to scope 3\n))", "c.value = 2", "c.value = 2", "c", "c@uniq", null)]
    [InlineData("func run()\n    var c = Cell.init()\n    set(c@uniq, (label scope: do\n        c.value = 2\n        exit to scope 3\n    ))\nrun()", "c.value = 2", "c.value = 2", "c", "c@uniq", null)]
    [InlineData("var c = Cell.init()\nc.set((label scope: do\n    c.value = 2\n    exit to scope 3\n))", "c.value = 2", "c.value = 2", "c", "c.set", "`c` implicitly borrowed exclusively as the receiver of `set`")]
    [InlineData("var pair = (Cell.init(), Cell.init())\nset(pair.0@uniq, (label scope: do\n    pair.0.value = 2\n    exit to scope 3\n))", "pair.0.value = 2", "pair.0.value = 2", "pair.0", "pair.0@uniq", null)]
    [InlineData("var text = \"owned\"\nKimi.Intrinsics.exchange(text@uniq, with: text@move)", "text@move", "text", "text", "text@uniq", null)]
    public void AnOperationDuringPreparationConflictsOnceWithTheReservation(string body, string at, string operation, string reserved, string reservedAt, string? note)
        => this.AssertOneConflict(Cell + body, "CallReservationConflict_Kd", operation, ReservationConflict, note, "reservation", reserved, ReservationLabel, at, reservedAt);

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

    // Two independent overlaps keep one record each.
    [Fact]
    public void IndependentOverlapsKeepTheirOwnRecords()
    {
        var source = Cell + Both + Change + "var c = Cell.init()\nvar d = Cell.init()\nboth(c@uniq, c@uniq)\nset(d@uniq, change(d@uniq))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        c.Ownership.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static x => Assert.Equal("CallReservationConflict_Kd", x.Code));
        Assert.Equal([source.IndexOf("c@uniq)", StringComparison.Ordinal), source.IndexOf("d@uniq))", StringComparison.Ordinal)], errors.Select(static x => x.Span.Start).Order());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmReservationAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Cell + "var c = Cell.init()\nset(c@uniq, read(c))");
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);

        var success = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                success &= c.Ownership.Analyze().IsVerified;
                success &= c.Emission.WriteIr(TextWriter.Null, out _);
            },
            iterations: 128,
            warmupIterations: 100);
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
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    private static void AssertAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        c.Ownership.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.True(c.Emission.Validate(out var error), error);
    }

    // One published Error at the expected range with one related location and its Note, in the console and in both
    // language-server placements. By default the related location is the declaration of `second` that retains the Loan; at
    // and relatedAt, when given, fix the start of the primary and the related range and make the related text exact.
    private void AssertOneConflict(string source, string code, string text, string message, string? note, string role = "loan", string related = "let second", string label = RetainedLoanLabel, string? at = null, string? relatedAt = null)
    {
        var path = Path.GetFullPath("reservation-conflict.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.False(c.Ownership.Result.IsVerified);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(text, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        if (at is not null)
        {
            Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), error.Span.Value.Start);
        }

        Assert.Equal(message, error.Message);
        Assert.Equal(note, error.Note);
        var retained = Assert.Single(error.Related!);
        Assert.Equal(role, retained.Role);
        var relatedText = source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length);
        Assert.StartsWith(related, relatedText, StringComparison.Ordinal);
        if (relatedAt is not null)
        {
            Assert.Equal(source.IndexOf(relatedAt, StringComparison.Ordinal), retained.Span.Value.Start);
            Assert.Equal(related, relatedText);
        }

        Assert.Equal(label, retained.Label);

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(code, console.Text, StringComparison.Ordinal);
        Assert.Contains(label, console.Text, StringComparison.Ordinal);
        if (note is not null)
        {
            Assert.Contains("Note: " + note, console.Text, StringComparison.Ordinal);
        }

        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var placement in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, placement)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(message, sent.Message, StringComparison.Ordinal);
            if (note is not null)
            {
                Assert.Contains("note: " + note, sent.Message, StringComparison.Ordinal);
            }

            if (placement)
            {
                Assert.Equal(retained.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains(label, sent.Message, StringComparison.Ordinal);
            }
        }

        Assert.False(c.Emission.Validate(out _));
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CallReservationTest
{
    private const string Cell = "struct Cell\n    public var value: i32 = 1\n    public func set(self: uniq/Self, n?: i32) => self.value = n\nfunc set(c?: uniq/Cell, n?: i32) => c.value = n\nfunc read(c?: ref/Cell) -> i32 => c.value\n";

    [Theory]
    [InlineData("Implicit", "var c = Cell.init()\nset(c, c.value + 1)")]
    [InlineData("Explicit", "var c = Cell.init()\nset(c@uniq, c.value + 1)")]
    [InlineData("Typed", "var c = Cell.init()\nset((c@uniq/Cell), read(c))")]
    [InlineData("Method", "var c = Cell.init()\nc.set(read(c))")]
    [InlineData("Reborrow", "var c = Cell.init()\nlet u = c@uniq\nset(u@uniq, read(u))")]
    [InlineData("Parameter", "func run(c?: uniq/Cell) => set(c, read(c))\nvar c = Cell.init()\nrun(c)")]
    [InlineData("OldShared", "var c = Cell.init()\nlet r = c@ref\nset(c@uniq, read(r))")]
    [InlineData("Named", "var c = Cell.init()\nset(c: c@uniq, n: read(c))")]
    [InlineData("NamedOrder", "func change(c?: uniq/Cell) -> i32\n    c.value = 4\n    return c.value\nvar c = Cell.init()\nset(n: change(c), c: c@uniq)\nrequire c.value == 4 else => $abort(\"order\")")]
    [InlineData("Replace", "var p: i32 = 1\nKimi.Intrinsics.replace(p, with: p + 1)")]
    [InlineData("ExplicitExchange", "var p: i32 = 1\nlet old = Kimi.Intrinsics.exchange(p@uniq, with: p + 1)")]
    [InlineData("Default", "func next(c?: uniq/Cell, n?: i32 = c.value + 1) => c.value = n\nvar c = Cell.init()\nnext(c@uniq)\nrequire c.value == 2 else => $abort(\"default\")")]
    [InlineData("DefaultReceiver", "struct Bump\n    public var value: i32 = 1\n    public func next(self: uniq/Self, n?: i32 = self.value + 1) => self.value = n\nvar c = Bump.init()\nc.next()\nrequire c.value == 2 else => $abort(\"receiver default\")")]
    [InlineData("Payload", "var o = Kimi.Intrinsics.makeObj(Cell.init())\nset(o@uniq/Cell, read(o@ref/Cell))")]
    [InlineData("PayloadMethod", "var o = Kimi.Intrinsics.makeObj(Cell.init())\no.set(read(o@ref/Cell))")]
    [InlineData("ReplaceBorrowed", "func update(c?: uniq/Cell) => Kimi.Intrinsics.replace(c, with: Cell.init())\nvar c = Cell.init()\nupdate(c)")]
    [InlineData("Disjoint", "func two(a?: uniq/Cell, b?: uniq/Cell)\n    a.value = 2\n    b.value = 3\nvar pair = (Cell.init(), Cell.init())\ntwo(pair.0@uniq, pair.1@uniq)")]
    [InlineData("Caught", "var c = Cell.init()\nset(c@uniq, (scope: do => exit to scope: read(c)))")]
    [InlineData("Return", "func run() -> i32\n    var c = Cell.init()\n    defer => c.value = 7\n    set(c@uniq, do => return read(c))\n    return 0\nrequire run() == 1 else => $abort(\"return\")")]
    [InlineData("Generic", "func run<T>(c?: uniq/Cell, x?: ref/T) => set(c@uniq, read(c))\nvar c = Cell.init()\nlet x = true\nrun(c, x)")]
    [InlineData("Constructor", "struct S\n    public init(c?: uniq/Cell, n?: i32) => c.value = n\nvar c = Cell.init()\nlet s = S.init(c@uniq, read(c))")]
    [InlineData("Indirect", "let concrete = func (c: uniq/Cell, n: i32) => c.value = n\nlet f: (uniq/Cell, i32) -> () = concrete\nvar c = Cell.init()\nf(c@uniq, read(c) + 1)\nrequire c.value == 2 else => $abort(\"indirect\")")]
    [InlineData("Concrete", "let f = func (c: uniq/Cell, n: i32) => c.value = n\nvar c = Cell.init()\nf(c@uniq, read(c) + 1)\nrequire c.value == 2 else => $abort(\"concrete\")")]
    [InlineData("Deferred", "func run(flag?: bool)\n    var c = Cell.init()\n    defer => set(c@uniq, read(c) + 1)\n    if flag => return\nrun(true)\nrun(false)")]
    [InlineData("GenericArgument", "func use<T>(c?: uniq/T, n?: i32) => ()\nvar c = Cell.init()\nuse(c@uniq, read(c))")]
    [InlineData("ExplicitCallable", "func inspect<T>(value?: ref/T) -> i32 => 2\nlet n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nrequire (f@uniq)(inspect(f)) == 3 else => $abort(\"callable\")")]
    [InlineData("ImplicitCallable", "func inspect<T>(value?: ref/T) -> i32 => 2\nlet n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nrequire f(inspect(f)) == 3 else => $abort(\"callable\")")]
    [InlineData("DisjointReborrow", "func two(a?: uniq/Cell, b?: uniq/Cell)\n    a.value = 2\n    b.value = 3\nvar pair = (Cell.init(), Cell.init())\nlet u = pair@uniq\ntwo(u.0@uniq, u.1@uniq)")]
    [InlineData("MultipleDefaults", "func next(c?: uniq/Cell, n?: i32 = c.value, m?: i32 = c.value + n) => c.value = m\nvar c = Cell.init()\nnext(c)\nnext(c@uniq)\nrequire c.value == 4 else => $abort(\"defaults\")")]
    [InlineData("Object", "func put(o?: objuniq/Cell, n?: i32) => set(o@uniq/Cell, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o@objuniq, read(o@ref/Cell) + 1)\nrequire read(o@ref/Cell) == 2 else => $abort(\"object\")")]
    [InlineData("ObjectReborrow", "func put(o?: objuniq/Cell, n?: i32) => set(o@uniq/Cell, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq\nput(u@objuniq, read(u@ref/Cell) + 1)\nrequire read(u@ref/Cell) == 2 else => $abort(\"object reborrow\")")]
    [InlineData("ObjectShared", "func peek(o?: objref/Cell) -> i32 => read(o@ref/Cell)\nfunc put(o?: objuniq/Cell, n?: i32) => set(o@uniq/Cell, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o@objuniq, peek(o@objref) + 1)")]
    [InlineData("ObjectImplicit", "func put(o?: objuniq/Cell, n?: i32) => set(o@uniq/Cell, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput(o, read(o@ref/Cell) + 1)")]
    [InlineData("ObjectTyped", "func put(o?: objuniq/Cell, n?: i32) => set(o@uniq/Cell, n)\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nput((o@objuniq/Cell), read(o@ref/Cell) + 1)")]
    [InlineData("ObjectReceiver", "struct ObjCell\n    public var value: i32 = 1\n    public func put(self: objuniq/Self, n?: i32)\n        let p = self@uniq/Self\n        p.value = n\nvar o = Kimi.Intrinsics.makeObj(ObjCell.init())\nlet n = (o@ref/ObjCell).value\no.put(n + 1)\nrequire (o@ref/ObjCell).value == 2 else => $abort(\"object receiver\")")]
    [InlineData("LoopTransfer", "var c = Cell.init()\nvar n: i32 = 0\nwhile n < 2\n    n += 1\n    set(c@uniq, (if n == 1 => continue else => read(c)))\nrequire n == 2 else => $abort(\"continue\")")]
    public void AcceptsPreparedReads(string name, string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, name + "\n" + string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), name + ": " + error);
        ScalarEmissionTest.EmitFixture("CallReservation" + name, Cell + body, string.Empty);
    }

    [Theory]
    [InlineData("var c = Cell.init()\nlet r = c@uniq\nset(r, c.value)")]
    [InlineData("func both(c?: uniq/Cell, other?: ref/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@ref)")]
    [InlineData("func both(c?: uniq/Cell, other?: uniq/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@uniq)")]
    [InlineData("var c = Cell.init()\nlet r = c@ref\nset(c, read(r))\nlet n = r.value")]
    [InlineData("func change(c?: uniq/Cell) -> i32\n    c.value = 2\n    return 3\nvar c = Cell.init()\nset(c, change(c))")]
    [InlineData("func same(c?: uniq/Cell) -> uniq{c}/Cell => c\nvar c = Cell.init()\nset(same(c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((if true => c@uniq else => c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((do => c@uniq), read(c))")]
    [InlineData("var c = Cell.init()\nset((match true\n    true => c@uniq\n    false => c@uniq), read(c))")]
    [InlineData("func both(other?: ref/Cell, c?: uniq/Cell) => ()\nvar c = Cell.init()\nboth(c@ref, c@uniq)")]
    [InlineData("let f = func (c: uniq/Cell, r: ref/Cell) => ()\nvar c = Cell.init()\nf(c@uniq, c@ref)")]
    [InlineData("var c = Cell.init()\nlet r = c@ref\nlet f = func [r] () => read(r)\nset(c@uniq, f())\nlet n = f()")]
    [InlineData("var p: i32 = 1\nKimi.Intrinsics.swap(p, p)")]
    [InlineData("var text = \"owned\"\nKimi.Intrinsics.exchange(text, with: text)")]
    [InlineData("func both(o?: objuniq/Cell, r?: objref/Cell) => ()\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nboth(o@objuniq, o@objref)")]
    [InlineData("func put(o?: objuniq/Cell, n?: i32) => ()\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq\nput(u@objuniq, read(o@ref/Cell))")]
    [InlineData("var c = Cell.init()\nset(c@uniq, (scope: do\n    c.value = 2\n    exit to scope: 3))")]
    [InlineData("var c = Cell.init()\nset(c@uniq, (scope: do\n    defer => c.value = 2\n    exit to scope: 3))")]
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

    [Fact]
    public void GenericValueReservationDoesNotClaimUnsupportedStorageAbi()
    {
        var c = MinimalEmissionTest.Analyze(Cell + "func use<T>(c?: T, n?: i32) => ()\nvar c = Cell.init()\nuse(c@uniq, read(c))");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AbortDoesNotActivateOrUnwind()
        => ScalarEmissionTest.EmitFixture("CallReservationAbort", Cell + "func run()\n    var c = Cell.init()\n    defer => Console.writeLine(\"unexpected cleanup\")\n    set(c@uniq, $abort(\"stop\"))\nrun()", string.Empty, 1, "Hello.kimi:9:17: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("func both(c?: uniq/Cell, r?: ref/Cell) => ()\nvar c = Cell.init()\nboth(c@uniq, c@ref)", true)]
    [InlineData("var c = Cell.init()\nset(c@uniq, (scope: do\n    c.value = 2\n    exit to scope: 3))", false)]
    public void DiagnosticsIdentifyPreparationOrActivation(string body, bool activation)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.Contains(c.Ownership.Issues, x => x.Reservation >= 0 && x.Activation == activation);
    }

    [Fact]
    public void WarmReservationAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Cell + "var c = Cell.init()\nset(c@uniq, read(c))");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("var o = Kimi.Intrinsics.makeObj(Cell.init())\nlet r = o@objref\nlet u = r@objuniq")]
    [InlineData("let o = Kimi.Intrinsics.makeObj(Cell.init())\nlet u = o@objuniq")]
    [InlineData("var c = Cell.init()\nlet u = c@objuniq/Cell")]
    [InlineData("func bad(c?: uniq/Cell, n?: i32 = (scope: do\n    c.value = 2\n    exit to scope: 3)) => ()\nvar c = Cell.init()\nbad(c)")]
    [InlineData("func bad(c?: uniq/Cell, r?: ref/Cell = c@ref) => ()\nvar c = Cell.init()\nbad(c)")]
    public void RejectsPermissionAndDefaultEscapes(string body)
    {
        var c = MinimalEmissionTest.Analyze(Cell + body);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }
}

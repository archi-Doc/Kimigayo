// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class SharedObjectOwnershipTest(ITestOutputHelper output)
{
    private const string View = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n";
    private const string Item = "struct Item\n    public var value: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.value\n    drop => Console.writeLine(\"drop\")\n";
    private const string Observer = "struct Observer {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n    drop\n        require self.value == 7 else => $abort(\"observed\")\n        Console.writeLine(\"observed\")\n";

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Returned", "let copy = label result: do\n    let local = Kimi.Intrinsics.makeRc(View.init(n@ref))\n    exit to result Kimi.Intrinsics.clone(local@ref)\nrequire copy.value == 7 else => $abort(\"result\")", 1)]
    [InlineData("Replaced", "var local = Kimi.Intrinsics.makeRc(View.init(n@ref))\nlet copy = Kimi.Intrinsics.clone(local@ref)\nlocal = Kimi.Intrinsics.makeRc(View.init(n@ref))\nrequire copy.value == 7 and local.value == 7 else => $abort(\"replace\")", 2)]
    [InlineData("NestedSlot", "let local = Kimi.Intrinsics.makeRc(View.init(n@ref))\nlet stored = (local@move, 1)\nlet copy = Kimi.Intrinsics.clone(stored.0@ref)\n_ = stored@move\nrequire copy.value == 7 else => $abort(\"slot\")", 1)]
    public void CloneRetainsExternalDependenciesWithoutBorrowingItsSourceSlot(string name, string use, int allocations)
        => SharedObjectRuntimeTest.WriteModes("Origin" + name, View + "let n = 7\n" + use, allocations, allocations, allocations * 24);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Tuple", "let stored = (owner@move, 1)\nlet copy = Kimi.Intrinsics.clone(stored.0@ref)\nrequire copy.read() == 7 else => $abort(\"tuple\")")]
    [InlineData("TuplePattern", "let stored = (owner@move, 1)\nmatch stored@move\n    (let item, _)\n        require item.read() == 7 else => $abort(\"tuple\")")]
    [InlineData("FixedArray", "let stored: [1 of rc/Item] = [owner@move]\nlet copy = Kimi.Intrinsics.clone(stored[0]@ref)\nrequire copy.read() == 7 else => $abort(\"array\")")]
    [InlineData("Option", "let stored: Option<rc/Item> = .Some(owner@move)\nmatch stored@move\n    .Some(let item)\n        let copy = Kimi.Intrinsics.clone(item@ref)\n        require copy.value == 7 else => $abort(\"option\")\n    .None => $abort(\"none\")")]
    [InlineData("Capture", "let visit = func [owner@move] () => owner.read()\nrequire visit() == 7 and visit() == 7 else => $abort(\"capture\")")]
    [InlineData("Borrow", "let view = owner@objref\nlet payload = owner@follow@ref\nrequire view.value == 7 and payload.value == 7 else => $abort(\"borrow\")")]
    public void OrdinaryStorageAndBorrowPlansPreserveTheFinalRelease(string name, string use)
        => SharedObjectRuntimeTest.WriteModes("Storage" + name, Item + "let owner = Kimi.Intrinsics.makeRc(Item.init())\n" + use, 1, 1, 20, "drop\n");

    // SPEC 15.6.5, 16.3.3: the payload slots of an owned handle parameter are the callee's fixed Origins, naming the caller's
    // Loans as a by-value aggregate's slots do, so the callee may read, Move or destroy the handle and its release observes them.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Destroyed", "func consume(handle: rc/Observer)\n    Console.writeLine(\"consumed\")\nvar n = 7\nconsume(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))\nn = 9\nConsole.writeLine(\"after\")", 1, "consumed\nobserved\nafter\n")]
    [InlineData("Moved", "func consume(handle: rc/Observer)\n    let local = handle@move\n    Console.writeLine(\"consumed\")\nvar n = 7\nconsume(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))\nn = 9\nConsole.writeLine(\"after\")", 1, "consumed\nobserved\nafter\n")]
    [InlineData("Read", "func consume(handle: rc/Observer)\n    require handle.value == 7 else => $abort(\"read\")\n    Console.writeLine(\"consumed\")\nvar n = 7\nconsume(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))\nn = 9\nConsole.writeLine(\"after\")", 1, "consumed\nobserved\nafter\n")]
    [InlineData("Named", "func consume(handle: rc/Observer{a})\n    Console.writeLine(\"consumed\")\nvar n = 7\nconsume(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))\nn = 9\nConsole.writeLine(\"after\")", 1, "consumed\nobserved\nafter\n")]
    [InlineData("Retained", "func consume(handle: rc/Observer)\n    Console.writeLine(\"consumed\")\nlet n = 7\nlet h = Kimi.Intrinsics.makeRc(Observer.init(n@ref))\nlet keep = Kimi.Intrinsics.clone(h@ref)\nconsume(h@move)\nConsole.writeLine(\"after\")\n_ = keep@move", 1, "consumed\nafter\nobserved\n")]
    [InlineData("Nested", "struct Box {s}\n    public let inner: rc/(Observer during s)\n    public init(inner: rc/(Observer during s)) => self.inner = inner@move\nfunc consume(handle: rc/Box)\n    Console.writeLine(\"consumed\")\nlet n = 7\nconsume(Kimi.Intrinsics.makeRc(Box.init(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))))\nConsole.writeLine(\"after\")", 2, "consumed\nobserved\nafter\n")]
    public void OwnedHandleParametersReleaseThroughTheirCallersLoans(string name, string use, int allocations, string stdout)
        => SharedObjectRuntimeTest.WriteModes("Callee" + name, Observer + use, allocations, allocations, allocations * 24, stdout);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExclusiveObjectParametersReleaseThroughTheirCallersLoans()
        => NativeAllocationAudit.WriteFixture("SharedRcObjCallee", Observer + "func consume(handle: obj/Observer)\n    let local = handle@move\n    Console.writeLine(\"consumed\")\nvar n = 7\nconsume(Kimi.Intrinsics.makeObj(Observer.init(n@ref)))\nn = 9\nConsole.writeLine(\"after\")", 1, 1, 24, "consumed\nobserved\nafter\n");

    // Borrows reached through an owned handle parameter still hold it, and the caller's Loan still protects the borrowed local.
    [Theory]
    [InlineData("func consume(handle: rc/Observer)\n    let r = handle@follow@ref\n    let local = handle@move\n    require r.value == 7 else => $abort(\"read\")\nlet n = 7\nconsume(Kimi.Intrinsics.makeRc(Observer.init(n@ref)))", "handle@move", "handle")]
    [InlineData("func consume(handle: rc/Observer) => ()\nvar n = 7\nlet h = Kimi.Intrinsics.makeRc(Observer.init(n@ref))\nlet keep = Kimi.Intrinsics.clone(h@ref)\nconsume(h@move)\nn = 9\n_ = keep@move", "n = 9", "n = 9")]
    public void OwnedHandleParametersKeepTheLoansOfTheirBorrows(string use, string anchor, string target)
    {
        foreach (var atomic in new[] { false, true })
        {
            var source = Observer + use;
            source = atomic ? SharedObjectRuntimeTest.ArcSource(source) : source;
            var error = Assert.Single(Diagnose(source).Diagnostics);
            Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
            Assert.Equal(source.IndexOf(anchor, StringComparison.Ordinal), error.Span!.Value.Start);
            Assert.Equal(target, source.Substring(error.Span.Value.Start, error.Span.Value.Length));
        }
    }

    // SPEC 16.3.3: a handle Moved or initialized on only some paths is released only where it is still initialized.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Moves", "func local(source: ref/rc/Item, flag: bool)\n    let copy = Kimi.Intrinsics.clone(source)\n    if flag => sink(copy@move)\n    Console.writeLine(\"local\")\nfunc parameter(handle: rc/Item, flag: bool)\n    if flag => sink(handle@move)\n    Console.writeLine(\"parameter\")\nfunc arm(source: ref/rc/Item, flag: bool)\n    let copy = Kimi.Intrinsics.clone(source)\n    match flag\n        true => sink(copy@move)\n        false => ()\n    Console.writeLine(\"arm\")\nfunc parts(source: ref/rc/Item, flag: bool)\n    let tuple = (Kimi.Intrinsics.clone(source), 5)\n    let option: Option<rc/Item> = .Some(Kimi.Intrinsics.clone(source))\n    let wrap = Wrap.init(Kimi.Intrinsics.clone(source))\n    if flag\n        _ = tuple@move\n        _ = option@move\n        _ = wrap@move\n    Console.writeLine(\"parts\")\nfunc cycle(owner: ref/rc/Item, flag: bool)\n    local(owner, flag)\n    parameter(Kimi.Intrinsics.clone(owner), flag)\n    arm(owner, flag)\n    parts(owner, flag)\nlet owner = Kimi.Intrinsics.makeRc(Item.init())\ncycle(owner@ref, true)\ncycle(owner@ref, false)\nConsole.writeLine(\"alive\")", 1, "sunk\nlocal\nsunk\nparameter\nsunk\narm\nparts\nlocal\nparameter\narm\nparts\nalive\ndrop\n")]
    [InlineData("Initialization", "func fill(source: ref/rc/Item, flag: bool)\n    var slot: rc/Item\n    if flag => slot = Kimi.Intrinsics.clone(source)\n    Console.writeLine(\"filled\")\nfunc refill(source: ref/rc/Item, flag: bool)\n    var slot = Kimi.Intrinsics.clone(source)\n    if flag => sink(slot@move)\n    slot = Kimi.Intrinsics.makeRc(Item.init())\n    Console.writeLine(\"refilled\")\nfunc cycle(owner: ref/rc/Item, flag: bool)\n    fill(owner, flag)\n    refill(owner, flag)\nlet owner = Kimi.Intrinsics.makeRc(Item.init())\ncycle(owner@ref, true)\ncycle(owner@ref, false)\nConsole.writeLine(\"alive\")", 3, "filled\nsunk\nrefilled\ndrop\nfilled\nrefilled\ndrop\nalive\ndrop\n")]
    public void PathDependentHandlesReleaseOnlyWhereInitialized(string name, string use, int allocations, string stdout)
        => SharedObjectRuntimeTest.WriteModes("Path" + name, Item + "struct Wrap\n    public let handle: rc/Item\n    public init(handle: rc/Item) => self.handle = handle@move\nfunc sink(handle: rc/Item) => Console.writeLine(\"sunk\")\n" + use, allocations, allocations, allocations * 20, stdout);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExclusiveObjectsShareThePathDependentRelease()
        => NativeAllocationAudit.WriteFixture("SharedRcObjPath", Item + "func sink(handle: obj/Item) => Console.writeLine(\"sunk\")\nfunc run(flag: bool)\n    let owner = Kimi.Intrinsics.makeObj(Item.init())\n    if flag => sink(owner@move)\n    Console.writeLine(\"run\")\nrun(true)\nrun(false)", 2, 2, 40, "sunk\ndrop\nrun\nrun\ndrop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void EmptyCasesNeverReleaseAnInactiveHandle()
        => SharedObjectRuntimeTest.WriteModes("EmptyOption", Item + "let value: Option<rc/Item> = .None\nmatch value@move\n    .Some(let item) => $abort(\"some\")\n    .None => Console.writeLine(\"none\")", 0, 0, 0, "none\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExclusiveObjectsShareTheCompleteHandlePatternPath()
        => NativeAllocationAudit.WriteFixture("SharedRcObjPattern", Item + "let value: Option<obj/Item> = .Some(Kimi.Intrinsics.makeObj(Item.init()))\nmatch value@move\n    .Some(let item)\n        require item.value == 7 else => $abort(\"value\")\n    .None => $abort(\"none\")", 1, 1, 20, "drop\n");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExternalLoansRemainLiveForReadsAndDestructorObservations(bool destructor, bool atomic)
    {
        var source = View + (destructor ? "    drop => require self.value == 7 else => $abort(\"drop\")\n" : string.Empty) +
            "var n = 7\nlet first = Kimi.Intrinsics.makeRc(View.init(n@ref))\nlet copy = Kimi.Intrinsics.clone(first@ref)\n_ = first@move\nn = 9\n" +
            (destructor ? "()" : "require copy.value == 7 else => $abort(\"read\")");
        var c = MinimalEmissionTest.Analyze(atomic ? SharedObjectRuntimeTest.ArcSource(source) : source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    // SPEC 3.6.2, 8.10: a generic definition is judged with the temporary handle's Loan before any instance exists.
    [Fact]
    public void GenericDefinitionsKeepTheTemporaryHandleLoan()
    {
        const string Source = "struct Item\n    public var id: i32 = 7\nfunc keep<T>(make: () -> rc/T) -> ()\n    T is ObjectPayload\n    let r = make()@objref\n    let s = r\n()";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "let r = make()@objref"), (error.Code, Source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // SPEC 3.6.2: an iteration source's temporaries, a value or a handle, last for the loop, as a match Subject's do.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void IterationSourcesKeepTheirTemporariesForTheLoop()
        => SharedObjectRuntimeTest.WriteModes("IterationTemporary", "func values() -> Array<i32> => [1, 2]\nfunc handles() -> rc/Array<i32> => Kimi.Intrinsics.makeRc(values())\nvar sum: i32 = 0\nfor x in values()@ref\n    sum += x@follow\nfor y in handles()@follow@ref\n    sum += y@follow\nrequire sum == 6 else => $abort(\"for\")\nConsole.writeLine(\"done\")", 3, 3, 72, "done\n");

    // SPEC 13.5.5.2: object borrows, implicit object views and payload follows retain nothing; the run shows one release.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewsAndPayloadFollowsChangeNoCount(bool atomic)
    {
        var source = Item + "func inspect(view: objref/Item) -> i32 => view.value\nlet owner = Kimi.Intrinsics.makeRc(Item.init())\nlet view = owner@objref\nlet adapted: objref/Item = owner\nlet pair = (owner@objref, 1)\nlet payload = owner@follow@ref\n" +
            "require view.value + adapted.value + pair.0.value + payload.value + inspect(owner) + owner.read() == 42 else => $abort(\"views\")";
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(atomic ? SharedObjectRuntimeTest.ArcSource(source) : source));
        Assert.Contains("define internal void @__kimi_clone_" + (atomic ? "arc(" : "rc("), ir, StringComparison.Ordinal);
        Assert.DoesNotContain("call void @__kimi_clone_", ir, StringComparison.Ordinal);
    }

    // SPEC 13.5.8, 15.1: strong duplication is only clone, so a bare handle is never implicitly duplicated, and a Moved
    // handle is never reused; reading, moving or cloning it again reports the Move.
    [Theory]
    [InlineData("let alias = first", "TransferRequired_Kd", true)]
    [InlineData("let moved = first@move\nlet again = first@move", "MovedPlace_Kd", true)]
    [InlineData("let moved = first@move\nlet value = first.value", "MovedPlace_Kd", true)]
    [InlineData("let moved = first@move\nlet copy = Kimi.Intrinsics.clone(first@ref)", "MovedPlace_Kd", false)]
    public void HandlesAreNeitherDuplicatedNorReusedAfterAMove(string use, string code, bool alone)
    {
        foreach (var atomic in new[] { false, true })
        {
            var source = Item + "let first = Kimi.Intrinsics.makeRc(Item.init())\n" + use;
            source = atomic ? SharedObjectRuntimeTest.ArcSource(source) : source;
            var result = Diagnose(source);
            var error = alone ? Assert.Single(result.Diagnostics) : Assert.Single(result.Diagnostics, x => x.Code == code);
            Assert.Equal(code, error.Code);
            Assert.Equal(source.LastIndexOf("first", StringComparison.Ordinal), error.Span!.Value.Start);
        }
    }

    // SPEC 13.5.5.1, 13.5.8, 16.2.2: a payload follow, a Field borrow and a receiver-borrow result keep a Loan on the handle,
    // and a handle whose payload borrows a local keeps that local's Loan wherever it goes.
    [Theory]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet payload = owner@follow@ref\nlet moved = owner@move\nrequire payload.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner@move", 5)]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet payload = owner@follow@ref\nsink(owner@move)\nrequire payload.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner@move", 5)]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet payload = owner@follow@ref\nowner = Kimi.Intrinsics.makeRc(Item.init())\nrequire payload.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner = Kimi.Intrinsics.makeRc(Item.init())", 0)]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet payload = owner@follow@ref\nlet slot = owner@uniq\nrequire payload.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner@uniq", 5)]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet field = owner.value@ref\nlet moved = owner@move\nrequire field == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner@move", 5)]
    [InlineData("var owner = Kimi.Intrinsics.makeRc(Item.init())\nlet result = owner.view()\nlet moved = owner@move\nrequire result == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "owner@move", 5)]
    [InlineData("let kept = label out: do\n    let owner = Kimi.Intrinsics.makeRc(Item.init())\n    exit to out owner@follow@ref\nrequire kept.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "exit to out owner@follow@ref", 0)]
    [InlineData("let kept = label out: do\n    let n = 7\n    exit to out Kimi.Intrinsics.makeRc(View.init(n@ref))\nrequire kept.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "exit to out Kimi.Intrinsics.makeRc(View.init(n@ref))", 0)]
    [InlineData("let kept = label out: do\n    let n = 7\n    let local = Kimi.Intrinsics.makeRc(View.init(n@ref))\n    exit to out Kimi.Intrinsics.clone(local@ref)\nrequire kept.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "exit to out Kimi.Intrinsics.clone(local@ref)", 0)]
    [InlineData("let m = 1\nvar slot = Kimi.Intrinsics.makeRc(View.init(m@ref))\ndo\n    let n = 7\n    slot = Kimi.Intrinsics.makeRc(View.init(n@ref))\nrequire slot.value == 7 else => $abort(\"live\")", "ComparisonLoanConflict_Kd", "let n = 7\n    slot = Kimi.Intrinsics.makeRc(View.init(n@ref))", 0)]
    [InlineData("func leak() -> rc/View\n    let n = 7\n    return Kimi.Intrinsics.makeRc(View.init(n@ref))\nrequire leak().value == 7 else => $abort(\"live\")", "UnsatisfiedOriginRelation_Kd", "Kimi.Intrinsics.makeRc(View.init(n@ref))", 0)]
    public void HandlesKeepTheLoansOfTheirBorrowsAndPayloads(string use, string code, string target, int length)
    {
        foreach (var atomic in new[] { false, true })
        {
            var source = View + Item + "    public func view(self: ref/Self) -> ref/i32 during self => self.value@ref\nfunc sink(handle: rc/Item) => ()\n" + use;
            source = atomic ? SharedObjectRuntimeTest.ArcSource(source) : source;
            target = atomic ? SharedObjectRuntimeTest.ArcSource(target) : target;
            var error = Assert.Single(Diagnose(source).Diagnostics);
            Assert.Equal(code, error.Code);
            Assert.Equal(target[..(length == 0 ? target.Length : length)], source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PublicOutputsProtectBorrowedHandlesAgainstMoves(bool conflict, bool atomic)
    {
        var path = Path.GetFullPath(atomic ? "arc-loan.kimi" : "rc-loan.kimi");
        var source = Item + "let owner = Kimi.Intrinsics.makeRc(Item.init())\nlet view = owner@objref\n" +
            (conflict ? "let moved = owner@move\n" : string.Empty) + "require view.value == 7 else => $abort(\"live\")";
        source = atomic ? SharedObjectRuntimeTest.ArcSource(source) : source;
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        if (conflict)
        {
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
            Assert.Equal("owner", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
            foreach (var related in new[] { false, true })
            {
                var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
                Assert.Equal(error.Display!.Range, sent.Range);
                Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
            }
        }
        else
        {
            Assert.Empty(result.Diagnostics);
        }
    }

    private static DiagnosticResult Diagnose(string source)
    {
        var path = Path.GetFullPath("shared-object-ownership.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        return c.Diagnostics.Finalize();
    }
}

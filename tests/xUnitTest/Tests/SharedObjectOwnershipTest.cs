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
}

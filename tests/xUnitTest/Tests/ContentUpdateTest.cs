// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.7: complete storage has its own Loan, distinct from the references held by its current contents.
public class ContentUpdateTest
{
    private const string Generic = "func put<T>(slot: uniq/T, value: T) => Kimi.Intrinsics.replace(slot, with: value@move)\n" +
        "func change<T>(slot: uniq/T, value: T) -> T => Kimi.Intrinsics.exchange(slot, with: value@move)\n" +
        "func flip<T>(left: uniq/T, right: uniq/T) => Kimi.Intrinsics.swap(left, right)\n";

    private const string Inputs = "var a: i32 = 1\nvar b: i32 = 2\n";
    private const string View = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n";

    [Theory]
    [InlineData("Shared", "func run(a: ref/i32 during s, b: ref/i32 during s) -> i32\n    var slot = a\n    let old = change(slot@uniq, b)\n    put(slot@uniq, a)\n    var other = b\n    flip(slot@uniq, other@uniq)\n    return old@follow + slot@follow + other@follow\n", "require run(a@ref, b@ref) == 4 else => $abort(\"shared slots\")")]
    [InlineData("UniqueMove", "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a@move\n    let old = change(slot@uniq, b@move)\n    old@follow = 10\n    slot@follow = 20\n", "run(a@uniq, b@uniq)\nrequire a == 10 and b == 20 else => $abort(\"unique slots\")")]
    [InlineData("UniqueReverse", "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a@move\n    let old = change(slot@uniq, b@move)\n    slot@follow = 20\n    old@follow = 10\n", "run(a@uniq, b@uniq)\nrequire a == 10 and b == 20 else => $abort(\"unique reverse\")")]
    public void GenericUpdatesRequireNoOwnedConstraint(string name, string function, string use)
        => ScalarEmissionTest.EmitFixture("ContentUpdate" + name, Generic + function + Inputs + use, string.Empty);

    [Theory]
    [InlineData("Replace", "    Kimi.Intrinsics.replace(slot@uniq, with: b)\n    a@follow = 9\n    return slot@follow\n", 2)]
    [InlineData("Assignment", "    let target = slot@uniq\n    target@follow = b\n    a@follow = 9\n    return slot@follow\n", 2)]
    [InlineData("Exchange", "    let old = Kimi.Intrinsics.exchange(slot@uniq, with: b)\n    let seen = slot@follow\n    b@follow = 9\n    return old@follow + seen\n", 3)]
    [InlineData("Swap", "    var other = b\n    Kimi.Intrinsics.swap(slot@uniq, other@uniq)\n    let seen = slot@follow\n    b@follow = 9\n    return other@follow + seen\n", 3)]
    public void UpdatesTransferOnlyTheOldOrNewContentsLoans(string name, string operations, int expected)
    {
        var source = "func run(a: uniq/i32 during s, b: uniq/i32 during s) -> i32\n    var slot = a\n" + operations + Inputs +
            "require run(a@uniq, b@uniq) == " + expected + " else => $abort(\"content loans\")";
        ScalarEmissionTest.EmitFixture("ContentUpdate" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void DependentHandleSlotsKeepIndependentExternalReferences(string mode)
    {
        var make = "Kimi.Intrinsics.make" + mode + "(View.init(n))";
        var source = View + Generic + "func run(n: ref/i32 during a)\n    var first = " + make + "\n    var second = " + make +
            "\n    let kept: ref/i32 during a = first.value\n    let old = change(first@uniq, " + make + ")\n    flip(first@uniq, second@uniq)\n    put(second@uniq, old@move)\n    require kept@follow == 7 and first.value == 7 and second.value == 7 else => $abort(\"external reference\")\nlet n = 7\nrun(n@ref)";
        NativeAllocationAudit.WriteFixture("ContentUpdateHandle" + mode, source, 3, 3, 72);
    }

    [Theory]
    [InlineData("    let old = Kimi.Intrinsics.exchange(slot@uniq, with: b)\n    a@follow = 9\n    return old@follow\n")]
    [InlineData("    Kimi.Intrinsics.replace(slot@uniq, with: b)\n    b@follow = 9\n    return slot@follow\n")]
    public void RetainedOldAndNewLoansRejectParentWrites(string operations)
    {
        var source = "func run(a: uniq/i32 during s, b: uniq/i32 during s) -> i32\n    var slot = a\n" + operations;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static issue => issue.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.InternalInvariant_Kd);
        Assert.False(c.Validate(out _));
    }

    [Fact]
    public void SharedStringReferencesUsePointerValues()
    {
        var source = Generic + "func run(a: ref/string during s, b: ref/string during s)\n    var first = a\n    var second = b\n    let old = change(first@uniq, b)\n    flip(first@uniq, second@uniq)\n    put(second@uniq, a)\n    require old == \"a\" and first == \"b\" and second == \"a\" else => $abort(\"string references\")\nlet a = \"a\"\nlet b = \"b\"\nrun(a@ref, b@ref)";
        ScalarEmissionTest.EmitFixture("ContentUpdateStringReferences", source, string.Empty);
    }

    [Fact]
    public void ObjectPayloadUpdatesPreserveTheAllocationAndExternalReference()
    {
        var source = View + "func run(n: ref/i32 during a)\n    var owner = Kimi.Intrinsics.makeObj(View.init(n))\n    let kept: ref/i32 during a = owner.value\n    let address = owner@follow@raw\n" +
            "    let old = Kimi.Intrinsics.exchange(owner@follow@uniq, with: View.init(n))\n    Kimi.Intrinsics.replace(owner@follow@uniq, with: old@move)\n    require kept@follow == 7 and owner.value == 7 and address == owner@follow@raw else => $abort(\"payload\")\nlet n = 7\nrun(n@ref)";
        NativeAllocationAudit.WriteFixture("ContentUpdatePayload", source, 1, 1, 24);
    }

    // The first row expects the rule before Loan identity by base (A5 S1): `replace` of the base `slot` now ends the child
    // (SPEC 15.6.3), so the row is valid; the default pipeline keeps the old rule until R6-check.
    [Theory]
    [Trait("OldRule", "S1")]
    [InlineData("    let child = slot\n    Kimi.Intrinsics.replace(slot@uniq, with: b)\n    _ = child@follow")]
    [InlineData("    let view = slot@ref\n    Kimi.Intrinsics.replace(slot@uniq, with: b)\n    _ = view@follow@follow")]
    public void OldContentAndSlotViewsCannotSurviveReplacement(string operations)
    {
        var source = "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a\n" + operations;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static issue => issue.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.InternalInvariant_Kd);
        Assert.False(c.Validate(out _));
    }

    [Fact]
    public void ReplacementEndsOldDependenciesOnLoopBackEdges()
    {
        const string function = "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a@follow@ref\n    var i = 0\n    while i < 2\n        Kimi.Intrinsics.replace(slot@uniq, with: b@follow@ref)\n        a@follow += 1\n        require slot@follow == 2 else => $abort(\"loop reference\")\n        i += 1\n";
        ScalarEmissionTest.EmitFixture("ContentUpdateLoop", function + Inputs + "run(a@uniq, b@uniq)\nrequire a == 3 else => $abort(\"loop\")", string.Empty);
    }

    [Fact]
    public void InstalledDependenciesRemainLiveOnLoopBackEdges()
    {
        const string source = "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = b@follow@ref\n    var i = 0\n    while i < 2\n        a@follow += 1\n        Kimi.Intrinsics.replace(slot@uniq, with: a@follow@ref)\n        require slot@follow > 0 else => $abort(\"loop reference\")\n        i += 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static issue => issue.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.InternalInvariant_Kd);
    }

    // The `replace` row expects the rule before Loan identity by base (A5 S1): `replace` of the base now ends the child
    // (SPEC 15.6.3), so the row is valid; the default pipeline keeps the old rule until R6-check.
    [Theory]
    [Trait("OldRule", "S1")]
    [InlineData("Kimi.Intrinsics.replace(slot@uniq, with: b)")]
    [InlineData("_ = Kimi.Intrinsics.exchange(slot@uniq, with: b)")]
    [InlineData("var other = b\n    Kimi.Intrinsics.swap(slot@uniq, other@uniq)")]
    [InlineData("let target = slot@uniq\n    target@follow = b")]
    public void ExclusiveContentChildrenBlockEveryUpdate(string update)
    {
        var source = "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a\n    let child = slot\n    " + update + "\n    child@follow = 3";
        var result = DiagnosticCorpus.Check(source);
        Assert.Contains(result.Diagnostics, static d => d.Code == "ComparisonLoanConflict_Kd");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code.Contains("Unsupported", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentSharedCopiesSurviveContentUpdates(bool exchange)
    {
        var source = "func run(a: ref/i32 during s, b: ref/i32 during s) -> i32\n    var slot = a\n    let copy = slot\n    " +
            (exchange ? "_ = Kimi.Intrinsics.exchange(slot@uniq, with: b)" : "Kimi.Intrinsics.replace(slot@uniq, with: b)") +
            "\n    return copy@follow + slot@follow\n" + Inputs + "require run(a@ref, b@ref) == 3 else => $abort(\"shared copy\")";
        ScalarEmissionTest.EmitFixture("ContentUpdateSharedCopy" + exchange, source, string.Empty);
    }

    [Fact]
    public void OverwritingACallUpdatedSlotKeepsReturnedChildrenExclusive()
    {
        var source = Generic + "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a@move\n    let old = change(slot@uniq, b@move)\n    Kimi.Intrinsics.replace(slot@uniq, with: old)\n    old@follow = 4\n    slot@follow = 5";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AggregateExclusiveContentsRemainIndependent(bool generic, bool oldFirst)
    {
        var update = generic ? "change(slot@uniq, (b@move, 2))" : "Kimi.Intrinsics.exchange(slot@uniq, with: (b@move, 2))";
        var source = Generic + "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = (a@move, 1)\n    let old = " + update +
            (oldFirst ? "\n    old.0@follow = 10\n    slot.0@follow = 20\n" : "\n    slot.0@follow = 20\n    old.0@follow = 10\n") + Inputs + "run(a@uniq, b@uniq)\nrequire a == 10 and b == 20 else => $abort(\"aggregate loans\")";
        ScalarEmissionTest.EmitFixture("ContentUpdateAggregateExclusive" + generic + oldFirst, source, string.Empty);
    }

    [Fact]
    public void AReplacedStoredPartCannotReuseCallIndependence()
    {
        var source = Generic + "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = (a@move, 1)\n    let old = change(slot@uniq, (b@move, 2))\n    slot.0 = old.0@follow@uniq\n    old.0@follow = 4\n    slot.0@follow = 5";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
    }

    [Theory]
    [InlineData("replace", "Kimi.Intrinsics.replace(slot@uniq, with: (b, 2))")]
    [InlineData("exchange", "_ = Kimi.Intrinsics.exchange(slot@uniq, with: (b, 2))")]
    public void AggregateOldContentBorrowsRejectUpdates(string name, string update)
    {
        var source = "func run(a: ref/i32 during s, b: ref/i32 during s)\n    var slot = (a, 1)\n    let child = slot.0@ref\n    " + update + "\n    _ = child@follow@follow";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, name + MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static issue => issue.Code.IsLoanConflict());
    }

    [Fact]
    public void DependentPayloadDestructionRunsBeforePlacement()
    {
        const string item = "struct Item {a}\n    public let source: ref/i32 during a\n    public let id: i32\n    public init(source: ref/i32 during a, id: i32)\n        self.source = source\n        self.id = id\n    drop => Console.writeLine(Text.toString(self.id))\n";
        var source = item + Generic + "func run(n: ref/i32 during a)\n    var slot = Item.init(n, 1)\n    let old = change(slot@uniq, Item.init(n, 2))\n    var other = Item.init(n, 3)\n    flip(slot@uniq, other@uniq)\n    put(slot@uniq, old@move)\n    Console.writeLine(\"updated\")\nlet n = 7\nrun(n@ref)";
        ScalarEmissionTest.EmitFixture("ContentUpdateDestruction", source, "3\nupdated\n2\n1\n");
    }

    // Expects the rule before Loan identity by base (A5 S1): `replace` of the base `slot` now ends the child (SPEC 15.6.3),
    // so the program is valid; the default pipeline keeps the old rule until R6-check.
    [Fact]
    [Trait("OldRule", "S1")]
    public void OldContentConflictExplainsTheCauseInEveryAdapter()
    {
        const string source = "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a\n    let child = slot\n    Kimi.Intrinsics.replace(slot@uniq, with: b)\n    child@follow = 3";
        var path = Path.GetFullPath("content-update.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", record.Code);
        Assert.Equal("This operation conflicts with an active loan", record.Message);
        Assert.Equal("Kimi.Intrinsics.replace(slot@uniq, with: b)", source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        var loan = Assert.Single(record.Related!);
        Assert.Equal("loan", loan.Role);
        Assert.Equal("let child = slot", source.Substring(loan.Span!.Value.Start, loan.Span.Value.Length));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains(record.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("a@follow = 3")]
    [InlineData("Kimi.Intrinsics.replace(a, with: 3)")]
    [InlineData("_ = Kimi.Intrinsics.exchange(a, with: 3)")]
    public void OpaqueReferentUpdatesPreserveTheStorageLoanAtEveryAccess(string update)
    {
        var source = "var values: Array<i32> = [1, 2]\nmatch values.tryGetPairUniq(first: 0, second: 1)\n    .Some((let a, let b))\n        let child = a@follow@ref\n        " + update +
            "\n        b@follow = 4\n        " + update + "\n        require child == 1 else => $abort(\"child\")\n    .None => ()";
        var result = DiagnosticCorpus.Check(source);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.All(result.Diagnostics, static record => Assert.True(record.Code is "ComparisonLoanConflict_Kd" or "CallActivationConflict_Kd"));
    }

    // SPEC 15.3.6, 15.6.4 step 3 (DECISIONS R4-M155): the call's solve takes the substituted well-formedness of every filled parameter
    // beside the argument's own Type, and an exclusive layer's target is the argument's own (the fit makes it equal), so it adds no
    // operand: `right`'s Origin is second's borrow alone. The operand `a` that the argument's Type entails made the old analysis read
    // second's reservation as dependent on first's Loan (CallActivationConflict_Kd in DependentHandleSlotsKeepIndependentExternalReferences).
    [Fact]
    public void AnExclusiveArgumentsRegionTakesNoOperandItsOwnTypeGives()
    {
        const string make = "Kimi.Intrinsics.makeObj(View.init(n))";
        var c = MinimalEmissionTest.Analyze(View + Generic + "func run(n: ref/i32 during a)\n    var first = " + make + "\n    var second = " + make + "\n    flip(first@uniq, second@uniq)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Problems);
        var flip = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(static call => call.ToString().StartsWith("flip(", StringComparison.Ordinal)).CallOf()!;
        Assert.All(flip.ArgumentOperations.ToArray(), static operation => Assert.NotEqual(OriginKind.Intersection, operation.ParameterType!.Origin!.Kind));
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void ContentFlowAndCallIndependenceReuseStorage()
        {
            var c = MinimalEmissionTest.Analyze(Generic + "func run(a: uniq/i32 during s, b: uniq/i32 during s)\n    var slot = a@move\n    let old = change(slot@uniq, b@move)\n    slot@follow = 20\n    old@follow = 10\n" + Inputs + "run(a@uniq, b@uniq)");
            Assert.True(c.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
            var restored = CompilationTestHelper.Reload(c);
            Assert.True(restored.Bind().IsComplete);
            restored.Binding.CheckStartup(OutputKind.Application);
            Assert.True(restored.Analyze().IsVerified);
            Assert.True(restored.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
        }
    }
}

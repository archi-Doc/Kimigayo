// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class OwnershipPropertyEmissionTest
{
    internal const string Resource = "struct Resource\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"\\(self.id)\")\n";
    internal const string Holder = "struct Holder\n    public var item: Resource\n        set(value: Resource) -> ()\n            Console.writeLine(\"set\")\n            storage = value@move\n    public init(value: Resource) => self.item = value@move\n";

    [Fact]
    public void StandardReplacementIsTheOwnershipControl()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyStandard", Resource + "struct S\n    public var item: Resource\n    public init(value: Resource) => self.item = value@move\ndo\n    var s = S.init(Resource.init(1))\n    s.item = Resource.init(2)", "1\n2\n");

    [Fact]
    public void NonCopySetterTransfersInputAndReplacesStorageOnce()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyReplace", Resource + Holder + "do\n    var h = Holder.init(Resource.init(1))\n    h.item = Resource.init(2)\n    require h.item.id == 2 else => $abort(\"replacement\")", "set\n1\n2\n");

    [Fact]
    public void NonCopySetterDiscardsInputAndPreservesStorage()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyDiscard", Resource + Holder.Replace("storage = value@move", "_ = value@move", StringComparison.Ordinal) + "do\n    var h = Holder.init(Resource.init(1))\n    h.item = Resource.init(2)\n    require h.item.id == 1 else => $abort(\"storage\")", "set\n2\n1\n");

    [Theory]
    [InlineData("Owned", "var h = Holder.init(Resource.init(1))\nh.item = Resource.init(2)")]
    [InlineData("Borrowed", "func update(h: uniq/Holder) => h.item = Resource.init(2)\nvar h = Holder.init(Resource.init(1))\nupdate(h@uniq)")]
    [InlineData("Moved", "var h = Holder.init(Resource.init(1))\nlet value = Resource.init(2)\nh.item = value@move")]
    public void SetterInputUsesOrdinaryCallAcquisition(string name, string body)
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyInput" + name, Resource + Holder + body, "set\n1\n2\n");

    [Fact]
    public void SetterInputRunsBeforeReceiverAndConstructionBypassesSetter()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyOrder", Resource + Holder + "func receiver(h: uniq/Holder during source) -> uniq/Holder during source\n    Console.writeLine(\"receiver\")\n    return h\nfunc input() -> Resource\n    Console.writeLine(\"input\")\n    return Resource.init(2)\nvar h = Holder.init(Resource.init(1))\nreceiver(h@uniq).item = input()", "input\nreceiver\nset\n1\n2\n");

    [Fact]
    public void ComputedSetterAcceptsItsOwnNonCopyInputType()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyComputedSetter", Resource + "struct S\n    var saved: i32 = 0\n    public computed value: i32\n        get() -> i32 => self.saved\n        set(value: Resource) -> () => self.saved = value.id\nvar s = S.init()\ns.value = Resource.init(2)\nrequire s.value == 2 else => $abort(\"computed setter\")", "2\n");

    [Fact]
    public void SetterHandlesAggregateAndCollectionCleanup()
        => ScalarEmissionTest.EmitFixture("OwnershipPropertyAggregateSetter", Resource + "struct S\n    public var items: Array<Resource>\n        set(value: Array<Resource>) -> () => storage = value@move\n    public init() => self.items = [Resource.init(1)]\nvar s = S.init()\ns.items = [Resource.init(2)]", "1\n2\n");

    [Theory]
    [InlineData("h.item = value", "TransferRequired_Kd")]
    [InlineData("h.item = value@move\n_ = value.id", "MovedPlace_Kd")]
    [InlineData("let view = h.item@ref\nh.item = value@move\n_ = view.id", "CallActivationConflict_Kd")]
    public void SetterPreservesOrdinaryOwnershipRequirements(string body, string code)
    {
        var result = DiagnosticCorpus.Check(Resource + Holder + "var h = Holder.init(Resource.init(1))\nlet value = Resource.init(2)\n" + body);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void SetterMoveFailureKeepsTheUnavailableRootInCliAndLsp()
    {
        var source = Resource + Holder + "var h = Holder.init(Resource.init(1))\nlet value = Resource.init(2)\nh.item = value@move\n_ = value.id";
        var path = Path.GetFullPath("ownership-property.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("MovedPlace_Kd", record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("This value may have been moved on an incoming path", record.Message);
        Assert.Equal(source.LastIndexOf("value.id", StringComparison.Ordinal), record.Span!.Value.Start);
        Assert.Equal(5, record.Span.Value.Length);
        Assert.Empty(record.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    [Fact]
    public void IndependentUnavailableRootsKeepTheirOwnRecords()
    {
        var source = Resource + "let a = Resource.init(1)\nlet b = Resource.init(2)\n_ = a@move\n_ = b@move\n_ = a.id\n_ = b.id";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, records.Length);
        Assert.All(records, static record => Assert.Equal("MovedPlace_Kd", record.Code));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SetterPipelineReusesPreparedStorage()
    {
        var c = MinimalEmissionTest.Analyze(Resource + Holder + "var h = Holder.init(Resource.init(1))\nh.item = Resource.init(2)");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}

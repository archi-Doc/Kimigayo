// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class PropertyResultEmissionTest
{
    internal const string Holder = "struct Holder\n    public var item: Resource\n    public computed view: ref/Resource\n        get() -> ref/Resource => self.item@ref\n    public computed fresh: Resource\n        get() -> Resource => Resource.init(self.item.id + 1)\n    public init(value: Resource) => self.item = value@move\n";

    [Fact]
    public void BorrowGetterRetainsItsReceiverForTheCall()
        => ScalarEmissionTest.EmitFixture("PropertyResultBorrow", OwnershipPropertyEmissionTest.Resource + Holder + "func inspect(r: ref/Resource)\n    require r.id == 1 else => $abort(\"view\")\nvar h = Holder.init(Resource.init(1))\ninspect(h.view)", "1\n");

    [Fact]
    public void OwnedGetterResultTransfersItsOwnDestructionResponsibility()
        => ScalarEmissionTest.EmitFixture("PropertyResultOwned", OwnershipPropertyEmissionTest.Resource + Holder + "do\n    let h = Holder.init(Resource.init(1))\n    let r = h.fresh\n    require r.id == 2 else => $abort(\"fresh\")", "2\n1\n");

    [Fact]
    public void StandardBorrowIsTheResultLifetimeControl()
        => ScalarEmissionTest.EmitFixture("PropertyResultControl", OwnershipPropertyEmissionTest.Resource + "struct S\n    public var item: Resource\n    public init(value: Resource) => self.item = value@move\nlet s = S.init(Resource.init(1))\nlet view = s.item@ref\nrequire view.id == 1 else => $abort(\"view\")", "1\n");

    [Theory]
    [InlineData("let view = h.view\nh.item = Resource.init(2)\n_ = view.id", "ComparisonLoanConflict_Kd")]
    [InlineData("let view = h.view\n_ = h@move\n_ = view.id", "ComparisonLoanConflict_Kd")]
    [InlineData("let view = Holder.init(Resource.init(2)).view\n_ = view.id", "ComparisonLoanConflict_Kd")]
    [InlineData("let view = h.fresh@ref\n_ = view.id", "ComparisonLoanConflict_Kd")]
    public void GetterResultsKeepLoansAndTemporaryLifetimes(string body, string code)
    {
        var result = DiagnosticCorpus.Check(OwnershipPropertyEmissionTest.Resource + Holder + "var h = Holder.init(Resource.init(1))\n" + body);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("Borrowed", "func check(h: ref/Holder)\n    require h.view.id == 1 else => $abort(\"borrowed\")\nlet h = Holder.init(Resource.init(1))\ncheck(h@ref)")]
    [InlineData("Saved", "var h = Holder.init(Resource.init(1))\nlet view = h.view\nrequire view.id == 1 else => $abort(\"saved\")\nh.item = Resource.init(2)")]
    [InlineData("Temporary", "func check(r: ref/Resource)\n    require r.id == 2 else => $abort(\"temporary\")\nlet h = Holder.init(Resource.init(1))\ncheck(h.fresh@ref)")]
    public void GetterValuesUseOrdinaryAcquisition(string name, string body)
        => ScalarEmissionTest.EmitFixture("PropertyResult" + name, OwnershipPropertyEmissionTest.Resource + Holder + body, name == "Borrowed" ? "1\n" : name == "Temporary" ? "2\n1\n" : "1\n2\n");

    [Fact]
    public void GetterBorrowCanBeReturnedUnderTheReceiverContract()
        => ScalarEmissionTest.EmitFixture("PropertyResultForward", OwnershipPropertyEmissionTest.Resource + Holder + "func view(h: ref/Holder during source) -> ref/Resource during source => h.view\nlet h = Holder.init(Resource.init(1))\nrequire view(h@ref).id == 1 else => $abort(\"forward\")", "1\n");

    [Fact]
    public void GenericGetterInstantiatesItsResultAndBody()
        => ScalarEmissionTest.EmitFixture("PropertyResultGeneric", OwnershipPropertyEmissionTest.Resource + "struct Box<T>\n    var item: T\n    public computed view: ref/T\n        get() -> ref/T => self.item@ref\n    public init(value: T) => self.item = value@move\nlet b = Box.init(Resource.init(1))\nrequire b.view.id == 1 else => $abort(\"generic\")", "1\n");

    [Theory]
    [InlineData("get() -> ref/Resource => self.item@ref")]
    [InlineData("get(self: ref/Self during source) -> ref/Resource during source => self.item@ref")]
    public void GetterOriginSpellingsHaveTheSameLifetime(string getter)
        => ScalarEmissionTest.EmitFixture("PropertyResultOrigin" + (getter.Contains("during", StringComparison.Ordinal) ? "Explicit" : "Implicit"), OwnershipPropertyEmissionTest.Resource + Holder.Replace("get() -> ref/Resource => self.item@ref", getter, StringComparison.Ordinal) + "let h = Holder.init(Resource.init(1))\nlet view = h.view\nrequire view.id == 1 else => $abort(\"origin\")", "1\n");

    [Fact]
    public void GetterKeepsStoredExternalOrigins()
        => ScalarEmissionTest.EmitFixture("PropertyResultExternal", "struct View {a}\n    let item: ref/i32 during a\n    public computed value: ref/i32 during a\n        get() -> ref/i32 during a => self.item\n    public init(item: ref/i32 during a) => self.item = item\nlet n: i32 = 7\nlet v = View.init(n@ref)\nlet r = v.value\nrequire r == 7 else => $abort(\"external\")", string.Empty);

    [Fact]
    public void ExternalGetterResultDoesNotBorrowItsReceiverStorage()
        => ScalarEmissionTest.EmitFixture("PropertyResultExternalSurvives", "struct View {a}\n    let item: ref/i32 during a\n    public computed value: ref/i32 during a\n        get() -> ref/i32 during a => self.item\n    public init(item: ref/i32 during a) => self.item = item\nlet n: i32 = 7\nlet r = label result: do\n    let v = View.init(n@ref)\n    exit to result v.value\nrequire r == 7 else => $abort(\"external lifetime\")", string.Empty);

    [Fact]
    public void FixedGetterReceiverOriginMustBeSatisfied()
    {
        const string source = "struct S\n    public computed value: i32\n        get(self: ref/Self during static) -> i32 => 1\nlet s = S.init()\n_ = s.value";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("UnsatisfiedOriginRelation_Kd", record.Code);
    }

    [Fact]
    public void EscapingGetterBorrowExplainsTemporaryCleanupInCliAndLsp()
    {
        var source = OwnershipPropertyEmissionTest.Resource + Holder + "let h = Holder.init(Resource.init(1))\nlet view = h.fresh@ref\n_ = view.id";
        var path = Path.GetFullPath("property-result.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", record.Code);
        Assert.Equal("This operation conflicts with an active loan", record.Message);
        Assert.NotEmpty(record.Related!);
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

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GetterPipelineReusesOriginAndStoragePlans()
    {
        var c = MinimalEmissionTest.Analyze(OwnershipPropertyEmissionTest.Resource + Holder + "let h = Holder.init(Resource.init(1))\nlet view = h.view\n_ = view.id\n_ = h.fresh");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}

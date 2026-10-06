// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class StoredReborrowAncestryTest(ITestOutputHelper output)
{
    private const string Types = """
        struct Resource
            public var value: i32 = 7
        struct View {source}
            public let item: ref/Resource during source
            public init(item: ref/Resource during source) => self.item = item
        """;

    [Theory]
    [InlineData("Struct", "let view = View.init(item@follow@ref)", "view.item")]
    [InlineData("Moved", "let original = View.init(item@follow@ref)\nlet view = original@move", "view.item")]
    [InlineData("Tuple", "let view = (item@follow@ref, 0)", "view.0")]
    [InlineData("NestedTuple", "let view = (0, (item@follow@ref, 1))", "view.1.0")]
    [InlineData("NestedStruct", "let view = (0, View.init(item@follow@ref))", "view.1.item")]
    [InlineData("CopiedTuple", "let original = (0, item@follow@ref)\nlet view = original", "view.1")]
    [InlineData("Local", "let view = item@follow@ref", "view")]
    public void StoredChildrenRetainTheirActualParent(string name, string storage, string read)
    {
        var source = Source(storage, read, false);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("StoredReborrow" + name, source, string.Empty);
    }

    [Fact]
    public void AStoredChildStillSuspendsItsParent()
    {
        var source = Source("let view = View.init(item@follow@ref)", "view.item", true);
        var path = Path.GetFullPath("stored-reborrow.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal("item.value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var related = Assert.Single(error.Related!);
        Assert.Equal("loan", related.Role);
        Assert.Contains("view", source.Substring(related.Span!.Value.Start, related.Span.Value.Length), StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(error.Display!.Range, sent.Range);
        Assert.Equal(related.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
    }

    [Fact]
    public void RawAnchorParentsKeepStoredChildren()
    {
        var source = Types + "\nfunc run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        let view = View.init(item@follow@ref)\n        let seen = view.item.value\n        item.value = seen + 1\n        return seen\npublic func main() => ()";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualOriginNamesDoNotAuthorizeSiblingAccess(bool conflict)
    {
        var source = Types + "\nfunc run(left: uniq/Resource during a, right: uniq/Resource during a) -> i32\n    let view = View.init(left@follow@ref)\n    " +
            (conflict ? "left" : "right") + ".value = 9\n    return view.item.value\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(!conflict, c.Ownership.Result.IsVerified);
        if (conflict)
        {
            var error = Assert.Single(c.Ownership.Issues);
            Assert.Equal(OwnershipFailure.ComparisonLoanConflict, error.Failure);
            Assert.Equal("left.value", error.Source.ToString());
        }
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void SelectingOnePayloadDoesNotAuthorizeTheOtherParent(string parent)
    {
        var source = Types + "\nfunc run(left: uniq/Resource, right: uniq/Resource) -> i32\n    let view = (left@follow@ref, right@follow@ref)\n    let seen = view.1.value\n    " + parent + ".value = seen\n    return view.0.value + view.1.value\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var error = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.ComparisonLoanConflict, error.Failure);
        Assert.Equal(parent + ".value", error.Source.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmStoredAncestryAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("let view = (0, View.init(item@follow@ref))", "view.1.item", false));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Source(string storage, string read, bool conflict)
        => Types + "\nfunc run(item: uniq/Resource) -> i32\n    " + storage.Replace("\n", "\n    ", StringComparison.Ordinal) +
            "\n    let seen = " + read + ".value\n    item.value = seen + 1\n" +
            (conflict ? "    let later = " + read + ".value\n" : string.Empty) +
            "    return seen\nvar item = Resource.init()\nrequire run(item@uniq) == 7 and item.value == 8 else => $abort(\"ancestry\")";
}

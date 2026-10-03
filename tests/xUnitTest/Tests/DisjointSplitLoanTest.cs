// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DisjointSplitLoanTest(ITestOutputHelper output)
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSiblingLoanAnalysisAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public var value: i32 = 1\nvar values: Array<Item> = [Item.init(), Item.init()]\nmatch values.tryGetPairUniq(first: 0, second: 1)\n    .Some((let a, let b))\n        let child = a@follow@ref\n        b.value += child.value\n        a.value += b.value\n    .None => ()");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, AllocationMeasurement.Measure(
            () =>
            {
                if (!c.Ownership.Analyze().IsVerified)
                {
                    throw new InvalidOperationException("Sibling Loan analysis failed.");
                }
            },
            iterations: 64,
            warmupIterations: 32));
    }

    [Theory]
    [InlineData("Array<i32>", "Array")]
    [InlineData("[2 of i32]", "Fixed")]
    public void ReborrowsAndMovedSiblingsKeepTheirIdentity(string type, string name)
    {
        var source = $$"""
            var values: {{type}} = [1, 2]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    let child = a@follow@ref
                    let moved = b@move
                    moved@follow += child@follow
                    let other = moved@follow@uniq
                    other@follow += child@follow
                    a@follow = other@follow
                .None => $abort("pair")
            require values[0] == 4 and values[1] == 4 else => $abort("writeback")
            """;
        ScalarEmissionTest.EmitFixture("DisjointSplitReborrows" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("ref", "Shared")]
    [InlineData("uniq", "Exclusive")]
    public void ReturnedChildrenKeepTheirSplitIdentity(string mode, string name)
    {
        var source = $$"""
            struct Item
                public var value: i32 = 1
            func child(value: {{mode}}/Item) -> {{mode}}/Item during value => value
            var values: Array<Item> = [Item.init(), Item.init()]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    let first = child(a)
                    b.value += first.value
                    let second = child(b)
                    require first.value == 1 and second.value == 2 else => $abort("children")
                .None => $abort("pair")
            """;
        ScalarEmissionTest.EmitFixture("DisjointSplitReturned" + name, source, string.Empty);
    }

    [Fact]
    public void ReturnedChildrenStillSuspendTheirOwnParent()
    {
        const string Source = "struct Item\n    public var value: i32 = 1\nfunc child(value: ref/Item) -> ref/Item during value => value\nvar values: Array<Item> = [Item.init(), Item.init()]\nmatch values.tryGetPairUniq(first: 0, second: 1)\n    .Some((let a, let b))\n        let first = child(a)\n        a.value = 3\n        b.value = 4\n        require first.value == 1 else => $abort(\"child\")\n    .None => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal("a.value", error.Text);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public void ResultsNamingBothInputsKeepBothLoans(string target)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            struct Item
                public var value: i32 = 1
            func choose(left: ref/Item, right: ref/Item) -> ref/Item during (left and right) => left
            var values: Array<Item> = [Item.init(), Item.init()]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    let both = choose(a, b)
                    {{target}}.value = 3
                    require both.value == 1 else => $abort("child")
                .None => ()
            """);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(target + ".value", error.Text);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Array<Item>", "Array")]
    [InlineData("[2 of Item]", "Fixed")]
    public void SiblingFieldsAndCallArgumentsRemainIndependent(string type, string name)
    {
        var source = $$"""
            struct Item
                public var value: i32
                public init(value: i32) => self.value = value
            func change(left: uniq/Item, right: uniq/Item)
                left.value += 3
                right.value += 4
            var values: {{type}} = [Item.init(10), Item.init(20)]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    a.value += 1
                    b.value += 2
                    change(a, b)
                    require a.value == 14 and b.value == 26 else => $abort("fields")
                .None => $abort("pair")
            require values[0].value == 14 and values[1].value == 26 else => $abort("writeback")
            """;
        ScalarEmissionTest.EmitFixture("DisjointSplitFields" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Array<uniq/i32>", "Array")]
    [InlineData("[2 of uniq/i32]", "Fixed")]
    public void SiblingStoredExclusiveReferencesRemainIndependent(string type, string name)
    {
        var source = $$"""
            var low = 1
            var high = 9
            var values: {{type}} = [low@uniq, high@uniq]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    a@follow@follow += 10
                    b@follow@follow -= 1
                    require a@follow@follow == 11 and b@follow@follow == 8 else => $abort("references")
                .None => $abort("pair")
            require low == 11 and high == 8 else => $abort("writeback")
            """;
        ScalarEmissionTest.EmitFixture("DisjointSplitStored" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("let moved = values@move")]
    [InlineData("values.reserve(10)")]
    [InlineData("values.clear()")]
    public void ARemainingSiblingKeepsTheCollectionBorrowed(string operation)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            var values: Array<i32> = [1, 2]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    a@follow = 3
                    {{operation}}
                    b@follow = 4
                .None => ()
            """);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void SharedReceiversCannotPublishExclusivePairs()
    {
        var c = MinimalEmissionTest.Analyze("func invalid(values: ref/Array<i32>)\n    _ = values.tryGetPairUniq(first: 0, second: 1)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void SplitReferencesCannotOutliveTheirCollection()
    {
        var c = MinimalEmissionTest.Analyze("let pair = label scope: do\n    var values: Array<i32> = [1, 2]\n    exit to scope values.tryGetPairUniq(first: 0, second: 1)\nmatch pair@move\n    .Some((let a, let b))\n        a@follow = 3\n        b@follow = 4\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChildStillSuspendsItsOwnParent(bool independent)
    {
        var source = "var values: Array<i32> = [1, 2]\nmatch values.tryGetPairUniq(first: 0, second: 1)\n" +
            "    .Some((let a, let b))\n        let child = a@follow@ref\n        a@follow = 3\n" +
            (independent ? "        b@follow = 4\n        a@follow = 5\n" : "        b@follow = 4\n") +
            "        require child == 1 else => $abort(\"child\")\n    .None => ()";
        var path = Path.GetFullPath("split-child.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("a", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Equal("This operation conflicts with an active loan", error.Message);
            var retained = Assert.Single(error.Related!);
            Assert.Equal("loan", retained.Role);
            Assert.Contains("child", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal(result.Diagnostics[i].Display!.Range, sent[i].Range);
                if (related)
                {
                    Assert.Equal(result.Diagnostics[i].Related![0].Range, Assert.Single(sent[i].RelatedInformation!).Location.Range);
                }
                else
                {
                    Assert.Contains("value retaining the conflicting loan", sent[i].Message, StringComparison.Ordinal);
                }
            }
        }
    }
}

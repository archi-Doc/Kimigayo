// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

#pragma warning disable SA1117, SA1118 // Multiline language fixtures.

public class UniqSliceTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("var values = [1, 2]\nlet read = values[true]")]
    [InlineData("var values = [1, 2]\nvar view = values.sliceUniq()\nlet read = view[true]")]
    public void InvalidKeysExplainThePositionRequirement(string source)
    {
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal("TypeMismatch_Kd", error.Code);
        Assert.Equal("true", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Null(error.Repairs);
        DiagnosticOutputTestHelper.Single(check, "a Type proven Position or PositionRange", output);
    }

    [Theory]
    [InlineData("view[^0]", "^0")]
    [InlineData("view.sliceUniq(2..1)", "2..1")]
    [InlineData("values.slice(3..1)", "3..1")]
    public void ViewOperationsRetainLiteralFailureWarnings(string operation, string key)
    {
        var source = "var values = [1, 2]\nvar view = values.sliceUniq()\nlet result = " + operation;
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(key, source.Substring(warning.Span!.Value.Start, warning.Span.Value.Length));
    }

    [Theory]
    [InlineData("var second = values.sliceUniq(1..)\nrequire second.length == 1 else => $abort(\"second\")")]
    [InlineData("view.length = 5")]
    public void RegionsDoNotSplitAuthorityOrExposeWritableMetadata(string use)
    {
        var c = MinimalEmissionTest.Analyze("var values = [1, 2]\nvar view = values.sliceUniq(..1)\n" + use + "\nrequire view.length == 1 else => $abort(\"view\")");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("SharedEntries", "let values: [3 of i32] = [1, 2, 3]\nlet whole = values.slice()\nlet part = whole.slice(1..)\nrequire part.slice()[1] == 3 else => $abort(\"shared\")")]
    [InlineData("TemporaryWrite", "var values: [2 of i32] = [1, 2]\nvalues.sliceUniq()[0] = 7\nvalues.sliceUniq(1..)[0] += 3\nrequire values[0] == 7 and values[1] == 5 else => $abort(\"temporary\")")]
    [InlineData("LetExclusiveReference", "var values = [1, 2]\nvar view = values.sliceUniq()\nlet access = view@uniq\naccess[0] = 8\nrequire access[0] == 8 else => $abort(\"reference\")")]
    [InlineData("NestedElements", "var values: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nvar view = values.sliceUniq()\nview[0][1] = 9\nrequire view[0][1] == 9 and view.length == 2 else => $abort(\"nested\")")]
    [InlineData("ZeroSized", "var values: [3 of ()] = [(), (), ()]\nvar view = values.sliceUniq(1..)\nview[1] = ()\nrequire view.length == 2 and view.indices.length == 2 else => $abort(\"zero\")")]
    [InlineData("Empty", "var values: [0 of i32] = []\nvar view = values.sliceUniq()\nvar child = view.sliceUniq(..)\nrequire child.isEmpty and child.slice().isEmpty else => $abort(\"empty\")")]
    [InlineData("Move", "var values = [1, 2]\nvar view = values.sliceUniq()\nvar moved = view@move\nmoved[0] = 5\nrequire moved[0] == 5 else => $abort(\"move\")")]
    [InlineData("DependentElements", "let first = 4\nlet second = 7\nvar values: [2 of ref/i32] = [first@ref, second@ref]\nvar view = values.sliceUniq()\nview[0] = second@ref\nrequire view[0]@follow == 7 else => $abort(\"dependency\")")]
    [InlineData("NoInitViews", "unsafe\n    var values: [2 of bool] = noinit\n    var view = values.sliceUniq()\n    view[0] = true\n    view[1] = false\n    require view.slice()[0] and not view.slice()[1] else => $abort(\"noinit\")")]
    [InlineData("IntegerKeys", "var values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\nlet key: u128 = 1\nview[key] = 8\nlet range = (0..2).resolve(2)\nrequire view[range][1] == 8 else => $abort(\"keys\")")]
    [InlineData("GenericIndexable", "func first<S>(items: uniq/S) -> place uniq/S.Element during items\n    S is UniqIndexable<isize>\n    return items[0]\nvar values = [1, 2]\nvar view = values.sliceUniq()\nfirst(view@uniq) = 9\nrequire view[0] == 9 else => $abort(\"generic\")")]
    public void ViewsPreserveOrdinaryElementAndAcquisitionRules(string name, string source)
        => ScalarEmissionTest.EmitFixture("UniqSlice" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void ReplacementDestroysElementsButDroppingTheViewDoesNot()
        => ScalarEmissionTest.EmitFixture(
            "UniqSliceDrops", """
            struct Item
                let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
            var values: [2 of Item] = [Item.init("first"), Item.init("old")]
            do
                var view = values.sliceUniq()
                view[1] = Item.init("new")
            Console.writeLine("after view")
            """, "old\nafter view\nnew\nfirst\n");

    [Fact]
    public void AssignmentEvaluatesRightReceiverAndKeyExactlyOnce()
        => ScalarEmissionTest.EmitFixture(
            "UniqSliceOrder", """
            func receiver(values: uniq/[2 of i32]) -> UniqSlice<i32> during values
                Console.writeLine("receiver")
                return values.sliceUniq()
            func key() -> i32
                Console.writeLine("key")
                return 1
            func right() -> i32
                Console.writeLine("right")
                return 9
            var values: [2 of i32] = [1, 2]
            receiver(values@uniq)[key()] = right()
            require values[1] == 9 else => $abort("order")
            """, "right\nreceiver\nkey\n");

    [Theory]
    [InlineData("view[0] = 3", "var child = view.slice()", "require child[0] == 1 else => $abort(\"child\")")]
    [InlineData("view[0] = 3", "var child = view.sliceUniq(1..)", "require child[0] == 2 else => $abort(\"child\")")]
    [InlineData("values[0] = 3", "var child = view.slice()", "require child[0] == 1 else => $abort(\"child\")")]
    [InlineData("let read = values[0]", "", "require view.length == 2 else => $abort(\"length\")")]
    [InlineData("values.append(3)", "", "require view.length == 2 else => $abort(\"length\")")]
    [InlineData("let moved = values@move", "", "require view.length == 2 else => $abort(\"length\")")]
    public void LiveViewsKeepTheirParentAndBackingLoans(string conflict, string child, string use)
    {
        var c = MinimalEmissionTest.Analyze("var values = [1, 2]\nvar view = values.sliceUniq()\n" + child + "\n" + conflict + "\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let view = values.sliceUniq()\nview[0] = 3")]
    [InlineData("var view = values.sliceUniq()\nlet copy = view")]
    [InlineData("var view = values.sliceUniq()\nlet moved = view[0]@move")]
    [InlineData("var view = values.sliceUniq()\nlet shared = view@ref\nshared[0] = 3")]
    [InlineData("var view = values.sliceUniq()\nfor item in view => ()")]
    [InlineData("let view = values.slice()\nlet child = view.sliceUniq()")]
    [InlineData("let view = [1, 2].sliceUniq()\nlet count = view.length")]
    public void UnsupportedAcquisitionsDoNotGainAuthority(string use)
    {
        var c = MinimalEmissionTest.Analyze("var values = [1, 2]\n" + use);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void EmptyViewsStillKeepExclusiveAuthority()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = []\nvar view = values.sliceUniq()\nvalues.append(1)\nrequire view.isEmpty else => $abort(\"empty\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("Element", "let result = view[2]", "Hello.kimi:3:14")]
    [InlineData("Write", "view[^0] = 1", "Hello.kimi:3:1")]
    [InlineData("Wide", "let result = view[18446744073709551615@u64]", "Hello.kimi:3:14")]
    [InlineData("Range", "let result = view.sliceUniq(2..1)", "Hello.kimi:3:14")]
    [InlineData("SharedRange", "let result = view[0..3]", "Hello.kimi:3:14")]
    public void InvalidBoundsAbortAtTheCaller(string name, string operation, string location)
        => ScalarEmissionTest.EmitFixture("UniqSliceBounds" + name, "var values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\n" + operation, string.Empty, 1, location + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmViewPhasesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("var values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\nview[^1] = 9\nrequire view[1..][0] == 9 else => $abort(\"value\")");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                valid &= c.Bind().IsComplete;
                c.Binding.CheckStartup(OutputKind.Application);
                valid &= c.Ownership.Analyze().IsVerified && c.Emission.WriteIr(TextWriter.Null, out _);
            }, iterations: 8, warmupIterations: 8);
        Assert.Equal(0, bytes);
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void FixedArrayViewsReadAndWriteWithoutCopyingElements()
        => ScalarEmissionTest.EmitFixture(
            "UniqSliceFixed", """
            var values: [3 of i32] = [1, 2, 3]
            var view = values.sliceUniq()
            view[1] = 7
            require view.length == 3 and not view.isEmpty else => $abort("shape")
            require view[^2] == 7 else => $abort("write")
            let shared = view[1..]
            require shared.length == 2 and shared[0] == 7 else => $abort("shared")
            Console.writeLine("ok")
            """, "ok\n");

    [Fact]
    public void DynamicAndNestedViewsUseRelativeBounds()
        => ScalarEmissionTest.EmitFixture(
            "UniqSliceNested", """
            var values = [1, 2, 3, 4]
            var view = values.sliceUniq(1..)
            var child = view.sliceUniq(..2)
            child[^1] = 8
            require child.slice()[1] == 8 else => $abort("child")
            require values.slice()[2] == 8 else => $abort("source")
            Console.writeLine("ok")
            """, "ok\n");
}

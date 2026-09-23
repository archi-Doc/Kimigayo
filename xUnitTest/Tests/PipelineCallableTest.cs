// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PipelineCallableTest
{
    [Fact]
    public void LibraryBindsWithoutSource()
    {
        var c = Compilation.CreateForTest();
        c.Bind();
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => x.ToString())));
    }

    [Fact]
    public void SliceIteratorEmits()
        => ScalarEmissionTest.EmitFixture("SliceIterator", "struct Item\n    public let value: i32 = 42\nlet a: [1 of Item] = [Item.init()]\nvar cursor = a[..].iterate()\nmatch cursor@uniq.next()\n    .Some(let value) => require value.value == 42 else => $abort(\"value\")\n    .None => $abort(\"empty\")", string.Empty);

    private const string Source = "struct Item\n    public let value: i32 = 3\nfunc apply<T, F>(value: ref/T, visit: uniq/F) -> bool\n    F is Callable<uniq, (ref/T) -> bool>\n    return visit(value)\nlet item = Item.init()\nvar visit = func (value: ref/Item) => value.value == 3\nrequire apply(item@ref, visit@uniq) else => $abort(\"callback\")";

    [Fact]
    public void BindsPerCallBorrowedInput()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EmitsPerCallBorrowedInput()
        => ScalarEmissionTest.EmitFixture("PipelineCallableInput", Source, string.Empty);

    [Theory]
    [InlineData("dispatch")]
    [InlineData("success")]
    [InlineData("initializer")]
    public void RejectsCorruptMatchPlans(string mutation)
    {
        // BodyLowering validates every lowered body, including each monomorphized instance (SPEC 21.3.1);
        // the corrupt match plan is rejected on the ordinary body that owns it.
        var c = MinimalEmissionTest.Analyze("func present(items: Slice<i32>{source}) -> isize\n    var cursor = items.iterate()\n    var count: isize = 0\n    loop\n        match cursor@uniq.next()\n            .Some(let item) => count = count + 1\n            .None => exit\n    return count\nlet values: [1 of i32] = [4]\nrequire present(values[..]) == 1 else => $abort(\"match\")");
        Assert.True(c.Emission.Validate(out var failure), MinimalEmissionTest.Describe(c, failure));
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "present");
        var match = Assert.Single(body.Matches);
        if (mutation == "dispatch")
        {
            var dispatch = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.MatchDispatch);
            body.OperationSteps[dispatch] = -1;
        }
        else if (mutation == "success")
        {
            var test = body.MatchArms[match.ArmStart].Test;
            var edge = body.EdgeHeads[test];
            while (body.Edges[edge].Kind != OwnershipEdgeKind.True)
            {
                edge = body.Edges[edge].Next;
            }

            body.EdgeStorage[edge] = body.Edges[edge] with { To = body.MatchArms[match.ArmStart + 1].BodyEntry };
        }
        else
        {
            var init = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.InitializeSubject);
            body.OperationStorage[init] = body.OperationStorage[init] with { Kind = OwnershipOperationKind.Write };
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("(value: ref/Item)", "(value: uniq/Item)")]
    [InlineData("(value: ref/Item)", "(value: ref{static}/Item)")]
    public void RejectsStrongerInputContract(string original, string replacement)
        => Assert.False(MinimalEmissionTest.Analyze(Source.Replace(original, replacement, StringComparison.Ordinal)).Binding.Result.IsComplete);
}

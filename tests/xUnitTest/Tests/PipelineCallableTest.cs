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
        => ScalarEmissionTest.EmitFixture("SliceIterator", "struct Item\n    public let value: i32 = 42\nlet a: [1 of Item] = [Item.init()]\nvar cursor = a[..].iterate()\nmatch cursor@uniq.next()@move\n    .Some(let value) => require value.value == 42 else => $abort(\"value\")\n    .None => $abort(\"empty\")", string.Empty);

    [Theory]
    [InlineData("Direct", "0..1")]
    [InlineData("Resolved", "(0..1)")]
    public void BorrowingASliceValueUsesItsTemporaryInsteadOfAnElementSlot(string name, string key)
        => ScalarEmissionTest.EmitFixture("SliceBorrowTemporary" + name, "struct Item\n    public let value: i32 = 42\nlet a: [1 of Item] = [Item.init()]\nvar cursor = a[" + key + "].iterate()\nmatch cursor@uniq.next()@move\n    .Some(let value) => require value.value == 42 else => $abort(\"value\")\n    .None => $abort(\"empty\")", string.Empty);

    private const string Source = "struct Item\n    public let value: i32 = 3\nfunc apply<T, F>(value: ref/T, visit: uniq/F) -> bool\n    F is Callable<uniq, (ref/T) -> bool>\n    return visit(value)\nlet item = Item.init()\nvar visit = func (value: ref/Item) => value.value == 3\nrequire apply(item@ref, visit@uniq) else => $abort(\"callback\")";

    [Fact]
    public void BindsPerCallBorrowedInput()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void EmitsPerCallBorrowedInput()
        => ScalarEmissionTest.EmitFixture("PipelineCallableInput", Source, string.Empty);

    [Theory]
    [InlineData("(value: ref/Item)", "(value: uniq/Item)")]
    [InlineData("(value: ref/Item)", "(value: ref/Item during static)")]
    public void RejectsStrongerInputContract(string original, string replacement)
        => Assert.False(MinimalEmissionTest.Analyze(Source.Replace(original, replacement, StringComparison.Ordinal)).Binding.Result.IsComplete);
}

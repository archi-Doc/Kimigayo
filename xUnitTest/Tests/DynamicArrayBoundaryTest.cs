// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayBoundaryTest
{
    [Theory]
    [InlineData("let values: Array<Array<i32>> = [[1]]")]
    [InlineData("let values: Array<()> = [()]")]
    [InlineData("struct Empty\n    public init() => ()\nlet values: Array<Empty> = [Empty.init()]")]
    [InlineData("let values: Array<[2 of ()]> = [[(), ()]]")]
    [InlineData("let values: Array<((), ())> = [((), ())]")]
    [InlineData("func count<T>(values: Array<T>) -> isize => values.length\nlet n = count<()>([()])")]
    public void UnsupportedNeighboringShapesNeverPublishPartialIr(string source)
    {
        // These are valid specification forms, deliberately outside the verified generation boundary:
        // ownership analysis reports them as Unsupported before any generation is attempted.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("struct Pair\n    Self is Copy\n    public let id: i32\n    public let unit: ()\n    public init(id: i32)\n        self.id = id\n        self.unit = ()\nlet values: Array<Pair> = [Pair.init(7)]")]
    [InlineData("let values: Array<(i32, ())> = [(1, ())]")]
    [InlineData("let values: Array<i32> = [1, 2]\nfor value in values => ()")]
    [InlineData("let values: Array<string> = [\"value\"]\nfor value in values => ()")]
    [InlineData("let values: Array<string> = [\"a\", \"b\"]\nfor value in values[0..1] => ()")]
    [InlineData("struct Item\n    Self is Copy\n    public let id: i32\n    public init(id: i32) => self.id = id\nlet values: Array<Item> = [Item.init(42)]\nlet item = values[0]")]
    public void ElementsWithStorageRemainSupported(string source)
    {
        // A zero-sized component inside an element with bytes keeps an ordinary stride.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
    }
}

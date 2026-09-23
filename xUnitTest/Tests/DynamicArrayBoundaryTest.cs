// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DynamicArrayBoundaryTest
{
    [Theory]
    [InlineData("let values: Array<Array<i32>> = [[1]]")]
    [InlineData("let values: Array<()> = [()]")]
    [InlineData("let values: Array<string> = [\"value\"]\nfor value in values => ()")]
    [InlineData("struct Item\n    Self is Copy\n    public let id: i32\n    public init(id: i32) => self.id = id\nlet values: Array<Item> = [Item.init(42)]\nlet item = values[0]")]
    public void UnsupportedNeighboringShapesNeverPublishPartialIr(string source)
    {
        // These are valid specification forms, deliberately outside the verified generation boundary.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(writer.ToString());
    }
}

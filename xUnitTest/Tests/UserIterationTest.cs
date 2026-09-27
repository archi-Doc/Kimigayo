// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UserIterationTest
{
    private const string Drain =
        "struct Drain<T>\n    Self is Iterator\n    associate Iterator.Item is T\n    var value: Option<T>\n" +
        "    public init(value: T) => self.value = .Some(value@move)\n" +
        "    public func next(self: uniq/Self) -> Option<T> => Kimi.Intrinsics.exchange(self.value@uniq, with: .None)\n" +
        "struct Batch<T>\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Drain<T>\n    let value: T\n" +
        "    public init(value: T) => self.value = value@move\n    public func intoIterator(self: Self) -> Drain<T> => Drain<T>.init(self.value@move)\n";

    private const string Counter =
        "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var n: i32 = 0\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        require self.n < 3 else => return .None\n        self.n += 1\n        return .Some(self.n)\n";

    private const string Three =
        "struct Three\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Counter\n" +
        "    public func intoIterator(self: Self) -> Counter => Counter.init()\n";

    [Fact]
    public void OwnedEntryBindsItsDeclaredItem()
    {
        var c = MinimalEmissionTest.Analyze(Counter + Three + "for item in Three.init()\n    let value: i32 = item");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var loop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>());
        Assert.Equal(BoundType.I32, loop.Bindings[0].BoundType);
    }

    [Theory]
    [InlineData("i32", "7", "item", "let value: i32 = item")]
    [InlineData("(i32, bool)", "(7, true)", "(number, flag)", "let value: i32 = number\n    let yes: bool = flag")]
    public void GenericEntrySubstitutesTheCompleteItem(string type, string value, string binding, string body)
    {
        var c = MinimalEmissionTest.Analyze(Drain + "var n: i32 = 7\nfor " + binding + " in Batch<" + type + ">.init(" + value + ")\n    " + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void ItemKeepsItsExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Drain + "func inspect(n: ref/i32 during a)\n    for item in Batch<ref/i32 during a>.init(n)\n        let value: ref/i32 during a = item");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(Counter + "for item in Counter.init() => ()")]
    [InlineData(Counter + "struct Fake\n    public func intoIterator(self: Self) -> Counter => Counter.init()\nfor item in Fake.init() => ()")]
    [InlineData(Counter + Three + "let values = Three.init()\nfor item in values => ()")]
    [InlineData(Counter + Three + "var values = Three.init()\nfor item in values@uniq => ()")]
    [InlineData(Counter + Three + "for (one, two) in Three.init() => ()")]
    public void MissingEntriesAndWrongBindingShapesAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.NotEmpty(c.Binding.Issues);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class TypedBorrowConversionTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 0\n";

    [Theory]
    [InlineData("Shared", Counter + "func read(c: ref/Counter) -> i32 => c.value\nlet c = Counter.init()\nrequire read(c@ref/Counter) == 0 else => $abort(\"value\")")]
    [InlineData("Exclusive", Counter + "func write(c: uniq/Counter) => c.value = 42\nvar c = Counter.init()\nwrite(c@uniq/Counter)\nrequire c.value == 42 else => $abort(\"value\")")]
    [InlineData("Returned", Counter + "func view(c: ref/Counter) -> ref/Counter during c => c@ref/Counter\nlet c = Counter.init()\nrequire view(c).value == 0 else => $abort(\"value\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("TypedBorrow" + name, source, string.Empty);

    [Theory]
    [InlineData(Counter + "let c = Counter.init()\nlet r = c@ref/i32")]
    [InlineData(Counter + "let c = Counter.init()\nlet r = c@uniq/Counter")]
    [InlineData(Counter + "func invalid(c: ref/Counter) => c@uniq/Counter")]
    [InlineData(Counter + "func invalid() -> ref/Counter\n    let c = Counter.init()\n    return c@ref/Counter")]
    [InlineData(Counter + "var c = Counter.init()\nlet r = c@ref/Counter\nc.value = 42\nlet n = r.value")]
    public void RejectsInvalidBorrow(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void GenericFieldOriginComesFromReceiver()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func view(self: ref/Self) -> ref/T during self => self.value@ref/T");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}

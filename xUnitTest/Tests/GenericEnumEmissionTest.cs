// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericEnumEmissionTest
{
    [Theory]
    [InlineData("Some", "func wrap<T>(x?: T) -> Option<T> => .Some(x)\nlet value = wrap<i32>(7)\nmatch value\n    .Some(7) => ()\n    .Some(_) => $abort(\"value\")\n    .None => $abort(\"case\")")]
    [InlineData("None", "func empty<T>() -> Option<T> => .None\nlet value = empty<i32>()\nmatch value\n    .Some(_) => $abort(\"case\")\n    .None => ()")]
    [InlineData("Identity", "func identity<T>(x?: Option<T>) -> Option<T> => x\nlet value: Option<i32> = .Some(7)\nlet other = identity<i32>(value)\nmatch other\n    .Some(7) => ()\n    .Some(_) => $abort(\"value\")\n    .None => $abort(\"case\")")]
    [InlineData("Move", "func wrap<T>(x?: T) -> Option<T> => .Some(x)\nlet value = wrap<string>(\"owned\")")]
    [InlineData("Two", "enum E<T>\n    Found(isize, T)\n    Missing\nfunc wrap<T>(i?: isize, x?: T) -> E<T> => .Found(i, x)\nlet value = wrap<i32>(3, 7)\nmatch value\n    .Found(3, 7) => ()\n    .Found(_, _) => $abort(\"value\")\n    .Missing => $abort(\"case\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("GenericEnum" + name, source, string.Empty);

    [Fact]
    public void DestroysOnlyActiveOwnedPayload()
        => ScalarEmissionTest.EmitFixture("GenericEnumCleanup", "struct Token\n    deinit => Console.writeLine(\"payload\")\nfunc wrap<T>(x?: T) -> Option<T> => .Some(x)\nfunc empty<T>() -> Option<T> => .None\nlet a = wrap<Token>(Token.init())\nlet b = empty<Token>()\nConsole.writeLine(\"body\")", "body\npayload\n");

    [Theory]
    [InlineData("func wrap<T>(x?: T) -> Option<T>\n    let first: Option<T> = .Some(x)\n    return .Some(x)")]
    [InlineData("func wrap<T>(x?: T) -> Option<T> => .Some(1)")]
    public void RejectsInvalidSymbolicPayload(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("payload")]
    public void RejectsDamagedConstructionAndRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func wrap<T>(x?: T) -> Option<T> => .Some(x)\nlet result = wrap<i32>(7)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "wrap");
        body.ConstructionStorage[0] = defect == "case" ? body.Constructions[0] with { Case = null } : body.Constructions[0] with { PayloadCount = 0 };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericEnumEmissionTest
{
    [Theory]
    [InlineData("Some", "func wrap<T>(x: T) -> Option<T> => .Some(x@move)\nlet value = wrap<i32>(7)\nmatch value\n    .Some(7) => ()\n    .Some(_) => $abort(\"value\")\n    .None => $abort(\"case\")")]
    [InlineData("None", "func empty<T>() -> Option<T> => .None\nlet value = empty<i32>()\nmatch value\n    .Some(_) => $abort(\"case\")\n    .None => ()")]
    [InlineData("Identity", "func identity<T>(x: Option<T>) -> Option<T> => x@move\nlet value: Option<i32> = .Some(7)\nlet other = identity<i32>(value)\nmatch other\n    .Some(7) => ()\n    .Some(_) => $abort(\"value\")\n    .None => $abort(\"case\")")]
    [InlineData("Move", "func wrap<T>(x: T) -> Option<T> => .Some(x@move)\nlet value = wrap<string>(\"owned\")")]
    [InlineData("Two", "enum E<T>\n    Found(isize, T)\n    Missing\nfunc wrap<T>(i: isize, x: T) -> E<T> => .Found(i, x@move)\nlet value = wrap<i32>(3, 7)\nmatch value@move\n    .Found(3, 7) => ()\n    .Found(_, _) => $abort(\"value\")\n    .Missing => $abort(\"case\")")]
    [InlineData("ResultCopy", "let value = Result<i32, i32>.Err(7)\nlet first = value\nlet second = value\nmatch first\n    .Err(7) => ()\n    .Err(_) => $abort(\"first\")\n    .Ok(_) => $abort(\"case\")\nmatch value\n    .Err(7) => ()\n    .Err(_) => $abort(\"source\")\n    .Ok(_) => $abort(\"case\")\nmatch second\n    .Err(7) => ()\n    .Err(_) => $abort(\"second\")\n    .Ok(_) => $abort(\"case\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("GenericEnum" + name, source, string.Empty);

    [Fact]
    public void DestroysOnlyActiveOwnedPayload()
        => ScalarEmissionTest.EmitFixture("GenericEnumCleanup", "struct Token\n    drop => Console.writeLine(\"payload\")\nfunc wrap<T>(x: T) -> Option<T> => .Some(x@move)\nfunc empty<T>() -> Option<T> => .None\nlet a = wrap<Token>(Token.init())\nlet b = empty<Token>()\nConsole.writeLine(\"body\")", "body\npayload\n");

    [Theory]
    [InlineData("func wrap<T>(x: T) -> Option<T>\n    let first: Option<T> = .Some(x@move)\n    return .Some(x@move)")]
    [InlineData("func wrap<T>(x: T) -> Option<T> => .Some(x)")]
    [InlineData("func wrap<T>(x: T) -> Option<T> => .Some(1)")]
    public void RejectsInvalidSymbolicPayload(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }
}

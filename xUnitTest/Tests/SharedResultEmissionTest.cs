// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// G10b: shared scalar joins retain conditional evaluation and secured results.
public class SharedResultEmissionTest
{
    [Theory]
    [InlineData("ShortCircuit", "func check<T>(value?: ref/T, a?: bool, b?: bool) -> bool => a and b or not a\nlet v = 1\nrequire check(v, true, true) and not check(v, true, false) and check(v, false, false) else => $abort(\"result\")", "")]
    [InlineData("SkippedEffects", "func note() -> bool\n    Console.writeLine(\"called\")\n    return true\nfunc check<T>(value?: ref/T, flag?: bool) -> bool => flag and note()\nlet v = 1\nrequire not check(v, false) and check(v, true) else => $abort(\"result\")", "called\n")]
    [InlineData("SkippedOverflow", "func check<T>(value?: ref/T, flag?: bool, n?: i8) -> bool => flag or n + 1 > 0\nlet v = 1\nrequire check(v, true, 127) and check(v, false, 1) else => $abort(\"result\")", "")]
    [InlineData("Conditional", "func pick<T>(value?: ref/T, flag?: bool, n?: i16) -> i16 => if flag => n * 2 else => n / 2\nlet v = true\nrequire pick(v, true, 21) == 42 and pick(v, false, 8) == 4 else => $abort(\"result\")", "")]
    [InlineData("NestedLoop", "func count<T>(value?: ref/T, limit?: i32) -> i32\n    var n = 0\n    while n < limit and n < 4\n        n += if n == 0 => 2 else => 1\n    return n\nlet v = true\nrequire count(v, 3) == 3 and count(v, 8) == 4 else => $abort(\"result\")", "")]
    [InlineData("ThreeArrivals", "func pick<T>(value?: ref/T, n?: i32) -> i32 => if n == 0 => 10 else if n == 1 => 20 else => 30\nlet v = true\nrequire pick(v, 0) == 10 and pick(v, 1) == 20 and pick(v, 2) == 30 else => $abort(\"result\")", "")]
    [InlineData("SecuredBeforeCleanup", "func pick<T>(value?: T, flag?: bool) -> i32\n    return if flag\n        let moved = value\n        yield 42\n    else\n        yield 12\nrequire pick(\"first\", true) == 42 and pick(\"second\", false) == 12 else => $abort(\"result\")", "")]
    [InlineData("OneArrival", "func pick<T>(value?: ref/T, flag?: bool) -> i32\n    let n = if flag => 42 else => return 12\n    return n\nlet v = true\nrequire pick(v, true) == 42 and pick(v, false) == 12 else => $abort(\"result\")", "")]
    [InlineData("DeadJoin", "func pick<T>(value?: ref/T) -> i32\n    return 42\n    let n = if true => 1 else => 2\nlet v = true\nrequire pick(v) == 42 else => $abort(\"result\")", "")]
    [InlineData("DestructionOrder", "struct Token\n    let id: i32\n    public init(id?: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\nfunc pick<T>(value?: T, flag?: bool) -> i32\n    return if flag\n        let moved = value\n        yield 42\n    else\n        yield 12\nrequire pick(Token.init(1), true) == 42 else => $abort(\"result\")\nConsole.writeLine(\"returned\")\nrequire pick(Token.init(2), false) == 12 else => $abort(\"result\")\nConsole.writeLine(\"returned\")", "drop\nreturned\ndrop\nreturned\n")]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("SharedResult" + name, source, stdout);

    [Theory]
    [InlineData("duplicate")]
    [InlineData("edge")]
    [InlineData("value")]
    [InlineData("write")]
    public void RejectsCorruptArrivalsAndRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func pick<T>(value?: ref/T, flag?: bool, n?: i32) -> i32 => if flag => n + 1 else => n - 1\nlet v = true\nlet r = pick(v, true, 2)");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.TryPrepare(out _, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Values.Any(v => v.Kind == OwnershipValueKind.Phi));
        var phi = body.Values.First(x => x.Kind == OwnershipValueKind.Phi);
        var input = body.PhiInputs[phi.Start];
        body.PhiInputs[phi.Start] = defect switch
        {
            "duplicate" => body.PhiInputs[phi.Start + 1],
            "edge" => input with { Edge = 0 },
            "value" => input with { Value = body.PhiInputs[phi.Start + 1].Value },
            _ => input with { Write = -1 },
        };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Equal(string.Empty, output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }
}

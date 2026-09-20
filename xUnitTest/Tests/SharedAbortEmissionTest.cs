// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedAbortEmissionTest
{
    [Theory]
    [InlineData("RequirePass", "func check<T>(value?: ref/T, flag?: bool) -> i32\n    require flag else => $abort(\"failed\")\n    return 42\nlet v = true\nrequire check(v, true) == 42 else => $abort(\"wrong\")", "", 0, "", "failed=0;wrong=0")]
    [InlineData("RequireFail", "func check<T>(value?: ref/T, flag?: bool) -> i32\n    require flag else => $abort(\"failed\")\n    return 42\nlet v = true\nlet n = check(v, false)", "", 1, "Hello.kimi:2:26: abort KIMI_E_ABORT: failed\n", "failed=0")]
    [InlineData("OwnedMessage", "func fail<T>(value?: ref/T)\n    let message = \"stop\"\n    let held = \"held\"\n    $abort(message)\nlet v = true\nfail(v)", "", 1, "Hello.kimi:4:5: abort KIMI_E_ABORT: stop\n", "stop=0;held=0")]
    [InlineData("SkipCleanup", "func fail<T>(value?: ref/T)\n    defer => Console.writeLine(\"cleanup\")\n    $abort(\"stop\")\n    Console.writeLine(\"after\")\nlet v = true\nfail(v)", "", 1, "Hello.kimi:3:5: abort KIMI_E_ABORT: stop\n", "cleanup=0;stop=0;after=0")]
    [InlineData("Conditional", "func choose<T>(value?: ref/T, flag?: bool) -> i32 => if flag => 42 else => $abort(\"bad\")\nlet v = true\nrequire choose(v, true) == 42 else => $abort(\"wrong\")", "", 0, "", "bad=0;wrong=0")]
    [InlineData("MessageOnce", "func fail<T>(value?: ref/T)\n    $abort((message: do\n        Console.writeLine(\"once\")\n        exit to message: \"stop\"\n    ))\nlet v = true\nfail(v)", "once\n", 1, "Hello.kimi:2:5: abort KIMI_E_ABORT: stop\n", "once=1;stop=0")]
    public void Executes(string name, string source, string stdout, int exit, string stderr, string destructions)
    {
        name = "SharedAbort" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout, exit, stderr);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destructions, exit, stderr);
    }

    [Fact]
    public void RejectsInventedNormalContinuationAndRecovers()
    {
        var c = MinimalEmissionTest.Analyze("func fail<T>(value?: ref/T)\n    $abort(\"stop\")\nlet v = true\nfail(v)");
        Assert.True(c.Emission.TryPrepare(out _, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.GenericArguments.Count != 0);
        var edge = body.EdgeStorage.FindIndex(x => x.Kind == OwnershipEdgeKind.Abort);
        Assert.True(edge >= 0);
        body.EdgeStorage[edge] = body.Edges[edge] with { Kind = OwnershipEdgeKind.Normal };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Equal(string.Empty, output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void ChangedEntryCannotExecuteCheckingOnlyCode()
    {
        var c = MinimalEmissionTest.Analyze("func fail<T>(value?: ref/T)\n    $abort(\"stop\")\n    Console.writeLine(\"after\")\nlet v = true\nfail(v)");
        Assert.True(c.Emission.TryPrepare(out _, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.GenericArguments.Count != 0);
        var after = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Source is Kimi.Compiler.Parsing.StringLiteralKoto { Literal: "after" });
        Assert.True(after >= 0);
        Assert.False(body.IsReachable(after));
        var edge = body.EdgeHeads[0];
        body.EdgeStorage[edge] = body.Edges[edge] with { To = after };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Equal(string.Empty, output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }
}

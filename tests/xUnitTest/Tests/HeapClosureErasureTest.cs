// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class HeapClosureErasureTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("String", "let text = \"owned\"\nlet concrete = func [text@move] () => text == \"owned\"\nlet erased: () -> bool = concrete@move\nrequire erased() and erased() else => $abort(\"call\")", 1, 24, "")]
    [InlineData("Object", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet concrete = func [owner@move] () => owner.id\nlet erased: () -> i32 = concrete@move\nrequire erased() == 7 and erased() == 7 else => $abort(\"call\")", 2, 28, "drop\n")]
    [InlineData("LargeCopy", "let a: i64 = 1\nlet b: i64 = 2\nlet c: i64 = 3\nlet concrete = func [a, b, c] () => a + b + c\nlet erased: () -> i64 = concrete\nrequire erased() == 6 and concrete() == 6 else => $abort(\"call\")", 1, 24, "")]
    [InlineData("Return", "func make() -> () -> bool\n    let text = \"owned\"\n    let concrete = func [text@move] () => text == \"owned\"\n    return concrete@move\nlet erased = make()\nrequire erased() else => $abort(\"return\")", 1, 24, "")]
    public void OwnedEnvironmentsExecuteAndReleaseOnce(string name, string source, int count, int bytes, string stdout)
        => NativeAllocationAudit.WriteFixture("HeapClosure" + name, source, count, count, bytes, stdout);

    [Theory]
    [InlineData("let text = \"owned\"\nlet concrete = func [text@move] () => text == \"owned\"\nlet erased: () -> bool = concrete")]
    [InlineData("let n = 7\nlet concrete = func [n@ref] () => n + 1\nlet erased: () -> i32 = concrete")]
    [InlineData("let n = 7\nlet concrete = func [var n] () => ++n\nlet erased: () -> i32 = concrete")]
    public void ErasurePreservesAcquisitionOwnershipAndReceiverRules(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }
}

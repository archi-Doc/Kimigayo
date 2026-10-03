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
    [InlineData("SmallInline", "let n = 7\nlet concrete = func [n] () => n + 1\nlet erased: () -> i32 = concrete\nrequire erased() == 8 and concrete() == 8 else => $abort(\"inline\")", 0, 0, "")]
    public void OwnedEnvironmentsExecuteAndReleaseOnce(string name, string source, int count, int bytes, string stdout)
        => NativeAllocationAudit.WriteFixture("HeapClosure" + name, source, count, count, bytes, stdout);

    [Theory]
    [InlineData("let text = \"owned\"\nlet concrete = func [text@move] () => text == \"owned\"\nlet erased: () -> bool = concrete")]
    [InlineData("let n = 7\nlet concrete = func [n@ref] () => n + 1\nlet erased: () -> i32 = concrete")]
    [InlineData("let n = 7\nlet concrete = func [var n] () => ++n\nlet erased: () -> i32 = concrete")]
    [InlineData("let text = \"owned\"\nlet concrete = func [text@move] () => text@ref\nlet erased: () -> ref/string during static = concrete@move")]
    public void ErasurePreservesAcquisitionOwnershipAndReceiverRules(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReplacementReleasesThePreviousEnvironmentBeforeContinuing()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                drop => Console.writeLine("drop")
            func make() -> () -> i32
                let owner = Kimi.Intrinsics.makeObj(Item.init())
                let concrete = func [owner@move] () => owner.id
                return concrete@move
            var first = make()
            first = make()
            Console.writeLine("replaced")
            require first() == 7 else => $abort("replacement")
            """;
        NativeAllocationAudit.WriteFixture("HeapClosureReplacement", Source, 4, 4, 56, "drop\nreplaced\ndrop\n");
    }

    [Fact]
    public void ConvertedArgumentsAndStoredHandlesRetainTheirEnvironments()
    {
        const string Source = """
            func use(value: () -> bool) -> bool => value()
            func make() -> () -> bool
                let text = "owned"
                let concrete = func [text@move] () => text == "owned"
                return concrete@move
            let text = "argument"
            let concrete = func [text@move] () => text == "argument"
            require use(concrete@move) else => $abort("argument")
            let values = [make(), make()]
            require values[0]() and values[1]() else => $abort("array")
            let optional: Option<() -> bool> = .Some(make())
            match optional@move
                .Some(let value) => require value() else => $abort("enum")
                .None => $abort("empty")
            """;
        ScalarEmissionTest.EmitFixture("HeapClosurePlacements", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnAbandonedConvertedArgumentReleasesItsEnvironment()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                drop => Console.writeLine("drop")
            func use(value: () -> i32, later: i32) => ()
            func run()
                let owner = Kimi.Intrinsics.makeObj(Item.init())
                let concrete = func [owner@move] () => owner.id
                use(concrete@move, (return))
            run()
            """;
        NativeAllocationAudit.WriteFixture("HeapClosureAbandoned", Source, 2, 2, 28, "drop\n");
    }

    [Fact]
    public void AllocationFailureKeepsTheErasureLocation()
    {
        const string Source = "let text = \"owned\"\nlet concrete = func [text@move] () => text == \"owned\"\nlet erased: () -> bool = concrete@move";
        var c = MinimalEmissionTest.Analyze(Source);
        var ir = CompilationTestHelper.WriteIr(c).Replace("call ptr @HeapAlloc(", "call ptr @fail_closure_alloc(", StringComparison.Ordinal) +
            "\ndefine internal ptr @fail_closure_alloc(ptr %heap, i32 %flags, i64 %bytes) {\nentry:\n  ret ptr null\n}\n";
        ScalarEmissionTest.WriteFixture("HeapClosureAllocationFailure", ir, string.Empty, 1, "Hello.kimi:3:26: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmHeapErasureEmissionReusesItsPlans()
    {
        var c = MinimalEmissionTest.Analyze("let text = \"owned\"\nlet concrete = func [text@move] () => text == \"owned\"\nlet erased: () -> bool = concrete@move\nrequire erased() else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}

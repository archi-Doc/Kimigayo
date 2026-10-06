// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TupleCaptureTest
{
    [Theory]
    [InlineData("Copy", "let pair = (2, 3)\nlet f = func [pair] () => pair.0 + pair.1\nlet erased: () -> i32 = f\nrequire f() == 5 and erased() == 5 and pair.0 == 2 else => $abort(\"copy\")", "", SemanticsKind.Ref)]
    [InlineData("Mutation", "let pair = (2, 3)\nvar f = func [var pair] () -> i32\n    pair.0 += pair.1\n    return pair.0\nrequire f() == 5 and f() == 8 and pair.0 == 2 else => $abort(\"mutation\")", "", SemanticsKind.Uniq)]
    [InlineData("Nested", "let pair = ((2, 3), true)\nvar f = func [var pair] () -> i32\n    pair.0.0 += pair.0.1\n    return pair.0.0\nrequire f() == 5 and f() == 8 else => $abort(\"nested\")", "", SemanticsKind.Uniq)]
    [InlineData("Owned", "let pair = (\"owned\", 7)\nlet f = func [pair@move] () => pair.1\nlet erased: () -> i32 = f@move\nrequire erased() == 7 and erased() == 7 else => $abort(\"owned\")", "", SemanticsKind.Ref)]
    [InlineData("Consume", "let pair = (\"owned\", 7)\nlet f = func [pair@move] () => pair@move\nlet result = f@move()\nrequire result.0 == \"owned\" and result.1 == 7 else => $abort(\"consume\")", "", SemanticsKind.Owner)]
    [InlineData("Borrow", "let n = 7\nlet pair = (n@ref, 3)\nlet f = func [pair] () => pair.0 + pair.1\nrequire f() == 10 else => $abort(\"borrow\")", "", SemanticsKind.Ref)]
    [InlineData("Contextual", "let t = (1, 2)\nlet f: () -> i32 = func [t] () => t.0 + t.1\nrequire f() == 3 else => $abort(\"contextual\")", "", SemanticsKind.Ref)]
    [InlineData("BorrowWhole", "func read(pair: ref/(string, i32)) -> i32 => pair.1\nlet pair = (\"owned\", 7)\nlet f = func [pair@move] () => read(pair)\nrequire f() == 7 and f() == 7 else => $abort(\"read\")", "", SemanticsKind.Ref)]
    [InlineData("PartialMove", "let pair = (\"owned\", \"other\")\nlet f = func [pair@move] () => pair.0@move\nlet text = f@move()\nrequire text == \"owned\" else => $abort(\"part\")", "", SemanticsKind.Owner)]
    public void TupleEnvironmentsKeepTheirAcquisitionAndReceiver(string name, string source, string stdout, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(receiver, Assert.Single(c.Ownership.Bodies, x => x.Function.IsAnonymous).Function.BoundClosure!.Receiver);
        ScalarEmissionTest.WriteFixture("TupleCapture" + name, CompilationTestHelper.WriteIr(c), stdout);
    }

    [Theory]
    [InlineData("let pair = (2, 3)\nlet f = func [var pair] () -> i32\n    pair.0 += 1\n    return pair.0\nf()")]
    [InlineData("let pair = (2, 3)\nlet f: () -> i32 = func [var pair] () -> i32\n    pair.0 += 1\n    return pair.0")]
    [InlineData("let pair = (\"owned\", 7)\nlet f = func [pair] () => pair.1")]
    [InlineData("let pair = (\"owned\", 7)\nlet f = func [pair@move] () => pair@move\nf@move()\nf@move()")]
    [InlineData("let n = 7\nlet pair = (n@ref, 3)\nlet f: () -> i32 = func [pair] () => pair.0 + pair.1")]
    [InlineData("var n = 7\nlet pair = (n@ref, 3)\nlet f = func [pair] () => pair.0 + pair.1\nn = 9\nf()")]
    public void InvalidTupleAcquisitionAndErasureAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ErasedTupleDestroysItsElementsInReverseOrder()
    {
        const string Source = """
            struct Item
                public let id: i32
                public init(id: i32) => self.id = id
                drop
                    if self.id == 1 => Console.writeLine("first")
                    else => Console.writeLine("second")
            let first = Kimi.Intrinsics.makeObj(Item.init(1))
            let second = Kimi.Intrinsics.makeObj(Item.init(2))
            let pair = (first@move, second@move)
            let f: () -> i32 = func [pair@move] () => 7
            require f() == 7 and f() == 7 else => $abort("objects")
            """;
        NativeAllocationAudit.WriteFixture("TupleCaptureDestruction", Source, 3, 3, 56, "second\nfirst\n");
    }

    [Fact]
    public void TupleMutationUsesTheGenericCallableReceiver()
    {
        const string Source = """
            func advance<F>(action: uniq/F) -> i32
                F is Callable<uniq, () -> i32>
                return action()
            let pair = (2, 3)
            var next = func [var pair] () -> i32
                pair.0 = pair.0 + pair.1
                return pair.0
            require advance(next@uniq) == 5 and advance(next@uniq) == 8 else => $abort("generic")
            require pair.0 == 2 else => $abort("snapshot")
            """;
        ScalarEmissionTest.EmitFixture("TupleCaptureGeneric", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTuplePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let pair = (\"owned\", 7)\nlet f: () -> i32 = func [pair@move] () => pair.1\nrequire f() == 7 else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData("let value = pair.0.id")]
    [InlineData("let f = func [pair@move] () => pair.0.id\nf()")]
    public void StoredObjectPayloadViewsUseTheOrdinaryBorrowPlan(string use)
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet pair = (owner@move, 1)\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var issue), MinimalEmissionTest.Describe(c, issue));
    }
}

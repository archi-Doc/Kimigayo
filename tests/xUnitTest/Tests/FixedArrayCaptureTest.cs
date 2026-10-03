// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FixedArrayCaptureTest
{
    [Theory]
    [InlineData("Copy", "let values: [2 of i32] = [2, 3]\nlet f: () -> i32 = func [values] () => values[0] + values[1]\nrequire f() == 5 and values[0] == 2 else => $abort(\"copy\")", SemanticsKind.Ref)]
    [InlineData("Mutation", "let values: [2 of i32] = [2, 3]\nvar f = func [var values] () -> i32\n    values[0] += values[1]\n    return values[0]\nrequire f() == 5 and f() == 8 and values[0] == 2 else => $abort(\"mutation\")", SemanticsKind.Uniq)]
    [InlineData("Dynamic", "let values: [2 of i32] = [2, 3]\nvar f = func [var values] (i: isize) -> i32\n    values[i] += 1\n    return values[i]\nrequire f(0) == 3 and f(1) == 4 else => $abort(\"dynamic\")", SemanticsKind.Uniq)]
    [InlineData("Nested", "let values: [1 of (i32, i32)] = [(2, 3)]\nvar f = func [var values] () -> i32\n    values[0].0 += values[0].1\n    return values[0].0\nrequire f() == 5 and f() == 8 else => $abort(\"nested\")", SemanticsKind.Uniq)]
    [InlineData("Consume", "let values: [2 of string] = [\"first\", \"last\"]\nlet f = func [values@move] () => values@move\nlet result = f@move()\nrequire result[0] == \"first\" and result[1] == \"last\" else => $abort(\"consume\")", SemanticsKind.Owner)]
    [InlineData("PartialMove", "let values: [2 of string] = [\"first\", \"last\"]\nlet f = func [values@move] () => values[0]@move\nlet result = f@move()\nrequire result == \"first\" else => $abort(\"part\")", SemanticsKind.Owner)]
    [InlineData("Empty", "let values: [0 of string] = []\nlet f: () -> i32 = func [values@move] () => 7\nrequire f() == 7 else => $abort(\"empty\")", SemanticsKind.Ref)]
    [InlineData("Borrow", "let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0] + 0\nrequire f() == 7 else => $abort(\"borrow\")", SemanticsKind.Ref)]
    [InlineData("BorrowWhole", "func read(values: ref/[2 of string]) -> bool => values[0] == \"first\"\nlet values: [2 of string] = [\"first\", \"last\"]\nlet f = func [values@move] () => read(values)\nrequire f() and f() else => $abort(\"borrow\")", SemanticsKind.Ref)]
    public void FixedArrayEnvironmentsPreserveElementAuthority(string name, string source, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(receiver, Assert.Single(c.Ownership.Bodies, x => x.Function.IsAnonymous).Function.BoundClosure!.Receiver);
        var ir = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("FixedArrayCapture" + name, ir, string.Empty);
        if (name is "Consume" or "PartialMove")
        {
            // Each comparison literal is a separate temporary in addition to the captured string it checks.
            StringEmissionTest.WriteAuditedFixture("FixedArrayCapture" + name, source, ir, string.Empty, name == "Consume" ? "first=2;last=2" : "first=2;last=1");
        }
    }

    [Theory]
    [InlineData("let values: [2 of i32] = [2, 3]\nlet f = func [var values] () -> i32\n    values[0] += 1\n    return values[0]\nf()")]
    [InlineData("let values: [2 of i32] = [2, 3]\nlet f: () -> i32 = func [var values] () -> i32\n    values[0] += 1\n    return values[0]")]
    [InlineData("let values: [2 of string] = [\"first\", \"last\"]\nlet f = func [values] () => values[0] == \"first\"")]
    [InlineData("let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f: () -> i32 = func [values] () => values[0] + 0")]
    public void InvalidAcquisitionAndErasureRemainRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void OwnedArrayErasureUsesOneEnvironmentAllocation()
        => NativeAllocationAudit.WriteFixture("FixedArrayCaptureHeap", "let values: [2 of string] = [\"first\", \"last\"]\nlet f: () -> bool = func [values@move] () => values[0] == \"first\" and values[1] == \"last\"\nrequire f() and f() else => $abort(\"read\")", 1, 1, 48, string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void CapturedObjectArraysDestroyElementsInReverseOrder()
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
            let values: [2 of obj/Item] = [first@move, second@move]
            let f: () -> i32 = func [values@move] () => 7
            require f() == 7 and f() == 7 else => $abort("objects")
            """;
        NativeAllocationAudit.WriteFixture("FixedArrayCaptureDestruction", Source, 3, 3, 56, "second\nfirst\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmArrayCaptureEmissionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let values: [2 of string] = [\"first\", \"last\"]\nlet f: () -> bool = func [values@move] () => values[0] == \"first\"\nrequire f() else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void ReturningACapturedElementReferenceKeepsItsFixedOrigin()
    {
        ScalarEmissionTest.EmitFixture("FixedArrayCaptureReferenceResult", "let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\nlet result = f()\nrequire result == 7 else => $abort(\"result\")", string.Empty);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StructCaptureTest
{
    private const string Packet = """
        struct Packet
            public let text: string
            public var number: i32
            public computed twice: i32
                get(self: ref/Self) -> i32 => self.number * 2
                set(self: uniq/Self, value: i32) -> () => self.number = value / 2
            public init(text: string, number: i32)
                self.text = text@move
                self.number = number
            drop => Console.writeLine("drop")

        """;

    [Theory]
    [InlineData("Read", "let packet = Packet.init(\"owned\", 7)\nlet f = func [packet@move] () => packet.number\nrequire f() == 7 and f() == 7 else => $abort(\"read\")", SemanticsKind.Ref)]
    [InlineData("Inspect", "func inspect(packet: ref/Packet) -> i32 => packet.number\nlet packet = Packet.init(\"owned\", 7)\nlet f = func [packet@move] () => inspect(packet)\nrequire f() == 7 and f() == 7 else => $abort(\"inspect\")", SemanticsKind.Ref)]
    [InlineData("Getter", "let packet = Packet.init(\"owned\", 7)\nlet f = func [packet@move] () => packet.twice\nrequire f() == 14 and f() == 14 else => $abort(\"getter\")", SemanticsKind.Ref)]
    [InlineData("Consume", "let packet = Packet.init(\"owned\", 7)\nlet f = func [packet@move] () => packet@move\nlet result = f@move()\nrequire result.number == 7 else => $abort(\"consume\")", SemanticsKind.Owner)]
    [InlineData("Mutate", "let packet = Packet.init(\"owned\", 7)\nvar f = func [var packet@move] () -> i32\n    packet.number += 1\n    return packet.number\nrequire f() == 8 and f() == 9 else => $abort(\"mutate\")", SemanticsKind.Uniq)]
    [InlineData("Setter", "let packet = Packet.init(\"owned\", 7)\nvar f = func [var packet@move] () -> i32\n    packet.twice = 18\n    return packet.number\nrequire f() == 9 and f() == 9 else => $abort(\"setter\")", SemanticsKind.Uniq)]
    public void StoredFieldsKeepTheirCaptureAuthority(string name, string source, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(Packet + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(receiver, Assert.Single(c.Ownership.Bodies, x => x.Function.IsAnonymous).Function.BoundClosure!.Receiver);
        ScalarEmissionTest.WriteFixture("StructCapture" + name, CompilationTestHelper.WriteIr(c), "drop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Existing", "let concrete = func [packet@move] () => packet.number\nlet erased: () -> i32 = concrete@move")]
    [InlineData("Contextual", "let erased: () -> i32 = func [packet@move] () => packet.number")]
    public void OwnedStructErasureReleasesTheEnvironmentOnce(string name, string create)
        => NativeAllocationAudit.WriteFixture("StructCaptureErase" + name, Packet + "let packet = Packet.init(\"owned\", 7)\n" + create + "\nrequire erased() == 7 and erased() == 7 else => $abort(\"erase\")", 1, 1, 32, "drop\n");

    [Theory]
    [InlineData("let packet = Packet.init(\"owned\", 7)\nlet f = func [packet] () => packet.number")]
    [InlineData("let packet = Packet.init(\"owned\", 7)\nlet f = func [packet@move] () => packet.number\nlet bad = packet.number")]
    [InlineData("let packet = Packet.init(\"owned\", 7)\nlet f = func [var packet@move] () -> i32\n    packet.number += 1\n    return packet.number\nf()")]
    [InlineData("let packet = Packet.init(\"owned\", 7)\nlet f: () -> i32 = func [var packet@move] () -> i32\n    packet.number += 1\n    return packet.number")]
    public void InvalidAcquisitionAndReceiversRemainRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(Packet + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AbandonedStructEnvironmentIsDestroyedBeforeReturning()
    {
        var source = Packet + "func use(f: () -> i32, later: i32) => ()\nfunc run()\n    let packet = Packet.init(\"owned\", 7)\n    use(func [packet@move] () => packet.number, (return))\nrun()";
        NativeAllocationAudit.WriteFixture("StructCaptureAbandoned", source, 1, 1, 32, "drop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmStructCapturePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Packet + "let packet = Packet.init(\"owned\", 7)\nlet f: () -> i32 = func [packet@move] () => packet.twice\nrequire f() == 14 else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorrowedStructDependenciesCannotBeErased(bool erase)
    {
        var source = "struct View {source}\n    public let item: ref/i32 during source\n    public init(item: ref/i32 during source) => self.item = item\nlet n = 7\nlet view = View.init(n@ref)\nlet f" +
            (erase ? ": () -> i32" : string.Empty) + " = func [view@move] () => view.item + 1\nrequire f() == 8 else => $abort(\"view\")";
        var c = MinimalEmissionTest.Analyze(source);
        if (erase)
        {
            Assert.False(c.Binding.Result.IsComplete);
            c.Binding.ReportDiagnostics();
            var error = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
            Assert.Equal("UnsatisfiedConstraint_Kd", error.Code); // SPEC 15.2.3: an Owned failure is a Constraint failure.
            Assert.Contains("Owned environment", error.Note, StringComparison.Ordinal);
            Assert.False(c.Emission.Validate(out _));
        }
        else
        {
            ScalarEmissionTest.WriteFixture("StructCaptureBorrowed", CompilationTestHelper.WriteIr(c), string.Empty);
        }
    }
}

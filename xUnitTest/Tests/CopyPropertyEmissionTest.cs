// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CopyPropertyEmissionTest
{
    internal const string Meter = "struct Meter\n    public var raw: i32 = 2\n    public var level: i32\n        get() -> i32\n            Console.writeLine(\"get\")\n            return storage\n        set(value: i32) -> ()\n            Console.writeLine(\"set\")\n            storage = value\n    public computed doubled: i32\n        get() -> i32 => self.raw * 2\n        set(value: i32) -> () => self.raw = value / 2\n    public init() => self.level = 3\n";

    [Theory]
    [InlineData("Owned", "var s = S.init()\nrequire s.point.x == 9 else => $abort(\"result\")")]
    [InlineData("Borrowed", "func inspect(s: ref/S)\n    require s.point.x == 9 else => $abort(\"result\")\nlet s = S.init()\ninspect(s@ref)")]
    [InlineData("Saved", "let s = S.init()\nvar saved = s.point\nsaved.x = 7\nrequire saved.x == 7 else => $abort(\"result\")")]
    public void GetterAggregateProjectionUsesTheReturnedCopy(string name, string body)
        => ScalarEmissionTest.EmitFixture("CopyPropertyProjection" + name, "struct Point\n    Self is Copy\n    public var x: i32 = 1\nstruct S\n    public var point: Point = Point.init()\n        get() -> Point\n            Console.writeLine(\"get\")\n            var result = storage\n            result.x = 9\n            return result\n" + body, "get\n");

    [Fact]
    public void CompoundRightSideCanInspectReceiverAfterGetterReturns()
        => ScalarEmissionTest.EmitFixture("CopyPropertyCompoundInspect", Meter + "var m = Meter.init()\nm.level += m.level\nrequire m.level == 6 else => $abort(\"value\")", "get\nget\nset\nget\n");

    [Fact]
    public void WarmCopyAccessorPipelineAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Meter + "var m = Meter.init()\nm.level += 2\n_ = m.doubled");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.Validate(out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Accessor Binding failed.");
            }

            c.Binding.CheckStartup(OutputKind.Application);
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.Validate(out _))
            {
                throw new InvalidOperationException("Accessor generation failed.");
            }
        }));
    }

    [Fact]
    public void CallsCopyAccessorsAfterDirectConstruction()
        => ScalarEmissionTest.EmitFixture("CopyPropertyBasic", Meter + "var m = Meter.init()\nm.level = 8\nrequire m.level == 8 else => $abort(\"level\")\nm.doubled = 12\nrequire m.doubled == 12 and m.raw == 6 else => $abort(\"computed\")", "set\nget\n");

    [Fact]
    public void SimpleAssignmentSecuresInputBeforeReceiver()
        => ScalarEmissionTest.EmitFixture("CopyPropertyOrder", Meter + "func receiver(m: uniq/Meter during source) -> uniq/Meter during source\n    Console.writeLine(\"receiver\")\n    return m\nfunc input() -> i32\n    Console.writeLine(\"input\")\n    return 8\nvar m = Meter.init()\nreceiver(m@uniq).level = input()\nrequire m.level == 8 else => $abort(\"level\")", "input\nreceiver\nset\nget\n");

    [Theory]
    [InlineData("Both", "get() -> i32\n            Console.writeLine(\"get\")\n            return storage\n        set(value: i32) -> ()\n            Console.writeLine(\"set\")\n            storage = value", "get\ninput\nset\nget\n")]
    [InlineData("Get", "get() -> i32\n            Console.writeLine(\"get\")\n            return storage", "get\ninput\nget\n")]
    [InlineData("Set", "set(value: i32) -> ()\n            Console.writeLine(\"set\")\n            storage = value", "input\nset\n")]
    public void CompoundUpdateLocatesReceiverOnce(string name, string accessors, string output)
    {
        var source = "struct S\n    public var item: i32 = 3\n        " + accessors + "\nfunc receiver(s: uniq/S during source) -> uniq/S during source\n    Console.writeLine(\"receiver\")\n    return s\nfunc input() -> i32\n    Console.writeLine(\"input\")\n    return 4\nvar s = S.init()\nreceiver(s@uniq).item += input()\nrequire s.item == 7 else => $abort(\"result\")";
        ScalarEmissionTest.EmitFixture("CopyPropertyCompound" + name, source, "receiver\n" + output);
    }

    [Fact]
    public void ComputedSetterHasItsOwnInputType()
        => ScalarEmissionTest.EmitFixture("CopyPropertySetterType", "struct S\n    var raw: i32 = 0\n    public computed item: i32\n        get() -> i32 => self.raw\n        set(value: bool) -> ()\n            if value => self.raw = 9\nvar s = S.init()\ns.item = true\nrequire s.item == 9 else => $abort(\"input\")", string.Empty);

    [Fact]
    public void ComputedExclusiveGetterBorrowsWritableReceiver()
        => ScalarEmissionTest.EmitFixture("CopyPropertyExclusiveGet", "struct S\n    var hits: i32 = 0\n    public computed next: i32\n        get(self: uniq/Self) -> i32\n            self.hits += 1\n            return self.hits\nvar s = S.init()\nrequire s.next == 1 and s.next == 2 else => $abort(\"hits\")", string.Empty);

    [Fact]
    public void SharedBorrowMaterializesGetterResultForTheCall()
        => ScalarEmissionTest.EmitFixture("CopyPropertyTemporaryBorrow", Meter + "func inspect(value: ref/i32)\n    require value == 3 else => $abort(\"value\")\nlet m = Meter.init()\ninspect(m.level@ref)", "get\n");

    [Theory]
    [InlineData("++m.level", 4, 4)]
    [InlineData("m.level++", 3, 4)]
    [InlineData("--m.level", 2, 2)]
    [InlineData("m.level--", 3, 2)]
    public void IncrementCallsIndependentOperations(string expression, int value, int final)
        => ScalarEmissionTest.EmitFixture("CopyPropertyIncrement" + value + final, Meter + $"var m = Meter.init()\nlet result = {expression}\nrequire result == {value} and m.level == {final} else => $abort(\"increment\")", "get\nset\nget\n");

    [Theory]
    [InlineData("let m = Meter.init()\nm.level = 2")]
    [InlineData("var m = Meter.init()\nlet edit = m.level@uniq")]
    [InlineData("let m = Meter.init()\nlet view = m.level@ref\n_ = view + 1")]
    [InlineData("var m = Meter.init()\nlet view = m@ref\nm.level = 1\n_ = view.raw")]
    public void RejectsInvalidReceiverAndResultLifetimes(string source)
    {
        var c = MinimalEmissionTest.Analyze(Meter + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("s.point.x = 9")]
    [InlineData("s.point.x += 9")]
    [InlineData("++s.point.x")]
    [InlineData("let edit = s.point.x@uniq")]
    [InlineData("s.point.update()")]
    public void RejectsWritesIntoGetterOwnedDescendants(string operation)
    {
        var c = MinimalEmissionTest.Analyze("struct Point\n    Self is Copy\n    public var x: i32 = 1\n    public func update(self: uniq/Self) => self.x = 9\nstruct S\n    public var point: Point = Point.init()\n        get() -> Point => storage\nvar s = S.init()\n" + operation);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("self.level = 3\n        self.level = 4")]
    [InlineData("if flag => self.level = 3\n        self.level = 4")]
    [InlineData("self.level = 3\n        _ = self.level")]
    [InlineData("self.level = 3\n        _ = self.doubled")]
    public void ConstructionNeverCallsCustomAccessors(string body)
    {
        var source = Meter.Replace("public init() => self.level = 3", "public init(flag: bool)\n        " + body, StringComparison.Ordinal) + "let m = Meter.init(true)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}

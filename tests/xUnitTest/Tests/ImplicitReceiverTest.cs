// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.3, 15.1.5: a Receiver Expression is acquired implicitly; other positions keep the spelling; one receiver shape per group.</summary>
public class ImplicitReceiverTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 0\n    public func bump(self: uniq/Self) => self.value += 1\n    public func read(self) -> i32 => self.value\n";

    [Fact]
    public void AnExclusiveReceiverOnAVarLocalIsAcquiredImplicitly()
    {
        const string body = "var c = Counter.init()\nc.bump()\nc.bump()\nrequire c.read() == 2 else => $abort(\"bump\")";
        var c = MinimalEmissionTest.Analyze(Counter + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("ImplicitReceiverBump", Counter + body, string.Empty);
    }

    [Theory]
    [InlineData("let c = Counter.init()\nc.bump()")]
    [InlineData("func plot(c: Counter) => c.bump()")]
    [InlineData("func plotRef(c: ref/Counter) => c.bump()")]
    public void AnUnwritableLendingPointCannotSupplyAnExclusiveReceiver(string body)
    {
        var c = MinimalEmissionTest.Analyze(Counter + body);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("var c = Counter.init()\nc@uniq.bump()")] // A redundant explicit spelling keeps its meaning.
    [InlineData("Counter.init().bump()")] // An owned temporary.
    [InlineData("func touch(c: uniq/Counter) => c.bump()")] // A borrow value is reborrowed.
    [InlineData("struct Game\n    public var items: Array<i32> = []\n    public func play(self: uniq/Self) => self.items.append(1)")] // Through an exclusive reference.
    public void ReceiverExpressionsAreAcquiredWhateverTheirValueKind(string body)
    {
        var c = MinimalEmissionTest.Analyze(Counter + body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Theory]
    [InlineData("struct Game\n    public var a: Counter = Counter.init()\n    public func play(self: uniq/Self) => Kimi.Intrinsics.replace(self.a, with: Counter.init())", false)]
    [InlineData("struct Game\n    public var a: Counter = Counter.init()\n    public func play(self: uniq/Self) => Kimi.Intrinsics.replace(self.a@uniq, with: Counter.init())", true)]
    [InlineData("var c = Counter.init()\nCounter.bump(c)", false)] // Unbound call: self is an ordinary argument.
    [InlineData("var c = Counter.init()\nCounter.bump(c@uniq)", true)]
    public void OtherPositionsNeedTheSpellingWhateverTheAccessPath(string body, bool expected)
    {
        var c = MinimalEmissionTest.Analyze(Counter + body);
        Assert.True(expected == c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        if (!expected)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ExclusiveBorrowRequired_Kd);
        }
    }

    [Fact]
    public void AnExclusiveClosureCallAcquiresTheOwnedClosureImplicitly()
    {
        const string body = "let n: i32 = 0\nvar next = func [var n] () -> i32\n    n += 1\n    return n\nlet a = next()\nlet b = next()\nrequire a == 1 and b == 2 else => $abort(\"closure\")";
        var c = MinimalEmissionTest.Analyze(body);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("ImplicitReceiverClosure", body, string.Empty);
    }

    [Fact]
    public void ALetBoundClosureIsNotCopiedForAnExclusiveCall()
    {
        var c = MinimalEmissionTest.Analyze("let n: i32 = 0\nlet tick = func [var n] () -> i32\n    n += 1\n    return n\nlet a = tick()");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("struct S\n    public func f(self) -> i32 => 1\n    public func f(self: uniq/Self, x: i32) -> i32 => x")]
    [InlineData("contract C\n    func f(self) -> i32\n    func f(self: uniq/Self, x: i32) -> i32")]
    [InlineData("contract A\n    func f(self) -> i32\ncontract B: A\n    func f(self: uniq/Self, x: i32) -> i32")]
    public void ReceiverShapesMustAgreeWithinAGroup(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ReceiverShapeMismatch_Kd);
    }

    [Theory]
    [InlineData("struct S\n    public func f(self: uniq/Self, x: i32) -> i32 => x\n    public func f(self: uniq/Self, x: string) -> i32 => 1")]
    [InlineData("struct S\n    public func f(self) -> i32 => 1\n    public func f(x: i32) -> i32 => x")] // A Type function is not counted.
    public void SameShapesAndReceiverFreeFunctionsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void ConstraintGatheredGroupsWithDifferentShapesAreRejectedAtTheUse()
    {
        var c = MinimalEmissionTest.Analyze("contract Reader\n    func read(self) -> i32\ncontract Consumer\n    func read(self: uniq/Self) -> i32\nfunc use<T>(value: uniq/T) -> i32\n    T is Reader\n    T is Consumer\n    return value.read()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ReceiverShapeMismatch_Kd);
    }

    [Fact]
    public void OverlappingImplicitAcquisitionsAreRejected()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = [1]\nvalues.append(values.remove(0))");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void ASharedReadIsPermittedDuringTheImplicitReservation()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<isize> = [1]\nvalues.insert(^0, values.length)\nrequire values.length == 2 else => $abort(\"reservation\")");
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
    }
}

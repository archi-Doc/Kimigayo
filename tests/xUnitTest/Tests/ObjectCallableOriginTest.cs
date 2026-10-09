// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectCallableOriginTest
{
    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void BorrowSignaturesSharePerCallOriginInstantiation(string mode)
    {
        var source = Program(mode);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ObjectCallableOrigin" + mode, source, string.Empty);
    }

    // SPEC 7.3, 7.6.3, 13.5.5.1: a direct call through an object handle or view, or of its written payload follow, acquires the
    // complete payload through the same checked borrow as h@follow@ref (Shared) or h@follow@uniq (Exclusive).
    [Theory]
    [InlineData("RcItem", "func seven() -> i32 => 7\nlet h = Kimi.Intrinsics.makeRc(seven)\nrequire h() == 7 else => $abort(\"item\")")]
    [InlineData("RcClosure", "let h = Kimi.Intrinsics.makeRc(func [] () -> i32 => 7)\nrequire h() == 7 and (h)() == 7 else => $abort(\"closure\")")]
    [InlineData("RcCapture", "let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 1)\nlet other = Kimi.Intrinsics.clone(h@ref)\nrequire h() == 5 and other() == 5 else => $abort(\"capture\")")]
    [InlineData("ArcCommon", "let n: i32 = 4\nlet f: () -> i32 = func [n] () -> i32 => n + 5\nlet h = Kimi.Intrinsics.makeArc(f@move)\nrequire h() == 9 else => $abort(\"common\")")]
    [InlineData("ObjrefParameter", "func call(v: objref/(() -> i32)) -> i32 => v()\nlet f: () -> i32 = func [] () -> i32 => 9\nlet h = Kimi.Intrinsics.makeRc(f@move)\nlet g: () -> i32 = func [] () -> i32 => 9\nlet a = Kimi.Intrinsics.makeArc(g@move)\nrequire call(h@objref) == 9 and call(a@objref) == 9 else => $abort(\"objref\")")]
    [InlineData("ObjrefLocal", "let n: i32 = 7\nlet h = Kimi.Intrinsics.makeArc(func [n] () -> i32 => n)\nlet v = h@objref\nrequire v() == 7 else => $abort(\"view\")")]
    [InlineData("ObjExclusive", "let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar h = Kimi.Intrinsics.makeObj(next@move)\nlet a = h()\nrequire a == 1 and h() == 2 else => $abort(\"exclusive\")")]
    [InlineData("ObjuniqExclusive", "let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar h = Kimi.Intrinsics.makeObj(next@move)\nlet u = h@objuniq\nlet a = u()\nrequire a == 1 and u() == 2 else => $abort(\"objuniq\")")]
    [InlineData("ObjuniqView", "let count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar h = Kimi.Intrinsics.makeObj(next@move)\nlet a = (h@objuniq)()\nrequire a == 1 and (h@objuniq)() == 2 else => $abort(\"objuniq view\")")]
    [InlineData("ObjrefView", "let n: i32 = 7\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n)\nrequire (h@objref)() == 7 else => $abort(\"objref view\")")]
    [InlineData("Field", "struct Holder\n    public var callback: rc/(() -> i32)\n    public init(callback: rc/(() -> i32)) => self.callback = callback@move\nlet n: i32 = 4\nlet f: () -> i32 = func [n] () -> i32 => n + 3\nlet holder = Holder.init(Kimi.Intrinsics.makeRc(f@move))\nrequire holder.callback() == 7 else => $abort(\"field\")")]
    [InlineData("WrittenFollow", "let n: i32 = 4\nlet f: () -> i32 = func [n] () -> i32 => n + 5\nlet g = Kimi.Intrinsics.makeArc(f@move)\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 1)\nlet count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar o = Kimi.Intrinsics.makeObj(next@move)\nlet first = o@follow()\nrequire g@follow() == 9 and h@follow() == 5 and first == 1 and o@follow() == 2 else => $abort(\"follow\")")]
    [InlineData("GenericSealed", "func call<F>(v: objref/F) -> i32\n    F is Callable<() -> i32> and ObjectPayload and Sealed\n    return v()\nlet n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 3)\nlet f: () -> i32 = func [n] () -> i32 => n + 5\nlet g = Kimi.Intrinsics.makeArc(f@move)\nrequire call(h@objref) == 7 and call(g@objref) == 9 else => $abort(\"generic\")")]
    [InlineData("UnsealedShared", "func call<F>(v: objref/F) -> i32\n    F is Callable<() -> i32> and ObjectPayload\n    return v()\nlet n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 3)\nrequire call(h@objref) == 7 else => $abort(\"unsealed\")")]
    [InlineData("GenericUniqObjuniq", "func call<F>(v: objuniq/F) -> i32\n    F is Callable<uniq, () -> i32> and ObjectPayload and Sealed\n    return v()\nlet count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nvar h = Kimi.Intrinsics.makeObj(next@move)\nrequire call(h@objuniq) == 1 and call(h@objuniq) == 2 else => $abort(\"generic uniq\")")]
    [InlineData("CapturedHandle", "let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 3)\nlet outer = func [h@move] () -> i32 => h() + 1\nrequire outer() == 8 and outer() == 8 else => $abort(\"captured\")")]
    [InlineData("EnvResult", "let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h()\nrequire r@follow == 4 else => $abort(\"result\")")]
    [InlineData("EnvResultFollow", "let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () => n@ref)\nlet r = h@follow()\nrequire r@follow == 4 else => $abort(\"result\")")]
    [InlineData("TemporaryCallees", "let n: i32 = 4\nfunc seven() -> i32 => 7\nlet count = 0\nvar next = func [var count] () -> i32\n    count += 1\n    return count\nrequire Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 3)() == 7 and Kimi.Intrinsics.makeRc(seven)() == 7 and (Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 1)@follow@ref)() == 5 and Kimi.Intrinsics.makeObj(next@move)@follow() == 1 else => $abort(\"callee\")")]
    public void CallablePayloadsCallThroughObjectCallees(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ObjectCallablePayload" + name, source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPayloadCallPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let n: i32 = 4\nlet h = Kimi.Intrinsics.makeRc(func [n] () -> i32 => n + 1)\nlet v = h@objref\nrequire h() == 5 and v() == 5 and h@follow() == 5 else => $abort(\"call\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        bool verified = true, written = true, bound = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => verified &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => written &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => bound &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.Equal((true, true, true), (verified, written, bound));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void WarmContractAndCallChecksReuseStorage(string mode)
    {
        var c = MinimalEmissionTest.Analyze(Program(mode));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }

    private static string Program(string mode)
        => "func identity(value: " + mode + "/i32) -> " + mode + "/i32 => value\nfunc apply<F>(f: ref/F, value: " + mode + "/i32) -> " + mode + "/i32\n    F is Callable<(" + mode + "/i32) -> " + mode + "/i32>\n    return f(value)\nvar n = 3" + (mode.StartsWith("obj", StringComparison.Ordinal) ? "@obj" : string.Empty) + "\nlet direct = apply(identity, n@" + mode + ")\nrequire direct@follow == 3 else => $abort(\"item\")\nlet erased: (" + mode + "/i32) -> " + mode + "/i32 = identity\nlet indirect = erased(n@" + mode + ")\nrequire indirect@follow == 3 else => $abort(\"erased\")";
}

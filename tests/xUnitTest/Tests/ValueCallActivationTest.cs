// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.6.4, 15.6.7: a value call activates its reserved exclusive arguments after the receiver's preparation, as an ordinary
// call does, and its receiver, with the Loans its environment holds, stays protected through the call.
public class ValueCallActivationTest
{
    private const string Generic = "func viaUniq<F>(value: uniq/i32, f: uniq/F) -> ()\n    F is Callable<uniq, (uniq/i32) -> ()>\n    f(value)\nfunc viaOwner<F>(value: uniq/i32, f: F) -> ()\n    F is Callable<owner, (uniq/i32) -> ()>\n    f@move(value)\n";

    private const string Bump = "func bump(v: uniq/i32) -> () => v@follow += 1\n";

    [Theory]
    [InlineData("RefParameter", "func local(value: uniq/i32, f: ref/((uniq/i32) -> ())) -> ()\n    f(value)\n    f(value)\n" + Bump + "var n: i32 = 1\nlet g: (uniq/i32) -> () = bump\nlocal(n@uniq, g@ref)\nConsole.writeLine(\"\\(n)\")", "3\n")]
    [InlineData("RefParameterResult", "func same(v: uniq/i32) -> uniq/i32 => v\nfunc local(value: uniq/i32, f: ref/((uniq/i32) -> uniq/i32)) -> ()\n    let r = f(value)\n    r@follow += 1\nvar n: i32 = 1\nlet g: (uniq/i32) -> uniq/i32 = same\nlocal(n@uniq, g@ref)\nConsole.writeLine(\"\\(n)\")", "2\n")]
    [InlineData("LocalReference", Bump + "var n: i32 = 1\nlet g: (uniq/i32) -> () = bump\nlet r = g@ref\nr(n@uniq)\nConsole.writeLine(\"\\(n)\")", "2\n")]
    [InlineData("CallableRef", "func viaRef<F>(value: uniq/i32, f: ref/F) -> ()\n    F is Callable<(uniq/i32) -> ()>\n    f(value)\n" + Bump + "var n: i32 = 1\nviaRef(n@uniq, bump)\nConsole.writeLine(\"\\(n)\")", "2\n")]
    [InlineData("CallableUniqAndOwner", Generic + Bump + "var n: i32 = 1\nvar c = func (v: uniq/i32) => v@follow += 10\nviaUniq(n@uniq, c@uniq)\nviaOwner(n@uniq, bump)\nConsole.writeLine(\"\\(n)\")", "12\n")]
    [InlineData("OwnerLocal", Bump + "func viaOwner<F>(f: F) -> i32\n    F is Callable<owner, (uniq/i32) -> ()>\n    var n: i32 = 1\n    f@move(n@uniq)\n    return n\nConsole.writeLine(\"\\(viaOwner(bump))\")", "2\n")]
    [InlineData("Consuming", "var n: i32 = 1\nlet text = \"t\"\nlet consume = func [text@move] (v: uniq/i32) -> string\n    v@follow += 100\n    return text@move\nConsole.writeLine(consume@move(n@uniq))\nConsole.writeLine(\"\\(n)\")", "t\n101\n")]
    [InlineData("SharedCapture", "var n: i32 = 1\nvar m: i32 = 5\nlet f = func [n@ref] (v: uniq/i32) => v@follow += n\nf(m@uniq)\nConsole.writeLine(\"\\(m)\")", "6\n")]
    public void ReservedArgumentsActivateAfterTheReceiver(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("ValueCallActivation" + name, source, stdout);
    }

    // The argument's reservation cannot activate while the receiver holds its Loan: the MIR's record (H5, DECISIONS R5-M185) relates the
    // receiver's Borrow (a capture's is the closure), the call and the reservation.
    [Theory]
    [InlineData("var n: i32 = 1\nlet f = func [n@ref] (v: uniq/i32) => v@follow += n\nf(n@uniq)", "f(n@uniq)", "borrow 2:9 func [n@ref] (v: uniq/i32) => v@follow += n | loan 3:1 f(n@uniq) | reservation 3:3 n@uniq")]
    [InlineData("var n: i32 = 1\nlet view = n@ref\nlet f = func [view] (v: uniq/i32) => v@follow += 1\nf(n@uniq)", "f(n@uniq)", "borrow 2:12 n@ref | loan 4:1 f(n@uniq) | reservation 4:3 n@uniq")]
    [InlineData("var n: i32 = 1\nlet text = \"t\"\nlet f = func [n@ref, text@move] (v: uniq/i32) -> string\n    v@follow += n\n    return text@move\n_ = f@move(n@uniq)", "f@move(n@uniq)", "borrow 3:9 func [n@ref, text@move] (v: uniq/i32) -> string\n    v@follow += n\n    return text@move | loan 6:5 f@move(n@uniq) | reservation 6:12 n@uniq")]
    [InlineData("func inc(value: i32) -> i32 => value + 1\nfunc pass(f: uniq/((i32) -> i32)) -> i32 => 1\nvar g: (i32) -> i32 = inc\nlet r = g@ref\n_ = r(pass(g@uniq))", "pass(g@uniq)", "borrow 4:9 g@ref | loan 5:5 r(pass(g@uniq)) | reservation 5:12 g@uniq")]
    public void AnArgumentCannotTakeWhatTheReceiverHolds(string source, string call, string related)
        => Assert.False(CallReservationTest.AssertMirRecord(source, DiagnosticCode.CallActivationConflict_Kd, call, related).Generate(TextWriter.Null));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValueCallActivationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func local(value: uniq/i32, f: ref/((uniq/i32) -> ())) -> ()\n    f(value)\n" + Bump +
            "var n: i32 = 1\nlet g: (uniq/i32) -> () = bump\nlocal(n@uniq, g@ref)\nlet r = g@ref\nr(n@uniq)\nrequire n == 3 else => $abort(\"n\")");
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}

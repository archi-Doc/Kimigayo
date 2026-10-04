// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
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
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var failure), failure);
        ScalarEmissionTest.EmitFixture("ValueCallActivation" + name, source, stdout);
    }

    [Theory]
    [InlineData("var n: i32 = 1\nlet f = func [n@ref] (v: uniq/i32) => v@follow += n\nf(n@uniq)")]
    [InlineData("var n: i32 = 1\nlet view = n@ref\nlet f = func [view] (v: uniq/i32) => v@follow += 1\nf(n@uniq)")]
    [InlineData("var n: i32 = 1\nlet text = \"t\"\nlet f = func [n@ref, text@move] (v: uniq/i32) -> string\n    v@follow += n\n    return text@move\n_ = f@move(n@uniq)")]
    [InlineData("func inc(value: i32) -> i32 => value + 1\nfunc pass(f: uniq/((i32) -> i32)) -> i32 => 1\nvar g: (i32) -> i32 = inc\nlet r = g@ref\n_ = r(pass(g@uniq))")]
    public void AnArgumentCannotTakeWhatTheReceiverHolds(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.True(issue.Failure == OwnershipFailure.ComparisonLoanConflict && issue.Activation, issue.ToString());
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValueCallActivationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func local(value: uniq/i32, f: ref/((uniq/i32) -> ())) -> ()\n    f(value)\n" + Bump +
            "var n: i32 = 1\nlet g: (uniq/i32) -> () = bump\nlocal(n@uniq, g@ref)\nlet r = g@ref\nr(n@uniq)\nrequire n == 3 else => $abort(\"n\")");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}

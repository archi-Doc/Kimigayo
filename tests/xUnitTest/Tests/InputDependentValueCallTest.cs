// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 10.7, 15.6.4: a value call whose result depends on its per-call inputs takes the arguments' Origins into that result,
// as an ordinary call does; the result retains the argument Loans and an exclusive result reborrows its argument.
public class InputDependentValueCallTest
{
    private const string Box = "struct Box<T>\n    var item: T\n\n    public init(item: T)\n        self.item = item@move\n\n" +
        "    public func get(self) -> ref/T during self => self.item@ref\n";

    private const string Pick = "func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\n";

    private const string Bump = "func bump(value: uniq/i32) -> uniq/i32 => value\n";

    [Theory]
    [InlineData("Item", Box + "let box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nrequire r == 5 else => $abort(\"item\")")]
    [InlineData("Closure", "let f = func (n: ref/i32) => n\nlet n = 7\nlet r = f(n@ref)\nrequire r == 7 else => $abort(\"closure\")")]
    [InlineData("TwoInputs", Pick + "let p = pick\nlet x = 1\nlet y = 2\nlet r = p(x@ref, y@ref)\nrequire r == 1 else => $abort(\"pick\")")]
    [InlineData("ErasedShared", Box + "let box = Box<i32>.init(item: 8)\nlet g: (ref/Box<i32>) -> ref/i32 = Box<i32>.get\nlet r = g(box@ref)\nrequire r == 8 else => $abort(\"erased\")")]
    [InlineData("ErasedClosure", "let f = func (n: ref/i32) => n\nlet g: (ref/i32) -> ref/i32 = f\nlet n = 7\nrequire g(n@ref) == 7 else => $abort(\"erased closure\")")]
    [InlineData("ErasedTwoInputs", Pick + "let p: (ref/i32, ref/i32) -> ref/i32 = pick\nlet x = 3\nlet y = 4\nrequire p(x@ref, y@ref) == 3 else => $abort(\"erased pick\")")]
    [InlineData("ExclusiveChain", Bump + "var k: i32 = 1\nlet b = bump\nlet d = b(k@uniq)\nlet e = b(d)\ne@follow = 4\nd@follow = 5\nrequire k == 5 else => $abort(\"chain\")")]
    [InlineData("ErasedExclusive", Bump + "var k: i32 = 1\nlet b: (uniq/i32) -> uniq/i32 = bump\nlet d = b(k@uniq)\nd@follow = 6\nrequire k == 6 else => $abort(\"erased exclusive\")")]
    [InlineData("AfterLastUse", Box + "var box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nrequire r == 5 else => $abort(\"read\")\nbox = Box<i32>.init(item: 6)\nrequire box.get() == 6 else => $abort(\"replaced\")")]
    public void ResultsTakeTheArgumentOrigins(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("InputDependentCall" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Box + "var box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)\nbox = Box<i32>.init(item: 6)\nrequire r == 5 else => $abort(\"read\")")]
    [InlineData("let f = func (n: ref/i32) => n\nvar n = 7\nlet m = f(n@ref)\nn = 8\nrequire m == 7 else => $abort(\"read\")")]
    [InlineData(Pick + "let p = pick\nlet x = 1\nvar y = 2\nlet r = p(x@ref, y@ref)\ny = 3\nrequire r == 1 else => $abort(\"read\")")]
    [InlineData(Box + "var box = Box<i32>.init(item: 8)\nlet g: (ref/Box<i32>) -> ref/i32 = Box<i32>.get\nlet r = g(box@ref)\nbox = Box<i32>.init(item: 9)\nrequire r == 8 else => $abort(\"read\")")]
    [InlineData(Bump + "var k: i32 = 1\nlet b = bump\nlet c = b(k@uniq)\nlet read = k\nc@follow = 2")]
    [InlineData(Bump + "var k: i32 = 1\nlet b = bump\nlet d = b(k@uniq)\nlet e = b(d)\nd@follow = 5\ne@follow = 4")]
    public void TheResultRetainsTheArgumentLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void TheCallResultIsOverTheArgumentPlace()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let box = Box<i32>.init(item: 5)\nlet get = Box<i32>.get\nlet r = get(box@ref)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.InvocationKoto>(), x => x.BoundValueCall is not null);
        var origin = call.BoundType!.Origin!;
        Assert.Equal(OriginKind.Projection, origin.Kind);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInputDependentValueCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InputDependentValueCall);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}

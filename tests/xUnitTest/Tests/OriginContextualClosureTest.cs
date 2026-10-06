// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 10.5, 15.6.4: a fixed expected call signature whose inputs are fresh per-call borrows types an anonymous function's
// omitted header; its inputs become the anonymous function's own inputs, and a result over them keeps the argument Loans.
public class OriginContextualClosureTest
{
    [Theory]
    [InlineData("Initializer", "let g: (ref/i32) -> ref/i32 = func (n) => n\nlet x: i32 = 5\nrequire g(x@ref) == 5 else => $abort(\"g\")")]
    [InlineData("WrittenHeader", "let g: (ref/i32) -> ref/i32 = func (n: ref/i32) -> ref/i32 => n\nlet x: i32 = 5\nrequire g(x@ref) == 5 else => $abort(\"g\")")]
    [InlineData("FreeResult", "let g: (ref/i32) -> i32 = func (n) => n@follow + 1\nlet x: i32 = 5\nrequire g(x@ref) == 6 else => $abort(\"g\")")]
    [InlineData("Exclusive", "let g: (uniq/i32) -> uniq/i32 = func (n) => n\nvar x: i32 = 5\nlet r = g(x@uniq)\nr@follow = 7\nrequire x == 7 else => $abort(\"g\")")]
    [InlineData("TwoInputs", "let g: (ref/i32, ref/i32) -> ref/i32 = func (a, b) => a\nlet x: i32 = 5\nlet y: i32 = 6\nrequire g(x@ref, y@ref) == 5 else => $abort(\"g\")")]
    [InlineData("Assignment", "func inc(n: ref/i32) -> i32 => n@follow + 1\nvar g: (ref/i32) -> i32 = inc\ng = func (n) => n@follow + 2\nlet x: i32 = 5\nrequire g(x@ref) == 7 else => $abort(\"g\")")]
    [InlineData("Return", "func make() -> (ref/i32) -> i32\n    return func (n) => n@follow + 1\nlet g = make()\nlet x: i32 = 5\nrequire g(x@ref) == 6 else => $abort(\"g\")")]
    [InlineData("Payload", "let o: Option<(ref/i32) -> i32> = .Some(func (n) => n@follow + 1)\nlet x: i32 = 5\nmatch o\n    .Some(let g) => require g(x@ref) == 6 else => $abort(\"g\")\n    .None => $abort(\"none\")")]
    [InlineData("Arms", "let ready = true\nlet g: (ref/i32) -> i32 = if ready => func (n) => n@follow + 1 else => func (n) => n@follow\nlet x: i32 = 5\nrequire g(x@ref) == 6 else => $abort(\"g\")")]
    [InlineData("Parameter", "func call(action: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 during x\n    return action(x)\nlet x: i32 = 5\nrequire call(func (n) => n, x@ref) == 5 else => $abort(\"g\")")]
    public void AnOriginBearingSignatureTypesTheHeader(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("OriginContextualClosure" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("let g: (ref/i32) -> ref/i32 = func (n) => n\nvar x: i32 = 5\nlet r = g(x@ref)\nx = 6\nrequire r == 5 else => $abort(\"g\")")]
    [InlineData("let g: (uniq/i32) -> uniq/i32 = func (n) => n\nvar x: i32 = 5\nlet r = g(x@uniq)\nlet read = x\nr@follow = 7")]
    public void TheResultKeepsTheArgumentLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let g: (ref/i32) -> ref/i32 = func (n: ref/i64) => n")]
    [InlineData("let g: (ref/i32) -> ref/i32 = func (n) -> i32 => 1")]
    [InlineData("let g: (ref/i32) -> ref/i32 = func (n, m) => n")]
    public void AWrittenPartThatDiffersIsAMismatch(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOriginContextualClosuresAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let g: (ref/i32) -> ref/i32 = func (n) => n\nlet h: (uniq/i32) -> uniq/i32 = func (n) => n\nvar x: i32 = 5\nlet r = h(x@uniq)\nr@follow = g(r)@follow + 1");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}

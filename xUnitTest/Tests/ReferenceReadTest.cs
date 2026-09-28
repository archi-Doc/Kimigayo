// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.3: a ref/T or uniq/T value with a Copy referent is read as its referent wherever a T is expected:
// an initializer, an assignment source, an argument, a result and an operand. One read removes one layer,
// and a Non-Copy referent is never extracted through a reference.
public class ReferenceReadTest
{
    private const string Source = "func value(v: i32) -> i32 => v\nfunc read(r: ref/i32) -> i32 => r\nfunc readUniq(u: uniq/i32) -> i32 => u\n" +
        "let x: i32 = 7\nvar y: i32 = 3\nlet r = x@ref\nlet n: i32 = r\nvar m: i32 = 0\nm = r\nlet apply = func (v: i32) -> i32 => v + 1\n" +
        "require value(r) == 7 and read(x@ref) == 7 and readUniq(y@uniq) == 3 and n == 7 and m == 7 else => $abort(\"acquire\")\n" +
        "require r == 7 and 7 == r and r == x and not (x != r) and r + 1 == 8 and -r == -7 and not (r < 7) and apply(r) == 8 else => $abort(\"operand\")\n" +
        "var wide: i64 = 1\nlet count: i32 = 3\nlet c = count@ref\nwide <<= c\nrequire wide == 8 and (wide >> c) == 1 else => $abort(\"shift\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void ReadsCopyReferentsWhereTheReferentTypeIsExpected()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source}")
            .Concat(c.Ownership.ControlFlow!.Issues.Select(x => $"flow {x.Message}: {x.Node}")).Concat(c.Ownership.ControlFlow.PendingBinding.Select(x => $"pending: {x}"))));
        Assert.True(c.Emission.TryPrepare(out _, out var error), MinimalEmissionTest.Describe(c, error));
        ScalarEmissionTest.EmitFixture("ReferenceRead", Source, "ok\n");
    }

    [Fact]
    public void ExactReferenceParameterOutranksTheRead()
    {
        // SPEC 10.2: the read is a cross-Semantics adaptation, so an Exact ref/i32 candidate wins over it.
        const string source = "func f(v: i32) -> i32 => 1\nfunc f(v: ref/i32) -> i32 => 2\nlet x: i32 = 7\nlet r = x@ref\n" +
            "require f(x) == 1 and f(r) == 2 and f(x@ref) == 2 else => $abort(\"ranking\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        ScalarEmissionTest.EmitFixture("ReferenceReadRanking", source, string.Empty);
    }

    [Theory]
    [InlineData("func f(r: ref/string) -> string => r")]
    [InlineData("func f<T>(r: ref/T) -> T => r")]
    [InlineData("func f(r: ref/i32, v: string) -> bool => r == v")]
    public void NeverExtractsNonCopyReferents(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Fact]
    public void NoConversionAppliesThroughAReference()
    {
        // SPEC 13.5.3: a referent is read only under the Copy read; `r@i64` converts nothing through `r`.
        var c = MinimalEmissionTest.Analyze("func f(r: ref/i32) -> i64 => r@i64");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Single(c.Binding.Issues);
    }

    [Fact]
    public void AggregateReferentReadsCopyTheCompleteValue()
    {
        const string Source = "let a: [2 of i32] = [1, 2]\nlet ra = a@ref\nlet b: [2 of i32] = ra@follow\nrequire b[0] == 1 and b[1] == 2 else => $abort(\"aggregate\")";
        NativeAllocationAudit.WriteFixture("ReferenceReadAggregate", Source, 0, 0, 0, string.Empty);
    }
}

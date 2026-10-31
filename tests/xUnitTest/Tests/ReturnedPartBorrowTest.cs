// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ReturnedPartBorrowTest
{
    private const string Prefix = "struct Counter\n    public var value: i32 = 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\nfunc relay(p: uniq/Counter) -> uniq/Counter during p => p\nfunc shared(p: ref/Counter) -> ref/Counter during p => p\nfunc second(a: ref/Counter, b: uniq/Counter) -> uniq/Counter during b => b\n";

    [Theory]
    [InlineData("ExclusiveMove", "var pair = Pair.init()\nlet a = relay(pair.left@uniq)\nlet moved = pair.right@move\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("SharedMove", "var pair = Pair.init()\nlet a = shared(pair.left@ref)\nlet moved = pair.right@move\nrequire a.value + moved.value == 2 else => $abort(\"value\")")]
    [InlineData("MoveFirst", "var pair = Pair.init()\nlet moved = pair.right@move\nlet a = relay(pair.left@uniq)\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("NestedCall", "var pair = Pair.init()\nlet a = relay(relay(pair.left@uniq))\nlet moved = pair.right@move\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("NamedSlot", "var pair = Pair.init()\nvar other = Counter.init()\nlet a = second(b: pair.left@uniq, a: other@ref)\nlet moved = pair.right@move\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("DisjointWrite", "var pair = Pair.init()\nlet a = relay(pair.left@uniq)\npair.right.value = 4\na.value += pair.right.value\nrequire pair.left.value == 5 else => $abort(\"value\")")]
    [InlineData("SelectedPart", "func pick(p: uniq/Pair) -> uniq/Counter during p => p.right@uniq\nvar pairs = (Pair.init(), Pair.init())\nlet a = pick(pairs.0@uniq)\nlet moved = pairs.1@move\na.value += moved.left.value\nrequire pairs.0.right.value == 2 else => $abort(\"value\")")]
    public void RetainsDeclaredInputFootprint(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, string.Join('\n', c.Problems));
        ScalarEmissionTest.EmitFixture("ReturnedPartBorrow" + name, Prefix + source, string.Empty);
    }

    [Theory]
    [InlineData("var pair = Pair.init()\nlet a = relay(pair.left@uniq)\nlet moved = pair.left@move\na.value += 1")]
    [InlineData("var pair = Pair.init()\nlet a = shared(pair.left@ref)\nlet moved = pair@move\nlet value = a.value")]
    [InlineData("var pair = Pair.init()\nlet a = relay(pair.left@uniq)\npair.left.value += 1\na.value += 1")]
    [InlineData("var pair = Pair.init()\nvar other = Counter.init()\nlet a = second(b: pair.left@uniq, a: other@ref)\npair.left = Counter.init()\na.value += 1")]
    [InlineData("func pick(p: uniq/Pair) -> uniq/Counter during p => p.right@uniq\nvar pair = Pair.init()\nlet a = pick(pair@uniq)\npair.left.value = 2\na.value += 1")]
    [InlineData("func pick(p: uniq/(Pair, Pair)) -> uniq/Pair during p => p.1@uniq\nvar pairs = (Pair.init(), Pair.init())\nlet a = pick(pairs@uniq).left@uniq\npairs.1.left.value = 2\na.value += 1")]
    public void ReturnedLoanStillProtectsItsInput(string source)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict() || x.Code is DiagnosticCode.MovedPlace_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReanalysisReusesBorrowStorage()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "var pair = Pair.init()\nlet a = relay(pair.left@uniq)\nlet moved = pair.right@move\na.value += moved.value");
        void Analyze()
        {
            if (!c.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Returned part Loan failed.");
            }
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Analyze();
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }
}

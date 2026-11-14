// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedCapabilityOriginTest
{
    private const string Next = "contract Read<E>\n    func get(self: ref/Self) -> E\ncontract C\n    associate Next is C\n" +
        "    associate Item(step) is Read<ref/i32 during step>\n";

    private const string Pull = "func pull<R>(value: ref/R, other: ref/i32 during a) -> ref/i32 during a\n    R is Read<ref/i32 during a>\n" +
        "    return value.get()\n";

    private const string Pair = "contract Read2<E, F>\n    func first(self: ref/Self) -> E\n    func second(self: ref/Self) -> F\ncontract C\n" +
        "    associate Next is C\n    associate Pair(x, y) is Read2<ref/i32 during x, ref/i32 during y>\n";

    private const string Concrete =
        "contract Read<E>\n    func get(self: ref/Self) -> E\ncontract C\n    associate Item(step) is Read<ref/i32 during step>\nstruct Reader {source}\n    Self is Read<ref/i32 during source>\n    let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n    public func get(self: ref/Self) -> ref/i32 during source => self.value\nstruct S\n    Self is C\n    associate Item(a) is Reader during a\n    public init() => ()\nfunc relay<I>(marker: ref/I, value: ref/I.Item(a), other: ref/i32 during a) -> ref/i32 during a\n    I is C\n    return value.get()\nfunc read(value: ref/i32 during a) -> ref/i32 during a\n    let marker = S.init()\n    let reader = Reader.init(value)\n    return relay(marker@ref, reader@ref, value)\n";

    [Theory]
    [InlineData("a", true)]
    [InlineData("b", false)]
    public void FamilyCapabilitySubstitutesItsOriginArgument(string result, bool valid)
    {
        var source = "contract Read<E>\n    func get(self: ref/Self) -> E\ncontract C\n    associate Item(step) is Read<ref/i32 during step>\nfunc relay<I>(value: ref/I.Item(a), other: ref/i32 during b) -> ref/i32 during " + result + "\n    I is C\n    return value.get()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // R7-C2c2 c2-2 row (2) (SPEC 8.5.1, 8.5.4: an associated Type's Origin arguments are applied positionally, per use): at depth 2 the
    // clause row of `I.(C).Next is C` is taken at the use's Origins, as row (1) takes a fact's: o2 (the use's `a` for the clause's
    // `step`), rv4 (the use's own Origin spelled `step`: by binder, not by name), rv5 (two Origins, `Pair(b, a)`, by position). Each
    // with its invalid twin. Until C3 row (1) also reaches o2 through the eager derived facts (design-c22 §7.1).
    [Theory]
    [InlineData("o2", Next + "func relay<I>(value: ref/I.(C).Next.(C).Item(a), other: ref/i32 during b) -> ref/i32 during a\n    I is C\n    return value.get()\n", "")]
    [InlineData("o2n", Next + "func relay<I>(value: ref/I.(C).Next.(C).Item(a), other: ref/i32 during b) -> ref/i32 during b\n    I is C\n    return value.get()\n", "MissingOriginBinding_Kd: func relay<I>(value: ref/I.(C).Next.(C).Item(a), other: ref/i32 during b) -> ref/i32 during b")]
    [InlineData("rv4", Next + Pull + "func relay<I>(value: ref/I.(C).Next.(C).Item(step), other: ref/i32 during step) -> ref/i32 during step\n    I is C\n    return pull(value, other)\n", "")]
    [InlineData("rv4n", Next + Pull + "func relay<I>(value: ref/I.(C).Next.(C).Item(b), other: ref/i32 during step) -> ref/i32 during step\n    I is C\n    return pull(value, other)\n", "MissingOriginBinding_Kd: func relay<I>(value: ref/I.(C).Next.(C).Item(b), other: ref/i32 during step) -> ref/i32 during step")]
    [InlineData("rv5", Pair + "func relay<I>(value: ref/I.(C).Next.(C).Pair(b, a), other: ref/i32 during b) -> ref/i32 during a\n    I is C\n    return value.second()\n", "")]
    [InlineData("rv5n", Pair + "func relay<I>(value: ref/I.(C).Next.(C).Pair(b, a), other: ref/i32 during b) -> ref/i32 during a\n    I is C\n    return value.first()\n", "UnprovenOriginRelation_Kd: return value.first()")]
    public void AClauseRowTakesTheUsesOriginArguments(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "public func main() => ()\n", records);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindOfADepthTwoClauseRowAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Next + "func relay<I>(value: ref/I.(C).Next.(C).Item(a), other: ref/i32 during b) -> ref/i32 during a\n    I is C\n" +
            "    return value.get()\npublic func main() => ()\n");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("The rebind failed.");
            }
        }));
    }

    [Fact]
    public void BoundCapabilityExecutesThroughGenericWitness()
        => ScalarEmissionTest.EmitFixture("AssociatedCapabilityOrigin", Concrete + "let value = 42\nrequire read(value@ref) == 42 else => $abort(\"origin\")\nConsole.writeLine(\"origin\")", "origin\n");

    [Fact]
    public void BoundCapabilityResultRetainsItsSourceLoan()
    {
        var c = MinimalEmissionTest.Analyze(Concrete + "var value = 42\nlet result = read(value@ref)\nvalue = 7\nrequire result == 42 else => $abort(\"loan\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
    }
}

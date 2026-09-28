// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedCapabilityOriginTest
{
    private const string Concrete =
        "contract Read<E>\n    func get(self: ref/Self) -> E\ncontract C\n    associate Item(step) is Read<ref/i32 during step>\nstruct Reader {source}\n    Self is Read<ref/i32 during source>\n    let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n    public func get(self: ref/Self) -> ref/i32 during source => self.value\nstruct S\n    Self is C\n    associate Item(a) is Reader during a\n    public init() => ()\nfunc relay<I>(marker: ref/I, value: ref/I.Item(a), other: ref/i32 during a) -> ref/i32 during a\n    I is C\n    return value.get()\nfunc read(value: ref/i32 during a) -> ref/i32 during a\n    let marker = S.init()\n    let reader = Reader.init(value)\n    return relay(marker@ref, reader@ref, value)\n";

    [Theory]
    [InlineData("a", true)]
    [InlineData("b", false)]
    public void FamilyCapabilitySubstitutesItsOriginArgument(string result, bool valid)
    {
        var source = "contract Read<E>\n    func get(self: ref/Self) -> E\ncontract C\n    associate Item(step) is Read<ref/i32 during step>\nfunc relay<I>(value: ref/I.Item(a), other: ref/i32 during b) -> ref/i32 during " + result + "\n    I is C\n    return value.get()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void BoundCapabilityExecutesThroughGenericWitness()
        => ScalarEmissionTest.EmitFixture("AssociatedCapabilityOrigin", Concrete + "let value = 42\nrequire read(value@ref) == 42 else => $abort(\"origin\")\nConsole.writeLine(\"origin\")", "origin\n");

    [Fact]
    public void BoundCapabilityResultRetainsItsSourceLoan()
    {
        var c = MinimalEmissionTest.Analyze(Concrete + "var value = 42\nlet result = read(value@ref)\nvalue = 7\nrequire result == 42 else => $abort(\"loan\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }
}

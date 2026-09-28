// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedOriginRefinementTest
{
    private const string Declarations = "contract Parent\n    associate Item(a)\ncontract Child: Parent\n    associate Value\n    associate Parent.Item(b) is Value\nstruct S\n    Self is Child\n    associate Child.Value is i32\n";

    [Fact]
    public void RefinementFixesEveryAppliedOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "func f(x: ref/i32 during a) -> S.(Parent).Item(a) => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(BoundType.I32, f.ReturnType!.BoundType);
    }

    [Fact]
    public void GenericRefinementNormalizesToStepIndependentItem()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "func f<T>(value: T.(Parent).Item(a), source: ref/T during a) -> T.(Child).Value\n    T is Child\n    return value@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void BorrowedRefinementMapsParameterNamesByPosition()
    {
        const string source = "contract Parent\n    associate Item(a)\ncontract Child: Parent\n    associate Parent.Item(b) is ref/i32 during b\nstruct S\n    Self is Child\nfunc f(value: ref/i32 during c) -> S.(Parent).Item(c) => value\nlet value = 7\nlet result = f(value@ref)\nrequire result == 7 else => $abort(\"refinement\")\nConsole.writeLine(\"refinement\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginRefinementBorrow", source, "refinement\n");
    }

    [Fact]
    public void EmitsInheritedConstantFamily()
        => ScalarEmissionTest.EmitFixture("AssociatedOriginRefinementConstant", Declarations + "func f(value: ref/i32 during a) -> S.(Parent).Item(a) => 42\nlet value = 7\nrequire f(value@ref) == 42 else => $abort(\"constant refinement\")\nConsole.writeLine(\"constant refinement\")", "constant refinement\n");

    [Theory]
    [InlineData("contract Parent\n    associate Item(a)\ncontract Child\n    associate Parent.Item(b) is i32")]
    [InlineData("contract Parent\n    associate Item(a)\ncontract Child: Parent\n    associate Parent.Item(b, c) is i32")]
    [InlineData("contract Parent\n    associate Item(a) is i32\ncontract Child: Parent\n    associate Parent.Item(b) is bool\nstruct S\n    Self is Child")]
    public void InvalidRefinementsAreRejected(string source)
    {
        ParseTestHelper.ParseSuccess(source);
        Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
    }
}

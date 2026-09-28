// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedNominalFormationTest
{
    private const string View = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n";
    private const string Family = "contract C\n    associate Item(a) is View during a\nstruct S\n    Self is C\nfunc wrap(value: ref/i32 during a) -> S.Item(a) => View.init(value)\n";

    [Fact]
    public void NominalOriginSuffixInRequirementParses()
        => ParseTestHelper.ParseSuccess(View + Family);

    [Fact]
    public void NominalFamilyRetainsItsStoredBorrow()
        => ScalarEmissionTest.EmitFixture("AssociatedNominalBorrow", View + Family + "var value = 7\nlet view = wrap(value@ref)\nrequire view.value == 7 else => $abort(\"view\")\nvalue = 9\nConsole.writeLine(\"view\")", "view\n");

    [Fact]
    public void NominalFamilyPreventsConflictingMutation()
    {
        var c = MinimalEmissionTest.Analyze(View + Family + "var value = 7\nlet view = wrap(value@ref)\nvalue = 9\nrequire view.value == 7 else => $abort(\"view\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void BorrowOfNominalTypePublishesStoredOriginConditions(string relation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(View + "contract C\n    associate Item(a, b) is ref/(View during a) during b\nstruct S\n    Self is C\nfunc f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    $abort(\"unused\")");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedFamilyKeepsItsBorrowedTypeArgument(bool mutate)
    {
        const string declarations = "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\ncontract C\n    associate Item(a) is Box<ref/i32 during a>\nstruct S\n    Self is C\nfunc wrap(value: ref/i32 during a) -> S.Item(a) => Box<ref/i32 during a>.init(value)\n";
        var source = declarations + "var value = 7\nlet box = wrap(value@ref)\n" + (mutate ? "value = 9\n" : string.Empty) + "require box.value == 7 else => $abort(\"box\")\nConsole.writeLine(\"box\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(!mutate, c.Ownership.Result.IsVerified);
        if (!mutate)
        {
            ScalarEmissionTest.EmitFixture("AssociatedConstructedBorrow", source, "box\n");
        }
    }
}

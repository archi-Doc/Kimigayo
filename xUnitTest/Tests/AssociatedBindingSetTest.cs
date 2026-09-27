// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedBindingSetTest
{
    private const string Related = "struct Related {first, second}\n    origin first outlives second\n    public let left: ref/i32 during first\n    public let right: ref/i32 during second\n";
    private const string Family = "contract C\n    associate Item(a, b) is Related{view}\n        origin view.first == a\n        origin view.second == b\nstruct S\n    Self is C\n";

    [Fact]
    public void BoundNominalFamilyExecutes()
    {
        const string source = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\ncontract C\n    associate Item(a) is View{view}\n        origin view.source == a\nstruct S\n    Self is C\nfunc wrap(value: ref/i32 during a) -> S.Item(a) => View.init(value)\nvar value = 7\nlet view = wrap(value@ref)\nrequire view.value == 7 else => $abort(\"view\")\nvalue = 9\nConsole.writeLine(\"bound\")";
        ScalarEmissionTest.EmitFixture("AssociatedBindingSet", source, "bound\n");
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void BindingSetsPublishTheBoundNominalDomain(string relation, bool valid)
    {
        var source = Related + Family + "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    $abort(\"unused\")";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    public void ImplementationCannotAddTheNominalDomain(string relation, bool valid)
    {
        var source = Related + "contract C\n    associate Item(a, b)\n        " + relation + "\nstruct S\n    Self is C\n    associate C.Item(a, b) is Related{view}\n        origin view.first == a\n        origin view.second == b";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}

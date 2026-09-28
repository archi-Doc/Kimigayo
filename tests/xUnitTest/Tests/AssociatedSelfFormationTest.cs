// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedSelfFormationTest
{
    private const string Lending = "contract Lending\n    associate Item(step) for uniq/Self during step\n    func next(self: uniq/Self during step) -> Self.Item(step)\nstruct Counter\n    Self is Lending\n    var value: i32 = 0\n    public init() => ()\n    associate Lending.Item(a) is ref/i32 during a\n    public func next(self: uniq/Self during step) -> ref/i32 during step\n        self.value += 1\n        return self.value@ref\nfunc advance<T>(iterator: uniq/T during step) -> T.(Lending).Item(step)\n    T is Lending\n    return iterator.next()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfFormationSupportsLendingWitnesses(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Lending + "var iterator = Counter.init()\nlet first = " + next + "\nrequire first == 1 else => $abort(\"first\")\nlet second = " + next + "\nrequire second == 2 else => $abort(\"second\")\nConsole.writeLine(\"self domain\")";
        ScalarEmissionTest.EmitFixture("AssociatedFormationSelf" + (generic ? "Generic" : "Direct"), source, "self domain\n");
    }

    [Theory]
    [InlineData("ref/T during a", true)]
    [InlineData("T", false)]
    public void GenericSelfNeedsFormationEvidence(string input, bool valid)
    {
        var source = "contract C\n    associate Item(a) for ref/Self during a\nfunc f<T>(value: " + input + ", other: ref/i32 during a) -> T.(C).Item(a)\n    T is C\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a) is i32 for ref/i32 during a")]
    [InlineData("contract C\n    associate Item for ref/i32 during static")]
    public void FormationTypeNeedsAnUnfixedFamily(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
}

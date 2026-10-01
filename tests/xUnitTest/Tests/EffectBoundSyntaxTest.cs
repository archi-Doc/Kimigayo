// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

// SPEC 8.4.10.1: effect clauses and effect specifications use contextual words in these positions only.
public class EffectBoundSyntaxTest
{
    [Fact]
    public void ParsesEffectClausesInTheConstraintRegion()
    {
        var tree = ParseSuccess("contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\n    func put(self: uniq/Self, value: i32) -> ()\n        Self is Copy\n        effect confined");
        var contract = Assert.IsType<ContractKoto>(Assert.Single(tree.RootKoto.NestedContainers));
        var functions = contract.Members.OfType<FunctionKoto>().ToArray();
        Assert.Equal(EffectBoundKind.PreservesResults, Assert.Single(functions[0].EffectBounds).Bound);
        var confined = Assert.Single(functions[1].EffectBounds);
        Assert.Equal(EffectBoundKind.Confined, confined.Bound);
        Assert.False(confined.IsSpecification);
        Assert.Single(functions[1].TypeConstraints);
        AssertRoundTrip(tree);
    }

    [Theory]
    [InlineData("contract StableSource: Source\n    effect Source.take preserves results", "Source", "take", EffectBoundKind.PreservesResults)]
    [InlineData("contract Strict: Indexable<i32>\n    effect (Indexable<i32>).index confined", "(Indexable<i32>)", "index", EffectBoundKind.Confined)]
    [InlineData("contract Stable: Kimi.Iterator\n    effect Kimi.LendingIterator.next preserves results", "Kimi.LendingIterator", "next", EffectBoundKind.PreservesResults)]
    [InlineData("contract Strict: confined\n    effect confined.f confined", "confined", "f", EffectBoundKind.Confined)]
    public void ParsesEffectSpecificationsAsContractItems(string source, string selector, string name, EffectBoundKind bound)
    {
        var tree = ParseSuccess(source);
        var contract = Assert.IsType<ContractKoto>(Assert.Single(tree.RootKoto.NestedContainers));
        var effect = Assert.IsType<EffectBoundKoto>(Assert.Single(contract.Members));
        Assert.True(effect.IsSpecification);
        Assert.Equal(selector, effect.Selector!.ToString());
        Assert.Equal(name, effect.Name!.ToString());
        Assert.Equal(bound, effect.Bound);
        AssertRoundTrip(tree);
    }

    [Fact]
    public void EffectIsAnOrdinaryNameElsewhere()
    {
        var generic = ParseSingleFunction("func f<effect>(value: effect) -> ()\n    effect is Copy\n    let x = value");
        Assert.Single(generic.TypeConstraints);
        Assert.Empty(generic.EffectBounds);

        var call = ParseSingleFunction("func g() -> ()\n    effect(1)\n    effect.run()");
        Assert.Empty(call.EffectBounds);
        Assert.Equal(2, call.Body!.Items.Count);

        var local = ParseSuccess("let effect = 1\nlet confined = effect + 1\nlet preserves = confined");
        Assert.Equal(3, GetChildren(local.RootKoto).Count);
    }

    [Fact]
    public void KeepsMisplacedEffectItemsForBinding()
    {
        // Ordinary functions and other containers accept no effect item; the syntax is kept so that Binding can say so.
        var function = ParseSingleFunction("func f() -> ()\n    effect confined\n    let x = 1");
        Assert.Single(function.EffectBounds);
        Assert.Single(function.Body!.Items);

        var tree = ParseSuccess("struct S\n    effect confined");
        Assert.IsType<EffectBoundKoto>(Assert.Single(Assert.IsType<StructKoto>(Assert.Single(tree.RootKoto.NestedContainers)).Members));
    }

    private static void AssertRoundTrip(Kimi.Compiler.Kotonoha tree)
    {
        var original = Unparse(tree);
        Assert.Contains("effect ", original);
        Assert.Equal(original, Unparse(ParseSuccess(original)));
    }
}

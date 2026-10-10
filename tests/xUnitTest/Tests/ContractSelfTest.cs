// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.4, 8.4.9: inside a Contract, Self is a dedicated Type parameter, distinct from every reference to the
/// Contract; every associated-Type projection names its receiver and the Contract reference that declares it.</summary>
public class ContractSelfTest
{
    [Fact]
    public void SelfIsADedicatedParameterAndProjectionsNameTheirContract()
    {
        const string Source = "public contract Holder\n    associate Item\n    func take(self: ref/Self) -> Item\n" +
            "func read<H>(holder: ref/H) -> H.Item\n    H is Holder\n    H.Item is Copy\n    return holder.take()\nlet x = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var holder = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Holder");
        var take = holder.Members.OfType<FunctionKoto>().Single();
        var self = take.Parameters[0].Type.TypeOf()!.Components[0];
        Assert.True(Binding.IsContractSelf(self));
        Assert.Equal(BoundTypeKind.Parameter, self.Kind);
        Assert.NotSame(holder.BoundSymbol!.Type, self);

        // Inside the Contract, Item is Self's Item declared by Holder; in a generic body, H's Item declared by Holder.
        Assert.Equal([self, holder.BoundSymbol.Type!], take.ReturnType!.TypeOf()!.Components);
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "read");
        var projection = read.ReturnType!.TypeOf()!;
        Assert.Equal(BoundTypeKind.AssociatedProjection, projection.Kind);
        Assert.Same(read.GenericArguments[0].TypeOf(), projection.Components[0]);
        Assert.Same(holder.BoundSymbol.Type, projection.Components[1]);
    }

    [Fact]
    public void AGenericContractNamesItsOwnParametersAndEachBoundReference()
    {
        const string Source = "func first<S>(items: ref/S) -> S.(Indexable<isize>).Element\n    S is Indexable<isize>\n    S.(Indexable<isize>).Element is Copy\n    return items[0]\nlet x = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var indexable = c.Binding.Library.Indexable!;
        var index = ((ContractKoto)indexable.Declaration).Members.OfType<FunctionKoto>().Single();
        var element = index.ReturnType!.TypeOf()!.Components[0];

        // `Element` of the requirement is Self's Element declared by Indexable<Key>, the Contract applied to its own Key.
        Assert.True(Binding.IsContractSelf(element.Components[0]));
        Assert.Equal(BoundTypeKind.Constructed, element.Components[1].Kind);
        Assert.Same(((ContractKoto)indexable.Declaration).GenericParameterNodes[0].TypeOf(), element.Components[1].Components[0]);

        var first = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "first");
        var projection = first.ReturnType!.TypeOf()!;
        Assert.Same(first.GenericArguments[0].TypeOf(), projection.Components[0]);
        Assert.Same(BoundType.ISize, projection.Components[1].Components[0]);
    }
}

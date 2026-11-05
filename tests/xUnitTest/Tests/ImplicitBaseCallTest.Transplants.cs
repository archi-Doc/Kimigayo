// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class ImplicitBaseCallTest
{
    [Fact]
    public void ATransplantedStructureReactivatesItsLatentConstructorInTheRecipient()
    {
        var donor = CompilationTestHelper.BindSuccess("struct Box\n    public var value: i32 = 1");
        var structure = Assert.IsType<StructKoto>(Assert.Single(donor.Kotonoha.RootKoto.NestedContainers));
        var latent = Assert.IsType<FunctionKoto>(structure.ImplicitConstructor);
        var field = Assert.IsType<PropertyKoto>(Assert.Single(structure.Members));
        var withoutInitializer = CompilationTestHelper.ParseSuccess("struct Box\n    public var value: i32");
        var bare = Assert.IsType<PropertyKoto>(Assert.Single(Assert.Single(withoutInitializer.Kotonoha.RootKoto.NestedContainers).Members));
        ParseTestHelper.Detach(bare, new CodeBlockKoto(bare.CodeContext));
        Assert.True(KotoHelper.Replace(structure, field, bare));
        Assert.True(donor.Bind().IsComplete);
        Assert.Null(structure.SynthesizedConstructor);
        var recipient = CompilationTestHelper.BindSuccess("struct Box\nlet box = Box.init()");
        var old = Assert.Single(recipient.Kotonoha.RootKoto.NestedContainers);
        ParseTestHelper.Detach(structure, new GroupKoto(structure.CodeContext, default, default));
        Assert.True(KotoHelper.Replace(old.Parent!, old, structure));
        Assert.True(KotoHelper.Replace(structure, bare, field));
        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient));
        Assert.Same(latent, structure.ImplicitConstructor);
        Assert.All(KotoTree.Walk(latent), node => Assert.Same(recipient.Kotonoha, node.Kotonoha));
        Assert.Same(recipient.Kotonoha.Syntax, latent.SyntaxOwner);
        Assert.True(recipient.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(recipient.Analyze().IsVerified, MinimalEmissionTest.Describe(recipient));
        Assert.NotEmpty(CompilationTestHelper.WriteIr(recipient));
    }

    [Fact]
    public void AConstructorReactivatesItsLatentBaseCallAfterTwoTransplants()
    {
        const string Derived = "open struct Base\n    protected init() => ()\nstruct Leaf: Base\n    public init() => ()\nlet leaf = Leaf.init()";
        var donor = MinimalEmissionTest.Analyze(Derived, "donor.kimi");
        var middle = CompilationTestHelper.BindSuccess("struct Leaf\n    public init() => ()");
        var recipient = MinimalEmissionTest.Analyze(Derived, "recipient.kimi");
        Assert.True(donor.Analysis.IsVerified, MinimalEmissionTest.Describe(donor));
        Assert.True(recipient.Analysis.IsVerified, MinimalEmissionTest.Describe(recipient));
        var moved = Constructor(donor);
        var latent = Assert.IsType<InvocationKoto>(moved.BaseInitializer);
        var interim = Constructor(middle);
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(interim.Parent!, interim, moved));
        Assert.True(middle.Bind().IsComplete, MinimalEmissionTest.Describe(middle));
        Assert.Null(moved.BaseInitializer);
        var target = Constructor(recipient);
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(target.Parent!, target, moved));
        Assert.True(recipient.Bind().IsComplete, MinimalEmissionTest.Describe(recipient));
        Assert.Same(latent, moved.BaseInitializer);
        Assert.All(KotoTree.Walk(latent), node => Assert.Same(recipient.Kotonoha, node.Kotonoha));
        Assert.Same(recipient.Kotonoha, latent.CallOf()!.Target.Declaration.Kotonoha);
        Assert.True(recipient.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(recipient.Analyze().IsVerified, MinimalEmissionTest.Describe(recipient));
        Assert.NotEmpty(CompilationTestHelper.WriteIr(recipient));

        static FunctionKoto Constructor(Compilation compilation)
            => Assert.Single(
                KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>(),
                node => node.IsConstructor && node.Parent is StructKoto { Name: "Leaf" });
    }
}

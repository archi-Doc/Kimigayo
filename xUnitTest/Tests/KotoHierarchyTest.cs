// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class KotoHierarchyTest
{
    [Fact]
    public void ModelsCollectionCapabilitiesInTheFlatHierarchy()
    {
        var root = Compilation.CreateForTest().Kotonoha.RootKoto;
        var group = Assert.IsType<GroupKoto>(root.GetOrAddCollection("Group", TokenKind.Group, default, default));
        var structure = Assert.IsType<StructKoto>(root.GetOrAddCollection("Struct", TokenKind.Struct, default, default));
        var enumeration = Assert.IsType<EnumKoto>(root.GetOrAddCollection("Enum", TokenKind.Enum, default, default));
        var extension = Assert.IsType<ExtensionKoto>(root.GetOrAddCollection("Target", TokenKind.Extension, default, default));
        var contract = Assert.IsType<ContractKoto>(root.GetOrAddCollection("Contract", TokenKind.Contract, default, default));

        Assert.Equal(typeof(CollectionKoto), typeof(GroupKoto).BaseType);
        Assert.Equal(typeof(CollectionKoto), typeof(StructKoto).BaseType);
        Assert.Equal(typeof(CollectionKoto), typeof(EnumKoto).BaseType);
        Assert.Equal(typeof(CollectionKoto), typeof(ExtensionKoto).BaseType);
        Assert.Equal(typeof(CollectionKoto), typeof(ContractKoto).BaseType);

        Assert.False(group.IsInstantiable);
        Assert.True(group.HasStaticMembersOnly);
        Assert.False(extension.IsInstantiable);
        Assert.True(extension.HasStaticMembersOnly);
        Assert.Equal("Target", extension.Target);

        Assert.True(structure.IsInstantiable);
        Assert.True(enumeration.IsInstantiable);
        Assert.True(structure.SupportsGenerics);
        Assert.True(structure.SupportsOrigins);

        Assert.False(contract.IsInstantiable);
        Assert.False(contract.HasStaticMembersOnly);

        var variant = Assert.IsType<StructKoto>(
            enumeration.GetOrAddCollection("Variant", TokenKind.Struct, default, default));
        Assert.Same(variant, Assert.Single(enumeration.Structs));
    }

    [Fact]
    public void ExpandsQualifiedRootGroupUsingCommonCollectionParsing()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;

        compilation.Kotonoha.CreateCodeContext().Parse(
            root,
            "rootgroup A.B\n    var value = 1");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        var groupA = Assert.IsType<GroupKoto>(root.GetOrAddCollection("A", TokenKind.Group, default, default));
        var groupB = Assert.IsType<GroupKoto>(root.GetOrAddCollection("A.B", TokenKind.Group, default, default));
        Assert.Same(root, groupA.Parent);
        Assert.Same(groupA, groupB.Parent);
        Assert.IsType<FieldKoto>(Assert.Single(groupB.Members));
        Assert.Contains(groupA, root.ChildNodes);
        Assert.Contains(groupB, groupA.ChildNodes);
    }

    [Fact]
    public void ExposesEveryDirectChildThroughTheCommonContract()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;
        compilation.Kotonoha.CreateCodeContext().Parse(root, "var value: i32 = 1 + 2");

        var field = Assert.IsType<FieldKoto>(Assert.Single(root.Members));
        Assert.Collection(
            field.ChildNodes,
            child => Assert.IsType<IdentifierNameKoto>(child),
            child => Assert.IsAssignableFrom<TypeKoto>(child),
            child => Assert.IsType<PlusKoto>(child));

        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.Equal(2, addition.ChildNodes.Count());
        Assert.All(addition.ChildNodes, child => Assert.Same(addition, child.Parent));
    }
}

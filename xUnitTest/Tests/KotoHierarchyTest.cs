// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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

    [Fact]
    public void ParsesOnlyMembersSupportedByEachCollectionKind()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;
        var source = """
            group Utilities
                var before = 1
                struct Rejected
                    var nested = 2
                func Run()
                    return

            struct Container<s/T> origin source
                T is Comparable
                var value: s/T
                group Rejected
                    var nested = 3
                func Get() -> s/T
                    return value

            enum Choice
                var ignored = 4

            extension Target
                func Ignored()
                    return

            contract ComparableContract
                associate A is Comparable
                associate B is Equatable and Serializable
                var ignored = 5

            var rootValue = 6
            """;

        compilation.Kotonoha.CreateCodeContext().Parse(root, source);

        var group = Assert.IsType<GroupKoto>(GetCollection(root, "Utilities"));
        Assert.Collection(
            group.Members,
            member => Assert.IsType<FieldKoto>(member),
            member => Assert.IsType<FunctionKoto>(member));
        Assert.Empty(group.NestedCollections);

        var structure = Assert.IsType<StructKoto>(GetCollection(root, "Container"));
        Assert.Single(structure.GenericArguments);
        Assert.Equal(["source"], structure.Origins);
        Assert.Single(structure.TypeConstraints);
        Assert.Collection(
            structure.Members,
            member => Assert.IsType<FieldKoto>(member),
            member => Assert.IsType<FunctionKoto>(member));
        Assert.Empty(structure.NestedCollections);

        var enumeration = Assert.IsType<EnumKoto>(GetCollection(root, "Choice"));
        Assert.Empty(enumeration.Members);

        var extension = Assert.IsType<ExtensionKoto>(GetCollection(root, "Target"));
        Assert.Empty(extension.Members);

        var contract = Assert.IsType<ContractKoto>(GetCollection(root, "ComparableContract"));
        Assert.Equal(2, contract.TypeConstraints.Count);
        Assert.Empty(contract.Members);
        Assert.All(contract.TypeConstraints, constraint => Assert.Same(contract, constraint.Parent));

        Assert.IsType<FieldKoto>(Assert.Single(root.Members));
        var diagnostics = compilation.Kotonoha.DiagnosticCollection.GetArray();
        Assert.Equal(3, diagnostics.Length);
        Assert.All(
            diagnostics,
            diagnostic => Assert.Equal(nameof(DiagnosticCode.UnexpectedToken_Kd), diagnostic.Entry.Name));
    }

    [Fact]
    public void RoundTripsContractAssociatedTypeConstraints()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;
        compilation.Kotonoha.CreateCodeContext().Parse(
            root,
            "contract C\n    associate A is Comparable");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        var contract = Assert.IsType<ContractKoto>(GetCollection(root, "C"));
        var constraint = Assert.Single(contract.TypeConstraints);
        Assert.Equal("A", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        Assert.Equal("Comparable", Assert.IsType<IdentifierNameKoto>(constraint.Right).IdentifierName);

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            Assert.Contains("associate A is Comparable", builder.ToString());
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void RecognizesAssociateAsAContextualKeyword()
    {
        Assert.False(TokenKind.Associate.IsKeyword());
        Assert.True(TokenKind.Associate.IsIdentifierOrContextualKeyword());
        Assert.Equal(Constants.AssociateKeyword, TokenKind.Associate.ToText());
    }

    private static CollectionKoto GetCollection(CollectionKoto parent, string name)
        => Assert.Single(parent.NestedCollections, collection => collection.Name == name);
}

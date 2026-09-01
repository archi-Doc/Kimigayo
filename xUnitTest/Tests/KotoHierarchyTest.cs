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
    public void ModelsDeclarationContainerCapabilitiesInTheFlatHierarchy()
    {
        var root = Compilation.CreateForTest().Kotonoha.RootKoto;
        var group = Assert.IsType<GroupKoto>(root.GetOrAddDeclarationContainer("Group", TokenKind.Group, default, default));
        var structure = Assert.IsType<StructKoto>(root.GetOrAddDeclarationContainer("Struct", TokenKind.Struct, default, default));
        var enumeration = Assert.IsType<EnumKoto>(root.GetOrAddDeclarationContainer("Enum", TokenKind.Enum, default, default));
        var extension = Assert.IsType<ExtensionKoto>(root.GetOrAddDeclarationContainer("Target", TokenKind.Extension, default, default));
        var contract = Assert.IsType<ContractKoto>(root.GetOrAddDeclarationContainer("Contract", TokenKind.Contract, default, default));

        Assert.Equal(typeof(DeclarationContainerKoto), typeof(GroupKoto).BaseType);
        Assert.Equal(typeof(DeclarationContainerKoto), typeof(StructKoto).BaseType);
        Assert.Equal(typeof(DeclarationContainerKoto), typeof(EnumKoto).BaseType);
        Assert.Equal(typeof(DeclarationContainerKoto), typeof(ExtensionKoto).BaseType);
        Assert.Equal(typeof(DeclarationContainerKoto), typeof(ContractKoto).BaseType);

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
    public void ExpandsQualifiedRootGroupUsingCommonDeclarationContainerParsing()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;

        compilation.Kotonoha.CreateCodeContext().Parse(
            root,
            "rootgroup A.B\n    var value = 1");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        var groupA = Assert.IsType<GroupKoto>(root.GetOrAddDeclarationContainer("A", TokenKind.Group, default, default));
        var groupB = Assert.IsType<GroupKoto>(root.GetOrAddDeclarationContainer("A.B", TokenKind.Group, default, default));
        Assert.Same(root, groupA.Parent);
        Assert.Same(groupA, groupB.Parent);
        Assert.IsType<PropertyKoto>(Assert.Single(groupB.Members));
        Assert.Contains(groupA, root.ChildNodes);
        Assert.Contains(groupB, groupA.ChildNodes);
    }

    [Fact]
    public void ExposesEveryDirectChildThroughTheCommonContract()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;
        compilation.Kotonoha.CreateCodeContext().Parse(root, "var value: i32 = 1 + 2");

        Assert.Empty(root.Members);
        var generatedFunction = Assert.IsType<FunctionKoto>(compilation.Kotonoha.GeneratedFunction);
        Assert.True(generatedFunction.IsGenerated);
        Assert.Equal(Constants.GeneratedFunctionName, generatedFunction.Name);
        Assert.Same(root, generatedFunction.Parent);
        Assert.Contains(generatedFunction, root.ChildNodes);

        var body = Assert.IsType<CodeBlockKoto>(generatedFunction.Body);
        Assert.Same(generatedFunction, body.Parent);
        var field = Assert.IsType<FieldKoto>(Assert.Single(body.Items));
        Assert.Same(body, field.Parent);
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
    public void CollectsExecutableTopLevelSyntaxInGeneratedFunction()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var source = """
            var value = 1
            value += 2
            if value > 0
                value
            return value
            func Local()
                return
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        Assert.Empty(kotonoha.RootKoto.Members);
        var generatedFunction = Assert.IsType<FunctionKoto>(kotonoha.GeneratedFunction);
        var body = Assert.IsType<CodeBlockKoto>(generatedFunction.Body);
        Assert.Collection(
            body.Items,
            item => Assert.IsType<FieldKoto>(item),
            item => Assert.IsType<PlusEqualsKoto>(item),
            item => Assert.IsType<IfKoto>(item),
            item => Assert.IsType<ReturnKoto>(item),
            item => Assert.IsType<FunctionKoto>(item));
        Assert.False(body.HasTrailingExpression);
        Assert.All(body.Items, item => Assert.Same(body, item.Parent));

        var builder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("var value = 1", text, StringComparison.Ordinal);
            Assert.Contains("value += 2", text, StringComparison.Ordinal);
            Assert.Contains("func Local()", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Constants.GeneratedFunctionName, text, StringComparison.Ordinal);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void DoesNotCreateGeneratedFunctionForDeclarationContainerDeclarationsOnly()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "struct Value");

        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        Assert.Null(kotonoha.GeneratedFunction);
        Assert.Single(kotonoha.RootKoto.NestedDeclarationContainers);
    }

    [Fact]
    public void ClearingRootRemovesGeneratedFunction()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "run()");
        Assert.NotNull(kotonoha.GeneratedFunction);

        kotonoha.RootKoto.Clear();

        Assert.Null(kotonoha.GeneratedFunction);
    }

    [Fact]
    public void ParsesOnlyMembersSupportedByEachDeclarationContainerKind()
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

        var group = Assert.IsType<GroupKoto>(GetDeclarationContainer(root, "Utilities"));
        Assert.Collection(
            group.Members,
            member => Assert.IsType<PropertyKoto>(member),
            member => Assert.IsType<FunctionKoto>(member));
        var nestedStructure = Assert.IsType<StructKoto>(GetDeclarationContainer(group, "Rejected"));
        Assert.IsType<PropertyKoto>(Assert.Single(nestedStructure.Members));

        var structure = Assert.IsType<StructKoto>(GetDeclarationContainer(root, "Container"));
        Assert.Single(structure.GenericArguments);
        Assert.Equal(["source"], structure.Origins);
        Assert.Single(structure.TypeConstraints);
        Assert.Collection(
            structure.Members,
            member => Assert.IsType<PropertyKoto>(member),
            member => Assert.IsType<FunctionKoto>(member));
        Assert.Empty(structure.NestedDeclarationContainers);

        var enumeration = Assert.IsType<EnumKoto>(GetDeclarationContainer(root, "Choice"));
        Assert.Empty(enumeration.Members);

        var extension = Assert.IsType<ExtensionKoto>(GetDeclarationContainer(root, "Target"));
        Assert.Empty(extension.Members);

        var contract = Assert.IsType<ContractKoto>(GetDeclarationContainer(root, "ComparableContract"));
        Assert.Equal(2, contract.TypeConstraints.Count);
        Assert.Empty(contract.Members);
        Assert.All(contract.TypeConstraints, constraint => Assert.Same(contract, constraint.Parent));

        Assert.Empty(root.Members);
        var generatedBody = Assert.IsType<CodeBlockKoto>(compilation.Kotonoha.GeneratedFunction?.Body);
        Assert.IsType<FieldKoto>(Assert.Single(generatedBody.Items));
        var diagnostics = compilation.Kotonoha.DiagnosticCollection.GetArray();
        Assert.Equal(2, diagnostics.Length);
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
        var contract = Assert.IsType<ContractKoto>(GetDeclarationContainer(root, "C"));
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

    private static DeclarationContainerKoto GetDeclarationContainer(DeclarationContainerKoto parent, string name)
        => Assert.Single(parent.NestedDeclarationContainers, container => container.Name == name);
}

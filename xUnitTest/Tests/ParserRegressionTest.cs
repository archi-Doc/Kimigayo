// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ParserRegressionTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(DeclarationContainerKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void StopsAtEndOfIncompleteExpression()
    {
        var (_, diagnostics) = Parse("var x = 1 +");

        Assert.NotEmpty(diagnostics);
    }

    [Theory]
    [InlineData("var x = call(")]
    [InlineData("var x = value[")]
    [InlineData("var x = (1")]
    [InlineData("var x = value@")]
    [InlineData("func F(value: (i32")]
    public void RecoversFromMissingExpressionDelimiter(string source)
    {
        var (_, diagnostics) = Parse(source);

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void ParsesLessThanAsComparison()
    {
        var (root, diagnostics) = Parse("var x = a < b");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        Assert.IsType<LessThanKoto>(field.InitializerKoto);
    }

    [Fact]
    public void BindsMemberAccessBeforeAddition()
    {
        var (root, diagnostics) = Parse("var x = a.b + c");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.IsType<MemberAccessKoto>(addition.Left);
    }

    [Fact]
    public void ParsesLogicalExpressionsWithCorrectPrecedence()
    {
        var source = """
            var first = A and B
            var second = not A or B
            var third = A and not B
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var fields = GetChildren(root).Select(Assert.IsType<FieldKoto>).ToArray();

        var first = Assert.IsType<AndKoto>(fields[0].InitializerKoto);
        Assert.IsType<IdentifierNameKoto>(first.Left);
        Assert.IsType<IdentifierNameKoto>(first.Right);

        var second = Assert.IsType<OrKoto>(fields[1].InitializerKoto);
        Assert.IsType<NotKoto>(second.Left);
        Assert.IsType<IdentifierNameKoto>(second.Right);

        var third = Assert.IsType<AndKoto>(fields[2].InitializerKoto);
        Assert.IsType<IdentifierNameKoto>(third.Left);
        Assert.IsType<NotKoto>(third.Right);
    }

    [Fact]
    public void ParsesLogicalExpressionsAsIsRightOperand()
    {
        var source = """
            var first = X is A and B
            var second = X is not A or B
            var third = X is A and not B
            var fourth = P or X is A and B
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var fields = GetChildren(root).Select(Assert.IsType<FieldKoto>).ToArray();

        var first = Assert.IsType<IsKoto>(fields[0].InitializerKoto);
        Assert.IsType<AndKoto>(first.Right);

        var second = Assert.IsType<IsKoto>(fields[1].InitializerKoto);
        var secondCondition = Assert.IsType<NotKoto>(second.Right);
        Assert.IsType<OrKoto>(secondCondition.Operand);

        var third = Assert.IsType<IsKoto>(fields[2].InitializerKoto);
        var thirdCondition = Assert.IsType<AndKoto>(third.Right);
        Assert.IsType<NotKoto>(thirdCondition.Right);

        var fourth = Assert.IsType<OrKoto>(fields[3].InitializerKoto);
        var fourthCondition = Assert.IsType<IsKoto>(fourth.Right);
        Assert.IsType<AndKoto>(fourthCondition.Right);
    }

    [Fact]
    public void ContinuesAfterOuterIndentedClosingDelimiterOnSameLine()
    {
        var source = """
            var x = foo(
                a
            ) + 1
            """;

        var (root, diagnostics) = Parse(source);

        // Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.IsType<InvocationKoto>(addition.Left);
    }

    [Fact]
    public void ParsesGroupBody()
    {
        var (root, diagnostics) = Parse("group A\n    var x = 1");

        Assert.Empty(diagnostics);
        var group = root.GetOrAddGroup("A", TokenKind.Group, default, default);
        Assert.Equal("A", group.Name);
        Assert.IsType<PropertyKoto>(GetChildren(group).Single());
    }

    [Fact]
    public void ParsesAttributedInteropDeclarationsInGroup()
    {
        const string Source = """
            public group Kernel32
                #LibraryImport(LibraryName) func ExitProcess(
                    #Description("Exit code")
                    uExitCode: u32
                    ) -> ()

                #Layout(C)
                public struct OVERLAPPED
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);

        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        var kernel32 = Assert.IsType<GroupKoto>(
            Assert.Single(kotonoha.RootKoto.NestedDeclarationContainers, x => x.Name == "Kernel32"));
        Assert.True(kernel32.Modifier.HasFlag(ModifierKind.Public));

        var function = Assert.IsType<FunctionKoto>(Assert.Single(kernel32.Members));
        Assert.Equal("LibraryImport", GetAttributeName(function.AttributeChain));
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("uExitCode", parameter.ExternalName);
        Assert.Equal("Description", GetAttributeName(parameter.AttributeChain));
        Assert.Same(function, parameter.AttributeChain?.Parent);

        var structure = Assert.IsType<StructKoto>(
            Assert.Single(kernel32.NestedDeclarationContainers, x => x.Name == "OVERLAPPED"));
        Assert.True(structure.Modifier.HasFlag(ModifierKind.Public));
        Assert.Equal("Layout", GetAttributeName(structure.AttributeChain));

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        var restoredKotonoha = restored ?? throw new InvalidOperationException();
        restoredKotonoha.OnDeserialized(compilation);
        var restoredKernel32 = Assert.IsType<GroupKoto>(
            Assert.Single(restoredKotonoha.RootKoto.NestedDeclarationContainers, x => x.Name == "Kernel32"));
        var restoredFunction = Assert.IsType<FunctionKoto>(Assert.Single(restoredKernel32.Members));
        Assert.Equal("Description", GetAttributeName(Assert.Single(restoredFunction.Parameters).AttributeChain));

        static string GetAttributeName(AttributeKoto? attribute)
            => Assert.IsType<IdentifierNameKoto>(Assert.IsType<InvocationKoto>(attribute?.Operand).Method).IdentifierName;
    }

    [Fact]
    public void ParsesBodylessFunctionAtEndOfGroupBeforeNextGroup()
    {
        const string Source = """
            public group Kernel32 // shared (no instance)
                #LibraryImport(LibraryName) public func GetStdHandle(nStdHandle: u32) -> ptr

            public group Helper // namespace - alias
                public let Id: i32 = 123
            """;

        var (root, diagnostics) = Parse(Source);

        Assert.Empty(diagnostics);

        var kernel32 = Assert.IsType<GroupKoto>(
            Assert.Single(root.NestedDeclarationContainers, x => x.Name == "Kernel32"));
        Assert.IsType<FunctionKoto>(Assert.Single(kernel32.Members));

        var helper = Assert.IsType<GroupKoto>(
            Assert.Single(root.NestedDeclarationContainers, x => x.Name == "Helper"));
        Assert.IsType<PropertyKoto>(Assert.Single(helper.Members));
    }

    [Fact]
    public void PreservesTopLevelStructModifiersThroughAddSource()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.AddSource(new SourceDocument("modifier.kimi", "public open struct TestStruct<s/C, D>"));

        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        var type = Assert.IsType<StructKoto>(
            kotonoha.RootKoto.GetOrAddGroup("TestStruct", TokenKind.Struct, default, default));
        Assert.True(type.Modifier.HasFlag(ModifierKind.Public));
        Assert.True(type.Modifier.HasFlag(ModifierKind.Open));
        Assert.Collection(
            type.GenericArguments,
            argument =>
            {
                Assert.Equal(SemanticsKind.Parameter, argument.SemanticsKind);
                Assert.Equal("s", argument.SemanticsParameter);
                Assert.Equal("C", argument.Identifier);
            },
            argument =>
            {
                Assert.Equal(SemanticsKind.Owner, argument.SemanticsKind);
                Assert.Equal("D", argument.Identifier);
            });

        var builder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder);
            Assert.Contains("public open struct TestStruct<s/C, D>", builder.ToString());
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ParsesRepeatedStructAfterBodylessDeclaration()
    {
        var source = """
            public open struct TestStruct<s/C, D>

            public open struct TestStruct<s/C, D>
                semantics is reference
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("TestStruct", TokenKind.Struct, default, default));
        Assert.True(type.Modifier.HasFlag(ModifierKind.Public));
        Assert.True(type.Modifier.HasFlag(ModifierKind.Open));
        Assert.Collection(
            type.GenericArguments,
            argument =>
            {
                Assert.Equal(SemanticsKind.Parameter, argument.SemanticsKind);
                Assert.Equal("s", argument.SemanticsParameter);
                Assert.Equal("C", argument.Identifier);
            },
            argument =>
            {
                Assert.Equal(SemanticsKind.Owner, argument.SemanticsKind);
                Assert.Equal("D", argument.Identifier);
            });
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal("semantics", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        Assert.Equal(SemanticsMask.Reference, Assert.IsType<SemanticsMaskKoto>(constraint.Right).Mask);
    }

    [Fact]
    public void AppliesSemanticsToCompoundType()
    {
        var (root, diagnostics) = Parse("func F(value: objref/SomeType<List<owner/T>, I>)");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var semantics = Assert.IsType<TypeSemanticsKoto>(function.Parameters.Single().Type);
        Assert.Equal(SemanticsKind.ObjRef, semantics.SemanticsKind);
        Assert.IsType<GenericsKoto>(semantics.Type);
        Assert.Equal("objref/SomeType<List<owner/T>, I>", semantics.ToString());
    }

    [Theory]
    [InlineData("Dog from owner", "owner")]
    [InlineData("ref/Dog from source", "source")]
    [InlineData("ref/SomeType<List<T>, U> from collection", "collection")]
    [InlineData("SomeType<T> from collection", "collection")]
    public void ParsesAndWritesTypeOrigin(string typeText, string expectedOrigin)
    {
        var (root, diagnostics) = Parse($"func F(value: {typeText})");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var type = Assert.IsType<TypeSemanticsKoto>(function.Parameters.Single().Type);
        Assert.Equal(expectedOrigin, type.OriginName);
        Assert.Equal(typeText, type.ToString());
    }

    [Fact]
    public void ParsesHierarchicalTypeConstraints()
    {
        var source = """
            public open struct A<s/T>
                Self is StructB and InterfaceA
                semantics is not valueborrow and (owning or objectborrow)
                s is reference
                T is Comparable and (Equatable or Serializable)

                var x: i32
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));

        var genericArgument = Assert.Single(type.GenericArguments);
        Assert.Equal("s", genericArgument.SemanticsParameter);
        Assert.Equal("T", genericArgument.Identifier);

        Assert.Equal(4, type.TypeConstraints.Count);

        var selfConstraint = type.TypeConstraints[0];
        Assert.Equal("Self", Assert.IsType<IdentifierNameKoto>(selfConstraint.Left).IdentifierName);
        var selfTypes = Assert.IsType<AndKoto>(selfConstraint.Right);
        Assert.Equal("StructB", Assert.IsType<IdentifierNameKoto>(selfTypes.Left).IdentifierName);
        Assert.Equal("InterfaceA", Assert.IsType<IdentifierNameKoto>(selfTypes.Right).IdentifierName);

        var semanticsConstraint = type.TypeConstraints[1];
        Assert.Equal("semantics", Assert.IsType<IdentifierNameKoto>(semanticsConstraint.Left).IdentifierName);
        var negation = Assert.IsType<NotKoto>(semanticsConstraint.Right);
        var semanticsAnd = Assert.IsType<AndKoto>(negation.Operand);
        Assert.Equal(SemanticsMask.ValueBorrow, Assert.IsType<SemanticsMaskKoto>(semanticsAnd.Left).Mask);
        var parentheses = Assert.IsType<ParenthesizedKoto>(semanticsAnd.Right);
        var semanticsOr = Assert.IsType<OrKoto>(parentheses.Operand);
        Assert.Equal(SemanticsMask.Owning, Assert.IsType<SemanticsMaskKoto>(semanticsOr.Left).Mask);
        Assert.Equal(SemanticsMask.ObjectBorrow, Assert.IsType<SemanticsMaskKoto>(semanticsOr.Right).Mask);

        var semanticsParameterConstraint = type.TypeConstraints[2];
        Assert.Equal("s", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Left).IdentifierName);
        Assert.Equal("reference", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Right).IdentifierName);

        var typeParameterConstraint = type.TypeConstraints[3];
        Assert.Equal("T", Assert.IsType<IdentifierNameKoto>(typeParameterConstraint.Left).IdentifierName);
        var typeAnd = Assert.IsType<AndKoto>(typeParameterConstraint.Right);
        Assert.IsType<IdentifierNameKoto>(typeAnd.Left);
        Assert.IsType<ParenthesizedKoto>(typeAnd.Right);

        Assert.IsType<PropertyKoto>(GetChildren(type).Single());

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("public open struct A<s/T>", text);
            Assert.Contains("Self is StructB and InterfaceA", text);
            Assert.Contains("semantics is not valueborrow and (owning or objectborrow)", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ParsesOriginDeclarationForSemanticsGenericArgument()
    {
        var source = """
            public open struct TestStruct<s/C> origin a, b
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("TestStruct", TokenKind.Struct, default, default));

        var genericArgument = Assert.Single(type.GenericArguments);
        Assert.Equal("s", genericArgument.SemanticsParameter);
        Assert.Equal("C", genericArgument.Identifier);

        Assert.Equal(["a", "b"], type.Origins);
        Assert.Empty(type.TypeConstraints);

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("public open struct TestStruct<s/C> origin a, b", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void DiagnosesAndIgnoresTypeConstraintsFromLaterStructDefinitions()
    {
        var source = """
            struct A<s/T> origin first, shared
                T is FirstConstraint
                var first: i32

            struct A origin ignored, later
                T is IgnoredConstraint
                semantics is DefinitelyInvalid
                var second: i32
            """;

        var (root, diagnostics) = Parse(source);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(
            diagnostics,
            diagnostic => Assert.Equal(
                nameof(DiagnosticCode.DuplicateTypeConstraintDefinition_Kd),
                diagnostic.Entry.Name));
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.Equal(["first", "shared"], type.Origins);
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal("T", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        Assert.Equal("FirstConstraint", Assert.IsType<IdentifierNameKoto>(constraint.Right).IdentifierName);
        Assert.Equal(2, GetChildren(type).OfType<PropertyKoto>().Count());

        var builder = default(IndentedStringBuilder);
        try
        {
            root.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("struct A<s/T> origin first, shared", text);
            Assert.Contains("T is FirstConstraint", text);
            Assert.DoesNotContain("ignored", text);
            Assert.DoesNotContain("IgnoredConstraint", text);
            Assert.DoesNotContain("DefinitelyInvalid", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void PreservesKotoSyntaxThroughTinyhandSerialization()
    {
        var source = """
            public open struct TestStruct<s/C> origin a, b
                C is Comparable

                #Example
                var item: obj/Container<C> = value
                var converted = item@unsafe/C
                var called = transform(item, "text")

                private func map<s/T>(value?: ref/T, fallback: owner/T = defaultValue) -> uniq/T
                    return
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        var expectedBuilder = default(IndentedStringBuilder);
        var actualBuilder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref expectedBuilder);
            var serialized = TinyhandSerializer.Serialize(kotonoha);
            var deserialized = new Kotonoha(compilation);
            TinyhandSerializer.DeserializeObject(serialized, ref deserialized);
            var restored = deserialized ?? throw new InvalidOperationException();
            restored.OnDeserialized(compilation);
            restored.RootKoto.UnparseAll(ref actualBuilder);

            Assert.Equal(expectedBuilder.ToString(), actualBuilder.ToString());

            var type = Assert.IsType<StructKoto>(
                restored.RootKoto.GetOrAddGroup("TestStruct", TokenKind.Struct, default, default));
            Assert.Same(restored, type.Kotonoha);
            Assert.Single(type.GenericArguments);
            Assert.Equal(["a", "b"], type.Origins);
            Assert.Single(type.TypeConstraints);
            Assert.All(type.GenericArguments, argument => Assert.Same(type, argument.Parent));
            Assert.All(type.TypeConstraints, constraint => Assert.Same(type, constraint.Parent));

            var properties = GetChildren(type).OfType<PropertyKoto>().ToArray();
            Assert.All(properties, property => Assert.Same(type, property.Parent));
            var item = Assert.Single(properties, property => property.NameKoto.IdentifierName == "item");
            var attribute = Assert.IsType<AttributeKoto>(item.AttributeChain);
            Assert.Equal("Example", Assert.IsType<IdentifierNameKoto>(attribute.IdentifierKoto).IdentifierName);

            var function = Assert.IsType<FunctionKoto>(GetChildren(type).OfType<FunctionKoto>().Single());
            Assert.Single(function.GenericArguments);
            Assert.Equal(2, function.Parameters.Count);
            var returnType = Assert.IsAssignableFrom<Koto>(function.ReturnType);
            Assert.All(function.GenericArguments, argument => Assert.Same(function, argument.Parent));
            Assert.All(function.Parameters, parameter => Assert.Same(function, parameter.Type.Parent));
            Assert.Same(function, returnType.Parent);
        }
        finally
        {
            expectedBuilder.Dispose();
            actualBuilder.Dispose();
        }
    }

    [Fact]
    public void DiagnosesGroupDeclarationTrailingSyntaxOnce()
    {
        var source = """
            struct A: InterfaceA, InterfaceB
                Self is InterfaceA
            """;

        var (root, diagnostics) = Parse(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnexpectedTrailingToken_Kd), diagnostic.Entry.Name);

        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
    }

    [Fact]
    public void ParsesOrderedStructMembersWithoutOrderWarning()
    {
        var source = """
            struct Ordered
                Self is Interface

                var field: i32

                func Method()
                    return
            """;

        var (root, diagnostics) = Parse(source);

        Assert.DoesNotContain(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.DeclarationOrderWarning_Kd));
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("Ordered", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
        Assert.Collection(
            GetChildren(type),
            x => Assert.IsType<PropertyKoto>(x),
            x => Assert.IsType<FunctionKoto>(x));
    }

    [Fact]
    public void WarnsForOutOfOrderStructMembersButParsesThem()
    {
        var source = """
            struct Mixed
                func Method()
                    return

                var field: i32

                Self is Interface
            """;

        var (root, diagnostics) = Parse(source);

        var warnings = diagnostics
            .Where(x => x.Entry.Name == nameof(DiagnosticCode.DeclarationOrderWarning_Kd))
            .ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, x => Assert.Equal(DiagnosticSeverity.Warning, x.Entry.Severity));

        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("Mixed", TokenKind.Struct, default, default));
        Assert.Single(type.TypeConstraints);
        Assert.Collection(
            GetChildren(type),
            x => Assert.IsType<FunctionKoto>(x),
            x => Assert.IsType<PropertyKoto>(x));
    }

    [Fact]
    public void RejectsIdentifierExpressionInStructBody()
    {
        var source = """
            struct A
                Field1.Method2()
                var field: i32
            """;

        var (root, diagnostics) = Parse(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnexpectedToken_Kd), diagnostic.Entry.Name);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.IsType<PropertyKoto>(Assert.Single(GetChildren(type)));
    }

    [Theory]
    [InlineData("owner", SemanticsMask.Owner)]
    [InlineData("ref", SemanticsMask.Ref)]
    [InlineData("uniq", SemanticsMask.Uniq)]
    [InlineData("obj", SemanticsMask.Obj)]
    [InlineData("rc", SemanticsMask.Rc)]
    [InlineData("arc", SemanticsMask.Arc)]
    [InlineData("objref", SemanticsMask.ObjRef)]
    [InlineData("objuniq", SemanticsMask.ObjUniq)]
    [InlineData("unsafe", SemanticsMask.Unsafe)]
    [InlineData("valueborrow", SemanticsMask.ValueBorrow)]
    [InlineData("object", SemanticsMask.Object)]
    [InlineData("objectborrow", SemanticsMask.ObjectBorrow)]
    [InlineData("borrow", SemanticsMask.Borrow)]
    [InlineData("owning", SemanticsMask.Owning)]
    [InlineData("value", SemanticsMask.Value)]
    [InlineData("reference", SemanticsMask.Reference)]
    public void ParsesNamedSemanticsConstraints(string text, SemanticsMask expected)
    {
        var (root, diagnostics) = Parse($"struct A\n    semantics is {text}");

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal(expected, Assert.IsType<SemanticsMaskKoto>(constraint.Right).Mask);
    }

    [Fact]
    public void DiagnosesInvalidSemanticsConstraint()
    {
        var (root, diagnostics) = Parse("struct A\n    semantics is Comparable");

        Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.InvalidSemanticsConstraint_Kd));
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.IsType<ErrorKoto>(constraint.Right);
    }

    [Fact]
    public void RemovesAttributeBeyondChainHead()
    {
        var (root, diagnostics) = Parse("#A\n#B\nvar x = 1");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var head = Assert.IsType<AttributeKoto>(field.AttributeChain);
        var tail = Assert.IsType<AttributeKoto>(head.AttributeChain);

        Assert.True(field.RemoveAttribute(tail));
        Assert.Null(head.AttributeChain);
        Assert.Null(tail.Parent);
        Assert.Null(tail.AttributeChain);
        Assert.False(field.RemoveAttribute(tail));
    }

    [Fact]
    public void ParsesInvocationWithTrailingComma()
    {
        const string Source = "var result = call(value,)";

        var (root, diagnostics) = Parse(Source);

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var invocation = Assert.IsType<InvocationKoto>(field.InitializerKoto);
        Assert.Single(invocation.Arguments);
        Assert.Equal("call(value,)", Source.AsSpan(invocation.Span.Start, invocation.Span.Length).ToString());
        Assert.Same(field, field.NameKoto.Parent);
        Assert.Same(field, invocation.Parent);
        Assert.Same(invocation, invocation.Method.Parent);
        Assert.Same(invocation, invocation.Arguments[0].Parent);
    }

    [Fact]
    public void ParsesLabeledAndAttributedInvocationArguments()
    {
        const string Source = "var y = array.remove(at: 1, #Attribute(2) \"One\")";
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);

        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        AssertInvocation(kotonoha);

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var deserialized = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref deserialized);
        var restored = deserialized ?? throw new InvalidOperationException();
        restored.OnDeserialized(compilation);
        AssertInvocation(restored);

        var builder = default(IndentedStringBuilder);
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            Assert.Contains(Source, builder.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            builder.Dispose();
        }

        static void AssertInvocation(Kotonoha kotonoha)
        {
            var field = Assert.IsType<FieldKoto>(GetChildren(kotonoha.RootKoto).Single());
            var invocation = Assert.IsType<InvocationKoto>(field.InitializerKoto);
            Assert.IsType<MemberAccessKoto>(invocation.Method);
            Assert.Collection(
                invocation.ArgumentLabels,
                label => Assert.Equal("at", label),
                label => Assert.Null(label));
            Assert.IsType<NumberLiteralKoto>(invocation.Arguments[0]);

            var text = Assert.IsType<StringLiteralKoto>(invocation.Arguments[1]);
            Assert.Equal("One", text.Literal);
            var attribute = Assert.IsType<AttributeKoto>(text.AttributeChain);
            Assert.Equal("Attribute", Assert.IsType<IdentifierNameKoto>(attribute.IdentifierKoto).IdentifierName);
            Assert.Single(attribute.Arguments);
            Assert.IsType<NumberLiteralKoto>(attribute.Arguments[0]);
            Assert.Same(text, attribute.Parent);
        }
    }

    [Fact]
    public void ExpressionSpansCoverCompleteSyntax()
    {
        const string Source = "var result = -a + target.method(value)";

        var (root, diagnostics) = Parse(Source);

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        Assert.Equal(Source, Source.AsSpan(field.Span.Start, field.Span.Length).ToString());

        var addition = Assert.IsType<PlusKoto>(field.InitializerKoto);
        Assert.Equal("-a + target.method(value)", Source.AsSpan(addition.Span.Start, addition.Span.Length).ToString());
        var prefix = Assert.IsType<PrefixMinusKoto>(addition.Left);
        Assert.Equal("-a", Source.AsSpan(prefix.Span.Start, prefix.Span.Length).ToString());
        var invocation = Assert.IsType<InvocationKoto>(addition.Right);
        Assert.Equal("target.method(value)", Source.AsSpan(invocation.Span.Start, invocation.Span.Length).ToString());
        var member = Assert.IsType<MemberAccessKoto>(invocation.Method);
        Assert.Equal("target.method", Source.AsSpan(member.Span.Start, member.Span.Length).ToString());
    }

    [Fact]
    public void ParsesChainedAttributePostfixExpressions()
    {
        var (root, diagnostics) = Parse("#Example<T>(value)\nvar result = 0");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var attribute = Assert.IsType<AttributeKoto>(field.AttributeChain);
        var invocation = Assert.IsType<InvocationKoto>(attribute.Operand);
        var generic = Assert.IsType<GenericsKoto>(invocation.Method);
        Assert.Single(generic.TypeArguments);
        Assert.Same(attribute, invocation.Parent);
        Assert.Same(invocation, generic.Parent);
        Assert.Same(generic, generic.Identifier!.Parent);
        Assert.Same(generic, generic.TypeArguments[0].Parent);
    }

    [Fact]
    public void CompileTimeStringEqualityIsOrdinal()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, "var result = \"Kimigayo\" == \"kimigayo\"");

        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());
        var field = Assert.IsType<FieldKoto>(GetChildren(kotonoha.RootKoto).Single());
        var value = BasicValueHelper.Evaluate(compilation, field.InitializerKoto!);
        Assert.Equal(BasicValueKind.Bool, value.Kind);
        Assert.False(value.Bool);
    }

    [Fact]
    public void FloatingPointLiteralKeepsItsNumericCategoryWhenWritten()
    {
        var (root, diagnostics) = Parse("var result = 1.0");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FieldKoto>(GetChildren(root).Single());
        var literal = Assert.IsType<NumberLiteralKoto>(field.InitializerKoto);
        Assert.Equal("1.0", literal.ToString());
    }

    [Fact]
    public void DeserializesFullDocumentChangeWithoutRange()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var change = JsonSerializer.Deserialize<TextDocumentContentChangeEvent>("{\"text\":\"replacement\"}", options);

        Assert.NotNull(change);
        Assert.Null(change.Range);
        Assert.Equal("replacement", change.Text);
    }

    private static (GroupKoto Root, Diagnostic[] Diagnostics) Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, source);
        return (kotonoha.RootKoto, kotonoha.DiagnosticCollection.GetArray());
    }

    private static List<Koto> GetChildren(DeclarationContainerKoto group)
        => ReferenceEquals(group, group.Kotonoha.RootKoto)
            ? group.Kotonoha.GeneratedFunction?.Body?.Items.ToList() ?? []
            : (List<Koto>)KotoListProperty.GetValue(group)!;
}

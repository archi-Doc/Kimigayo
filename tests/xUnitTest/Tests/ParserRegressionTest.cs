// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Tinyhand;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class ParserRegressionTest
{
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
    public void KeepsLogicalExpressionsOutsideRuntimeIs()
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

        var first = Assert.IsType<AndKoto>(fields[0].InitializerKoto);
        Assert.IsType<IsKoto>(first.Left);

        var second = Assert.IsType<OrKoto>(fields[1].InitializerKoto);
        Assert.True(Assert.IsType<IsKoto>(second.Left).IsNegated);
        Assert.IsType<IdentifierNameKoto>(Assert.IsType<IsKoto>(second.Left).Right);

        var third = Assert.IsType<AndKoto>(fields[2].InitializerKoto);
        Assert.IsType<IsKoto>(third.Left);
        Assert.IsType<NotKoto>(third.Right);

        var fourth = Assert.IsType<OrKoto>(fields[3].InitializerKoto);
        Assert.IsType<IsKoto>(Assert.IsType<AndKoto>(fourth.Right).Left);
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

        var diagnostics = TestDiagnostics.Of(kotonoha);
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

        Assert.Empty(TestDiagnostics.Of(kotonoha));
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
                s is reference
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
        Assert.Equal("s", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        Assert.Equal("reference", Assert.IsType<IdentifierNameKoto>(constraint.Right).IdentifierName);
    }

    [Fact]
    public void AppliesSemanticsToCompoundType()
    {
        var (root, diagnostics) = Parse("func F(value: objref/SomeType<List<owner/T>, I>) => ()");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var semantics = Assert.IsType<TypeSemanticsKoto>(function.Parameters.Single().Type);
        Assert.Equal(SemanticsKind.ObjRef, semantics.SemanticsKind);
        Assert.IsType<GenericsKoto>(semantics.Type);
        Assert.Equal("objref/SomeType<List<owner/T>, I>", semantics.ToString());
    }

    [Theory]
    [InlineData("Dog{owner}", "owner")]
    [InlineData("ref/Dog during source", "source")]
    [InlineData("ref/SomeType<List<T>, U> during collection", "collection")]
    [InlineData("SomeType<T>{collection}", "collection")]
    public void ParsesAndWritesTypeOrigin(string typeText, string expectedOrigin)
    {
        var (root, diagnostics) = Parse($"func F(value: {typeText}) => ()");

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

        Assert.Equal(3, type.TypeConstraints.Count);

        var selfConstraint = type.TypeConstraints[0];
        Assert.Equal("Self", Assert.IsType<IdentifierNameKoto>(selfConstraint.Left).IdentifierName);
        var selfTypes = Assert.IsType<AndKoto>(selfConstraint.Right);
        Assert.Equal("StructB", Assert.IsType<IdentifierNameKoto>(selfTypes.Left).IdentifierName);
        Assert.Equal("InterfaceA", Assert.IsType<IdentifierNameKoto>(selfTypes.Right).IdentifierName);

        var semanticsParameterConstraint = type.TypeConstraints[1];
        Assert.Equal("s", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Left).IdentifierName);
        Assert.Equal("reference", Assert.IsType<IdentifierNameKoto>(semanticsParameterConstraint.Right).IdentifierName);

        var typeParameterConstraint = type.TypeConstraints[2];
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
            Assert.Contains("s is reference", text);
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
            public open struct TestStruct<s/C> {a, b}
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
            Assert.Contains("public open struct TestStruct<s/C> {a, b}", text);
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
            struct A<s/T> {first, shared}
                T is FirstConstraint
                var first: i32

            struct A<s/T> {ignored, later}
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
                diagnostic.Code));
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
            Assert.Contains("struct A<s/T> {first, shared}", text);
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
    public void RebuildsKotoSyntaxFromSerializedSources()
    {
        var source = """
            public open struct TestStruct<s/C> {a, b}
                C is Comparable

                #Example
                var item: obj/Container<C> = value
                var converted = item@raw/C
                var called = transform(item, "text")

                private func map<s/T>(value: ref/T = defaultValue, fallback: owner/T = defaultValue) -> uniq/T
                    return
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, source);
        Assert.Empty(TestDiagnostics.Of(kotonoha));

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
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), diagnostic.Code);

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

        Assert.DoesNotContain(diagnostics, x => x.Code == nameof(DiagnosticCode.DeclarationOrderWarning_Kd));
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
            .Where(x => x.Code == nameof(DiagnosticCode.DeclarationOrderWarning_Kd))
            .ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, x => Assert.Equal(DiagnosticSeverity.Warning, x.Severity));

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
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), diagnostic.Code);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        Assert.IsType<PropertyKoto>(Assert.Single(GetChildren(type)));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("object")]
    [InlineData("reference")]
    public void ParsesNamedSemanticsRequirementsAsOrdinaryNames(string text)
    {
        // SPEC 8.2: there is no special constraint subject; `s is <category>` keeps its operands for Binding.
        var (root, diagnostics) = Parse($"struct A<s/T>\n    s is {text}");

        Assert.Empty(diagnostics);
        var type = Assert.IsType<StructKoto>(root.GetOrAddGroup("A", TokenKind.Struct, default, default));
        var constraint = Assert.Single(type.TypeConstraints);
        Assert.Equal("s", Assert.IsType<IdentifierNameKoto>(constraint.Left).IdentifierName);
        Assert.Equal(text, Assert.IsType<IdentifierNameKoto>(constraint.Right).IdentifierName);
    }

    [Fact]
    public void RemovesAttributeBeyondChainHead()
    {
        var (root, diagnostics) = Parse("#A\n#B\nfunc x() => 1");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
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
    public void DiagnosesAndRecoversLabeledAndAttributedInvocationArguments()
    {
        const string Source = "var y = array.remove(at: 1, #Attribute(2) \"One\")";
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);

        Assert.NotEmpty(TestDiagnostics.Of(kotonoha));
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

    // A block, a match arm list, a property accessor block and a conditional conformance body end at their last written token:
    // the blank and comment lines and the indentation before the dedented line belong to none of them (DIAGNOSTICS.md §4.4).
    [Fact]
    public void BodiesEndAtTheirLastWrittenToken()
    {
        const string Source = "struct S<T>\n    Self is C when T is Copy\n        public func f() => ()\n\n    // after the conformance\n" +
            "    public computed w: i32\n        get(self: ref/Self) -> i32\n            return 1\n\n    // after the accessor block\n" +
            "    public var v: i32 = 0\nfunc g(a: i32) -> i32\n    let k = match a\n        1 => 10\n        _ => 20\n\n    // after the match\n" +
            "    let h = func () -> i32\n        return 2\n\n    // after the closure\n    return k\n\n// after the function\n\n";

        var (root, diagnostics) = Parse(Source);

        Assert.Empty(diagnostics);
        var nodes = KotoTree.Walk(root).ToArray();
        Assert.Equal("Self is C when T is Copy\n        public func f() => ()", Text(nodes.OfType<SyntaxFormKoto>().Single(x => x.Akind == KotoKind.ConditionalConformance && x.Parent is DeclarationContainerKoto)));
        Assert.EndsWith("i32\n        get(self: ref/Self) -> i32\n            return 1", Text(nodes.OfType<PropertyKoto>().Single(x => x.NameKoto.IdentifierName == "w")));
        Assert.Equal("match a\n        1 => 10\n        _ => 20", Text(nodes.OfType<MatchKoto>().Single()));
        Assert.Equal("func () -> i32\n        return 2", Text(nodes.OfType<FunctionKoto>().Single(x => x.IsAnonymous)));
        var function = nodes.OfType<FunctionKoto>().Single(x => x.Name == "g");
        Assert.EndsWith("    return k", Text(function));
        Assert.StartsWith("let k = match a", Text(function.Body!));
        Assert.EndsWith("    return k", Text(function.Body!));

        static string Text(Koto node) => Source.AsSpan(node.Span.Start, node.Span.Length).ToString();
    }

    [Fact]
    public void DiagnosesAndRecoversChainedAttributePostfixExpressions()
    {
        // A malformed attribute is consumed with its chained postfix syntax and attaches to nothing; the declaration after it is checked on its own.
        var (root, diagnostics) = Parse("#Example<T>(value)\nfunc run() => ()");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), diagnostic.Code);
        Assert.Equal("<", diagnostic.Text);
        var function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        Assert.Null(function.AttributeChain);

        // A well-formed attribute keeps its operand tree with every parent link.
        (root, diagnostics) = Parse("#Example(value)\nfunc run() => ()");
        Assert.Empty(diagnostics);
        function = Assert.IsType<FunctionKoto>(GetChildren(root).Single());
        var attribute = Assert.IsType<AttributeKoto>(function.AttributeChain);
        var invocation = Assert.IsType<InvocationKoto>(attribute.Operand);
        Assert.Same(attribute, invocation.Parent);
        Assert.Same(invocation, invocation.Method.Parent);
    }

    [Fact]
    public void CompileTimeStringEqualityIsOrdinal()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();
        context.Parse(kotonoha.RootKoto, "var result = \"Kimigayo\" == \"kimigayo\"");

        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var field = Assert.IsType<FieldKoto>(GetChildren(kotonoha.RootKoto).Single());
        Assert.Equal(CompileTimeConditionResult.False, CompileTimeConditionEvaluator.Evaluate(compilation, field.InitializerKoto!));
    }

    [Fact]
    public void EarlyCompileTimeIfSelectsKnownTargetConditions()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;
        var source = """
            #if windows
            var byOsFlag = 1
            #if windows or linux
            var byCombinedFlags = 2
            #if os == "windows" or os == "linux"
            var byOsName = 3
            #if windows and pointerWidth == 64
            var byPointerWidth = 4
            #if linux
            var excluded = 5
            #if debug
            var debugOnly = 6
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var names = GetChildren(kotonoha.RootKoto)
            .OfType<FieldKoto>()
            .Select(x => x.NameKoto.IdentifierName)
            .ToArray();
        Assert.Equal(["byOsFlag", "byCombinedFlags", "byOsName", "byPointerWidth"], names);
    }

    [Fact]
    public void DebugCompileTimeVariableComesFromBuildOptions()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.KimiOptions.Debug = true;
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#if debug\nvar debugOnly = 1");

        Assert.Empty(TestDiagnostics.Of(kotonoha));
        Assert.Equal("debugOnly", Assert.IsType<FieldKoto>(Assert.Single(GetChildren(kotonoha.RootKoto))).NameKoto.IdentifierName);
    }

    [Fact]
    public void UnknownCompileTimeNameIsAnImmediateError()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, "#if genericCondition\nvar specialized = 1");
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(TestDiagnostics.Of(kotonoha)).Code);
        Assert.Empty(GetChildren(kotonoha.RootKoto));
    }

    [Fact]
    public void PascalCaseIfRemainsAnOrdinaryAttribute()
    {
        var (root, diagnostics) = Parse("#If(false)\nfunc attributed() => 1");

        Assert.Empty(diagnostics);
        var field = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(root)));
        var attribute = Assert.IsType<AttributeKoto>(field.AttributeChain);
        Assert.Equal("If", Assert.IsType<IdentifierNameKoto>(attribute.IdentifierKoto).IdentifierName);
        Assert.Single(attribute.Arguments);
    }

    [Fact]
    public void EarlyFalseCompileTimeIfParsesItsTargetAsExcludedSyntax()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;
        var source = """
            #if linux
            var incomplete =
            var retained = 1
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        // The excluded target is parsed with the ordinary grammar but contributes nothing (SPEC 19.5).
        Assert.Equal(nameof(DiagnosticCode.MissingSyntax_Kd), Assert.Single(TestDiagnostics.Of(kotonoha)).Code);
        Assert.Equal("retained", Assert.IsType<FieldKoto>(Assert.Single(GetChildren(kotonoha.RootKoto))).NameKoto.IdentifierName);
    }

    [Fact]
    public void CompileTimeIfSelectsDeclarationContainerMembers()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;
        var source = """
            struct TargetSpecific
                #if windows
                var retained: i32
                #if linux
                var excluded: i32
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.Empty(TestDiagnostics.Of(kotonoha));
        var structure = Assert.Single(kotonoha.RootKoto.NestedDeclarationContainers);
        Assert.Equal("retained", Assert.IsType<PropertyKoto>(Assert.Single(structure.Members)).NameKoto.IdentifierName);
    }

    [Fact]
    public void CompileTimeCaseRejectsTypeSelectionIncludingAfterSerialization()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;
        var source = """
            func select<s/T>()
                #switch
                    #case T is i32
                        var specialized = 1
                    #case _
                        var fallback = 2
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.Contains(TestDiagnostics.Of(kotonoha), x => x.Code == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        restored!.OnDeserialized(compilation);
        Assert.Contains(TestDiagnostics.Of(restored), x => x.Code == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
        var restoredFunction = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(restored.RootKoto)));
        var restoredGroup = Assert.IsType<CompileTimeSwitchKoto>(Assert.Single(restoredFunction.Body!.Items));
        Assert.Empty(restoredGroup.ChildNodes);
    }

    [Fact]
    public void CompileTimeCaseDiagnosesInvalidFallbackAndExhaustiveness()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var kotonoha = compilation.Kotonoha;
        var source = """
            func invalidFallback()
                #switch
                    #case _
                        return
                    #case _
                        return

            func nonExhaustive()
                #switch
                    #case linux
                        return
            """;

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        var names = TestDiagnostics.Of(kotonoha).Select(x => x.Code).ToArray();
        Assert.Contains(nameof(DiagnosticCode.CompileTimeCaseFallbackMustBeLast_Kd), names);
        Assert.Contains(nameof(DiagnosticCode.DuplicateCompileTimeCaseFallback_Kd), names);
        Assert.Contains(nameof(DiagnosticCode.NonExhaustiveCompileTimeCase_Kd), names);
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

    // SPEC 7.4: every function, constructor, destructor and accessor body begins with its Constraint prefix. A subject the
    // declaration does not permit is diagnosed, never read as a runtime test; parentheses make the test executable.
    [Theory]
    [InlineData("func f(value: i32)\n    value is Dog\n    ()", 1)]
    [InlineData("func f(value: i32)\n    (value is Dog)\n    ()", 0)]
    [InlineData("func f(value: i32)\n    require value is Dog else => return\n    ()", 0)]
    [InlineData("func f<T>(value: T)\n    T is Copy\n    ()", 0)]
    [InlineData("struct Box<T>\n    var item: T\n    public init(item: T)\n        T is Copy\n        self.item = item\n", 0)]
    [InlineData("struct Box<T>\n    var item: T\n    drop\n        T is Copy\n        ()\n", 1)]
    [InlineData("struct Box<T>\n    var item: T\n    public computed first: i32\n        get() -> i32\n            T is Copy\n            return 0\n", 1)]
    [InlineData("struct Plain\n    var item: i32 = 0\n    public func f(self)\n        item is i32\n        ()\n", 1)]
    public void EveryBodyBeginsWithItsConstraintPrefix(string source, int unexpected)
    {
        var (_, diagnostics) = Parse(source);
        Assert.True(unexpected == diagnostics.Length, string.Join("; ", diagnostics.Select(x => x.ToString())));
        Assert.Equal(unexpected, diagnostics.Count(x => x.Code == nameof(DiagnosticCode.MisplacedSyntax_Kd)));
    }

    // SPEC 8.4.3: a Contract-qualified projection names its Contract in parentheses, also in Constraint subjects and
    // requirement Types.
    [Theory]
    [InlineData("struct W<I>\n    I is LendingIterator\n    associate LendingIterator.LentItem(step) is I.(LendingIterator).LentItem(step)\n")]
    [InlineData("func f<T>(x: T)\n    T.(Iterator).Item is Copy\n    ()\n")]
    public void QualifiedProjectionsParseInConstraints(string source)
    {
        var (_, diagnostics) = Parse(source);
        Assert.Empty(diagnostics);
    }

    // A repeated modifier is named as written, whether it is an access modifier or a flag such as open.
    [Theory]
    [InlineData("open open struct S\n    let x: i32\n", "open")]
    [InlineData("public public struct S\n    let x: i32\n", "public")]
    public void DuplicateModifierIsNamedAsWritten(string source, string modifier)
    {
        var (_, diagnostics) = Parse(source);
        var duplicate = Assert.Single(diagnostics, x => x.Code == nameof(DiagnosticCode.DuplicateModifier_Kd));
        Assert.Contains($"'{modifier}'", duplicate.Message);
    }

    private static (GroupKoto Root, TestDiagnostic[] Diagnostics) Parse(string source)
    {
        var kotonoha = ParseTestHelper.Parse(source);
        return (kotonoha.RootKoto, TestDiagnostics.Of(kotonoha));
    }
}

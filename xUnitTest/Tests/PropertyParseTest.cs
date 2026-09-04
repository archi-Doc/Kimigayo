// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class PropertyParseTest
{
    [Fact]
    public void RecognizesAccessorWordsAsContextualKeywords()
    {
        AssertContextualKeyword(TokenKind.Get, Constants.GetKeyword);
        AssertContextualKeyword(TokenKind.Set, Constants.SetKeyword);
        AssertContextualKeyword(TokenKind.Has, Constants.HasKeyword);

        static void AssertContextualKeyword(TokenKind kind, string text)
        {
            Assert.False(kind.IsKeyword());
            Assert.True(kind.IsIdentifierOrContextualKeyword());
            Assert.Equal(text, kind.ToText());
        }
    }

    [Fact]
    public void ParsesStructMembersAsPropertiesAndAllowsContextualNames()
    {
        var source = """
            struct Keywords
                let get: i32 = 1
                var set: i32
                var has: i32 has get
            """;

        var (_, structure, diagnostics) = ParseStruct(source);

        Assert.Empty(diagnostics);
        var properties = structure.Members.Select(Assert.IsType<PropertyKoto>).ToArray();
        Assert.Equal(["get", "set", "has"], properties.Select(x => x.NameKoto.IdentifierName));
        Assert.Equal(VariableKind.Let, properties[0].VariableKind);
        Assert.Equal(VariableKind.Var, properties[1].VariableKind);
        Assert.IsType<NumberLiteralKoto>(properties[0].InitializerKoto);
        Assert.Empty(properties[0].Accessors);
        Assert.Empty(properties[1].Accessors);
        Assert.True(properties[2].HasInlineAccessors);
        Assert.Equal(PropertyAccessorKind.Get, Assert.Single(properties[2].Accessors).AccessorKind);
    }

    [Fact]
    public void ParsesInlineAccessorsAfterAnInitializer()
    {
        var source = """
            struct Counter
                public var Count: i32 = 0 has get, private set
            """;

        var (_, structure, diagnostics) = ParseStruct(source);

        Assert.Empty(diagnostics);
        var property = Assert.IsType<PropertyKoto>(Assert.Single(structure.Members));
        Assert.Equal(ModifierKind.Public, property.Modifier);
        Assert.True(property.HasInlineAccessors);
        Assert.IsType<NumberLiteralKoto>(property.InitializerKoto);
        Assert.Collection(
            property.Accessors,
            accessor =>
            {
                Assert.Equal(PropertyAccessorKind.Get, accessor.AccessorKind);
                Assert.Equal(ModifierKind.NoModifier, accessor.Modifier);
                Assert.True(accessor.IsBodyless);
            },
            accessor =>
            {
                Assert.Equal(PropertyAccessorKind.Set, accessor.AccessorKind);
                Assert.Equal(ModifierKind.Private, accessor.Modifier);
                Assert.True(accessor.IsBodyless);
            });
        Assert.All(property.Accessors, accessor => Assert.Same(property, accessor.Parent));
    }

    [Fact]
    public void ParsesExpressionBlockAndBodylessAccessors()
    {
        var source = """
            struct Dimensions
                var Area: i32
                    get => width * height

                var Percentage: i32 = 0
                    get => storage

                    private set
                        storage = clamp(value, 0, 100)

                var Count: i32
                    get
                    private set
            """;

        var (_, structure, diagnostics) = ParseStruct(source);

        Assert.Empty(diagnostics);
        var properties = structure.Members.Select(Assert.IsType<PropertyKoto>).ToArray();
        Assert.Equal(3, properties.Length);

        var areaGetter = Assert.Single(properties[0].Accessors);
        Assert.Equal(PropertyAccessorKind.Get, areaGetter.AccessorKind);
        Assert.IsType<AsteriskKoto>(areaGetter.Body);

        Assert.Collection(
            properties[1].Accessors,
            getter => Assert.IsType<IdentifierNameKoto>(getter.Body),
            setter =>
            {
                Assert.Equal(ModifierKind.Private, setter.Modifier);
                var body = Assert.IsType<CodeBlockKoto>(setter.Body);
                Assert.IsType<EqualsKoto>(Assert.Single(body.Items));
            });

        Assert.False(properties[2].HasInlineAccessors);
        Assert.Collection(
            properties[2].Accessors,
            getter =>
            {
                Assert.Equal(PropertyAccessorKind.Get, getter.AccessorKind);
                Assert.True(getter.IsBodyless);
            },
            setter =>
            {
                Assert.Equal(PropertyAccessorKind.Set, setter.AccessorKind);
                Assert.Equal(ModifierKind.Private, setter.Modifier);
                Assert.True(setter.IsBodyless);
            });
    }

    [Fact]
    public void DiagnosesInvalidAccessorListsAndRecovers()
    {
        var source = """
            struct Invalid
                let Frozen: i32 has get, set
                var Duplicate: i32 has get, get
                var Mixed: i32 has get
                    set
                var Continued: i32
            """;

        var (_, structure, diagnostics) = ParseStruct(source);

        Assert.Equal(3, diagnostics.Length);
        Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.LetPropertyCannotHaveSetter_Kd));
        Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.DuplicatePropertyAccessor_Kd));
        Assert.Contains(diagnostics, x => x.Entry.Name == nameof(DiagnosticCode.UnexpectedToken_Kd));
        Assert.Equal(
            ["Frozen", "Duplicate", "Mixed", "Continued"],
            structure.Members.Cast<PropertyKoto>().Select(x => x.NameKoto.IdentifierName));
    }

    [Fact]
    public void RoundTripsAndSerializesPropertyAccessors()
    {
        var source = """
            struct Counter
                public var Count: i32 = 0 has get, private set
                var Clamped: i32
                    get => storage
                    private set
                        storage = value
            """;
        var (kotonoha, _, diagnostics) = ParseStruct(source);
        Assert.Empty(diagnostics);

        var expected = Unparse(kotonoha.RootKoto);
        var bytes = TinyhandSerializer.Serialize(kotonoha);
        Kotonoha? restored = new Kotonoha(kotonoha.Compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        var restoredKotonoha = restored ?? throw new InvalidOperationException();
        restoredKotonoha.OnDeserialized(kotonoha.Compilation);
        var actual = Unparse(restoredKotonoha.RootKoto);

        Assert.Equal(expected, actual);
        Assert.Contains("public var Count: i32 = 0 has get, private set", actual);
        var structure = Assert.IsType<StructKoto>(Assert.Single(restoredKotonoha.RootKoto.NestedDeclarationContainers));
        var properties = structure.Members.Select(Assert.IsType<PropertyKoto>).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.All(properties, property => Assert.Same(structure, property.Parent));
        Assert.All(properties.SelectMany(x => x.Accessors), accessor => Assert.Same(accessor.Parent, properties.Single(x => x.Accessors.Contains(accessor))));

        var reparsed = ParseStruct(actual);
        Assert.True(
            reparsed.Diagnostics.Length == 0,
            $"{actual}{Environment.NewLine}{string.Join(Environment.NewLine, reparsed.Diagnostics.Select(x => x.ToString()))}");
    }

    private static (Kotonoha Kotonoha, StructKoto Structure, Diagnostic[] Diagnostics) ParseStruct(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        var structure = Assert.IsType<StructKoto>(Assert.Single(kotonoha.RootKoto.NestedDeclarationContainers));
        return (kotonoha, structure, kotonoha.DiagnosticCollection.GetArray());
    }

    private static string Unparse(GroupKoto root)
    {
        var builder = new IndentedStringBuilder();
        try
        {
            root.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}

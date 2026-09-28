// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class PropertyRevisionParseTest
{
    [Theory]
    [InlineData("computed", TokenKind.Computed)]
    [InlineData("property", TokenKind.Property)]
    [InlineData("move", TokenKind.Identifier)]
    [InlineData("storage", TokenKind.Identifier)]
    public void RecognizesCompleteContextualWords(string name, TokenKind kind)
    {
        Assert.Equal(kind, TokenHelper.GetKeywordOrIdentifierKind(name));
        Assert.True(kind.IsIdentifierOrContextualKeyword());
        Assert.Equal(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind(name + "Suffix"));
        ParseSuccess($"let {name}: i32 = 1\nlet result = {name}");
    }

    // SPEC 13.5.1: a bare @move (like a bare built-in Semantics name) completes the target, and a following
    // selection, call or index continues the postfix chain; an ordinary Type named move keeps its qualified forms.
    [Theory]
    [InlineData("move", "move")]
    [InlineData("move<i32>", "move<i32>")]
    [InlineData("Group.move", "Group.move")]
    public void MoveIsTheTransferOperationTarget(string target, string written)
    {
        var tree = ParseSuccess("let value = source@" + target);
        var conversion = Assert.IsType<ConversionKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        Assert.Equal(written, conversion.Right.ToString());
        Assert.Equal("source", conversion.Left.ToString());
        RoundTrip(tree);
    }

    [Fact]
    public void MoveTargetEndsBeforeAPostfixSelection()
    {
        var tree = ParseSuccess("let value = source@move.Member");
        var access = Assert.IsType<MemberAccessKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        var conversion = Assert.IsType<ConversionKoto>(access.Left);
        Assert.Equal("move", conversion.Right.ToString());
        Assert.Equal("Member", access.Right.ToString());
        RoundTrip(tree);
    }

    [Fact]
    public void MoveIsNotASemanticsPrefix()
    {
        var tree = Parse("let value = source@move/i32\nlet after = 1");
        Assert.Contains(tree.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.UnexpectedToken_Kd));
        Assert.Equal("after", Assert.IsType<FieldKoto>(tree.GeneratedFunction!.Body!.Items.Last()).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("var x@ref")]
    [InlineData("var x@uniq")]
    [InlineData("x@copy")]
    public void RejectsUnavailableCaptureOperations(string capture)
    {
        var tree = Parse($"let f = func[{capture}]() => ()\nlet after = 1");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        Assert.Equal("after", Assert.IsType<FieldKoto>(tree.GeneratedFunction!.Body!.Items.Last()).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("var x")]
    [InlineData("x@ref")]
    [InlineData("x@uniq")]
    [InlineData("x@move")]
    [InlineData("var x@move")]
    public void PreservesCurrentCaptureForms(string capture)
        => RoundTrip(ParseSuccess($"let f = func[{capture}]() => ()"));

    [Theory]
    [InlineData("struct S\n    let item: T\n        get")]
    [InlineData("struct S\n    #Marker\n    computed item: T\n        get(self: ref/Self) -> T => make()")]
    [InlineData("struct S\n    var item = source\n        private set")]
    [InlineData("struct S\n    public var item: T\n        private protected set\n        protected internal get")]
    [InlineData("struct S\n    var item: T\n        set(self: uniq/Self, value: T) -> () => storage = value")]
    [InlineData("struct S\n    let item: T\n        get(self: ref/Self) -> T => storage")]
    [InlineData("group G\n    var item: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value")]
    [InlineData("rootgroup G\n    computed item: T\n        get() -> T => make()")]
    [InlineData("struct S\n    computed item: T\n        set(self: uniq/Self, value: U) -> () => accept(value)\n        get(self: Self) -> T => self.source")]
    [InlineData("struct S {source}\n    var item: ref/T during source\n        get(self: ref/Self) -> ref/T during source => storage\n        set(self: uniq/Self, value: ref/T during source) -> () => storage = value")]
    [InlineData("contract C\n    property item: T has get")]
    [InlineData("contract C\n    property item: T has set, get")]
    [InlineData("contract C\n    property item: ref/T\n        get(self: ref/Self) -> ref/T during self\n        set(self: uniq/Self, value: T) -> ()")]
    [InlineData("struct S\n    #if true\n        computed item: T\n            get(self: ref/Self) -> T => make()")]
    [InlineData("contract C\n    #switch\n        #case true\n            property item: T\n                get(self: ref/Self) -> T")]
    public void PreservesDeclarationsAndSignatures(string source)
        => RoundTrip(ParseSuccess(source));

    [Theory]
    [InlineData("struct S\n    var item: i32\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value")]
    [InlineData("struct S\n    computed item: i32\n        get() -> i32 => self.measured")]
    [InlineData("contract C\n    property item: i32\n        get() -> i32\n        set(value: i32) -> ()")]
    [InlineData("struct S\n    func read(self) -> i32 => 1")]
    [InlineData("enum E\n    A\n    func read(self) -> i32 => 1")]
    [InlineData("contract C\n    func read(self) -> i32")]
    public void PreservesReceiverShorthandThroughWritingAndSerialization(string source)
        => RoundTrip(ParseSuccess(source));

    [Fact]
    public void RetainsPropertyKindsParameterTypesOriginsAndSourceSpans()
    {
        var source = """
            struct S
                var item: T
                computed view: ref/T
                    get(self: ref/Self) -> ref/T during self => self.item@ref
                    set(self: uniq/Self, value: Container<ref/T during source>) -> ()
                        use(value)
            """;
        var tree = ParseSuccess(source);
        var structure = Assert.Single(tree.RootKoto.NestedDeclarationContainers);
        var stored = Assert.IsType<PropertyKoto>(structure.Members.First());
        var computed = Assert.IsType<PropertyKoto>(structure.Members.Last());
        Assert.Equal(PropertyDeclarationKind.Var, stored.DeclarationKind);
        Assert.Equal(PropertyDeclarationKind.Computed, computed.DeclarationKind);
        var getter = computed.GetAccessor(PropertyAccessorKind.Get)!;
        var setter = computed.GetAccessor(PropertyAccessorKind.Set)!;
        Assert.True(getter.HasExplicitSignature);
        Assert.Equal("ref/Self", getter.ReceiverType!.ToString());
        Assert.Null(getter.ValueType);
        Assert.Equal("ref/T during self", getter.ReturnType!.ToString());
        Assert.Equal("uniq/Self", setter.ReceiverType!.ToString());
        Assert.Equal("Container<ref/T during source>", setter.ValueType!.ToString());
        Assert.IsType<CodeBlockKoto>(setter.Body);
        Assert.Equal("ref/Self", source[getter.ReceiverType.Span.Start..getter.ReceiverType.Span.End]);
        Assert.StartsWith("get(self:", source[getter.Span.Start..getter.Span.End]);
        Assert.EndsWith("use(value)", source[computed.Span.Start..computed.Span.End]);
        RoundTrip(tree);
    }

    [Theory]
    [InlineData("var p: T has get")]
    [InlineData("static var p: T = value")]
    [InlineData("open var p: T")]
    [InlineData("static computed p: T\n    get(self: ref/Self) -> T => value")]
    [InlineData("let p: T\n    set")]
    [InlineData("var p: T\n    get\n    get")]
    [InlineData("var p: T\n    set\n    set")]
    [InlineData("var p: T\n    get => value")]
    [InlineData("var p: T\n    set\n        use(value)")]
    [InlineData("var p: T\n    get -> T => value")]
    [InlineData("var p: T\n    get(self: ref/Self) => value")]
    [InlineData("var p: T\n    get(self: ref/Self) -> T")]
    [InlineData("var p: T\n    get(self: ref/Self, extra: T) -> T => value")]
    [InlineData("var p: T\n    get(self: ref/Self = other) -> T => value")]
    [InlineData("var p: T\n    get(! self: ref/Self) -> T => value")]
    [InlineData("var p: T\n    get(self:) -> T => value")]
    [InlineData("var p: T\n    get(self: ref/Self) ->")]
    [InlineData("var p: T\n    get(self: ref/Self) -> T =>")]
    [InlineData("var p: T\n    #Attribute get(self: ref/Self) -> T => value")]
    [InlineData("var p: T\n    get #Attribute")]
    [InlineData("var p: T\n    get #Attribute (self: ref/Self) -> T => value")]
    [InlineData("var p: T\n    get(self: ref/Self) #Attribute -> T => value")]
    [InlineData("var p: T\n    get(self: ref/Self) -> T #Attribute => value")]
    [InlineData("var p: T\n    set(self: uniq/Self) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, other: T) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, value: T = other) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, value:) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, value T) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, #Attribute value: T) -> () => ()")]
    [InlineData("var p: T\n    set(self: uniq/Self, value: T) -> T => value")]
    [InlineData("computed p: T")]
    [InlineData("computed p\n    get(self: ref/Self) -> T => value")]
    [InlineData("computed p: T = value\n    get(self: ref/Self) -> T => value")]
    [InlineData("computed p: T has get")]
    [InlineData("computed p: T\n    get")]
    [InlineData("computed p: T\n    set(self: uniq/Self, value: T) -> () => ()")]
    [InlineData("property p: T has get")]
    public void RejectsInvalidConcretePropertiesAndRecovers(string member)
    {
        var tree = Parse("struct S\n    " + member.Replace("\n", "\n    ") + "\n    var after: i32");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        var structure = Assert.Single(tree.RootKoto.NestedDeclarationContainers);
        Assert.Equal("after", Assert.IsType<PropertyKoto>(structure.Members.Last()).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("var p: T has get")]
    [InlineData("let p: T has get")]
    [InlineData("computed p: T\n    get(self: ref/Self) -> T => value")]
    [InlineData("property p: T")]
    [InlineData("property p has get")]
    [InlineData("property p: T = value has get")]
    [InlineData("property p: T has set")]
    [InlineData("property p: T has get, get")]
    [InlineData("property p: T has get, set, set")]
    [InlineData("property p: T has get,")]
    [InlineData("property p: T has private get")]
    [InlineData("property p: T has get -> T")]
    [InlineData("property p: T has get(self: ref/Self) -> T")]
    [InlineData("property p: T has get\n    set(self: uniq/Self, value: T) -> ()")]
    [InlineData("property p: T\n    get")]
    [InlineData("property p: T\n    private get(self: ref/Self) -> T")]
    [InlineData("property p: T\n    get(self: ref/Self) -> T => value")]
    [InlineData("property p: T\n    get(self: ref/Self) -> T\n        return value")]
    [InlineData("public property p: T has get")]
    [InlineData("#Attribute property p: T has get")]
    public void RejectsInvalidRequirementsAndRecovers(string member)
    {
        var tree = Parse("contract C\n    " + member.Replace("\n", "\n    ") + "\n    property after: i32 has get");
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        var contract = Assert.Single(tree.RootKoto.NestedDeclarationContainers);
        Assert.Equal("after", Assert.IsType<PropertyKoto>(contract.Members.Last()).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("enum E\n    var p: T")]
    [InlineData("enum E\n    computed p: T\n        get(self: ref/Self) -> T => value")]
    [InlineData("func f()\n    computed p: T\n        get() -> T => value")]
    [InlineData("group G\n    property p: T has get")]
    [InlineData("group G\n    static let p: T = value")]
    [InlineData("group G\n    computed p: T\n        get(self: ref/Self) -> T => value")]
    public void RejectsInvalidDeclarationContexts(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    private static void RoundTrip(Kotonoha tree)
    {
        VerifyParents(tree.RootKoto);
        var restored = new Kotonoha(tree.Compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(tree), ref restored);
        Assert.NotNull(restored);
        restored.OnDeserialized(tree.Compilation);
        AssertValid(restored);
        VerifyParents(restored.RootKoto);
        var original = Unparse(tree.RootKoto);
        Assert.Equal(original, Unparse(restored.RootKoto));
        var written = ParseSuccess(original);
        Assert.Equal(original, Unparse(written.RootKoto));
        VerifyParents(written.RootKoto);
    }

    private static void VerifyParents(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            Assert.Same(node, child.Parent);
            VerifyParents(child);
        }
    }

    private static string Unparse(GroupKoto root)
    {
        var builder = default(IndentedStringBuilder);
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

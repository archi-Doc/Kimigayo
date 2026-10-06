// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public sealed class HoverSyntaxTest
{
    [Fact]
    public void ParserRetainsEverySplitFragmentAndExactStructuralToken()
    {
        var compilation = Create("struct Box<T>\n    var item: T\nfunc f<s/T>(x: ref/T, value: i32?, array: [2 of i32], callback: () -> ()) => factory()(value)\n");
        compilation.Kotonoha.AddSource(new("second.kimi", "struct Box<T>\n    func get(self: ref/Self) -> ref/T => self.item\n"));
        var anchors = compilation.HoverAnchors;
        var fragments = anchors.Where(static a => a.Syntax is StructKoto { Name: "Box" }).ToArray();
        Assert.Equal(2, fragments.Length);
        Assert.Same(fragments[0].Syntax, fragments[1].Syntax);
        Assert.NotSame(fragments[0].Source, fragments[1].Source);
        Assert.All(fragments, static a => Assert.Equal("Box", Text(a)));
        Assert.Contains(anchors, static a => a.Syntax is GenericParameterKoto { SemanticsParameter: "s" } && Text(a) == "T");
        Assert.Contains(anchors, static a => a.Syntax is TypeSemanticsKoto { Type: not null, SemanticsKind: SemanticsKind.Ref } && Text(a) == "ref");
        Assert.Contains(anchors, static a => a.Syntax is OptionalTypeKoto && Text(a) == "?");
        Assert.Contains(anchors, static a => a.Syntax is FixedArrayTypeKoto && Text(a) == "[");
        Assert.Contains(anchors, static a => a.Syntax is FunctionTypeKoto && Text(a) == "->");
        var calls = anchors.Where(static a => a.Syntax is InvocationKoto).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, static a => Assert.Equal("(", Text(a)));
        Assert.NotEqual(calls[0].Token.Start, calls[1].Token.Start);
        Assert.Contains(anchors, static a => a.Syntax is TupleTypeKoto && Text(a) == "(");
    }

    [Fact]
    public void CollectionIsOptInAndExcludedSyntaxAddsNoTargets()
    {
        var source = "#if false\n    struct Hidden\nstruct Visible\n";
        Assert.Empty(Create(source, false).HoverAnchors);
        var collected = Create(source).HoverAnchors;
        Assert.DoesNotContain(collected, static a => a.Syntax is StructKoto { Name: "Hidden" });
        Assert.Contains(collected, static a => a.Syntax is StructKoto { Name: "Visible" });
    }

    [Fact]
    public void HeaderKeepsContractsAndDefaultsButOmitsExecutableCode()
    {
        var compilation = Create("func apply<T>(x: T, count: i32 = 42) -> T\n    T is Copy\n    return x\nstruct Record\n    public var value: i32 = 123\n        get\n        private set\n    drop => ()\n");
        var function = Assert.Single(compilation.HoverAnchors, static a => a.Syntax is FunctionKoto { Name: "apply" }).Syntax;
        Assert.Equal("func apply<T>(x: T, count: i32 = <default omitted>) -> T\n    T is Copy", Binding.HoverHeader(function));
        Assert.Contains("42", function.ToString());
        var property = Assert.Single(compilation.HoverAnchors, static a => a.Syntax is PropertyKoto).Syntax;
        Assert.Equal("public var value: i32\n    get\n    private set", Binding.HoverHeader(property));
        Assert.Contains("123", property.ToString());
        var destructor = Assert.Single(compilation.HoverAnchors, static a => a.Syntax is FunctionKoto { IsDestructor: true }).Syntax;
        Assert.Equal("drop", Binding.HoverHeader(destructor));
    }

    [Fact]
    public void ContainerHeaderMergesConstraintsAndKeepsConditionalCopy()
    {
        var compilation = Create("struct Box<T>\n    T is Copy\n    Self is Copy when T is Copy\n    var item: T\nstruct Box<T>\n    func get(self: ref/Self) -> T => self.item\n");
        var type = compilation.HoverAnchors.First(static a => a.Syntax is StructKoto).Syntax;
        Assert.Equal("struct Box<T>\n    T is Copy\n    Self is Copy when T is Copy", Binding.HoverHeader(type));
    }

    private static string Text(Compilation.HoverAnchor anchor) => anchor.Source.SourceText.Substring(anchor.Token.Start, anchor.Token.Length);

    private static Compilation Create(string source, bool collect = true)
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectHover = collect;
        compilation.Kotonoha.AddSource(new("main.kimi", source));
        Assert.Empty(TestDiagnostics.Of(compilation));
        return compilation;
    }
}

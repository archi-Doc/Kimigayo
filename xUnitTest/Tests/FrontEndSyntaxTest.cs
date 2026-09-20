// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class FrontEndSyntaxTest
{
    [Theory]
    [InlineData("abstract open struct S\n    let value: i32", "abstract")]
    [InlineData("virtual group G\n    func f() => ()", "virtual")]
    [InlineData("override rootgroup G\n    let value: i32", "override")]
    [InlineData("abstract enum E\n    A", "abstract")]
    [InlineData("abstract contract C\n    func f()", "abstract")]
    [InlineData("virtual func f() => ()", "virtual")]
    [InlineData("virtual func f()", "virtual")]
    [InlineData("public abstract open struct S", "abstract")]
    [InlineData("virtual override abstract public unsafe func f() => ()", "virtual")]
    [InlineData("virtual open func f() => ()", "virtual")]
    [InlineData("abstract specialize func f<i32>() => ()", "abstract")]
    [InlineData("struct S\n    virtual init() => ()", "virtual")]
    [InlineData("struct S\n    override public deinit => ()", "override")]
    [InlineData("struct S\n    virtual public let value: i32", "virtual")]
    [InlineData("struct S\n    override var value: i32", "override")]
    [InlineData("struct S\n    abstract computed value: i32\n        get(self: ref/Self) -> i32 => 1", "abstract")]
    [InlineData("struct S\n    var value: i32\n        abstract get\n        set", "abstract")]
    [InlineData("struct S\n    var value: i32\n        get\n        override open set", "override")]
    [InlineData("struct S\n    computed value: i32\n        virtual get(self: ref/Self) -> i32\n            return 1", "virtual")]
    [InlineData("contract C\n    virtual public func f()", "virtual")]
    [InlineData("contract C\n    abstract property value: i32 has get", "abstract")]
    [InlineData("contract C\n    override associate Element", "override")]
    [InlineData("contract C\n    property value: i32 has abstract open get, set", "abstract")]
    [InlineData("contract C\n    property value: i32 has get, virtual set", "virtual")]
    [InlineData("contract C\n    property value: i32\n        abstract get(self: ref/Self) -> i32", "abstract")]
    [InlineData("func outer()\n    virtual func inner() => ()", "virtual")]
    [InlineData("#if true\nabstract struct S", "abstract")]
    [InlineData("#switch\n    #case true\n        virtual func f() => ()", "virtual")]
    public void UnavailableModifiersReportOneCauseAndRecover(string source, string modifier)
    {
        var tree = Parse(source + "\nstruct Following\n");
        var diagnostic = Assert.Single(tree.DiagnosticCollection.GetArray());
        Assert.Equal("UnavailableFeature_Kd", diagnostic.Entry.Name);
        Assert.Equal(source.IndexOf(modifier, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal(modifier.Length, diagnostic.Span.Length);
        Assert.Contains(modifier, diagnostic.Message);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
        VerifyParents(tree.RootKoto);
    }

    [Theory]
    [InlineData("struct abstract\n    let override: i32\n    func virtual(self: ref/Self) -> i32 => self.override")]
    [InlineData("func virtual(x?: i32) -> i32 => x\nlet override: i32 = virtual(1)")]
    [InlineData("let value = x.abstract()\nvirtual(value)")]
    [InlineData("abstract\nlet next: i32 = 2")]
    [InlineData("func f()\n    abstract\n    let next: i32 = 2")]
    [InlineData("func f()\n    abstract\nstruct Next")]
    [InlineData("abstract /* comment */\nstruct Next")]
    [InlineData("abstract\r\nfunc next() => ()")]
    [InlineData("let abstract = func() => ()\nabstract()")]
    [InlineData("#if false\nvirtual func f() => ()\nstruct Next")]
    [InlineData("#if false\nabstract open struct S\n    let value: i32\nstruct Next")]
    public void UnavailableModifierSpellingsRemainOrdinaryNames(string source)
        => AssertValid(Parse(source));

    [Fact]
    public void UnavailableDeclarationRecoveryRetainsIndependentSiblings()
    {
        var tree = Parse("struct S\n    virtual func removed()\n        func nested() => ()\n    func retained() => ()\nstruct Following");
        Assert.Equal("UnavailableFeature_Kd", Assert.Single(tree.DiagnosticCollection.GetArray()).Entry.Name);
        var structure = tree.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Equal("retained", Assert.IsType<FunctionKoto>(Assert.Single(structure.Members)).Name);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
    }

    [Theory]
    [InlineData("let a: [4 of i32] = [1, 2, 3, 4]")]
    [InlineData("let a: [(N * 2 + 1) of ref{source}/T] = values")]
    [InlineData("let a: [Sizes.width of [2 of _]] = values")]
    [InlineData("let a: [::width of i32] = values")]
    [InlineData("func f<length N, T>(a?: [N of T]) -> [N of T] => a")]
    [InlineData("let a = f<4, i32>(values)")]
    [InlineData("let a = f<(N + 1), i32>(values)")]
    [InlineData("let a = ::Root.member")]
    [InlineData("let a = source@move")]
    [InlineData("let a = pair.0.1")]
    [InlineData("let a = pair.0.name")]
    [InlineData("let a = $abort(\"failed\")")]
    [InlineData("let _日本語 = 1")]
    [InlineData("let a: [4 of _] = values")]
    [InlineData("let a: List<\n    List<i32>\n> = values")]
    [InlineData("let a: List <\n    List <i32>\n> = values")]
    [InlineData("func f <\n    T\n>(value: T) => value")]
    [InlineData("let a = f<\n    [4 of i32]\n>(values)")]
    [InlineData("func f<\n    length N,\n    T\n>(value?: [N of T]) => ()")]
    [InlineData("let a = source\n    .first()\n    .second()")]
    [InlineData("let a = if ready\n    .Some(1)\nelse\n    .None")]
    [InlineData("group Empty\nstruct EmptyType\ncontract EmptyContract")]
    [InlineData("enum E\n    #if true\n    A\n    #switch\n        #case true\n            B\n        #case _\n            C")]
    [InlineData("let a = x is Type and ready")]
    [InlineData("let a = x is not Group.Type or fallback")]
    [InlineData("let a = .Some(1)")]
    [InlineData("func f()\n    .None")]
    [InlineData("let a = func(x) => x")]
    [InlineData("let a = func[](x: i32) -> i32 => x")]
    [InlineData("let a = func[source@ref, var count, value@uniq](x) => x")]
    [InlineData("func f()\n    require valid else => return\n    work()")]
    [InlineData("func f()\n    require valid\n    else\n        return\n    work()")]
    [InlineData("enum Option<T>\n    None\n    Some(T)")]
    [InlineData("enum E {a}\n    A\n    B(ref{a}/T)\n    func f() => ()")]
    [InlineData("let a = match value\n    .Some(let x) if x > 0 => x\n    .None => 0\n    _ => -1")]
    [InlineData("let a = match value\n    Option<i32>.Some(var x) => x\n    (let a, (var b, _)) => b\n    (1,) => 1\n    () => 0")]
    [InlineData("open struct Base\nstruct Derived : Base\n    init(value?: i32) : base(value)\n        return\n    deinit\n        return")]
    [InlineData("contract Sequence : Base, Other\n    associate Element\n    func next(self: ref/Self) -> Element\n    func use<T>(value?: T)\n        T is Comparable\n    property size: i32 has get")]
    [InlineData("struct S\n    Self is Sequence\n    associate Sequence.Element is i32")]
    [InlineData("func f<T>(x?: T)\n    T.Element is Comparable\n    return")]
    [InlineData("func f<F>(x?: F)\n    F is Callable<ref, (i32) -> bool>\n    return")]
    [InlineData("specialize func f<i32>(x: i32) -> i32 => x")]
    [InlineData("protected internal struct S\n    private protected func f() => ()")]
    [InlineData("group G\n    func f(other => self: i32) => ()")]
    [InlineData("struct S\n    func f(self: ref/Self, self => other: i32) => ()")]
    public void PreservesSpecifiedSyntax(string source)
    {
        var tree = ParseSuccess(source);
        VerifyParents(tree.RootKoto);
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            var roundTrip = ParseSuccess(text);
            VerifyParents(roundTrip.RootKoto);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("let require = 1")]
    [InlineData("    let a = 1")]
    [InlineData("#if false\nlet e\u0301 = 1")]
    [InlineData("let a: _ = value")]
    [InlineData("let a: [4 of _]")]
    [InlineData("func f(value?: [4 of _]) => value")]
    [InlineData("let a: List<[4 of _]> = values")]
    [InlineData("let a: ([4 of _], i32) = values")]
    [InlineData("let a: [2 of List<[4 of _]>] = values")]
    [InlineData("let a = f<[4 of _]>(values)")]
    [InlineData("struct S\n    func f(self: ref/Self, self: ref/Self) => ()")]
    [InlineData("struct S\n    func f(other => self: ref/Self) => ()")]
    [InlineData("enum E\n    A\n    func f(self?: E = E.A) => ()")]
    [InlineData("contract C\n    func f(self: ref/Self, self: ref/Self)")]
    [InlineData("group G\n    static func f() => ()")]
    [InlineData("func f()\n    static let x = 1")]
    [InlineData("struct S\n    static var x: i32")]
    [InlineData("open group G")]
    [InlineData("open enum E\n    A")]
    [InlineData("struct S\n    open func f() => ()")]
    [InlineData("for (a, b,) in pairs\n    ()")]
    [InlineData("for () in pairs\n    ()")]
    [InlineData("let a = match x\n    let _ => 1\n    _ => 0")]
    [InlineData("let a = match x\n    .Some(var _) => 1\n    _ => 0")]
    [InlineData("let a = $unknown(1)")]
    [InlineData("let a = $abort()")]
    [InlineData("let a = $abort(message: \"failed\")")]
    [InlineData("let a = $abort(\"failed\",)")]
    [InlineData("let defer = 1")]
    [InlineData("let Self = 1")]
    [InlineData("let init = 1")]
    [InlineData("let public = 1")]
    [InlineData("let a: [1.5 of T] = values")]
    [InlineData("let a: [N + 1 of T] = values")]
    [InlineData("let a: [4 T] = values")]
    [InlineData("struct S<length N>")]
    [InlineData("let a = func[var x@ref]() => x")]
    [InlineData("let a = func[x@T]() => x")]
    [InlineData("let a = func(x?: T) => x")]
    [InlineData("let a = match x\n    Name => 1")]
    [InlineData("let a = match x\n    1.5 => 1")]
    [InlineData("let a = match x\n    .A() => 1")]
    [InlineData("let a = match x\n    1 + 2 => 1")]
    [InlineData("contract C\n    func f(x?: T = value)")]
    [InlineData("contract C\n    func f() => value")]
    [InlineData("func f()\n        return")]
    [InlineData("let a = value.true")]
    [InlineData("let a = value.init")]
    [InlineData("enum Empty")]
    [InlineData("extension Future")]
    [InlineData("func f<i32>() => ()")]
    [InlineData("func f<List<T>>() => ()")]
    [InlineData("let a = value as Type")]
    [InlineData("let a: List<\nT\n> = values")]
    [InlineData("let a = 0x")]
    [InlineData("let a = 0B")]
    [InlineData("let a = 0o___")]
    [InlineData("let a = 0x_ + 1")]
    public void RejectsInvalidSyntax(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("#if false\nstatic func f() => ()\nlet a = 1")]
    [InlineData("#if false\nopen group G\nlet a = 1")]
    public void ExcludedDeclarationsDoNotReportModifierPlacement(string source)
        => Assert.Empty(Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void RetainsCaptureAcquisitionAndUnevaluatedLengths()
    {
        var tree = ParseSuccess("let f = func[var count, source@ref](x) => x\nlet a: [(N + 1) of i32] = values");
        var items = tree.GeneratedFunction!.Body!.Items;
        var function = Assert.IsType<FunctionKoto>(Assert.IsType<FieldKoto>(items[0]).InitializerKoto);
        Assert.True(function.IsAnonymous);
        Assert.Null(function.Captures![0].Operation);
        Assert.True(function.Captures[0].IsMutable);
        Assert.Equal("ref", function.Captures[1].Operation);
        var array = Assert.IsType<FixedArrayTypeKoto>(Assert.IsType<FieldKoto>(items[1]).TypeKoto);
        Assert.IsType<ParenthesizedKoto>(array.Length);
    }

    [Theory]
    [InlineData("1.000000059604644775390625000000000001")]
    [InlineData("1e999999")]
    [InlineData("1e-999999")]
    [InlineData("1__234.5_678e+1_2")]
    public void RetainsExactDecimalSpellingUntilFitting(string literal)
    {
        var tree = ParseSuccess("let value = " + literal);
        var number = Assert.IsType<NumberLiteralKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        Assert.False(number.IsInteger);
        Assert.Equal(literal, number.SourceSpelling.ToString());
        Assert.Equal(literal, number.ToString());
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(literal, Assert.IsType<FieldKoto>(Assert.Single(restored.GeneratedFunction!.Body!.Items)).InitializerKoto!.ToString());
    }

    private static void VerifyParents(Koto parent)
    {
        foreach (var child in parent.ChildNodes)
        {
            Assert.Same(parent, child.Parent);
            if (parent.CodeContext.SourceDocument is not null)
            {
                Assert.Same(parent.CodeContext.SourceDocument, child.CodeContext.SourceDocument);
            }

            VerifyParents(child);
        }
    }
}

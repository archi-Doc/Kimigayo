// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class SpecRevisionAnalysisTest
{
    [Theory]
    [InlineData("computed child: objref/Node\n    get(self: ref/Self) -> objref/Node during self => self.node@objref")]
    [InlineData("computed value: obj/Node\n    get(self: ref/Self) -> obj/Node => Node.new()")]
    [InlineData("var value: i64\n    get(self: ref/Self) -> i64\n        return 1")]
    [InlineData("var value: i32\n    get")]
    [InlineData("var value: i32\n    get(self: ref/Self) -> i32 => 1")]
    [InlineData("computed value: (i32, string) -> bool\n    get(self: ref/Self) -> (i32, string) -> bool => callback")]
    public void GetterAnnotationsRoundTripAndRetainTreeOwnership(string member)
    {
        var source = "struct Example\n    " + member.Replace("\n", "\n    ");
        var tree = Parse(source);
        var structure = Assert.IsType<StructKoto>(Assert.Single(tree.RootKoto.NestedDeclarationContainers));
        var property = Assert.IsType<PropertyKoto>(Assert.Single(structure.Members));
        var getter = property.GetAccessor(PropertyAccessorKind.Get)!;
        if (member.Contains("->", StringComparison.Ordinal))
        {
            Assert.NotNull(getter.ReturnType);
            Assert.Same(getter, getter.ReturnType.Parent);
            Assert.Contains(getter.ReturnType, getter.ChildNodes);
        }

        var text = property.ToString();
        Parse("struct Example\n    " + text.Replace("\n", "\n    "));
        var bytes = TinyhandSerializer.Serialize(tree);
        var restored = new Kotonoha(tree.Compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        Assert.NotNull(restored);
        restored.OnDeserialized(tree.Compilation);
        Assert.Equal(text, Assert.Single(Assert.Single(restored.RootKoto.NestedDeclarationContainers).Members).ToString());
    }

    [Theory]
    [InlineData("var value: i32 has get ->")]
    [InlineData("var value: i32 has get ->, set")]
    [InlineData("var value: i32 has set -> i32")]
    [InlineData("var value: i32\n    set -> i32 => 1")]
    [InlineData("var value: i32 has get,")]
    public void InvalidAccessorsRecoverToTheNextProperty(string member)
    {
        var source = "struct Example\n    " + member.Replace("\n", "\n    ") + "\n    var after: i32";
        var tree = Parse(source, valid: false);
        Assert.NotEmpty(TestDiagnostics.Of(tree));
        Assert.Equal("after", Assert.Single(tree.RootKoto.NestedDeclarationContainers).Members.OfType<PropertyKoto>().Last().NameKoto.IdentifierName);
    }

    [Fact]
    public void ContractGetterAnnotationIsPreserved()
    {
        var tree = Parse("contract Collection\n    property child: objref/Node\n        get(self: ref/Self) -> objref/Node during self");
        var property = Assert.IsType<PropertyKoto>(Assert.Single(Assert.Single(tree.RootKoto.NestedDeclarationContainers).Members));
        Assert.True(property.IsContractRequirement);
        Assert.Equal("objref/Node during self", property.Accessors[0].ReturnType!.ToString());
        Assert.True(property.Accessors[0].IsBodyless);
    }

    [Theory]
    [InlineData("let value: i32 = label work: do\n    exit to work 1")]
    [InlineData("let value: () = label work: do\n    exit to work ()")]
    [InlineData("let result = label work: do\n    exit to work 1")]
    [InlineData("func f() -> i32\n    let result = label work: do\n        return 1")]
    [InlineData("let result = label work: do\n    loop\n        continue")]
    [InlineData("label work: do\n    exit to work")]
    [InlineData("label work: do\n    let unused = loop\n        exit 1\n    ()")]
    [InlineData("let result = label work: do\n    loop\n        if false\n            exit to work 1")]
    public void AcceptsLabeledBlockResults(string source)
        => AssertValidAnalysis(source);

    [Theory]
    [InlineData("let value: i32 = label work: do\n    ()")]
    [InlineData("let value: i32 = label work: do\n    exit to work")]
    [InlineData("label work: do\n    exit to work 1\n    exit to work")]
    [InlineData("label work: do\n    exit to work 1\n    exit to work \"text\"")]
    [InlineData("let value: i32 = label work: do\n    loop\n        if false\n            exit to work \"text\"")]
    [InlineData("defer => exit 1")]
    public void RejectsInvalidBlockResultsEvenWhenUnreachable(string source)
        => Assert.False(Parse(source).Compilation.Bind().IsComplete);

    [Theory]
    [InlineData("defer => exit")]
    [InlineData("defer\n    defer => ()\n    exit")]
    [InlineData("defer\n    loop\n        exit\n    exit")]
    [InlineData("defer\n    let result = if true => 1 else => 2\n    ()")]
    [InlineData("defer\n    func inner()\n        return\n    exit")]
    [InlineData("func f() -> i32\n    defer => ()\n    return 1")]
    public void AcceptsDeferredLocalTransfers(string source) => AssertValidAnalysis(source);

    [Theory]
    [InlineData("func f()\n    defer => return", "No valid target")]
    [InlineData("loop\n    defer => continue", "No valid target")]
    [InlineData("label outer: loop\n    defer => exit to outer", "No valid target")]
    [InlineData("if true\n    defer => yield 1", "No valid target")]
    [InlineData("defer\n    if false\n        return", "No valid target")]
    public void DeferredBoundaryCannotBeCrossed(string source, string diagnostic)
        => Assert.Contains(Analyze(source).Issues, issue => issue.Message.Contains(diagnostic, StringComparison.Ordinal));

    [Fact]
    public void DeferredDivergenceAffectsExitRatherThanRegistration()
    {
        var analysis = Analyze("func f() -> i32\n    defer\n        loop\n            continue\n    let after = 1\n    return 2");
        Assert.Empty(analysis.Issues);
        var after = analysis.Nodes.Single(x => x.Key is FieldKoto { NameKoto.IdentifierName: "after" });
        Assert.True(after.Value.CanCompleteNormally);
    }

    [Fact]
    public void BranchScopedCleanupDoesNotBlockOtherReturnPaths()
    {
        var tree = Parse("func f(flag: bool) -> i32\n    if flag\n        defer\n            loop\n                continue\n        return \"text\"\n    return 1");
        tree.Compilation.Bind();
        var analysis = tree.Compilation.AnalyzeControlFlow();
        Assert.True(analysis.Nodes.Single(x => x.Key is FunctionKoto { IsGenerated: false }).Value.CanCompleteNormally);
        // The blocked return is still checked against the available result contract.
        Assert.Contains(tree.Compilation.Binding.Issues, issue => issue.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
    }

    [Fact]
    public void UnreachedRegistrationDoesNotBlockAnEarlierReturn()
        => Assert.Empty(Analyze("func f() -> i32\n    return 1\n    defer\n        loop\n            continue").Issues);

    // SPEC 11: a stored Property's custom getter returns exactly the Property's Type.
    [Theory]
    [InlineData("get(self: ref/Self) -> bool => 1", true)]
    [InlineData("get(self: ref/Self) -> i64 => 1", true)]
    [InlineData("get(self: ref/Self) -> i32 => 1", false)]
    public void StoredGetterResultIsExactlyThePropertyType(string getter, bool invalid)
        => Assert.Equal(invalid, !Parse("struct Example\n    var value: i32\n        " + getter).Compilation.Bind().IsComplete);

    [Theory]
    [InlineData("func f()\n    #if false\n        ()")]
    [InlineData("defer\n    #if false\n        ()")]
    [InlineData("unsafe\n    #if false\n        ()")]
    [InlineData("label work: do\n    #if false\n        ()")]
    [InlineData("if true\n    #if false\n        ()")]
    [InlineData("struct Example\n    var value: i32\n        get(self: ref/Self) -> i32\n            #if false\n                return 1")]
    public void EmptySelectedBodiesRemainValidAfterWriting(string source)
    {
        var tree = Parse(source);
        var node = tree.GeneratedFunction?.Body?.Items.FirstOrDefault() ?? Assert.Single(Assert.Single(tree.RootKoto.NestedDeclarationContainers).Members);
        var text = node.ToString();
        var rewritten = node is PropertyKoto ? "struct Example\n    " + text.Replace("\n", "\n    ") : text;
        var reparsed = Parse(rewritten);
        var next = reparsed.GeneratedFunction?.Body?.Items.FirstOrDefault() ?? Assert.Single(Assert.Single(reparsed.RootKoto.NestedDeclarationContainers).Members);
        Assert.Equal(text, next.ToString());
    }

    [Theory]
    [InlineData("let pointer: raw/i32 = null")]
    [InlineData("let pointer: raw/i32 = null\nlet empty = pointer == null")]
    [InlineData("let pointer: raw/i32 = null\nlet empty = null != pointer")]
    [InlineData("let pointer: raw/i32 = null\nlet empty = pointer == (null)")]
    [InlineData("func f(flag: bool, pointer: raw/i32) -> raw/i32 => if flag => pointer else => null")]
    [InlineData("func f(flag: bool, pointer: raw/i32) -> raw/i32 => if flag => (null) else => pointer")]
    [InlineData("func f() -> raw/i32 => null")]
    public void NullIsAContextuallyTypedLiteral(string source)
    {
        var tree = Parse(source);
        Assert.True(tree.Compilation.Bind().IsComplete);
        var nulls = KotoTree.Walk(tree.GeneratedFunction!).OfType<NullLiteralKoto>().ToArray();
        Assert.NotEmpty(nulls);
        Assert.All(nulls, x => Assert.True(ReferenceTypes.IsPointer(x.TypeOf())));
        Parse(tree.GeneratedFunction!.ToString());
    }

    [Theory]
    [InlineData("let unknown = null")]
    [InlineData("let unknown = (null)")]
    [InlineData("null")]
    [InlineData("func f() => null")]
    [InlineData("let value: i32 = null")]
    [InlineData("let value = null == null")]
    [InlineData("let value = 1 == null")]
    public void RejectsNullWithoutAPointerContext(string source)
        => Assert.False(Parse(source).Compilation.Bind().IsComplete);

    [Theory]
    [InlineData("unsafe func f(pointer: raw/i32) -> i32\n    return *pointer", false)]
    [InlineData("func f(pointer: raw/i32) -> i32\n    unsafe => return *pointer", true)]
    [InlineData("unsafe\n    func f(pointer: raw/i32) -> i32\n        return *pointer", false)]
    [InlineData("func f(pointer: raw/i32)\n    unsafe\n        defer => *pointer", true)]
    [InlineData("func f(pointer: raw/i32)\n    defer => unsafe => *pointer", true)]
    [InlineData("func f(pointer: raw/i32)\n    defer => *pointer", false)]
    [InlineData("let pointer: raw/i32 = null\nlet next = pointer + 1", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe\n    let next = pointer + 1", true)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer[^1]", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer[(^1)]", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer[0..4]", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer[(0..4)]", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer@i32", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer - pointer", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => 1 + pointer", false)]
    [InlineData("let pointer: raw/i32 = null\nlet address = pointer@usize", true)]
    [InlineData("let pointer: raw/i32 = null\nunsafe\n    let address = pointer@usize", true)]
    [InlineData("let a: raw/i32 = null\nlet b: raw/u8 = null\na == b", false)]
    [InlineData("let a: raw/i32 = null\na < a", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer * pointer", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer / 2", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => pointer << 1", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe => -pointer", false)]
    [InlineData("unsafe => *1", false)]
    [InlineData("let pointer: raw/i32 = 1", false)]
    [InlineData("let pointer: raw/i32 = null\nlet other: raw/u8 = pointer", false)]
    [InlineData("let pointer: raw/i32 = null\nunsafe\n    let other: raw/u8 = pointer@raw/u8", true)]
    public void ChecksKnownUnsafeOperationsAndLexicalPermission(string source, bool valid)
        => Assert.Equal(valid, BindsAndFlows(source));

    [Theory]
    [InlineData("f()", false)]
    [InlineData("unsafe => f()", true)]
    [InlineData("unsafe => f<i32>()", true)]
    [InlineData("unsafe => (f)()", true)]
    [InlineData("let value = f", false)]
    [InlineData("unsafe\n    let value = f", false)]
    [InlineData("func caller()\n    f()", false)]
    public void ChecksUnsafeCallsAfterBindingSelectsAFunction(string call, bool valid)
    {
        var declaration = call.Contains("f<i32>", StringComparison.Ordinal) ? "unsafe func f<T>() => ()" : "unsafe func f() => ()";
        Assert.Equal(valid, BindsAndFlows(declaration + "\n" + call));
    }

    [Theory]
    [InlineData("i8", "-128", true)]
    [InlineData("i8", "-(127)", true)]
    [InlineData("i8", "-129", false)]
    [InlineData("u8", "255", true)]
    [InlineData("u8", "256", false)]
    [InlineData("u8", "-1", false)]
    [InlineData("u128", "340282366920938463463374607431768211455", true)]
    [InlineData("i128", "170141183460469231731687303715884105728", false)]
    [InlineData("i128", "-170141183460469231731687303715884105728", true)]
    [InlineData("i128", "-170141183460469231731687303715884105729", false)]
    public void ChecksIntegerMagnitudesWithoutSignedBitPatternFormatting(string type, string literal, bool valid)
        => Assert.Equal(valid, Parse($"func f() -> {type} => {literal}").Compilation.Bind().IsComplete);

    [Theory]
    [InlineData("340282366920938463463374607431768211455")]
    [InlineData("170141183460469231731687303715884105728")]
    public void LargeUnsignedLiteralsRetainTheirMagnitudeWhenWritten(string literal)
    {
        var tree = Parse("let value: u128 = " + literal);
        var field = Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items));
        var number = Assert.IsType<NumberLiteralKoto>(field.InitializerKoto);
        Assert.Equal(literal, number.Literal);
        Assert.False(number.TryGetBasicValue(out _));
        AssertValidAnalysis(field.ToString());
        var reparsed = Parse(field.ToString());
        Assert.Equal(literal, Assert.IsType<NumberLiteralKoto>(Assert.IsType<FieldKoto>(Assert.Single(reparsed.GeneratedFunction!.Body!.Items)).InitializerKoto).Literal);
    }

    private static Kotonoha Parse(string source, bool valid = true)
    {
        var tree = Compilation.CreateForTest().Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        if (valid)
        {
            Assert.True(TestDiagnostics.Of(tree).Length == 0, string.Join("\n", TestDiagnostics.Of(tree).Select(x => x.ToString())));
        }

        return tree;
    }

    private static ControlFlowAnalysis Analyze(string source)
    {
        var compilation = Parse(source).Compilation;
        compilation.Bind();
        return compilation.AnalyzeControlFlow();
    }

    // Whether Binding accepts the source and control flow reports no Error.
    private static bool BindsAndFlows(string source)
    {
        var compilation = Parse(source).Compilation;
        return compilation.Bind().IsComplete && compilation.AnalyzeControlFlow().Issues.Count == 0;
    }

    private static void AssertValidAnalysis(string source)
    {
        var compilation = Parse(source).Compilation;
        Assert.True(compilation.Bind().IsComplete, string.Join("\n", compilation.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
        var analysis = compilation.AnalyzeControlFlow();
        Assert.True(analysis.Issues.Count == 0, string.Join("\n", analysis.Issues.Select(x => x.Message)));
    }
}

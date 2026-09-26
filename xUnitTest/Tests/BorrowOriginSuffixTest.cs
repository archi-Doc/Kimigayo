// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowOriginSuffixTest
{
    [Theory]
    [InlineData("ref/T during a")]
    [InlineData("uniq/T during a")]
    [InlineData("objref/T during a")]
    [InlineData("objuniq/T during a")]
    [InlineData("s/T during a")]
    [InlineData("ref/T? during a")]
    [InlineData("ref/T?? during a")]
    [InlineData("(ref/T during a)?")]
    [InlineData("ref/(T?) during a")]
    [InlineData("ref/(uniq/T during b)? during a")]
    [InlineData("unsafe/(ref/T during a)")]
    [InlineData("ref/View<ref/U during c>{v} during a")]
    [InlineData("ref/View{v} during a")]
    [InlineData("ref/View{v}? during a")]
    [InlineData("(ref/T during a, [2 of ref/U during b])")]
    [InlineData("ref/((T) -> U) during a")]
    [InlineData("(T) -> ref/U? during b")]
    [InlineData("ref/T during (a and b)")]
    [InlineData("ref/T during value.source")]
    [InlineData("ref/T during (static)")]
    [InlineData("ref/T during during")]
    [InlineData("ref/T during from")]
    [InlineData("View<T> during a")]
    [InlineData("View<T>? during a")]
    [InlineData("Kimi.Text.Utf8Slice during value.source")]
    [InlineData("Slice<ref/T during b> during a")]
    public void AttachmentSurvivesWritingAndReload(string type)
    {
        var tree = ParseTestHelper.ParseSuccess($"func f(x: {type}) => ()");
        var parameter = Assert.Single(Assert.Single(ParseTestHelper.GetChildren(tree.RootKoto).OfType<FunctionKoto>()).Parameters).Type;
        Assert.Equal(type, parameter.ToString());
        ParseTestHelper.AssertValid(ParseTestHelper.Parse($"func f(x: {parameter}) => ()"));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(tree));
        Assert.NotNull(restored);
        restored.OnDeserialized(Compilation.CreateForTest());
        ParseTestHelper.AssertValid(restored);
    }

    [Theory]
    [InlineData("ref{a}/T")]
    [InlineData("s{a}/T")]
    [InlineData("(ref/T)? during a")]
    [InlineData("(ref/T?) during a")]
    [InlineData("(ref/T during a) during b")]
    [InlineData("(ref/T during a)? during b")]
    [InlineData("View<T>{v} during a")]
    [InlineData("i32 during a")]
    [InlineData("(View<T>) during a")]
    [InlineData("ref/T during a during b")]
    [InlineData("ref/T during a?")]
    [InlineData("ref/T during (a and b)?")]
    [InlineData("ref/T during ()")]
    [InlineData("ref/T during (a,)")]
    [InlineData("ref/T during (a, b)")]
    [InlineData("s/T during (wrong => a)")]
    [InlineData("ref/T during _")]
    [InlineData("ref/T during f(1)")]
    [InlineData("ref/T during a and b")]
    [InlineData("ref/T during a.b.c")]
    [InlineData("unsafe/ref/T during a")]
    [InlineData("owner/ref/T during a")]
    [InlineData("obj/T during a")]
    [InlineData("ref/T from a")]
    public void RejectsInvalidAnnotations(string type)
        => Assert.NotEmpty(ParseTestHelper.Parse($"func f(x: {type}) => ()").DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("x@(ref/T? during a)", true)]
    [InlineData("x@(ref/T during a)?", true)]
    [InlineData("x@Option<ref/T during a>", true)]
    [InlineData("x@(ref/T? during value.source).count", true)]
    [InlineData("x@ref/T? during a", false)]
    [InlineData("x@ref during a", false)]
    [InlineData("x@(ref/T during a)", false)]
    [InlineData("x@View<T>{v} / y", true)]
    [InlineData("x@s{a}/T", true)]
    public void AdaptationBoundariesAreSyntactic(string expression, bool valid)
    {
        var diagnostics = ParseTestHelper.Parse("let result = " + expression).DiagnosticCollection.GetArray();
        Assert.True(valid == (diagnostics.Length == 0), string.Join("\n", diagnostics.Select(x => x.Message)));
    }

    [Theory]
    [InlineData("View<T>{v}")]
    [InlineData("s{a}")]
    [InlineData("ref{a}")]
    public void ACompletedBindingSetLeavesTheSlashAsDivision(string target)
    {
        var tree = ParseTestHelper.ParseSuccess($"let result = x@{target} / y");
        var declaration = Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items));
        var division = Assert.IsType<SlashKoto>(declaration.InitializerKoto);
        var adaptation = Assert.IsType<ConversionKoto>(division.Left);
        Assert.Equal(target, adaptation.Right.ToString());
        Assert.Equal("y", division.Right.ToString());
    }

    [Fact]
    public void OptionalKeepsOuterTargetAndContainedSourceRanges()
    {
        const string Source = "func f(x: ref/(uniq/T during b)?? during a) => ()";
        var function = ParseTestHelper.ParseSingleFunction(Source);
        var optional = Assert.IsType<OptionalTypeKoto>(function.Parameters[0].Type);
        var second = Assert.IsType<OptionalTypeKoto>(optional.Type);
        var outer = Assert.IsType<TypeSemanticsKoto>(second.Type);
        var inner = Assert.IsType<TypeSemanticsKoto>(Assert.IsType<ParenthesizedTypeKoto>(outer.Type).Type);
        Assert.Equal("a", outer.OriginName);
        Assert.Equal("b", inner.OriginName);
        Assert.Equal("during a", Source[outer.BorrowOriginSpan.Start..outer.BorrowOriginSpan.End]);
        Assert.True(outer.Span.End <= second.Span.End && second.Span.End <= optional.Span.End);
        Assert.True(outer.OriginExpression!.Span.End <= outer.Span.End);
        Assert.Equal("ref/(uniq/T during b)?? during a", optional.ToString());
    }

    [Theory]
    [InlineData("func during(from: i32) -> i32 => from\nlet value = during(1)")]
    [InlineData("func f<T>(x: ref/T)\n    -> ref/T during x\n    return x")]
    [InlineData("func f<T>(x: ref/T)\n    -> (ref/T\n        during x)\n    return x")]
    [InlineData("func f(x: ref/i32 during a, cb: ref/((ref/i32 during a) -> ref/i32 during a)) -> ref/((ref/i32 during a) -> ref/i32 during a) during cb => cb@follow@ref/((ref/i32 during a) -> ref/i32 during a)")]
    public void ContextualNamesAndExistingContinuationBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
    }

    [Theory]
    [InlineData("func f(x: i32) -> ref/i32 during x => x@ref", "declared schema slot")]
    [InlineData("func f<s/T>(x: s/T)\n    s is ref\n    let y = x@s{a}/T", "outer Origin must be inferred")]
    [InlineData("func f(x: i32) => x@ref{a}/i32", "outer Origin must be inferred")]
    [InlineData("func f<T>(x: ref/i32 during a, y: ref/i32 during b)\n    T is ref/i32 during a and b\n    ()", "intersection requires parentheses")]
    [InlineData("struct View {source}\n    let item: ref/i32 during source\nfunc f<T>(x: ref/i32 during a, view: View{v})\n    T is ref/i32 during a and v.source\n    ()", "intersection requires parentheses")]
    [InlineData("func f(x: ref/i32? during a) -> ref/i32\n    return match x\n        .Some(let value) => value@follow\n        .None => $abort(\"empty\")", "omitted result Origin is static")]
    [InlineData("struct V {source}\n    let value: ref/i32 during source\nfunc f(view: V, x: ref/i32? during view.source) -> ref/i32\n    return match x\n        .Some(let value) => value@follow\n        .None => $abort(\"empty\")", "omitted result Origin is static")]
    public void InvalidUsesKeepTheirFailureAndExplainTheOriginRule(string source, string hint)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var failures = c.Binding.Issues.ToArray();
        c.Binding.ReportDiagnostics();
        var messages = c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray().Select(x => x.Message).ToArray();
        Assert.True(messages.Any(x => x.Contains(hint, StringComparison.Ordinal)), string.Join("\n", messages));
        Assert.Equal(failures, c.Binding.Issues);
    }

    [Theory]
    [InlineData("func f(x: ref/i32) -> ref/i32\n    during x\n    return x", "same line", "Type's line")]
    [InlineData("func f(x: ref/i32 during a and b) => ()", "parentheses", "parentheses")]
    [InlineData("func f(x: ref/i32 from a) => ()", "during", "during")]
    [InlineData("func f<T>(x: ref/i32 during a)\n    T is Box<ref/i32 during a and b>\n    ()", "parentheses in Type arguments", "parentheses")]
    public void SyntaxErrorsSuggestLocalCorrections(string source, string description, string hint)
    {
        var messages = ParseTestHelper.Parse(source).DiagnosticCollection.GetArray().Select(x => x.Message);
        Assert.True(messages.Any(x => x.Contains(hint, StringComparison.Ordinal)), description);
    }

    [Fact]
    public void StaticResultAndOrdinaryContextualNamesHaveNoWarnings()
    {
        const string Source = "func during(from: i32) -> i32 => from\nfunc f(x: ref/i32? during a, y: ref/i32 during static) -> ref/i32 => y\nfunc use()\n    during(1)\n    let from = during(2)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
    }

    [Fact]
    public void RepeatedSuffixRecoveryRetainsTheFirstOriginAndNextDeclaration()
    {
        var tree = ParseTestHelper.Parse("func f(x: ref/i32 during first" + string.Concat(Enumerable.Repeat(" during ignored", 256)) + ") => ()\nfunc after() => ()");
        var functions = ParseTestHelper.GetChildren(tree.RootKoto).OfType<FunctionKoto>().ToArray();
        Assert.Equal(2, functions.Length);
        Assert.Equal("first", Assert.IsType<TypeSemanticsKoto>(functions[0].Parameters[0].Type).OriginName);
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
    }

    [Fact]
    public void ATypeNamedLikeSemanticsKeepsItsBindingSetRole()
    {
        const string Source = "struct ref {source}\n    let item: ref/i32 during source\nfunc f(x: ref{input})\n    let result = x@ref{output} / 1\n        origin output.source == input.source";
        var c = MinimalEmissionTest.Analyze(Source);
        var function = Assert.Single(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).OfType<FunctionKoto>());
        var field = Assert.IsType<FieldKoto>(Assert.Single(function.Body!.Items));
        var division = Assert.IsType<SlashKoto>(field.InitializerKoto);
        var conversion = Assert.IsType<ConversionKoto>(division.Left);
        Assert.Equal(BindingSymbolKind.Type, conversion.Right.BoundSymbol?.Kind);
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray(), d => d.Message.Contains("brace borrow", StringComparison.Ordinal));
    }
}

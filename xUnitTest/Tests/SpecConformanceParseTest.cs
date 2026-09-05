// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class SpecConformanceParseTest
{
    [Theory]
    [InlineData("(i32, string)")]
    [InlineData("char")]
    [InlineData("List<char>")]
    [InlineData("()")]
    [InlineData("(i32, string) -> bool")]
    [InlineData("ref/(i32, string) from owner")]
    [InlineData("List<(i32, string)>")]
    [InlineData("List<(i32) -> bool>")]
    [InlineData("List<List<i32>>")]
    [InlineData("ref/T from self.source")]
    [InlineData("ref/T from x and y.source")]
    [InlineData("ref/T from static")]
    [InlineData("Pair<A, B> from (left => a, right => b.source and c)")]
    [InlineData("View<T> from (source => s)")]
    [InlineData("has")]
    public void TypeSyntaxWorksInEveryDeclarationPosition(string type)
    {
        var source = $"""
            var local: {type}
            struct Example
                var property: {type}
                func use(value: {type}) -> {type}
                    return value
            """;
        var parsed = Parse(source);
        AssertValid(parsed);
        RoundTrip(parsed);
    }

    [Fact]
    public void ParsesFunctionOriginsAndSeparatesConstraintsFromExecutableBody()
    {
        var parsed = Parse("""
            func View.unwrap<s/T> origin source, owner(value: s/T from source)
                -> ref/T from value and owner
                s is ref or obj
                T is Comparable and (Equatable or Hashable)

                return value
            """);
        AssertValid(parsed);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.Equal("View.unwrap", function.Name);
        Assert.Equal(["source", "owner"], function.Origins);
        Assert.Equal(2, function.TypeConstraints.Count);
        Assert.IsType<ReturnKoto>(Assert.Single(function.Body!.Items));
        Assert.All(function.TypeConstraints, constraint => Assert.Same(function, constraint.Parent));
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("func f<T>()\n    other is Comparable\n    return")]
    [InlineData("func f<T>()\n    run()\n    T is Comparable\n    return")]
    public void RejectsInvalidFunctionConstraintDeclarations(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Fact]
    public void ParsesContractPropertyRequirementsAndDestructor()
    {
        var parsed = Parse("""
            contract Sequence
                associate Element is Comparable
                var count: i32 has get
                var item: Element has get, set

            struct Logger origin sink
                let output: uniq/Writer from sink
                deinit
                    self.output.flush()
                    return
            """);
        AssertValid(parsed);
        var contract = Assert.Single(parsed.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>());
        Assert.All(contract.Members.Cast<PropertyKoto>(), p => Assert.True(p.IsContractRequirement));
        Assert.Equal(2, contract.Members.Count);
        var structure = Assert.Single(parsed.RootKoto.NestedDeclarationContainers.OfType<StructKoto>());
        var destructor = Assert.Single(structure.Members.OfType<FunctionKoto>());
        Assert.True(destructor.IsDestructor);
        Assert.Equal(2, destructor.Body!.Items.Count);
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("\"a\\rb\"", "a\rb")]
    [InlineData("\"a\nb\"", "a\nb")]
    [InlineData("\"a\rb\"", "a\rb")]
    [InlineData("\"\na\\nb\"", "\na\nb")]
    [InlineData("\"\"\"a\nb\"\"\"", "a\nb")]
    [InlineData("\"\"\"a\\(value)\"\"\"", "a\\(value)")]
    [InlineData("\"a\\\\(value)\"", "a\\(value)")]
    public void ParsesAndDecodesAllSpecifiedStringForms(string source, string expected)
    {
        var parsed = Parse("var value = " + source);
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.Equal(expected, Assert.IsType<StringLiteralKoto>(field.InitializerKoto).Literal);
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("u()")]
    [InlineData("u(0000041)")]
    [InlineData("u(0x41)")]
    [InlineData("u( 41)")]
    [InlineData("u(+41)")]
    [InlineData("u(4_1)")]
    [InlineData("u(４１)")]
    [InlineData("u(D800)")]
    [InlineData("u(DFFF)")]
    [InlineData("u(110000)")]
    [InlineData("u(D83D)\\u(DE00)")]
    [InlineData("x41")]
    public void CharAndStringLiteralsRejectTheSameInvalidCharacterEscapes(string escape)
    {
        var character = Parse("let value = '\\" + escape + "'");
        var text = Parse("let value = \"\\" + escape + "\"");
        var charError = Assert.Single(character.DiagnosticCollection.GetArray());
        var stringError = Assert.Single(text.DiagnosticCollection.GetArray());
        Assert.Equal(charError.Entry.Name, stringError.Entry.Name);
    }

    [Theory]
    [InlineData("\"Hello, \\(name).\"")]
    [InlineData("\"Total: \\(price * quantity)\"")]
    [InlineData("\"\\(f((a + b), \"text)\"))\"")]
    [InlineData("\"\\(f(/* ) */ value))\\(other)\"")]
    [InlineData("\"\\(\"nested \\(value)\")\"")]
    [InlineData("\"first\n\\(value)last\"")]
    public void ParsesNestedInterpolationExpressions(string source)
    {
        var parsed = Parse("var result = " + source);
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        var interpolation = Assert.IsType<InterpolatedStringKoto>(field.InitializerKoto);
        Assert.Equal(interpolation.Expressions.Length + 1, interpolation.Segments.Length);
        Assert.All(interpolation.Expressions, expression =>
        {
            Assert.Same(interpolation, expression.Parent);
            Assert.True(expression.Span.Start > field.Span.Start);
        });
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("var value = \"\\q\"")]
    [InlineData("var value = \"\\u(D800)\"")]
    [InlineData("var value = \"\\()\"")]
    [InlineData("var value = \"\\(a b)\"")]
    [InlineData("var value = \"\\(a\"")]
    public void DiagnosesInvalidStringSyntaxDuringParsing(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("(1, \"text\")")]
    [InlineData("(1,)")]
    [InlineData("((1, 2), 3)")]
    public void ParsesTupleValues(string source)
    {
        var parsed = Parse("var tuple = " + source);
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.IsType<TupleLiteralKoto>(field.InitializerKoto);
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("has")]
    [InlineData("get")]
    [InlineData("set")]
    [InlineData("in")]
    public void ContextualKeywordsWorkAsValues(string name)
    {
        var parsed = Parse($"var {name} = 1\nvar value = {name}");
        AssertValid(parsed);
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("#if unknown")]
    [InlineData("#if true")]
    [InlineData("#case unknown")]
    [InlineData("#case true")]
    public void DirectivesRetainDeclarationContext(string directive)
    {
        var parsed = Parse($"""
            struct Sample<T>
                {directive}
                    var value: T has get, set
                    func getValue() -> T => value

            contract Sequence
                {directive}
                    associate Element is Comparable
                    var count: i32 has get
            """);
        AssertValid(parsed);
        RoundTrip(parsed);
        var sample = Assert.Single(parsed.RootKoto.NestedDeclarationContainers.OfType<StructKoto>());
        var body = Assert.Single(sample.Members) switch
        {
            CompileTimeIfKoto conditional => Assert.IsType<CodeBlockKoto>(conditional.Target),
            CompileTimeCaseGroupKoto cases => cases.Arms[0].Body,
            CodeBlockKoto block => block,
            _ => throw new InvalidOperationException(),
        };
        Assert.IsType<PropertyKoto>(body.Items[0]);
        Assert.IsType<FunctionKoto>(body.Items[1]);
    }

    [Fact]
    public void DeferredDirectiveDoesNotBecomeAnUnconditionalFunctionConstraint()
    {
        var parsed = Parse("""
            func inspect<T>(value: T)
                #if unknown
                T is Comparable
                return
            """);
        AssertValid(parsed);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.IsType<CompileTimeIfKoto>(Assert.Single(function.TypeConstraints));
        RoundTrip(parsed);
    }

    [Fact]
    public void NestedGenericClosersDoNotChangeShiftAndComparisonOperators()
    {
        var parsed = Parse("""
            var value: List<List<i32>>=source
            var result = factory<List<i32>>(value)
            var shifted = value >> 2
            var comparison = value >= 2
            """);
        AssertValid(parsed);
        var items = parsed.GeneratedFunction!.Body!.Items.Cast<FieldKoto>().ToArray();
        Assert.IsType<InvocationKoto>(items[1].InitializerKoto);
        Assert.IsType<GreaterThanGreaterThanKoto>(items[2].InitializerKoto);
        Assert.IsType<GreaterThanEqualsKoto>(items[3].InitializerKoto);
        RoundTrip(parsed);
    }

    [Fact]
    public void ParsesDereferenceWithoutThrowing()
    {
        var parsed = Parse("var value = *pointer");
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.IsType<UnwrapKoto>(field.InitializerKoto);
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("var bad =")]
    [InlineData("var bad = 1 +")]
    [InlineData("var bad = -")]
    [InlineData("var bad = &value")]
    [InlineData("var bad: ?")]
    [InlineData("var bad: ref/")]
    [InlineData("var bad: List<i32")]
    [InlineData("var bad: ref/T from")]
    [InlineData("var bad: ref/T from source.")]
    [InlineData("var bad: ref/T from source and")]
    public void RecoversFromMalformedSyntaxWithoutLosingTheNextDeclaration(string source)
    {
        var parsed = Parse(source + "\nvar after = 1");
        Assert.NotEmpty(parsed.DiagnosticCollection.GetArray());
        Assert.Contains(parsed.GeneratedFunction!.Body!.Items.OfType<FieldKoto>(), x => x.NameKoto.IdentifierName == "after");
    }

    [Fact]
    public void InterpolationDiagnosticsUseOriginalSourceOffsets()
    {
        const string Source = "func f()\n    var value = \"\\(x + )\"";
        var parsed = Parse(Source);
        var diagnostic = Assert.Single(parsed.DiagnosticCollection.GetArray());
        Assert.Equal(Source.LastIndexOf(')'), diagnostic.Span.Start);
    }

    [Fact]
    public void WritingStringsPreservesPhysicalNewlinesAndIndentation()
    {
        const string Source = "func f()\n    var text = \"one\r\n  two\rthree\"\n    var raw = \"\"\"one\r\n  two\"\"\"";
        var parsed = Parse(Source);
        AssertValid(parsed);
        var text = Write(parsed);
        Assert.Contains("\"one\r\n  two\rthree\"", text);
        Assert.Contains("\"\"\"one\r\n  two\"\"\"", text);
        RoundTrip(parsed);
    }

    [Fact]
    public void ParsesMultilineInterpolationInAnIndentedFunction()
    {
        var parsed = Parse("""
            func f()
                var value = "Total: \(
                    sum(values)
                )"
                return
            """);
        AssertValid(parsed);
        RoundTrip(parsed);
    }

    [Fact]
    public void PreservesExpressionBodiedDestructor()
    {
        var parsed = Parse("struct Resource\n    deinit => release()");
        AssertValid(parsed);
        Assert.Contains("deinit => release()", Write(parsed));
        RoundTrip(parsed);
    }

    [Fact]
    public void PreservesConditionalAssociatedTypeConstraints()
    {
        var parsed = Parse("""
            contract Sequence
                #if unknown
                associate Element is Comparable
                var count: i32 has get
            """);
        AssertValid(parsed);
        Assert.Contains("associate Element", Write(parsed));
        RoundTrip(parsed);
    }

    [Theory]
    [InlineData("#if true")]
    [InlineData("#if false")]
    [InlineData("func f()\n    #if unknown\nfunc g() => 1")]
    [InlineData("group Example\n    #if true\ngroup Other")]
    public void DiagnosesCompileTimeIfWithoutATarget(string source)
        => Assert.NotEmpty(Parse(source).DiagnosticCollection.GetArray());

    private static Kotonoha Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        return kotonoha;
    }

    private static void AssertValid(Kotonoha parsed)
        => Assert.True(
            parsed.DiagnosticCollection.GetArray().Length == 0,
            string.Join(Environment.NewLine, parsed.DiagnosticCollection.GetArray().Select(x => $"{x.Span}: {x.Message}")));

    private static string Write(Kotonoha parsed)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            parsed.RootKoto.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void RoundTrip(Kotonoha parsed)
    {
        var text = Write(parsed);
        var reparsed = Parse(text);
        AssertValid(reparsed);
        Assert.Equal(text, Write(reparsed));
        var bytes = TinyhandSerializer.Serialize(parsed);
        var compilation = Compilation.CreateForTest();
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        Assert.NotNull(restored);
        restored.OnDeserialized(compilation);
        Assert.Equal(text, Write(restored));
    }
}

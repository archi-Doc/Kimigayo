// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class ExpressionPrecedenceTest
{
    public static IEnumerable<object[]> ComparisonPairs()
    {
        string[] operators = ["<", "<=", ">", ">=", "==", "!="];
        foreach (var first in operators)
        {
            foreach (var second in operators)
            {
                yield return [first, second];
            }
        }
    }

    [Theory]
    [InlineData("flags & mask == 0", "(flags & mask) == 0")]
    [InlineData("0 == flags & mask", "0 == (flags & mask)")]
    [InlineData("flags ^ mask != 0", "(flags ^ mask) != 0")]
    [InlineData("flags | mask == expected", "(flags | mask) == expected")]
    [InlineData("a & b < c | d", "(a & b) < (c | d)")]
    [InlineData("a + b * c", "a + (b * c)")]
    [InlineData("a - b - c", "(a - b) - c")]
    [InlineData("a / b * c % d", "((a / b) * c) % d")]
    [InlineData("1 << n + 1", "1 << (n + 1)")]
    [InlineData("a + b << count", "(a + b) << count")]
    [InlineData("a << b >> c", "(a << b) >> c")]
    [InlineData("a & b << c", "a & (b << c)")]
    [InlineData("a & b + c", "a & (b + c)")]
    [InlineData("a | b ^ c & d", "a | (b ^ (c & d))")]
    [InlineData("a & b & c", "(a & b) & c")]
    [InlineData("a ^ b ^ c", "(a ^ b) ^ c")]
    [InlineData("a | b | c", "(a | b) | c")]
    [InlineData("not ready and flags & mask != 0", "(not ready) and ((flags & mask) != 0)")]
    [InlineData("a < b and b <= c or done", "((a < b) and (b <= c)) or done")]
    [InlineData("a or b and c", "a or (b and c)")]
    [InlineData("a and b and c", "(a and b) and c")]
    [InlineData("a or b or c", "(a or b) or c")]
    [InlineData("not not ready", "not (not ready)")]
    [InlineData("a + b@i64 * c", "a + ((b@i64) * c)")]
    [InlineData("-value@i64", "(-value)@i64")]
    [InlineData("-128@i8", "(-128)@i8")]
    [InlineData("+value@i64", "(+value)@i64")]
    [InlineData("*pointer@i64", "(*pointer)@i64")]
    [InlineData("++value@i64", "(++value)@i64")]
    [InlineData("--value@i64", "(--value)@i64")]
    [InlineData("value++@i64", "(value++)@i64")]
    [InlineData("value--@i64", "(value--)@i64")]
    [InlineData("not ready@bool", "(not ready)@bool")]
    [InlineData("value@i64@f64", "(value@i64)@f64")]
    [InlineData("value@i64 < limit", "(value@i64) < limit")]
    [InlineData("value@i64 <= limit", "(value@i64) <= limit")]
    [InlineData("value@i64 == limit", "(value@i64) == limit")]
    [InlineData("value@i64 >> shift", "(value@i64) >> shift")]
    [InlineData("value@T < limit", "(value@T) < limit")]
    [InlineData("value@T<limit", "(value@T) < limit")]
    [InlineData("value@T < other@T", "(value@T) < (other@T)")]
    [InlineData("value@Box<i32> < limit", "(value@Box<i32>) < limit")]
    [InlineData("value@A.B<i32> < limit", "(value@A.B<i32>) < limit")]
    [InlineData("value@A<B<C>> < limit", "(value@A<B<C>>) < limit")]
    [InlineData("value@ref/i32 < limit", "(value@ref/i32) < limit")]
    [InlineData("list@uniq.add(1).add(2)", "((list@uniq).add(1)).add(2)")]
    [InlineData("counter@uniq()", "(counter@uniq)()")]
    [InlineData("builder@move.finish()", "(builder@move).finish()")]
    [InlineData("values@ref[0]", "(values@ref)[0]")]
    [InlineData("holder@uniq.items.append(1)", "((holder@uniq).items).append(1)")]

    // SPEC F.9.4: a group or a fixed array after "@" is a DelimitedTarget, so its "." is the adapted value's PostfixSuffix (F.9.1), on a
    // chain line too (F.4.6); a BoundContainerQualifier is no target, and a projection target is grouped (F.5's notes).
    [InlineData("pair@(i32, u8).0", "(pair@(i32, u8)).0")]
    [InlineData("pair@(i32, u8)\n    .0", "(pair@(i32, u8)).0")]
    [InlineData("pair@ref/(i32, u8).0", "(pair@ref/(i32, u8)).0")]
    [InlineData("pair@(i32, u8).(C).Item", "(pair@(i32, u8)).(C).Item")]
    [InlineData("pair@().(C).Item", "(pair@()).(C).Item")]
    [InlineData("pair@(S{k}).Item", "(pair@(S{k})).Item")]
    [InlineData("pair@((i32, u8).(C).Item).0", "(pair@((i32, u8).(C).Item)).0")]
    [InlineData("items@[2 of i32].length", "(items@[2 of i32]).length")]
    [InlineData("items@[2 of i32]\n    .length", "(items@[2 of i32]).length")]
    [InlineData("items@ref/[2 of i32].length", "(items@ref/[2 of i32]).length")]
    [InlineData("items@[2 of i32].0", "(items@[2 of i32]).0")]
    [InlineData("try pending@move * 2", "(try (pending@move)) * 2")]
    [InlineData("try prepare() + 1", "(try prepare()) + 1")]
    [InlineData("try prepare().count", "try (prepare().count)")]
    [InlineData("try a * b", "(try a) * b")]
    [InlineData("try try nested@move", "try (try (nested@move))")]
    [InlineData("*pointer + offset", "(*pointer) + offset")]
    [InlineData("^count + offset", "(^count) + offset")]
    [InlineData("pointer@raw/i32 + offset", "(pointer@raw/i32) + offset")]
    [InlineData("make<A<B>>(x)[^1].value * -count@i64", "(make<A<B>>(x)[^1].value) * ((-count)@i64)")]
    [InlineData("start + 1..end - 1", "(start + 1)..(end - 1)")]
    [InlineData("start + 1..=end - 1", "(start + 1)..=(end - 1)")]
    [InlineData("start..end or flag", "start..(end or flag)")]
    [InlineData("start or flag..end", "(start or flag)..end")]
    [InlineData("..end + 1", "..(end + 1)")]
    [InlineData("..=end + 1", "..=(end + 1)")]
    [InlineData("values[start + 1..^1]", "values[(start + 1)..(^1)]")]
    [InlineData("target = flags & mask == 0", "target = ((flags & mask) == 0)")]
    [InlineData("target = start..end", "target = (start..end)")]
    [InlineData("a = b = c", "a = (b = c)")]
    [InlineData("a += b *= c", "a += (b *= c)")]
    [InlineData("a <<= b | c", "a <<= (b | c)")]
    [InlineData("a < b = c", "(a < b) = c")]
    [InlineData("return a + b * c", "return (a + (b * c))")]
    [InlineData("T is A and B", "(T is A) and B")]
    [InlineData("T is not A or B", "(T is not A) or B")]
    [InlineData("P or T is A and not B", "P or ((T is A) and (not B))")]
    [InlineData("(T is A) and flags & mask == 0", "(T is A) and ((flags & mask) == 0)")]
    [InlineData("if flags & mask == 0 => -value@i64\nelse => 0", "if (flags & mask) == 0 => (-value)@i64\nelse => 0")]
    public void GroupsExpressionsLikeExplicitParentheses(string expression, string grouped)
    {
        // These tests cover syntax; some combinations intentionally have no valid Type.
        var actual = ParseExpression(expression);
        var expected = Describe(ParseExpression(grouped));
        Assert.Equal(expected, Describe(actual));

        // Writing and reparsing must preserve the new interpretation as well.
        Assert.Equal(expected, Describe(ParseExpression(actual.ToString())));
    }

    [Theory]
    [MemberData(nameof(ComparisonPairs))]
    public void RejectsEveryUnparenthesizedComparisonPairAndRecovers(string first, string second)
    {
        var source = $"let result = a {first} b {second} c\nlet next = 42";
        var (body, diagnostics) = Parse(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ChainedComparison_Kd), diagnostic.Code);
        Assert.Equal(source.IndexOf($"b {second}", StringComparison.Ordinal) + 2, diagnostic.Span.Start);
        Assert.Equal(second.Length, diagnostic.Span.Length);
        Assert.Equal("next", Assert.IsType<FieldKoto>(body.Items[1]).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("value is Dog == flag", "==")]
    [InlineData("value is not Dog != flag", "!=")]
    [InlineData("a == value is Dog", "is")]
    [InlineData("value is Dog is Cat", "is")]
    [InlineData("value is Dog < limit", "<")]
    public void RuntimeIsTestsAreNonAssociativeComparisons(string expression, string second)
    {
        var source = $"let result = {expression}\nlet next = 42";
        var (body, diagnostics) = Parse(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ChainedComparison_Kd), diagnostic.Code);
        Assert.Equal(source.LastIndexOf(second, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal("next", Assert.IsType<FieldKoto>(body.Items[1]).NameKoto.IdentifierName);
    }

    [Fact]
    public void RequirementOperatorsSpanTheirOperands()
    {
        const string source = "func f<T>(x: T)\n    T is not Copy and Owned or Copy\n    ()";
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        Assert.Empty(TestDiagnostics.Of(compilation));
        var function = compilation.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();
        var requirement = ParseTestHelper.Unary(KotoKind.Not, Assert.IsType<IsKoto>(Assert.Single(function.TypeConstraints)).Right);
        var disjunction = ParseTestHelper.Binary(KotoKind.Or, requirement.Operand);
        var conjunction = ParseTestHelper.Binary(KotoKind.And, disjunction.Left);
        Assert.Equal("not Copy and Owned or Copy", source.Substring(requirement.Span.Start, requirement.Span.Length));
        Assert.Equal("Copy and Owned or Copy", source.Substring(disjunction.Span.Start, disjunction.Span.Length));
        Assert.Equal("Copy and Owned", source.Substring(conjunction.Span.Start, conjunction.Span.Length));
    }

    [Fact]
    public void RuntimeIsTestKeepsAdjacentTypeArguments()
    {
        var test = Assert.IsType<IsKoto>(ParseExpression("value is Box<i32>"));
        Assert.IsType<GenericsKoto>(test.Right);
        Assert.Empty(Parse("let result = (value is Dog) == flag").Diagnostics);
    }

    [Theory]
    [MemberData(nameof(ComparisonPairs))]
    public void AllowsExplicitlyGroupedOrSeparateComparisons(string first, string second)
    {
        var source = $"""
            let left = (a {first} b) {second} c
            let right = a {first} (b {second} c)
            let conjunction = a {first} b and b {second} c
            let disjunction = a {first} b or b {second} c
            """;
        var (body, diagnostics) = Parse(source);

        Assert.Empty(diagnostics);
        Assert.Equal(4, body.Items.Count);
    }

    [Fact]
    public void AppliesPrecedenceInsideCompileTimeConditionsAndInterpolation()
    {
        var (body, diagnostics) = Parse("""
            #if true or 64 == 0 and false
            let selected = 1
            let message = "clear = \(flags & mask == 0)"
            """);

        Assert.Empty(diagnostics);
        Assert.Equal("selected", Assert.IsType<FieldKoto>(body.Items[0]).NameKoto.IdentifierName);

        var field = Assert.IsType<FieldKoto>(body.Items[1]);
        var text = Assert.IsType<InterpolatedStringKoto>(field.InitializerKoto);
        var embedded = ParseTestHelper.Binary(KotoKind.EqualsEquals, Assert.Single(text.Expressions));
        ParseTestHelper.Binary(KotoKind.Ampersand, embedded.Left);
    }

    [Fact]
    public void DistinguishesConversionBeforeAndAfterNegation()
    {
        var afterNegation = Assert.IsType<ConversionKoto>(ParseExpression("-value@i64"));
        ParseTestHelper.Unary(KotoKind.PrefixMinus, afterNegation.Left);

        var beforeNegation = ParseTestHelper.Unary(KotoKind.PrefixMinus, ParseExpression("-(value@i64)"));
        Assert.IsType<ConversionKoto>(Assert.IsType<ParenthesizedKoto>(beforeNegation.Operand).Operand);
    }

    // SPEC F.9.4 and 19.4: Adapted := Prefix ("@" (ClosedTarget PostfixSuffix* | OpenTarget))*. A form outside the production is one
    // grammar record at the marked token, the same in selected and excluded syntax; a suffix after an OpenTarget is read over the
    // adaptation (the grouping the repair writes), so a second suffix adds no record.
    [Theory]
    [InlineData("items@Array<i32>[0]", "[0]", "postfix suffix after an open adaptation target")]
    [InlineData("p@P(1)", "(1)", "postfix suffix after an open adaptation target")]
    [InlineData("p@P(1).v", "(1)", "postfix suffix after an open adaptation target")]
    [InlineData("p@ref/P[0]", "[0]", "postfix suffix after an open adaptation target")]
    [InlineData("p@Box<Box<i32>>(1)", "(1)", "postfix suffix after an open adaptation target")]
    [InlineData("p@P{k}.v", ".v", "postfix suffix after an open adaptation target")]
    [InlineData("x@i32?.foo", ".foo", "postfix suffix after an open adaptation target")]
    [InlineData("x@i32?\n    .foo", ".foo", "postfix suffix after an open adaptation target")]
    [InlineData("pair@(i32, u8)?.0", ".0", "postfix suffix after an open adaptation target")]
    [InlineData("a@[2 of i32]?[0]", "[0]", "postfix suffix after an open adaptation target")]
    [InlineData("x@i64++", "++", "postfix suffix after an open adaptation target")]
    [InlineData("x@i64--", "--", "postfix suffix after an open adaptation target")]
    [InlineData("p@P@follow", "@follow", "postfix suffix after an open adaptation target")]
    [InlineData("f(p@(P)@P(1))", "(1)", "postfix suffix after an open adaptation target")]

    // SPEC 13.5.1, F.5: a bound Container qualifier never heads the target's own path, through any grouping or Semantics prefix.
    [InlineData("x@((S{k}).Item)", "(S{k})", "bound Container qualifier in an adaptation target")]
    [InlineData("x@((S during z).Item)", "(S during z)", "bound Container qualifier in an adaptation target")]
    [InlineData("x@((S{k}).(K).Item)", "(S{k})", "bound Container qualifier in an adaptation target")]
    [InlineData("x@((S{k}).Item<i32>)", "(S{k})", "bound Container qualifier in an adaptation target")]
    [InlineData("x@(((S{k}).Item))", "(S{k})", "bound Container qualifier in an adaptation target")]
    [InlineData("x@ref/((S{k}).Item)", "(S{k})", "bound Container qualifier in an adaptation target")]
    [InlineData("x@(ref/(S{k}).Item)", "(S{k})", "bound Container qualifier in an adaptation target")]
    public void AnAdaptationOutsideItsGrammarIsOneRecordInEverySelection(string expression, string at, string phrase)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = (excluded ? "#if false\n    let result = " + expression.Replace("\n", "\n    ", StringComparison.Ordinal) : "let result = " + expression) + "\nlet next = 42";
            var diagnostic = Assert.Single(Parse(source).Diagnostics);
            Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), diagnostic.Code);
            Assert.Contains(phrase, diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), diagnostic.Span.Start);
        }
    }

    // The valid twins: a ClosedTarget takes the value's suffixes, a grouped adaptation any suffix, an OpenTarget none, and "." continues
    // an open Type head (a PathSuffix); a second "@" follows any target.
    [Theory]
    [InlineData("(items@Array<i32>)[0]")]
    [InlineData("(p@P)(1)")]
    [InlineData("(pair@(i32, u8)?).0")]
    [InlineData("p@(P)(1)")]
    [InlineData("a@[2 of i32][0]")]
    [InlineData("a@(Array<i32>)[0]")]
    [InlineData("x@(i32?)[0]")]
    [InlineData("x@(i64)++")]
    [InlineData("p@(P)@follow")]
    [InlineData("pair@ref/(i32, u8).0")]
    [InlineData("a@uniq[0]")]
    [InlineData("p@move(1)")]
    [InlineData("p@P.Item")]
    [InlineData("p@P{k}")]
    [InlineData("p@P@ref.v")]
    [InlineData("x@i32?@ref.foo")]
    [InlineData("x@i32? == y")]
    [InlineData("f(p@P)(1)")]
    [InlineData("a[x@isize]")]
    [InlineData("[x@i64, 1]")]

    // SPEC 13.5.1, F.5: another PathQualifier is grouped as a target, a plain path and a binding set are no qualifier, and a Type nested
    // in the target's Core (a Type argument, an Optional's payload, a Tuple or fixed array element, a Function Type's part) keeps the form.
    [InlineData("x@((i32, u8).(C).Item)")]
    [InlineData("x@(((i32) -> i32).(C).Item)")]
    [InlineData("x@([2 of i32].(C).Item)")]
    [InlineData("x@(S.Item)")]
    [InlineData("x@(S{k})")]
    [InlineData("x@(S{k}).Item")]
    [InlineData("x@Option<(S{k}).Item>")]
    [InlineData("x@((S{k}).Item)?")]
    [InlineData("x@((S{k}).Item, i32)")]
    [InlineData("x@[2 of (S{k}).Item]")]
    [InlineData("x@(((S{k}).Item) -> i32)")]
    public void AnAdaptationInsideItsGrammarHasNoRecordInEverySelection(string expression)
    {
        Assert.Empty(Parse("let result = " + expression + "\nlet next = 42").Diagnostics);
        Assert.Empty(Parse("#if false\n    let result = " + expression + "\nlet next = 42").Diagnostics);
    }

    private static Koto ParseExpression(string expression)
    {
        var (body, diagnostics) = Parse("let result = " + expression);
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.Select(x => x.Message)));
        return Assert.IsType<FieldKoto>(Assert.Single(body.Items)).InitializerKoto!;
    }

    private static (CodeBlockKoto Body, TestDiagnostic[] Diagnostics) Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        return (kotonoha.GeneratedFunction!.Body!, TestDiagnostics.Of(kotonoha));
    }

    private static string Describe(Koto koto)
    {
        if (koto is ParenthesizedKoto parentheses)
        {
            return Describe(parentheses.Operand);
        }

        var children = koto.Children().ToArray();
        return children.Length == 0
            ? $"{koto.Akind}:{koto}"
            : $"{koto.Akind}({string.Join(", ", children.Select(Describe))})";
    }
}

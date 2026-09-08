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
    [InlineData("*pointer + offset", "(*pointer) + offset")]
    [InlineData("^count + offset", "(^count) + offset")]
    [InlineData("pointer@unsafe/i32 + offset", "(pointer@unsafe/i32) + offset")]
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
    [InlineData("T is A and B", "T is (A and B)")]
    [InlineData("T is not A or B", "T is not (A or B)")]
    [InlineData("P or T is A and not B", "P or (T is (A and (not B)))")]
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
        Assert.Equal(nameof(DiagnosticCode.ChainedComparison_Kd), diagnostic.Entry.Name);
        Assert.Equal(source.IndexOf($"b {second}", StringComparison.Ordinal) + 2, diagnostic.Span.Start);
        Assert.Equal(second.Length, diagnostic.Span.Length);
        Assert.Equal("next", Assert.IsType<FieldKoto>(body.Items[1]).NameKoto.IdentifierName);
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
            #if enabled or flags == 0 and ready
            let selected = 1
            let message = "clear = \(flags & mask == 0)"
            """);

        Assert.Empty(diagnostics);
        var directive = Assert.IsType<CompileTimeIfKoto>(body.Items[0]);
        var disjunction = Assert.IsType<OrKoto>(directive.Condition);
        var conjunction = Assert.IsType<AndKoto>(disjunction.Right);
        Assert.IsType<EqualsEqualsKoto>(conjunction.Left);

        var field = Assert.IsType<FieldKoto>(body.Items[1]);
        var text = Assert.IsType<InterpolatedStringKoto>(field.InitializerKoto);
        var embedded = Assert.IsType<EqualsEqualsKoto>(Assert.Single(text.Expressions));
        Assert.IsType<AmpersandKoto>(embedded.Left);
    }

    [Fact]
    public void DistinguishesConversionBeforeAndAfterNegation()
    {
        var afterNegation = Assert.IsType<ConversionKoto>(ParseExpression("-value@i64"));
        Assert.IsType<PrefixMinusKoto>(afterNegation.Left);

        var beforeNegation = Assert.IsType<PrefixMinusKoto>(ParseExpression("-(value@i64)"));
        Assert.IsType<ConversionKoto>(Assert.IsType<ParenthesizedKoto>(beforeNegation.Operand).Operand);
    }

    private static Koto ParseExpression(string expression)
    {
        var (body, diagnostics) = Parse("let result = " + expression);
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.Select(x => x.Message)));
        return Assert.IsType<FieldKoto>(Assert.Single(body.Items)).InitializerKoto!;
    }

    private static (CodeBlockKoto Body, Diagnostic[] Diagnostics) Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        return (kotonoha.GeneratedFunction!.Body!, kotonoha.DiagnosticCollection.GetArray());
    }

    private static string Describe(Koto koto)
    {
        if (koto is ParenthesizedKoto parentheses)
        {
            return Describe(parentheses.Operand);
        }

        var children = koto.ChildNodes.ToArray();
        return children.Length == 0
            ? $"{koto.Akind}:{koto}"
            : $"{koto.Akind}({string.Join(", ", children.Select(Describe))})";
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class CharLiteralParseTest
{
    [Theory]
    [InlineData("'A'", 0x41)]
    [InlineData("'あ'", 0x3042)]
    [InlineData("'😀'", 0x1F600)]
    [InlineData("' '", 0x20)]
    [InlineData("'~'", 0x7E)]
    [InlineData("'\u00A0'", 0xA0)]
    [InlineData("'\uD7FF'", 0xD7FF)]
    [InlineData("'\uE000'", 0xE000)]
    [InlineData("'\u0378'", 0x378)] // Unassigned code point.
    [InlineData("'\uFDD0'", 0xFDD0)] // Noncharacter.
    [InlineData("'\U0010FFFF'", 0x10FFFF)]
    [InlineData("'é'", 0xE9)]
    [InlineData("'Å'", 0x212B)] // Must not normalize to U+00C5.
    [InlineData("'\u0301'", 0x301)]
    [InlineData("'\"'", 0x22)]
    [InlineData("'('", 0x28)]
    [InlineData("')'", 0x29)]
    [InlineData("'\\0'", 0)]
    [InlineData("'\\e'", 0x1B)]
    [InlineData("'\\t'", 9)]
    [InlineData("'\\n'", 10)]
    [InlineData("'\\r'", 13)]
    [InlineData("'\\\\'", 0x5C)]
    [InlineData("'\\\''", 0x27)]
    [InlineData("'\\\"'", 0x22)]
    [InlineData("'\\u(000041)'", 0x41)]
    [InlineData("'\\u(E9)'", 0xE9)]
    [InlineData("'\\u(301)'", 0x301)]
    [InlineData("'\\u(1f600)'", 0x1F600)]
    [InlineData("'\\u(2028)'", 0x2028)]
    [InlineData("'\\u(2029)'", 0x2029)]
    [InlineData("'\\u(10FFFF)'", 0x10FFFF)]
    public void ParsesOneScalarAndPreservesSpellingAndOffsets(string literal, int expected)
    {
        const string Prefix = "let value: char = ";
        var parsed = Parse(Prefix + literal);
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        var node = Assert.IsType<CharLiteralKoto>(field.InitializerKoto);
        Assert.Equal(expected, node.Value!.Value.Value);
        Assert.Equal(KotoKind.CharLiteral, node.Akind);
        Assert.Same(field, node.Parent);
        Assert.Equal(new SourceSpan(Prefix.Length, literal.Length), node.Span);
        Assert.Equal(literal, node.ToString());
        Assert.Equal(new ControlFlowType("char"), new SyntaxControlFlowTypes().GetExpressionType(node));

        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(parsed));
        Assert.NotNull(restored);
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(parsed.GeneratedFunction.ToString(), restored.GeneratedFunction!.ToString());
        var reparsed = Parse("let value = " + node.ToString());
        AssertValid(reparsed);
        var reparsedField = Assert.IsType<FieldKoto>(Assert.Single(reparsed.GeneratedFunction!.Body!.Items));
        Assert.Equal(expected, Assert.IsType<CharLiteralKoto>(reparsedField.InitializerKoto).Value!.Value.Value);
    }

    [Theory]
    [InlineData("''")]
    [InlineData("'ab'")]
    [InlineData("'🇯🇵'")]
    [InlineData("'e\u0301'")]
    [InlineData("'e\\u(301)'")]
    [InlineData("'\\u(41)B'")]
    [InlineData("'\\u(41)\\u(42)'")]
    [InlineData("'\\u(D800)'")]
    [InlineData("'\\u(DFFF)'")]
    [InlineData("'\\u(D83D)\\u(DE00)'")]
    [InlineData("'\\u(110000)'")]
    [InlineData("'\\u()'")]
    [InlineData("'\\u(0000041)'")]
    [InlineData("'\\u( 41)'")]
    [InlineData("'\\u(+41)'")]
    [InlineData("'\\u(4_1)'")]
    [InlineData("'\\u(0x41)'")]
    [InlineData("'\\u(４１)'")]
    [InlineData("'\\u(41'")]
    [InlineData("'\\u41'")]
    [InlineData("'\\x41'")]
    [InlineData("'\\(value)'")]
    [InlineData("'\\'")]
    [InlineData("'abc")]
    [InlineData("'\n'")]
    [InlineData("'\r\n'")]
    [InlineData("'\u2028'")]
    [InlineData("'\u2029'")]
    public void RejectsMalformedLiteralDuringParsing(string literal)
        => Assert.NotEmpty(Parse("let value = " + literal).DiagnosticCollection.GetArray());

    [Fact]
    public void RequiresEscapesForEveryControlCodePoint()
    {
        foreach (var scalar in Enumerable.Range(0, 32).Concat(Enumerable.Range(0x7F, 33)))
        {
            Assert.NotEmpty(Parse("let value = '" + (char)scalar + "'").DiagnosticCollection.GetArray());
            AssertValid(Parse($"let value = '\\u({scalar:X})'"));
        }
    }

    [Theory]
    [InlineData("'abc\n")]
    [InlineData("'abc\r\n")]
    [InlineData("'abc\r")]
    [InlineData("'\\\n")]
    public void UnterminatedLiteralDoesNotConsumeNextDeclaration(string broken)
    {
        var parsed = Parse("let bad = " + broken + "let next = '😀'");
        Assert.Contains(parsed.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.MissingCharLiteralEnd_Kd));
        var last = Assert.IsType<FieldKoto>(parsed.GeneratedFunction!.Body!.Items.Last());
        Assert.Equal(0x1F600, Assert.IsType<CharLiteralKoto>(last.InitializerKoto).Value!.Value.Value);
    }

    [Theory]
    [InlineData("'('")]
    [InlineData("')'")]
    [InlineData("'\"'")]
    [InlineData("'\\\''")]
    [InlineData("'\\\\'")]
    [InlineData("'\\u(1F600)'")]
    public void InterpolationSkipsCharLiteralDelimitersAndParentheses(string literal)
    {
        var parsed = Parse("let value = \"before \\(" + literal + ") after\"");
        AssertValid(parsed);
        var field = Assert.IsType<FieldKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        var interpolation = Assert.IsType<InterpolatedStringKoto>(field.InitializerKoto);
        var node = Assert.IsType<CharLiteralKoto>(Assert.Single(interpolation.Expressions));
        Assert.Equal(literal, node.ToString());
        Assert.Same(interpolation, node.Parent);
        Assert.Equal(literal, parsed.SourceDocuments[0].SourceText.Substring(node.Span.Start, node.Span.Length));
        AssertValid(Parse("let copy = " + interpolation.ToString()));
    }

    [Fact]
    public void CharIsAPrimitiveKeywordAndLiteralDelimitersSeparateTokens()
    {
        Assert.Equal(TokenKind.Char, TokenHelper.GetKeywordOrIdentifierKind("char"));
        Assert.True(TokenKind.Char.IsPrimitiveType());
        Assert.Equal("char", TokenKind.Char.ToText());
        AssertValid(Parse("func f() -> char\n    return'😀'"));
        Assert.NotEmpty(Parse("let char = 'A'").DiagnosticCollection.GetArray());
    }

    [Fact]
    public void DeclaredAndInferredCharTypesAreKnownToControlFlow()
    {
        var parsed = Parse("func f() -> char => '😀'");
        AssertValid(parsed);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(parsed.GeneratedFunction!.Body!.Items));
        Assert.Equal(new ControlFlowType("char"), new SyntaxControlFlowTypes().GetDeclaredType(function.ReturnType));
        Assert.Empty(parsed.Compilation.AnalyzeControlFlow().Issues);
    }

    private static Kotonoha Parse(string source)
    {
        var parsed = Compilation.CreateForTest().Kotonoha;
        parsed.CreateCodeContext().Parse(parsed.RootKoto, source);
        return parsed;
    }

    private static void AssertValid(Kotonoha parsed)
        => Assert.True(
            parsed.DiagnosticCollection.GetArray().Length == 0,
            string.Join(Environment.NewLine, parsed.DiagnosticCollection.GetArray().Select(x => x.Message)));
}

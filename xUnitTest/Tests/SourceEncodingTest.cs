// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class SourceEncodingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodesUtf8SourceWithOptionalBom(bool withBom)
    {
        const string Source = "let value: char = '😀'\nlet mark = 'Å'";
        var bytes = Encoding.UTF8.GetBytes((withBom ? "\uFEFF" : string.Empty) + Source);
        var document = SourceDocument.FromUtf8("test.kimi", bytes);
        Assert.Equal(Source, document.SourceText);
        Assert.Equal("test.kimi", document.Path);
        var parsed = Compilation.CreateForTest().Kotonoha;
        parsed.AddSource(document);
        Assert.Empty(parsed.Compilation.Kimigayo.GetOrAddDiagnosticCollection(document.Path).GetArray());
    }

    [Theory]
    [InlineData("80")] // Isolated continuation byte.
    [InlineData("C080")] // Overlong NUL.
    [InlineData("E381")] // Truncated sequence.
    [InlineData("EDA080")] // Surrogate.
    [InlineData("F4908080")] // Above U+10FFFF.
    [InlineData("FFFE4100")] // UTF-16 BOM must not select another encoding.
    public void RejectsMalformedUtf8WithoutReplacement(string hex)
        => Assert.Throws<DecoderFallbackException>(() => SourceDocument.FromUtf8("bad.kimi", Convert.FromHexString(hex)));

    [Theory]
    [InlineData("let value = '", "'")]
    [InlineData("let value = \"", "\"")]
    [InlineData("let value = \"\"\"", "\"\"\"")]
    [InlineData("// ", "")]
    [InlineData("/* ", " */")]
    public void RejectsUnpairedSurrogatesInHostText(string prefix, string suffix)
    {
        foreach (var invalid in new[] { "\uD800", "\uDC00", "\uD800A", "\uD800\uD800" })
        {
            var parsed = Compilation.CreateForTest().Kotonoha;
            parsed.AddSource(new SourceDocument("bad.kimi", prefix + invalid + suffix));
            var diagnostic = Assert.Single(parsed.Compilation.Kimigayo.GetOrAddDiagnosticCollection("bad.kimi").GetArray());
            Assert.Equal(nameof(DiagnosticCode.InvalidSourceEncoding_Kd), diagnostic.Entry.Name);
            Assert.Equal(new SourceSpan(prefix.Length, 1), diagnostic.Span);
        }
    }

    [Fact]
    public async Task ProjectBuildReportsInvalidUtf8FileAsAnError()
    {
        var compilation = Compilation.CreateForTest();
        var path = Path.Combine(Path.GetTempPath(), $"kimi-char-{Guid.NewGuid():N}.kimi");
        try
        {
            File.WriteAllBytes(path, [0x2F, 0x2F, 0x20, 0xED, 0xA0, 0x80]);
            compilation.Project.AddKimiFile(path);
            Assert.False(await compilation.Project.Build());
            var diagnostic = Assert.Single(compilation.Kimigayo.GetOrAddDiagnosticCollection(path).GetArray());
            Assert.Equal(nameof(DiagnosticCode.InvalidSourceEncoding_Kd), diagnostic.Entry.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("let value = 'ab'", false)]
    [InlineData("let value = '\\u(D800)'", false)]
    [InlineData("let value = '😀'", true)]
    public async Task ProjectBuildRespectsLiteralDiagnostics(string source, bool expected)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.AddSource("test.kimi", source);
        Assert.Equal(expected, await compilation.Project.Build());
    }

    [Fact]
    public async Task RebuildingAfterFixingACharLiteralClearsItsError()
    {
        var compilation = Compilation.CreateForTest();
        var path = Path.Combine(Path.GetTempPath(), $"kimi-char-{Guid.NewGuid():N}.kimi");
        try
        {
            File.WriteAllText(path, "let value = 'ab'");
            compilation.Project.AddKimiFile(path);
            Assert.False(await compilation.Project.Build());
            File.WriteAllText(path, "let value = '😀'");
            Assert.True(await compilation.Project.Build());
            Assert.Empty(compilation.Kimigayo.GetOrAddDiagnosticCollection(path).GetArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

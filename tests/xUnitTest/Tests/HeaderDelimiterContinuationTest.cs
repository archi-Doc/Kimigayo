// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class HeaderDelimiterContinuationTest
{
    [Theory]
    [InlineData(".0", "Tuple")]
    [InlineData("@move.0", "AdaptedTuple")]
    public void ACompletedBodyKeepsItsSuffixAndTheRemainingArguments(string suffix, string name)
    {
        var source = "func select(x: i32, y: i32) -> i32 => x + y\nlet result = select((loop\n    exit (7, 9)\n)" + suffix + ", 2)\nConsole.writeLine(\"\\(result)\")";
        ScalarEmissionTest.EmitFixture("HeaderDelimiter" + name, source, "9\n");
    }

    [Fact]
    public void AnOuterCloserMayFollowTheSuffixOnTheNextLine()
    {
        const string Source = "func select(x: i32, y: i32) -> i32 => x + y\nlet result = select((loop\n    exit (7, 9)\n).0,\n    2\n)\nConsole.writeLine(\"\\(result)\")";
        ScalarEmissionTest.EmitFixture("HeaderDelimiterNextLine", Source, "9\n");
    }

    [Fact]
    public void AMissingOuterCloserAfterASuffixDoesNotCaptureTheNextStatement()
    {
        const string Source = "let value = select((loop\n    exit (7, 9)\n).0\nlet next = 3";
        var tree = ParseTestHelper.Parse(Source);
        Assert.Equal("MissingSyntax_Kd", Assert.Single(TestDiagnostics.Of(tree)).Code);
        var last = tree.GeneratedFunction!.Body!.Items[^1];
        Assert.Equal("next", Assert.IsType<FieldKoto>(last).NameKoto.IdentifierName);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void CompletedHeaderDelimitersReuseTheirStack()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics.GetOrAddCollection("header-delimiters");
        var source = new SourceDocument("Header.kimi", "let value = select((loop\n    exit (7, 9)\n).0,\n    2\n)\nlet next = 3");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var tokenizer = new Tokenizer(diagnostics, source);
            try
            {
                tokenizer.ReadAll();
                foreach (var token in tokenizer.Tokens)
                {
                    valid &= !token.IsMissing;
                }
            }
            finally
            {
                tokenizer.Dispose();
            }
        }));
        Assert.True(valid);
    }
}

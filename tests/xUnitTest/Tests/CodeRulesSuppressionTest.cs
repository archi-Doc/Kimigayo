// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class CodeRulesSuppressionTest
{
    [Theory]
    [InlineData("#pragma warning disable\nclass C { }", 1)]
    [InlineData("#pragma warning disable KR2001\nclass C { }", 1)]
    [InlineData("#pragma warning disable IDE0052, CS0169\nclass C { }", 1)]
    [InlineData("#pragma warning disable CS0169\nclass C { }", 0)]
    [InlineData("#pragma warning restore KR2001\nclass C { }", 0)]
    [InlineData("#if false\n#pragma warning disable\n#endif\nclass C { }", 0)]
    [InlineData("using S = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute; [S(\"CodeRules\", \"KR2001\")] class C { }", 1)]
    [InlineData("using System.Diagnostics.CodeAnalysis; [assembly: SuppressMessage(\"CodeRules\", \"KR2001\")] class C { }", 1)]
    [InlineData("using System.Diagnostics.CodeAnalysis; class C { [SuppressMessage(checkId: \"KR2001:title\", category: \"CodeRules\")] void M() { } }", 1)]
    [InlineData("using System.Diagnostics.CodeAnalysis; [UnconditionalSuppressMessage(\"CodeRules\", \"KR2001\")] class C { }", 1)]
    [InlineData("using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"CodeRules\", \"CA1000\")] class C { }", 0)]
    public async Task ProtectedSuppressionsAreDiagnosed(string source, int count)
    {
        var diagnostics = await CodeRulesTestHelper.Analyze(source);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR9004", x.Id));
    }

    [Fact]
    public async Task SelfSuppressionRemainsVisibleToTheErrorLog()
    {
        var diagnostics = await CodeRulesTestHelper.Analyze(
            "using System.Diagnostics.CodeAnalysis; class C { [SuppressMessage(\"CodeRules\", \"KR9004\")] void M() { } }");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR9004", diagnostic.Id);
        Assert.True(diagnostic.IsSuppressed);
    }
}

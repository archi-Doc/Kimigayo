// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.15, 23.1.1: a definition beyond the 64-case bound verifies none of its cases, so its generic adaptations depend on its one
// record and none of their plans is committed.
public class GenericAdaptationLimitTest
{
    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void NestedAdaptationsNeverCommitAPartiallyCheckedPlan(int selectors, bool valid)
    {
        var source = Program(selectors);
        var path = Path.GetFullPath("generic-adaptation-limit.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (valid)
        {
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
            return;
        }

        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), static conversion => Assert.Null(conversion.ConversionOf().Adaptation));
        Assert.False(c.Generate(TextWriter.Null));
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.OwnershipCaseLimit_Kd), DiagnosticCategory.Resource), (error.Code, error.Category));
        Assert.StartsWith("inspect<a/A", source.Substring(error.Span!.Value.Start, error.Span.Value.Length), StringComparison.Ordinal);
        Assert.Collection(
            error.Reason!,
            static fact => Assert.Equal(("cases", "128"), (fact.Name, fact.Value)),
            static fact => Assert.Equal(("limit", "64"), (fact.Name, fact.Value)));

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("128 cases", console.Text, StringComparison.Ordinal);
        Assert.Contains("at most 64", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("128 cases", sent.Message, StringComparison.Ordinal);
            Assert.Contains("at most 64", sent.Message, StringComparison.Ordinal);
        }
    }

    private static string Program(int selectors)
    {
        var source = new StringBuilder("func inspect<");
        for (var i = 0; i < selectors; i++)
        {
            if (i != 0)
            {
                source.Append(", ");
            }

            source.Append((char)('a' + i)).Append('/').Append((char)('A' + i));
        }

        source.Append(">(value: i32) -> ()\n");
        for (var i = 0; i < selectors; i++)
        {
            source.Append("    ").Append((char)('a' + i)).Append(" is owner or obj\n");
        }

        // Two expressions under the same definition beyond the bound give its one resource record.
        for (var expression = 0; expression < 2; expression++)
        {
            source.Append("    _ = value");
            for (var i = 0; i < selectors; i++)
            {
                source.Append('@').Append((char)('a' + i)).Append("@move");
            }

            source.Append('\n');
        }

        return source.Append("()").ToString();
    }
}

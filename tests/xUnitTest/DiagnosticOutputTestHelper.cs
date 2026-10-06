// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>Exercises public adapters after the caller has independently asserted the diagnostic record.</summary>
internal static class DiagnosticOutputTestHelper
{
    internal static void Single(CheckOutput check, string text, ITestOutputHelper output)
    {
        var record = Assert.Single(check.Diagnostics);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains(text, console.Text, StringComparison.Ordinal);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains(text, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        output.WriteLine(console.Text);
    }
}

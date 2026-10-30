// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class FreshProcessTest
{
    // A child takes no part in the run's evidence collection: KIMI_MIR_DIFF would start the difference ledger's assembly fixture in it,
    // which runs Compilation's type initializer before its test and aggregates the parent's open raw file. It keeps the pipeline
    // selection, whatever the case of the names, and every variable outside the KIMI_ namespace, under its own name: outside Windows,
    // names that differ only in case are distinct variables.
    [Fact]
    public void TheChildKeepsThePipelineSelectionAndNoVariableOfTheRunsEvidence()
    {
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = "path",
            ["HTTPS_PROXY"] = "upper",
            ["https_proxy"] = "lower",
            ["DOTNET_GCHighMemPercent"] = "0xA",
            ["Kimigayo"] = "outside the namespace",
            ["kimi_pipeline"] = "mir",
            ["KIMI_CODEGEN"] = "legacy", // No selection since G4 (DECISIONS R5-M160): another KIMI_ variable.
            ["KIMI_MIR_DIFF"] = "directory",
            ["KIMI_FIXTURE_DIRECTORY"] = "directory",
            ["KIMI_UNRELATED"] = "1",
        };
        var child = FreshProcess.ChildEnvironment(parent);
        string[] kept = ["DOTNET_GCHighMemPercent", "HTTPS_PROXY", "https_proxy", "Kimigayo", "PATH", "kimi_pipeline"];
        Assert.Equal(kept.Order(StringComparer.Ordinal), child.Keys.Order(StringComparer.Ordinal));
        Assert.All(child, x => Assert.Equal(parent[x.Key], x.Value));
    }
}

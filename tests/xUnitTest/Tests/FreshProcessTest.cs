// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class FreshProcessTest
{
    // A child takes no part in the run's evidence collection: the evidence variables (KIMI_DIAGNOSTIC_SNAPSHOT, KIMI_SEMANTIC_SNAPSHOT,
    // KIMI_MIR_PROBLEMS, KIMI_FIXTURE_DIRECTORY and the others) would make it write into the parent's evidence. It keeps no variable of the
    // KIMI_ namespace, whatever the case of the names, and every variable outside it, under its own name: outside Windows, names that
    // differ only in case are distinct variables.
    [Fact]
    public void TheChildKeepsNoVariableOfTheKimiNamespace()
    {
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = "path",
            ["HTTPS_PROXY"] = "upper",
            ["https_proxy"] = "lower",
            ["DOTNET_GCHighMemPercent"] = "0xA",
            ["Kimigayo"] = "outside the namespace",
            ["kimi_pipeline"] = "mir", // No selection since R6-check H12: another KIMI_ variable, here in lower case.
            ["KIMI_CODEGEN"] = "legacy", // No selection since G4 (DECISIONS R5-M160): another KIMI_ variable.
            ["KIMI_MIR_DIFF"] = "directory", // The difference ledger's until R6-check H10a: another KIMI_ variable.
            ["KIMI_FIXTURE_DIRECTORY"] = "directory",
            ["KIMI_UNRELATED"] = "1",
        };
        var child = FreshProcess.ChildEnvironment(parent);
        string[] kept = ["DOTNET_GCHighMemPercent", "HTTPS_PROXY", "https_proxy", "Kimigayo", "PATH"];
        Assert.Equal(kept.Order(StringComparer.Ordinal), child.Keys.Order(StringComparer.Ordinal));
        Assert.All(child, x => Assert.Equal(parent[x.Key], x.Value));
    }
}

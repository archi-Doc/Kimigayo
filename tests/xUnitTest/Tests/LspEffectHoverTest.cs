// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class LspEffectHoverTest : IDisposable
{
    private const string Source = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    F is Callable<uniq, () -> i32>\n        effect confined\n    return f()\npublic func main() => ()\n";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-effect-hover-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, true);
        }
    }

    [Fact]
    public void SnapshotDescribesOnlyValidContributingPremises()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var record = Assert.Single(HoverBuilder.CallableEffects(c), static x => x.Source.Value.EndsWith("Hello.kimi", StringComparison.Ordinal));
        Assert.Contains("Call: f()", record.Text);
        Assert.Contains("Available bound: confined", record.Text);
        Assert.Contains("Callable: F is Callable<() -> i32>", record.Text);
        Assert.DoesNotContain("BoundType {", record.Text);
    }

    [Fact]
    public async Task HoverListsContributingPremisesAndNeverUsesAnInvalidatedSnapshot()
    {
        // An independent diagnostic gives both document versions a publication that marks the product result; Hover is waited for, as
        // its adoption follows the test-presence decision (docs/dev/HOVER.md). Hover still describes the checked call when another
        // function is rejected, and never relies on a changed check outcome being logged.
        const string CheckedSource = Source + "func broken() -> i32 => true\n";
        Directory.CreateDirectory(this.directory);
        var main = Path.Combine(this.directory, "main.kimi");
        File.WriteAllText(main, CheckedSource);
        File.WriteAllText(Path.Combine(this.directory, "App.kimiproj"), $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        await using var client = new LspTestClient();
        await client.InitializeAsync("{\"checkQuietPeriodMs\":1000}");
        await client.OpenAsync(main, CheckedSource);
        Assert.Equal("TypeMismatch_Kd", Assert.Single((await client.PublishAsync(main)).EnumerateArray()).GetProperty("code").GetString());
        var hover = await client.WaitForHoverAsync(main, 5, 11, "Available bound: confined");
        Assert.Equal("plaintext", hover.GetProperty("contents").GetProperty("kind").GetString());
        var text = LspTestClient.HoverText(hover);
        Assert.Contains("Call: f()", text);
        Assert.Contains("Declared by: App.apply", text);
        Assert.Contains("Premise: F is Callable<() -> i32> effect confined", text);
        Assert.Contains("uniq", text);
        Assert.Equal(5, hover.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await client.HoverAsync(main, 6, 1)).ValueKind);

        await client.ChangeAsync(main, 2, LspTestClient.Full(CheckedSource.Replace("        effect confined\n", string.Empty, StringComparison.Ordinal)));
        Assert.Equal(JsonValueKind.Null, (await client.HoverAsync(main, 5, 11)).ValueKind);
        Assert.Equal("TypeMismatch_Kd", Assert.Single((await client.PublishAsync(main)).EnumerateArray()).GetProperty("code").GetString());
        var updated = await client.WaitForHoverAsync(main, 3, 11, "Available bound: none");
        Assert.DoesNotContain("Available bound: confined", LspTestClient.HoverText(updated));
    }

    [Fact]
    public async Task VirtualHoverPublishesOriginalGuaranteesAndDirectBaseSelection()
    {
        const string Text = "open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => base.read()\nfunc broken() -> i32 => true\npublic func main() => ()\n";
        Directory.CreateDirectory(this.directory);
        var main = Path.Combine(this.directory, "main.kimi");
        File.WriteAllText(main, Text);
        File.WriteAllText(Path.Combine(this.directory, "App.kimiproj"), $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        await using var client = new LspTestClient();
        await client.InitializeAsync("{\"checkQuietPeriodMs\":1000}");
        await client.OpenAsync(main, Text);
        Assert.Equal("TypeMismatch_Kd", Assert.Single((await client.PublishAsync(main)).EnumerateArray()).GetProperty("code").GetString());
        var offset = Text.Split('\n')[5].IndexOf("base.read", StringComparison.Ordinal) + 5;
        var hover = await client.WaitForHoverAsync(main, 5, offset, "Dispatch: direct base");
        var body = LspTestClient.HoverText(hover);
        Assert.Contains("Implementation: Base.read", body);
        Assert.Contains("Available bound: confined", body);
        Assert.Equal(5, hover.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(offset, hover.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }
}

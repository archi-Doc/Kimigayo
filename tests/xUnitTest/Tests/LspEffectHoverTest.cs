// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class LspEffectHoverTest : IDisposable
{
    private const string Source = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    F is Callable<uniq, () -> i32>\n        effect preserves results\n    return f()\npublic func main() => ()\n";
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
        var record = Assert.Single(c.Binding.CreateCallableEffectHovers(), static x => x.Source.Value.EndsWith("Hello.kimi", StringComparison.Ordinal));
        Assert.Contains("Call: f()", record.Text);
        Assert.Contains("Available bound: confined", record.Text);
        Assert.Contains("Callable: F is Callable<() -> i32>", record.Text);
        Assert.DoesNotContain("BoundType {", record.Text);
        Assert.Contains("Available bound: preserves results", record.Text);
    }

    [Fact]
    public async Task HoverListsContributingPremisesAndNeverUsesAnInvalidatedSnapshot()
    {
        // An independent diagnostic gives both document versions a publication to synchronize on. Hover still describes
        // the checked call when another function is rejected, and never relies on a changed check outcome being logged.
        const string CheckedSource = Source + "func broken() -> i32 => true\n";
        Directory.CreateDirectory(this.directory);
        var main = Path.Combine(this.directory, "main.kimi");
        File.WriteAllText(main, CheckedSource);
        File.WriteAllText(Path.Combine(this.directory, "App.kimiproj"), $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        await using var client = new LspTestClient();
        await client.InitializeAsync("{\"checkQuietPeriodMs\":1000}");
        await client.OpenAsync(main, CheckedSource);
        Assert.Equal("TypeMismatch_Kd", Assert.Single((await client.PublishAsync(main)).EnumerateArray()).GetProperty("code").GetString());
        var hover = await Request(client, main, 5, 11);
        Assert.Equal("plaintext", hover.GetProperty("contents").GetProperty("kind").GetString());
        var text = hover.GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Call: f()", text);
        Assert.Contains("Available bound: confined", text);
        Assert.Contains("Available bound: preserves results", text);
        Assert.Contains("Declared by: App.apply", text);
        Assert.Contains("Premise: F is Callable<() -> i32> effect confined", text);
        Assert.Contains("uniq", text);
        Assert.Equal(5, hover.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await Request(client, main, 6, 1)).ValueKind);

        await client.ChangeAsync(main, 2, LspTestClient.Full(CheckedSource.Replace("        effect confined\n", string.Empty, StringComparison.Ordinal)));
        Assert.Equal(JsonValueKind.Null, (await Request(client, main, 5, 11)).ValueKind);
        Assert.Equal("TypeMismatch_Kd", Assert.Single((await client.PublishAsync(main)).EnumerateArray()).GetProperty("code").GetString());
        var updated = await Request(client, main, 4, 11);
        Assert.DoesNotContain("Available bound: confined", updated.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Available bound: preserves results", updated.GetProperty("contents").GetProperty("value").GetString());
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
        var hover = await Request(client, main, 5, offset);
        var body = hover.GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("Dispatch: direct base", body);
        Assert.Contains("Implementation: Base.read", body);
        Assert.Contains("Available bound: confined", body);
        Assert.Equal(5, hover.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(offset, hover.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    private static async Task<JsonElement> Request(LspTestClient client, string path, int line, int character)
    {
        var result = await client.RequestAsync("textDocument/hover", $"{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\"}},\"position\":{{\"line\":{line},\"character\":{character}}}}}");
        return result.GetProperty("result");
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Xunit;

namespace XunitTest;

public sealed class HoverLinkTest
{
    private readonly string root = Path.GetFullPath("hover-project");

    [Theory]
    [InlineData("https://example.test/a?b=c#part", "https://example.test/a?b=c#part")]
    [InlineData("http://example.test/日本", "http://example.test/%E6%97%A5%E6%9C%AC")]
    [InlineData("mailto:author@example.test", "mailto:author@example.test")]
    public void ExternalLinksUseTheExistingProfile(string value, string expected)
        => Assert.Equal(expected, DocumentationLinks.ResolveEditor(value, null, null, HoverPlacement.Unsupported.Map));

    [Theory]
    [InlineData("")]
    [InlineData("#part")]
    [InlineData("?mode=1")]
    [InlineData("/root.kimi")]
    [InlineData("//server/file.kimi")]
    [InlineData("file:///C:/a.kimi")]
    [InlineData("command:run")]
    [InlineData("javascript:run()")]
    [InlineData("../../outside.kimi")]
    [InlineData("%2e%2e/%2e%2e/outside.kimi")]
    [InlineData("%2foutside.kimi")]
    [InlineData("%5coutside.kimi")]
    [InlineData("%0ax.kimi")]
    [InlineData("%C0%AE.kimi")]
    [InlineData("incomplete%.kimi")]
    public void UnsupportedDestinationsRemainLabels(string destination)
        => Assert.Null(this.Resolve(destination));

    [Fact]
    public void PhysicalPathsEncodePercentAndHashExactlyOnceAndPreserveUrlSuffixes()
    {
        var resolved = Assert.IsType<string>(this.Resolve("../%2520%20%23日本.kimi?view=1#section%202"));
        var uri = new Uri(resolved);
        Assert.Equal(Path.Combine(this.root, "%20 #日本.kimi"), uri.LocalPath);
        Assert.Contains("%2520%20%23", uri.AbsoluteUri);
        Assert.Equal("?view=1", uri.Query);
        Assert.Equal("#section%202", uri.Fragment);
        var twice = new Uri(Assert.IsType<string>(this.Resolve("%252e%252e/safe.kimi")));
        Assert.Equal(Path.Combine(this.root, "src", "%2e%2e", "safe.kimi"), twice.LocalPath);
    }

    [Fact]
    public void AnExactExternalMappingDoesNotSupplyNeighborPlacement()
    {
        var physical = Path.GetFullPath("elsewhere/external.kimi");
        var mapping = new HoverPlacement(null, [new("src/external.kimi", physical)]);
        Assert.Equal(physical, new Uri(Assert.IsType<string>(DocumentationLinks.ResolveEditor("external.kimi", "src/external.kimi", "P", mapping.Map))).LocalPath);
        Assert.Null(DocumentationLinks.ResolveEditor("neighbor.kimi", "src/external.kimi", "P", mapping.Map));
        Assert.Null(DocumentationLinks.ResolveEditor("peer.kimi", "compiler://Kimi/Core.kimi", "Kimi", HoverPlacement.Unsupported.Map));
    }

    [Fact]
    public void DependencyDocumentationUsesItsOwnProjectDirectory()
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = true;
        var module = new Kotonoha(compilation, "Dependency", Path.Combine(this.root, "Dependency.kimiproj"));
        module.AddSource(new(Path.Combine(this.root, "src", "value.kimi"), "/// A dependency.\npublic struct Value\n"));
        Assert.Equal("src/value.kimi", Assert.Single(module.DocumentationSources).LogicalName);
    }

    private string? Resolve(string destination)
        => DocumentationLinks.ResolveEditor(destination, "src/main.kimi", "P", new HoverPlacement(this.root, []).Map);
}

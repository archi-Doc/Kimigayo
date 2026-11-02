// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Lsp;
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
        => Assert.Equal(expected, DocumentationLinks.ResolveEditor(value, null, null, Unsupported));

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
        var resolved = DocumentationLinks.ResolveEditor("external.kimi", "src/external.kimi", "P", x => mapping.Map(x, HoverPlacement.Canonical));
        Assert.Equal(physical, new Uri(Assert.IsType<string>(resolved)).LocalPath);
        Assert.Null(DocumentationLinks.ResolveEditor("neighbor.kimi", "src/external.kimi", "P", x => mapping.Map(x, HoverPlacement.Canonical)));
        Assert.Null(DocumentationLinks.ResolveEditor("peer.kimi", "compiler://Kimi/Core.kimi", "Kimi", Unsupported));
    }

    // DECISIONS R5-M180: a file no URI spells (a device path) has no link, so its label is shown as text.
    [Theory]
    [InlineData(@"\\?\C:\device\external.kimi")]
    [InlineData(@"\\.\C:\device\external.kimi")]
    public void ADevicePathHasNoLink(string physical)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows device paths.");
        var mapping = new HoverPlacement(null, [new("src/external.kimi", physical)]);
        Assert.Null(DocumentationLinks.ResolveEditor("external.kimi", "src/external.kimi", "P", x => mapping.Map(x, HoverPlacement.Canonical)));
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

    // IMPL 20.1 (DECISIONS R5-M175): a fully qualified path under the base is its remainder, any other path its own text, each with the
    // host's separators as '/'; "under" ends at a separator and compares letters as the host's paths do.
    [Theory]
    [InlineData(@"C:\p\App", @"C:\p\App\tests\T.kimi", "tests/T.kimi")]
    [InlineData(@"C:\p\App", @"C:\p\AppX\A.kimi", "C:/p/AppX/A.kimi")]
    [InlineData(@"C:\p\App", "C:/p/App/Main.kimi", "Main.kimi")]
    [InlineData(@"C:\p\App", @"c:\P\APP\Main.kimi", "Main.kimi")]
    [InlineData(@"C:\p\App\", @"C:\p\App\Main.kimi", "Main.kimi")]
    [InlineData(@"C:\", @"C:\Main.kimi", "Main.kimi")]
    [InlineData(@"C:\p\App", @"D:\x\Main.kimi", "D:/x/Main.kimi")]
    [InlineData(@"C:\p\App", @"src\Main.kimi", "src/Main.kimi")]
    [InlineData(@"C:\p\App", @"C:\p\App\..\Lib\L.kimi", "../Lib/L.kimi")]
    [InlineData(@"C:\p\App", "C:\\p\\App\\a\0b.kimi", "a\0b.kimi")]
    [InlineData(@"C:\p\App", "compiler://Kimi/0.1/Console.kimi", "compiler://Kimi/0.1/Console.kimi")]
    public void DocumentationNamesASourceByTheLogicalNameRule(string directory, string path, string name)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path rows.");
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = true;
        compilation.Project.Directory = directory;
        compilation.Kotonoha.AddSource(new(path, "/// A value.\nstruct Value\n"));
        Assert.Equal(name, Assert.Single(compilation.Kotonoha.DocumentationSources).LogicalName);
    }

    [Fact]
    public void ARootWithoutADirectoryNamesAndLinksFromTheCurrentDirectoryItsCompilationCaptured()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        compilation.CollectHover = true;
        compilation.CollectDocumentation = true;
        const string source = "/// See [peer](B.kimi).\nstruct Value\n";
        var path = Path.GetFullPath("A.kimi");
        compilation.Kotonoha.AddSource(new(path, source));
        Assert.True(compilation.Bind().IsComplete);
        Assert.Equal("A.kimi", Assert.Single(compilation.Kotonoha.DocumentationSources).LogicalName);
        Assert.Equal("A.kimi", compilation.Kotonoha.HoverSourceName(compilation.Kotonoha.SourceDocuments[0]));
        var document = HoverBuilder.Create(compilation).Documents[SourceIdentity.FromPath(path)];
        var entry = document.Entries[document.Find(source.IndexOf("Value", StringComparison.Ordinal))];
        Assert.Contains($"[peer](<{new Uri(Path.GetFullPath("B.kimi")).AbsoluteUri}>)", HoverRenderer.Render(entry.Info, true, HoverPlacement.Canonical).Body);
    }

    private static string? Unsupported(DocumentationLinkTarget target)
        => HoverPlacement.Unsupported.Map(target, HoverPlacement.Canonical);

    private string? Resolve(string destination)
        => DocumentationLinks.ResolveEditor(destination, "src/main.kimi", "P", x => new HoverPlacement(this.root, []).Map(x, HoverPlacement.Canonical));
}

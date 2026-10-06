// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public sealed class HoverCollectionTest : IDisposable
{
    private const string Callable = "/// Applies the callback.\nfunc apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\npublic func main() => ()\n";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-hover-collection-" + Guid.NewGuid().ToString("N"));

    public HoverCollectionTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Theory]
    [InlineData("")]
    [InlineData("func broken() -> i32 => true\n")]
    [InlineData("func broken() -> i32 => 1 +\n")]
    [InlineData("func broken<T>()\n    T is Missing\n    return\n")]
    public void OptionalCollectionPreservesCheckRecordsAndOutcome(string extra)
    {
        var path = Path.Combine(this.directory, "App.kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), Callable + extra);
        var plain = Run(path, false);
        var hover = Run(path, true);
        Assert.Null(plain.Hover);
        Assert.Null(plain.HoverFault);
        Assert.Null(hover.HoverFault);
        Assert.NotNull(hover.Hover);
        Assert.Equal(CheckOutcome.Completed, hover.Outcome);
        Assert.Equal(extra.Length == 0, hover.Accepted);
        Assert.Equal(JsonSerializer.Serialize(plain), JsonSerializer.Serialize(hover));
        if (extra.Length == 0)
        {
            Assert.Contains("Available bound: confined", Assert.Single(hover.Hover.Effects).Text);
        }
    }

    [Fact]
    public void OptionalProjectionFailureIsSeparateFromFinalizedRecords()
    {
        var path = Path.Combine(this.directory, "App.kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), Callable + "func broken() -> i32 => true\n");
        var checkedOutput = Run(path, false);
        var original = JsonSerializer.Serialize(checkedOutput);
        var data = CheckService.TryCreateHover(
            checkedOutput,
            static output =>
            {
                Assert.Equal(CheckOutcome.Completed, output.Outcome);
                Assert.False(output.Accepted);
                throw new InvalidOperationException("projection failed");
            },
            out var fault);
        Assert.Null(data);
        Assert.Equal("projection failed", fault);
        Assert.Equal(original, JsonSerializer.Serialize(checkedOutput));
    }

    [Fact]
    public void ProjectionDoesNotSwallowCancellationOrPendingInputs()
    {
        Assert.Throws<OperationCanceledException>(() => CheckService.TryCreateHover(0, static _ => throw new OperationCanceledException(), out _));
        Assert.Throws<PendingInputException>(() => CheckService.TryCreateHover(0, static _ => throw new PendingInputException("changed.kimi"), out _));
        Assert.False(Compilation.OptionalHoverFailure(new OperationCanceledException()));
        Assert.False(Compilation.OptionalHoverFailure(new PendingInputException("changed.kimi")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionFailuresPreserveDeclarationsAndOtherSources(bool duringAssociation)
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectHover = true;
        compilation.CollectDocumentation = true;
        var failed = new SourceDocument(Path.Combine(this.directory, "failed.kimi"), "/// Lost description.\nstruct Failed\n");
        var healthy = new SourceDocument(Path.Combine(this.directory, "healthy.kimi"), "/// Kept description.\nstruct Healthy\n");
        compilation.Kotonoha.AddSource(failed);
        compilation.Kotonoha.AddSource(healthy);
        var documentation = Assert.Single(compilation.Kotonoha.DocumentationSources, source => ReferenceEquals(source.Source, failed));
        if (duringAssociation)
        {
            documentation.Exclude(-1, 0, 0); // An invalid optional association operation must not escape into core parsing.
        }
        else
        {
            documentation.SetLocation("\0"); // Physical placement cannot be computed from this invalid path.
        }

        Assert.True(compilation.HasDocumentationFailure(failed));
        Assert.False(compilation.HasDocumentationFailure(healthy));
        Assert.NotNull(compilation.DocumentationFailure);
        Assert.Empty(documentation.Comments);
        Assert.True(compilation.Bind().IsComplete);
        Assert.Empty(TestDiagnostics.Of(compilation));
        var snapshot = compilation.Binding.CreateHoverSnapshot();
        var failedIndex = snapshot.Documents[SourceIdentity.FromPath(failed.Path)];
        var failedInfo = failedIndex.Entries[failedIndex.Find(failed.SourceText.IndexOf("Failed", StringComparison.Ordinal))].Info;
        var failedDeclaration = Assert.Single(failedInfo.Declarations);
        Assert.Equal("struct Failed", failedDeclaration.Header);
        Assert.Equal("Documentation unavailable: collection failure", failedDeclaration.DocumentationNotice);
        Assert.Empty(failedDeclaration.Documentation);
        Assert.NotNull(failedInfo.Copy);
        var healthyIndex = snapshot.Documents[SourceIdentity.FromPath(healthy.Path)];
        var healthyInfo = healthyIndex.Entries[healthyIndex.Find(healthy.SourceText.IndexOf("Healthy", StringComparison.Ordinal))].Info;
        Assert.Null(Assert.Single(healthyInfo.Declarations).DocumentationNotice);
        Assert.Single(healthyInfo.Declarations[0].Documentation);
    }

    [Fact]
    public void StandaloneDocumentationRetainsItsFailureContract()
    {
        var source = new SourceDocument(Path.Combine(this.directory, "standalone.kimi"), "/// Comment");
        var documentation = new DocumentationSource(source);
        Assert.Throws<ArgumentException>(() => documentation.SetLocation("\0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedCollectionIsIndependentOfPriorModeAndKeepsBindingsLocal(bool firstCollects)
    {
        var first = Create(firstCollects);
        var second = Create(!firstCollects);
        var enabled = firstCollects ? first : second;
        var disabled = firstCollects ? second : first;
        Assert.Empty(disabled.Library.Kotonoha.DocumentationSources);
        Assert.Empty(disabled.Kotonoha.DocumentationSources);
        AssertEmbeddedDescriptions(enabled);

        var another = Create(true);
        AssertEmbeddedDescriptions(another);
        var earlier = IteratorComment(enabled);
        var later = IteratorComment(another);
        Assert.NotSame(earlier, later);
        Assert.NotSame(earlier.Declaration, later.Declaration);
        Assert.Same(enabled, earlier.Declaration!.CodeContext.Compilation);
        Assert.Same(another, later.Declaration!.CodeContext.Compilation);
        Assert.Equal(earlier.GetText().Text, later.GetText().Text);
        earlier.IsSelected = false;
        Assert.True(later.IsSelected);
    }

    [Fact]
    public async Task ConcurrentLibraryLoadsShareOnlyLexicalData()
    {
        var compilations = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() => Create(index % 2 == 0), TestContext.Current.CancellationToken)));
        for (var i = 0; i < compilations.Length; i++)
        {
            if (i % 2 == 0)
            {
                AssertEmbeddedDescriptions(compilations[i]);
            }
            else
            {
                Assert.Empty(compilations[i].Library.Kotonoha.DocumentationSources);
            }
        }
    }

    private static CheckOutput Run(string path, bool collect)
    {
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, path, out var project));
        return CheckService.Run(project, WindowsProfile.Target, CheckMode.Product, false, CheckInputSource.Disk, TestContext.Current.CancellationToken, collectHover: collect);
    }

    private static Compilation Create(bool collect)
    {
        var compilation = Compilation.CreateForTest();
        compilation.CollectDocumentation = collect;
        compilation.Kotonoha.AddSource(new("main.kimi", "/// A user type.\npublic struct Example\n"));
        Assert.True(compilation.Bind().IsComplete);
        return compilation;
    }

    private static DocumentationComment IteratorComment(Compilation compilation)
        => Assert.Single(
            compilation.Library.Kotonoha.DocumentationSources.SelectMany(static source => source.Comments),
            static comment => comment.Declaration is ContractKoto { Name: "Iterator" });

    private static void AssertEmbeddedDescriptions(Compilation compilation)
    {
        var iterator = IteratorComment(compilation);
        Assert.Contains("independent of an individual next call", iterator.GetText().Text);
        Assert.Same(iterator, Assert.Single(compilation.Binding.GetDocumentation(iterator.Declaration!)));
        var constructor = Assert.Single(
            compilation.Library.Kotonoha.DocumentationSources.SelectMany(static source => source.Comments),
            static comment => comment.Source.Path.EndsWith("/ArrayOperations.kimi", StringComparison.Ordinal) && comment.Declaration is FunctionKoto { IsConstructor: true });
        Assert.Contains("zero allocates nothing", constructor.GetText().Text);
    }
}

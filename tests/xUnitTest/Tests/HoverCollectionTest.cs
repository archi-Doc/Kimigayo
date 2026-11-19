// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
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

    // IMPL A.4, 21.3.2 (PLAN G761): a graph of 65,536 modules is Completed, rejected by the one ModuleCountLimit_Kd at the root project
    // file, with test presence Unknown (no source was parsed); collecting hover over that unanalyzed compilation leaves it unchanged.
    [Fact]
    public void OptionalCollectionKeepsAModuleCountRefusal()
    {
        var root = Path.Combine(this.directory, "App", "App.kimiproj");
        var target = $"Targets={{\"{WindowsProfile.Target}\"}}";
        var files = new Dictionary<string, byte[]>(SourceIdentity.PathComparer);
        var dependencies = new StringBuilder();
        for (var i = 1; i <= SyntaxTable.LibraryModule; i++)
        {
            dependencies.Append($"L{i}={{PackageId=\"l{i}\" PackageVersion=\"1\" Project=\"../L{i}/L{i}.kimiproj\"}} ");
            var library = $"OutputKind=\"Library\" PackageId=\"l{i}\" PackageVersion=\"1\" {target}";
            files.Add(Path.Combine(this.directory, $"L{i}", $"L{i}.kimiproj"), Encoding.UTF8.GetBytes(library));
        }

        files.Add(root, Encoding.UTF8.GetBytes($"OutputKind=\"Library\" {target} Dependencies={{{dependencies}}}"));
        var inputs = new ProjectFiles(files);
        var cancellation = TestContext.Current.CancellationToken;
        var resolution = DependencyResolver.Resolve(root, WindowsProfile.Target, Compilation.CurrentLanguageVersion, cancellation, null, false, inputs);
        files.Add(DependencyLock.PathForProject(root), DependencyLock.Serialize(resolution));
        var plain = Run(root, false, inputs);
        var hover = Run(root, true, inputs);
        Assert.Equal((CheckOutcome.Completed, false, TestPresence.Unknown, null), (hover.Outcome, hover.Accepted, hover.Presence, hover.HoverFault));
        var error = Assert.Single(hover.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ModuleCountLimit_Kd), root), (error.Code, hover.Sources[error.Source].Path));
        Assert.Equal(JsonSerializer.Serialize(plain), JsonSerializer.Serialize(hover));
    }

    // Hover's display of a Type is bounded by HoverLimits.Depth (HOVER "Deterministic limits"), counted per part of G348-d's explicit
    // stack as the recursion counted it: a Type of 1,024 layers interrupts the optional projection and leaves the records as they are.
    [Theory]
    [InlineData("func p(x: ref/D10<i32>.Item) => ()\n", true)]
    [InlineData("func p() -> ()\n    let x: D10<i32>.Item = true\n    return\n", false)]
    public void ADeepTypeInterruptsTheProjectionOnly(string use, bool accepted)
    {
        var doubling = "contract HasItem\n    associate Item\nstruct D0<T>\n    Self is HasItem\n    associate HasItem.Item is (T, i32)\n";
        for (var i = 1; i <= 10; i++)
        {
            doubling += $"struct D{i}<T>\n    Self is HasItem\n    associate HasItem.Item is D{i - 1}<D{i - 1}<T>.Item>.Item\n";
        }

        var path = Path.Combine(this.directory, "App.kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), doubling + use + "public func main() => ()\n");
        var plain = Run(path, false);
        var hover = Run(path, true);
        Assert.Equal("Hover depth limit exceeded", hover.HoverFault);
        Assert.Null(hover.Hover);
        Assert.Equal((CheckOutcome.Completed, accepted), (hover.Outcome, hover.Accepted));
        Assert.Equal(accepted, hover.Diagnostics.Length == 0);
        Assert.Equal(JsonSerializer.Serialize(plain), JsonSerializer.Serialize(hover));
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
            documentation.Merge(null!); // An invalid optional merge must not escape either (naming a source cannot fail, R5-M175).
        }

        Assert.True(compilation.HasDocumentationFailure(failed));
        Assert.False(compilation.HasDocumentationFailure(healthy));
        Assert.NotNull(compilation.DocumentationFailure);
        Assert.Empty(documentation.Comments);
        Assert.True(compilation.Bind().IsComplete);
        Assert.Empty(TestDiagnostics.Of(compilation));
        var snapshot = HoverBuilder.Create(compilation);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => documentation.Exclude(-1, 0, 0));
    }

    // PLAN G949 Stage 2 U3 (design 4.7; changed by decision, O-376 (a), L1): every compilation reads the process's frozen library, whose
    // freeze collects its documentation, so in every mode the library's documentation is the same objects, each compilation's Binding
    // documents them (Binding.GetDocumentation) and a library node names no compilation (detached, U2b). Stage 1's row gave each
    // compilation a library of its own, documented only when it collected, and wrote a comment's IsSelected (a write on a shared object:
    // removed).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedCollectionIsIndependentOfPriorModeAndKeepsBindingsLocal(bool firstCollects)
    {
        var first = Create(firstCollects);
        var second = Create(!firstCollects);
        var enabled = firstCollects ? first : second;
        var disabled = firstCollects ? second : first;
        Assert.Empty(disabled.Kotonoha.DocumentationSources);
        AssertEmbeddedDescriptions(enabled);
        AssertEmbeddedDescriptions(disabled);

        var another = Create(true);
        AssertEmbeddedDescriptions(another);
        var earlier = IteratorComment(enabled);
        Assert.Same(earlier, IteratorComment(another));
        Assert.Same(earlier, IteratorComment(disabled));
        Assert.NotSame(enabled.Binding, another.Binding);
        Assert.Throws<InvalidOperationException>(() => earlier.Declaration!.CodeContext.Compilation);
    }

    // PLAN G949 Stage 2 U3 (design 4.7; changed by decision): compilations made at once share the frozen library and its documentation,
    // whatever their mode. Stage 1's row (ConcurrentLibraryLoadsShareOnlyLexicalData) loaded a library per compilation, documented only
    // in the collecting ones.
    [Fact]
    public async Task ConcurrentCompilationsShareTheFrozenLibrarysDocumentation()
    {
        var compilations = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() => Create(index % 2 == 0), TestContext.Current.CancellationToken)));
        for (var i = 0; i < compilations.Length; i++)
        {
            AssertEmbeddedDescriptions(compilations[i]);
            Assert.Same(compilations[0].Library, compilations[i].Library);
        }
    }

    // h4 (PLAN G949 Stage 2 U3; N-f, R1-B3): a reading compilation that collects no documentation reads the frozen library's
    // documentation and its failures, which its freeze collected: a library whose one added document's collection failed in its freeze
    // reports that document as failed in the reading compilation, and no other library document.
    [Fact]
    public void AReadingCompilationReadsTheFrozenLibrarysDocumentationFailures()
    {
        var failed = new SourceDocument("ZFailed.kimi", "/// Lost description.\npublic struct ZFailed\n");
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        CompilationTestHelper.UseEditedLibrary(c, x =>
        {
            x.Kotonoha.AddSource(failed);
            Assert.Single(x.Kotonoha.DocumentationSources, source => ReferenceEquals(source.Source, failed)).Merge(null!);
        });
        c.Kotonoha.AddSource(new("main.kimi", "let x = 1\n"));
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.CollectDocumentation);
        AssertEmbeddedDescriptions(c);
        Assert.True(c.HasDocumentationFailure(failed));
        Assert.False(c.HasDocumentationFailure(IteratorComment(c).Source));
        var shipped = Create(false);
        Assert.False(shipped.HasDocumentationFailure(IteratorComment(shipped).Source));
    }

    // IMPL 20.1 (DECISIONS R5-M175): a library documentation source is named by its source's logical name, its compiler:// path, so
    // the library's fragments order by file and its relative links resolve from that file.
    [Fact]
    public void LibraryDocumentationIsNamedByItsSourcesPath()
    {
        var sources = Create(true).Library.Kotonoha.DocumentationSources.ToArray();
        Assert.NotEmpty(sources);
        Assert.All(sources, static source => Assert.StartsWith("compiler://Kimi/", source.LogicalName, StringComparison.Ordinal));
        Assert.All(sources, static source => Assert.Equal(source.Source.Path, source.LogicalName));
    }

    private static CheckOutput Run(string path, bool collect, CheckInputSource? inputs = null)
    {
        inputs ??= CheckInputSource.Disk;
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, path, inputs, out var project, out _));
        return CheckService.Run(project, WindowsProfile.Target, CheckMode.Product, false, inputs, TestContext.Current.CancellationToken, collectHover: collect);
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
            static comment => comment.Source.Path.EndsWith("/Array.kimi", StringComparison.Ordinal) && comment.Declaration is FunctionKoto { IsConstructor: true, Parameters: [{ InternalName: "capacity" }] });
        Assert.Contains("zero allocates nothing", constructor.GetText().Text);
    }

    // Project and lock files from memory, and no source in any directory.
    private sealed class ProjectFiles(Dictionary<string, byte[]> files) : CheckInputSource
    {
        public override byte[] ReadAllBytes(string path) => files.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path);

        public override SourceContent ReadSource(string path) => throw new FileNotFoundException(path);

        public override string[] GetFiles(string directory, string pattern) => [];
    }
}

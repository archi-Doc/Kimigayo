// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Helper;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.2: the shared check entry, compilation-owned diagnostics and immutable check output.
public sealed partial class CheckServiceTest : IDisposable
{
    private const string Valid = "public func one() -> i32 => 1\n";
    private const string Broken = "public func one() -> i32 => 1 +\n";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-check-" + Guid.NewGuid().ToString("N"));

    public CheckServiceTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void TextOnlyPreparationFailuresBecomeBlockedDiagnostics()
    {
        var project = this.WriteProject("App", settings: "Targets={}");
        var output = this.Run(project);

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("At least one compilation target", failure.Note);
        Assert.Equal(SourceIdentity.FromPath(project), Location(output, failure));
    }

    // SPEC 23.2.1, 23.2.2: an unsupported target is an Input problem of its own, Blocked in the check entry and reported by the
    // command, never an unexplained rejection.
    [Fact]
    public async Task AnUnsupportedTargetIsAnInputProblem()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "Targets={\"unknown-none-elf\"}");
        var output = this.Run(project, target: "unknown-none-elf");
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsupportedTarget_Kd), failure.Code);
        Assert.Equal(DiagnosticCategory.Input, failure.Category);

        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        loaded.KimiOptions = new Kimi.Command.KimiOptions { Target = "unknown-none-elf" };
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));
        Assert.Contains(nameof(DiagnosticCode.UnsupportedTarget_Kd), console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.CheckFaulted_Kd), console.Output, StringComparison.Ordinal);
    }

    // IMPL 20.1 (DECISIONS R5-M175): two source names equal under simple case folding are a project preparation failure, Blocked before
    // the front end: ASCII case, and U+1E9E/U+00DF and U+017F/s, which OrdinalIgnoreCase misses; ß and ss differ (simple, not full,
    // folding).
    [Theory]
    [InlineData("A.kimi", "a.kimi", true)]
    [InlineData("\u1E9E.kimi", "\u00DF.kimi", true)]
    [InlineData("\u017F.kimi", "s.kimi", true)]
    [InlineData("\u00DF.kimi", "ss.kimi", false)]
    public void SourceNamesAreUniqueUnderSimpleCaseFolding(string first, string second, bool equal)
    {
        var project = this.WriteProject("App");
        var folder = Path.GetDirectoryName(project)!;
        const string other = "public func two() -> i32 => 2\n";
        var inputs = new OverlayInputSource(new() { [Path.Combine(folder, first)] = Valid, [Path.Combine(folder, second)] = other });
        var output = this.Run(project, inputs: inputs);
        if (!equal)
        {
            Assert.Equal(CheckOutcome.Completed, output.Outcome);
            Assert.Empty(output.Diagnostics);
            return;
        }

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("simple case folding", failure.Note, StringComparison.Ordinal);
        Assert.Contains(first, failure.Note, StringComparison.Ordinal);
        Assert.Contains(second, failure.Note, StringComparison.Ordinal);
    }

    // IMPL 20.1 (DECISIONS R5-M175): a source name not in NFC is a project preparation failure, checked before folding: a decomposed
    // letter, and U+037E (a non-identifier scalar whose canonical decomposition is ';'); a precomposed name and one whose marks are in
    // canonical order are accepted.
    [Theory]
    [InlineData("e\u0301.kimi", false)]
    [InlineData("\u037E.kimi", false)]
    [InlineData("q\u0307\u0323.kimi", false)]
    [InlineData("\u00E9.kimi", true)]
    [InlineData("q\u0323\u0307.kimi", true)]
    public void SourceNamesAreInNfc(string file, bool normalized)
    {
        var project = this.WriteProject("App");
        var output = this.Run(project, inputs: new OverlayInputSource(new() { [Path.Combine(Path.GetDirectoryName(project)!, file)] = Valid }));
        if (normalized)
        {
            Assert.Equal(CheckOutcome.Completed, output.Outcome);
            Assert.Empty(output.Diagnostics);
            return;
        }

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("Normalization Form C", failure.Note, StringComparison.Ordinal);
    }

    // SPEC 23.1.2, IMPL 20.1 (DECISIONS R5-M175): each independent failure of a module's names is its own record: a name not in NFC,
    // and each group of names equal under simple case folding, three names in one group naming them all.
    [Fact]
    public void EachIndependentNameFailureIsReported()
    {
        var project = this.WriteProject("App");
        var folder = Path.GetDirectoryName(project)!;
        var files = new[] { "S.kimi", "s.kimi", "\u017F.kimi", "e\u0301.kimi", "B.kimi", "b.kimi" };
        var output = this.Run(project, inputs: new OverlayInputSource(files.ToDictionary(x => Path.Combine(folder, x), static _ => Valid)));
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.All(output.Diagnostics, static x => Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), x.Code));
        var notes = output.Diagnostics.Select(static x => x.Note!).ToArray();
        Assert.Equal(3, notes.Length);
        Assert.Single(notes, static x => x.Contains("e\u0301.kimi", StringComparison.Ordinal) && x.Contains("Normalization", StringComparison.Ordinal));
        Assert.Single(notes, static x => x.Contains("'S.kimi', 's.kimi', '\u017F.kimi'", StringComparison.Ordinal));
        Assert.Single(notes, static x => x.Contains("'B.kimi', 'b.kimi'", StringComparison.Ordinal));
    }

    // SPEC 23.1.2, IMPL 20.1: TestSources entries are compared as resolved paths, and each duplicate is its own record, in a product and
    // a test check alike.
    [Theory]
    [InlineData(CheckMode.Product)]
    [InlineData(CheckMode.Test)]
    public void EachDuplicateTestSourceIsReported(CheckMode mode)
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "TestSources={\"tests/../a.kimi\" \"a.kimi\" \"b.kimi\" \"b.kimi\"}");
        var output = this.Run(project, mode);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.All(output.Diagnostics, static x => Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), x.Code));
        Assert.Equal(["Duplicate resolved TestSources path: a.kimi", "Duplicate resolved TestSources path: b.kimi"], output.Diagnostics.Select(static x => x.Note));
    }

    // SPEC 23.1.2: each independent failure of the dependency graph is its own record, in a product and a test check alike.
    [Theory]
    [InlineData(CheckMode.Product)]
    [InlineData(CheckMode.Test)]
    public void EachIndependentDependencyFailureIsReported(CheckMode mode)
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), dependencies: "A={PackageId=\"a\" PackageVersion=\"1\" Project=\"../A/A.kimiproj\"} B={PackageId=\"b\" PackageVersion=\"1\" Project=\"../B/B.kimiproj\"}");
        var output = this.Run(project, mode);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Equal(2, output.Diagnostics.Length);
        Assert.All(output.Diagnostics, static x => Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), x.Code));
        Assert.Single(output.Diagnostics, static x => x.Note!.Contains("A.kimiproj", StringComparison.Ordinal));
        Assert.Single(output.Diagnostics, static x => x.Note!.Contains("B.kimiproj", StringComparison.Ordinal));
    }

    // SPEC 23.1.2: preparation reports each configuration failure beside the TestSources comparison; a duplicate of an entry that breaks
    // the path syntax depends on that failure and is left out.
    [Theory]
    [InlineData(CheckMode.Product)]
    [InlineData(CheckMode.Test)]
    public void PreparationReportsEachConfigurationFailure(CheckMode mode)
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failures), string.Join(", ", failures));
        loaded.ProjectFile.Alias = [" "];
        loaded.ProjectFile.TestSources = ["*.kimi", "*.kimi"];
        var output = CheckService.Run(loaded, WindowsProfile.Target, mode, false, CheckInputSource.Disk, TestContext.Current.CancellationToken);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Equal(
            ["Alias requires nonempty Container paths.", "TestSources requires explicit project-relative paths without globs."],
            output.Diagnostics.Select(static x => x.Note!).Order(StringComparer.Ordinal));
    }

    // SPEC 23.1.2, IMPL 20.1 (DECISIONS R5-M175): every module's names are checked, the root's and each dependency's, each failure naming
    // its module; names are compared only within one module, so a root Main.kimi beside a dependency's main.kimi is accepted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryModulesNamesAreCheckedWithinTheModule(bool invalid)
    {
        this.WriteLibrary("Lib", "lib", "public func value() -> i32 => 2\n");
        const string dependency = "Lib={PackageId=\"lib\" PackageVersion=\"1\" Project=\"../Lib/Lib.kimiproj\"}";
        var project = this.WriteProject("App", ("Main.kimi", Valid), dependencies: dependency);
        if (invalid)
        {
            File.WriteAllText(this.PathOf("App", "o\u0308.kimi"), "public func app() -> i32 => 3\n");
            File.WriteAllText(this.PathOf("Lib", "e\u0301.kimi"), "public func lib() -> i32 => 4\n");
        }

        var resolution = DependencyResolver.Resolve(project, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken);
        DependencyLock.Update(DependencyLock.PathForProject(project), resolution, TestContext.Current.CancellationToken);
        var output = this.Run(project);
        if (!invalid)
        {
            Assert.Equal(CheckOutcome.Completed, output.Outcome);
            Assert.Empty(output.Diagnostics);
            return;
        }

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var notes = output.Diagnostics.Select(static x => x.Note!).ToArray();
        Assert.Equal(2, notes.Length);
        Assert.Single(notes, static x => x.Contains("o\u0308.kimi", StringComparison.Ordinal) && x.Contains("'App'", StringComparison.Ordinal));
        Assert.Single(notes, static x => x.Contains("e\u0301.kimi", StringComparison.Ordinal) && !x.Contains("'App'", StringComparison.Ordinal));
    }

    // SPEC 6.2.1, IMPL 20.1 (DECISIONS R5-M176): source names order as UTF-8 byte sequences over the whole name, `/` an ordinary byte
    // and a proper prefix first; UTF-16 order differs where a supplementary scalar meets U+E000–U+FFFF.
    [Theory]
    [InlineData("a.kimi", "b.kimi", -1)]
    [InlineData("a/b.kimi", "a-b.kimi", 1)]
    [InlineData("a.kimi", "a0.kimi", -1)]
    [InlineData("a", "a.kimi", -1)]
    [InlineData("\U0001F600.kimi", "\uFF21.kimi", 1)]
    [InlineData("\U0001F600.kimi", "\U0001F601.kimi", -1)]
    [InlineData("\U0001F600.kimi", "\U00020000.kimi", -1)]
    public void SourceNamesOrderAsUtf8Bytes(string first, string second, int order)
    {
        Assert.Equal(order, Math.Sign(UnicodeNameHelper.ScalarOrder.Compare(first, second)));
        Assert.Equal(-order, Math.Sign(UnicodeNameHelper.ScalarOrder.Compare(second, first)));
        Assert.Equal(order, Math.Sign(Encoding.UTF8.GetBytes(first).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(second))));
    }

    // IMPL 20.1 (DECISIONS R5-M176): a lone surrogate (a name not in NFC) orders by its code unit, so the order stays total and
    // deterministic: above U+E000, a high one before its own pair (a proper prefix), a low one after.
    [Fact]
    public void LoneSurrogatesOrderByTheirCodeUnits()
    {
        string[] names = ["\uDC00", "\U00010000", "\uE000", "\uD800"];
        Assert.Equal(["\uE000", "\uD800", "\U00010000", "\uDC00"], names.Order(UnicodeNameHelper.ScalarOrder));
    }

    // SPEC 6.2.1 (DECISIONS R5-M176): a module's sources are parsed in logical declaration order, whatever the read order: a TestSources
    // entry and an in-memory source between product sources, and a supplementary scalar after U+FF21, which UTF-16 orders first.
    [Fact]
    public void SourcesAreParsedInLogicalDeclarationOrder()
    {
        var project = this.WriteProject("App", ("c.kimi", "public func c() -> i32 => 3\n"), settings: "TestSources={\"b.kimi\"}");
        File.WriteAllText(this.PathOf("App", "a.kimi"), "public func a() -> i32 => 1\n");
        File.WriteAllText(this.PathOf("App", "b.kimi"), "#Test\nfunc check()\n    $expect(true)\n");
        File.WriteAllText(this.PathOf("App", "\U0001F600.kimi"), "public func smile() -> i32 => 4\n");
        File.WriteAllText(this.PathOf("App", "\uFF21.kimi"), "public func wide() -> i32 => 5\n");
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failures), string.Join(", ", failures));
        loaded.AddSource(this.PathOf("App", "a0.kimi"), "public func a0() -> i32 => 0\n");
        var module = Assert.IsType<Compilation>(loaded.PrepareTests(TestContext.Current.CancellationToken)).Kotonoha;
        Assert.Equal(["a.kimi", "a0.kimi", "b.kimi", "c.kimi", "\uFF21.kimi", "\U0001F600.kimi"], module.SourceDocuments.Select(module.LogicalName));
    }

    // IMPL 20.1 (DECISIONS R5-M175): a name used twice in one module (an in-memory source beside the listed one) is the same failure.
    [Fact]
    public void ATwinSourceNameIsAPreparationFailure()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failures), string.Join(", ", failures));
        loaded.AddSource(Path.Combine(Path.GetDirectoryName(project)!, "main.kimi"), "public func twin() -> i32 => 2\n");
        var output = CheckService.Run(loaded, WindowsProfile.Target, CheckMode.Product, false, CheckInputSource.Disk, TestContext.Current.CancellationToken);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), Assert.Single(output.Diagnostics).Code);
    }

    // SPEC 23.2.2: an invalid test configuration is not established, so test preparation is Blocked before the front end.
    [Fact]
    public void AnInvalidTestProjectIdBlocksTestPreparation()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "TestProjectId=\" \"");
        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("TestProjectId", failure.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EarlierSyntaxErrorsSurviveALaterEncodingFailure()
    {
        var project = this.WriteProject("App", ("a.kimi", Broken));
        File.WriteAllBytes(this.PathOf("App", "b.kimi"), [0x66, 0xff, 0x0a]);
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));

        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var encoding = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd));
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "b.kimi")), Location(output, encoding));
        Assert.Null(encoding.Span);
        var syntax = output.Diagnostics.First(x => Location(output, x) == SourceIdentity.FromPath(this.PathOf("App", "a.kimi")));
        Assert.Contains(syntax.Code, console.Output);
        Assert.True(console.Output.IndexOf(syntax.Code, StringComparison.Ordinal) < console.Output.IndexOf(encoding.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableSourceIsBlockedUntilItCanBeRead()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        CheckOutput output;
        using (new FileStream(this.PathOf("App", "main.kimi"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            output = this.Run(project);
        }

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.SourceReadFailed_Kd));
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "main.kimi")), Location(output, failure));
        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public void EveryFailedSourceReadIsExplainedAtItsOwnInput()
    {
        var project = this.WriteProject("ReadFailures", ("a.kimi", Valid));
        var first = this.PathOf("ReadFailures", "a.kimi");
        var second = this.PathOf("ReadFailures", "b.kimi");
        File.WriteAllText(second, "public func two() -> i32 => 2\n");
        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var output = this.Run(project);
            Assert.Equal(CheckOutcome.Blocked, output.Outcome);
            Assert.False(output.Accepted);
            Assert.Equal([first, second], output.Diagnostics.Select(x => output.Sources[x.Source].Path));
            Assert.All(output.Diagnostics, static x =>
            {
                Assert.Equal(nameof(DiagnosticCode.SourceReadFailed_Kd), x.Code);
                Assert.Equal(DiagnosticCategory.Input, x.Category);
                Assert.Null(x.Span);
                Assert.NotEmpty(x.Note!);
            });
        }

        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public async Task EncodingFailuresPreserveEveryConsumedInputsOwnExplanation()
    {
        var project = this.WriteProject("EncodingFailures", ("a.kimi", Valid));
        File.WriteAllBytes(this.PathOf("EncodingFailures", "a.kimi"), [0xff]);
        File.WriteAllText(this.PathOf("EncodingFailures", "b.kimi"), Broken);
        File.WriteAllBytes(this.PathOf("EncodingFailures", "c.kimi"), [0xfe]);
        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Equal(["a.kimi", "c.kimi"], output.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd)).Select(x => Path.GetFileName(output.Sources[x.Source].Path)));
        Assert.Contains(output.Diagnostics, x => Path.GetFileName(output.Sources[x.Source].Path) == "b.kimi" && x.Category == DiagnosticCategory.Language);
        Assert.DoesNotContain(output.Diagnostics, static x => x.Code is nameof(DiagnosticCode.CheckFaulted_Kd) or nameof(DiagnosticCode.ProjectPreparationFailed_Kd));
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));
        Assert.Equal(2, console.Output.Split(nameof(DiagnosticCode.InvalidSourceEncoding_Kd), StringSplitOptions.None).Length - 1);
        Assert.Contains("a.kimi", console.Output, StringComparison.Ordinal);
        Assert.Contains("b.kimi", console.Output, StringComparison.Ordinal);
        Assert.Contains("c.kimi", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestSourceFailuresAreBlockedInputProblems(bool encoding)
    {
        var project = this.WriteProject("TestInput", ("main.kimi", Valid), settings: "TestSources={\"tests/one.kimi\"}");
        Directory.CreateDirectory(this.PathOf("TestInput", "tests"));
        var path = this.PathOf("TestInput", "tests", "one.kimi");
        if (encoding)
        {
            File.WriteAllBytes(path, [0xff]);
        }

        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.False(output.Accepted);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(encoding ? nameof(DiagnosticCode.InvalidSourceEncoding_Kd) : nameof(DiagnosticCode.SourceReadFailed_Kd), failure.Code);
        Assert.Equal(DiagnosticCategory.Input, failure.Category);
        Assert.Equal(path, output.Sources[failure.Source].Path);
        Assert.Null(failure.Span);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(output, [], SourceIdentity.FromPath(project))[identity]);
        Assert.Equal(failure.Code, sent.Code);
        Assert.Equal(default, sent.Range);
        Assert.Contains(failure.Message, sent.Message, StringComparison.Ordinal);
        File.WriteAllText(path, "#Test\nfunc check()\n    $expect(true)\n");
        Assert.True(this.Run(project, CheckMode.Test).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedProductInputDoesNotSwallowPendingOrCancelledTestReads(bool cancelled)
    {
        var project = this.WriteProject("PendingTest", ("main.kimi", Valid), settings: "TestSources={\"pending.kimi\"}");
        File.WriteAllBytes(this.PathOf("PendingTest", "main.kimi"), [0xff]);
        var path = this.PathOf("PendingTest", "pending.kimi");
        Exception exception = cancelled ? new OperationCanceledException() : new PendingInputException(path);
        var inputs = new FailingInputSource(path, exception);
        var thrown = Record.Exception(() => this.Run(project, CheckMode.Test, inputs: inputs));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task ConflictingDependencyInputsUnderALockReportTheCommandMessage()
    {
        this.WriteLibrary("LeafA", "leaf", "public func a() -> i32 => 1\n");
        this.WriteLibrary("LeafB", "leaf", "public func b() -> i32 => 2\n");
        var project = this.WriteProject(
            "App",
            ("main.kimi", Valid),
            dependencies: "A={PackageId=\"leaf\" PackageVersion=\"1\" Project=\"../LeafA/LeafA.kimiproj\"} B={PackageId=\"leaf\" PackageVersion=\"1\" Project=\"../LeafB/LeafB.kimiproj\"}");
        DependencyLock.Update(DependencyLock.PathForProject(project), DependencyResolver.Resolve(project, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));

        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Contains("Conflicting inputs for leaf@1", failure.Note);
        Assert.Contains("Conflicting inputs for leaf@1", console.Output);
    }

    [Fact]
    public void OverlaysReplaceDiskContentAndExistence()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        var added = this.PathOf("App", "added.kimi");
        var inputs = new OverlayInputSource(new() { [main] = Broken, [added] = "public func two() -> i32 => 2\n" });
        var output = this.Run(project, inputs: inputs);

        Assert.Contains(output.Diagnostics, x => Location(output, x) == SourceIdentity.FromPath(main) && x.Severity == DiagnosticSeverity.Error);
        Assert.Contains(added, inputs.Listed);
        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public void UnencodableOverlayTextIsAnEncodingDiagnostic()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        var output = this.Run(project, inputs: new OverlayInputSource(new() { [main] = "// \ud800\n" + Valid }));

        Assert.False(output.Accepted);
        var encoding = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd));
        Assert.Equal(SourceIdentity.FromPath(main), Location(output, encoding));
        Assert.NotNull(encoding.Span);
    }

    [Theory]
    [InlineData(Valid, TestPresence.No)]
    [InlineData("#Test\nfunc check()\n    $expect(true)\n", TestPresence.Yes)]
    [InlineData("#Test\nfunc check()\n    Missing.call()\n", TestPresence.Yes)]
    [InlineData(Broken, TestPresence.Unknown)]
    public void ProductChecksReportTestPresence(string source, TestPresence presence)
    {
        var project = this.WriteProject("App", ("main.kimi", source));
        Assert.Equal(presence, this.Run(project).Presence);
    }

    [Fact]
    public void TestModeChecksTestSources()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "TestSources={\"tests/one.kimi\"}");
        Directory.CreateDirectory(this.PathOf("App", "tests"));
        File.WriteAllText(this.PathOf("App", "tests", "one.kimi"), "#Test\nfunc returnsOne()\n    $expect(true)\n");

        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Completed, output.Outcome);
        Assert.True(output.Accepted, string.Join("; ", output.Diagnostics.Select(x => $"{x.Source}{x.Span}: {x.Message}")));
    }

    [Fact]
    public void TestModeRejectsAnUntestableTarget()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "Targets={\"x86_64-unknown-linux-gnu\"}");
        var output = this.Run(project, CheckMode.Test, "x86_64-unknown-linux-gnu");

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Contains(output.Diagnostics, static x => x.Note?.Contains("Windows x64", StringComparison.Ordinal) == true);
    }

    // SPEC 23.4: a pending input is the state owner's decision; loading a project never turns it into a load failure.
    [Fact]
    public void APendingProjectFileReadIsNotALoadFailure()
    {
        var project = this.WriteProject("Pending", ("main.kimi", Valid));
        var exception = new PendingInputException(project);
        var thrown = Record.Exception(() => Project.TryCreate(Kimigayo.CreateSilent(), null, project, new FailingInputSource(project, exception), out _, out _));
        Assert.Same(exception, thrown);
    }

    // SPEC 23.2.1: the scan is syntactic, so a marker on a local function is found; test preparation then explains the invalid definition.
    [Fact]
    public void ATestMarkerOnALocalFunctionIsFoundAndThenRejected()
    {
        const string Source = "public func outer() -> ()\n    #Test\n    func inner() -> () => ()\n    inner()\n";
        var project = this.WriteProject("Local", ("main.kimi", Source));
        Assert.Equal(TestPresence.Yes, this.Run(project).Presence);
        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Completed, output.Outcome);
        Assert.False(output.Accepted);
        Assert.Contains(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidTestDefinition_Kd));
    }

    private static SourceIdentity Location(CheckOutput output, CheckDiagnostic diagnostic)
        => diagnostic.Source < 0 ? default : SourceIdentity.FromPath(output.Sources[diagnostic.Source].Path);

    private CheckOutput Run(string projectPath, CheckMode mode = CheckMode.Product, string target = "", CheckInputSource? inputs = null)
    {
        inputs ??= CheckInputSource.Disk;
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, projectPath, inputs, out var project, out var failures), string.Join(", ", failures));
        return CheckService.Run(project, target.Length == 0 ? WindowsProfile.Target : target, mode, false, inputs, TestContext.Current.CancellationToken);
    }

    private string PathOf(params string[] parts)
        => Path.Combine([this.directory, .. parts]);

    private string WriteProject(string name, (string File, string Text)? source = null, string settings = "", string dependencies = "")
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        var targets = settings.Contains("Targets=", StringComparison.Ordinal) ? string.Empty : $"Targets={{\"{WindowsProfile.Target}\"}} ";
        File.WriteAllText(path, $"OutputKind=\"Library\" {targets}Dependencies={{{dependencies}}} {settings}");
        if (source is var (file, text))
        {
            File.WriteAllText(Path.Combine(folder, file), text);
        }

        return path;
    }

    private void WriteLibrary(string name, string id, string source)
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".kimiproj"), $"OutputKind=\"Library\" PackageId=\"{id}\" PackageVersion=\"1\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(folder, "main.kimi"), source);
    }

    private sealed class OverlayInputSource(Dictionary<string, string> overlays) : CheckInputSource
    {
        public List<string> Listed { get; } = [];

        public override byte[] ReadAllBytes(string path)
            => overlays.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : Disk.ReadAllBytes(path);

        public override SourceContent ReadSource(string path)
            => overlays.TryGetValue(path, out var text) ? SourceContent.FromText(text, false) : Disk.ReadSource(path);

        public override string[] GetFiles(string directory, string pattern)
        {
            var files = new SortedSet<string>(Disk.GetFiles(directory, pattern), StringComparer.Ordinal);
            foreach (var path in overlays.Keys)
            {
                if (SourceIdentity.PathComparer.Equals(Path.GetDirectoryName(path), directory) && path.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(path);
                }
            }

            this.Listed.AddRange(files);
            return files.ToArray();
        }
    }

    private sealed class FailingInputSource(string failing, Exception failure) : CheckInputSource
    {
        public override byte[] ReadAllBytes(string path) => SourceIdentity.PathComparer.Equals(path, failing) ? throw failure : Disk.ReadAllBytes(path);

        public override SourceContent ReadSource(string path) => SourceIdentity.PathComparer.Equals(path, failing) ? throw failure : Disk.ReadSource(path);

        public override string[] GetFiles(string directory, string pattern) => Disk.GetFiles(directory, pattern);
    }

    private sealed class CapturingConsole : IConsoleService
    {
        private readonly StringBuilder output = new();

        public string Output => this.output.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message).Append('\n');

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}

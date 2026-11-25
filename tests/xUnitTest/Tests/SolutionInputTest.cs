// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi;
using Kimi.Build;
using Kimi.Command;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class SolutionInputTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-input-" + Guid.NewGuid().ToString("N"));

    public SolutionInputTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    // IMPL 20.3.1: any input not ending in .kimi, .kimiproj, .kimisln or .exe, with or without another extension (PLAN G1274), resolves
    // to the path A, else A.kimiproj, else A.kimi.
    [Theory]
    [InlineData("A")]
    [InlineData("A.other")]
    public void ResolvesExactPathThenProjectThenSource(string name)
    {
        var stem = Path.Combine(this.directory, name);
        var source = this.Write(name + ".kimi", "::Kimi.Console.writeLine(\"single\")");
        Assert.Equal(source, Solution.ResolveInputPath(stem));
        var project = this.Write(name + ".kimiproj", "invalid project contents");
        Assert.Equal(project, Solution.ResolveInputPath(stem));
        Assert.Equal(source, Solution.ResolveInputPath(source));
        Assert.Equal(project, Solution.ResolveInputPath(Path.GetRelativePath(Directory.GetCurrentDirectory(), stem)));
        Directory.CreateDirectory(stem);
        Assert.Equal(stem, Solution.ResolveInputPath(stem));
        Assert.Throws<CommandInputException>(() => this.Load(stem));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A.other")]
    public void ExistingUnsupportedFileDoesNotFallBack(string name)
    {
        this.Write(name + ".kimiproj", "{}");
        this.Write(name + ".kimi", "()");
        var path = this.Write(name, "()");
        Assert.Throws<CommandInputException>(() => Solution.ResolveInputPath(path));
    }

    // IMPL 20.3.1: an input ending in one of the four extensions selects only that path, and its error names only that path.
    [Theory]
    [InlineData("A.kimi")]
    [InlineData("A.kimiproj")]
    [InlineData("A.kimisln")]
    [InlineData("A.EXE")]
    public void ExplicitExtensionIsNeverAppendedAgain(string name)
    {
        this.Write(name + ".kimiproj", "{}");
        this.Write(name + ".kimi", "()");
        var path = Path.Combine(this.directory, name);
        var exception = Assert.Throws<CommandInputException>(() => Solution.ResolveInputPath(path));
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(path + Constants.KimiProjectExtension, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("absent.ext")]
    public void MissingNameReportsAllCandidates(string name)
    {
        var path = Path.Combine(this.directory, name);
        var exception = Assert.Throws<CommandInputException>(() => Solution.ResolveInputPath(path));
        Assert.Contains(path, exception.Message);
        Assert.Contains(path + ".kimiproj", exception.Message);
        Assert.Contains(path + ".kimi", exception.Message);
    }

    [Fact]
    public async Task InvalidSelectedProjectDoesNotFallBackToValidSource()
    {
        this.Write("A.kimiproj", "OutputKind=\"Invalid\"");
        this.Write("A.kimi", "::Kimi.Console.writeLine(\"single\")");
        var solution = this.Load(Path.Combine(this.directory, "A"));
        Assert.Empty(solution.Projects);
        Assert.False(await solution.Generate(TestContext.Current.CancellationToken));
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        Assert.False(await solution.Build(TestContext.Current.CancellationToken));
        Assert.Throws<CommandInputException>(() => solution.PrepareRun());
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
    }

    [Fact]
    public void InvalidDiscoveredSolutionDoesNotFallBackToProjects()
    {
        this.Write("A.kimisln", "Projects = true");
        this.Write("A.kimiproj", "Targets=\"x86_64-pc-windows-msvc\" OutputKind=\"Application\"");
        Assert.Throws<CommandInputException>(() => this.Load(this.directory));
    }

    [Fact]
    public void DirectoryKeepsSolutionDiscoveryAndNeverCollectsLooseSources()
    {
        var project = this.Write("A.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} OutputKind=\"Application\"");
        this.Write("B.kimiproj", "OutputKind=\"Invalid\"");
        this.Write("Single.kimi", "()");
        this.Write("Chosen.kimisln", "Projects={\"A.kimiproj\"}");
        var solution = this.Load(this.directory);
        Assert.Equal(project, Assert.Single(solution.Projects).Key);
    }

    // IMPL 20.3.1: a directory holding more than one .kimisln is an error naming them, before any project loads (one selects it:
    // DirectoryKeepsSolutionDiscoveryAndNeverCollectsLooseSources); a named .kimisln beside another selects that solution.
    [Fact]
    public void SeveralSolutionsInADirectoryAreAnErrorAndANamedOneIsSelected()
    {
        this.Write("A.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} OutputKind=\"Application\"");
        var project = this.Write("B.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} OutputKind=\"Application\"");
        var first = this.Write("A.kimisln", "Projects={\"A.kimiproj\"}");
        var second = this.Write("B.kimisln", "Projects={\"B.kimiproj\"}");
        var exception = Assert.Throws<CommandInputException>(() => this.Load(this.directory));
        Assert.Contains(first, exception.Message, StringComparison.Ordinal);
        Assert.Contains(second, exception.Message, StringComparison.Ordinal);
        Assert.Equal(project, Assert.Single(this.Load(second).Projects).Key);
    }

    [Fact]
    public async Task ImplicitProjectEmitsOnlyTheSelectedSourceAndUsesExplicitTarget()
    {
        var source = this.Write("Single.kimi", "::Kimi.Console.writeLine(\"single\")");
        this.Write("BrokenSibling.kimi", "let broken =");
        var solution = this.Load(Path.Combine(this.directory, "Single"));
        var project = Assert.Single(solution.Projects).Value;
        Assert.Equal(source, Assert.Single(solution.Projects).Key);
        Assert.Equal("Single", project.Name);
        Assert.Equal(this.directory, project.Directory);
        Assert.Equal(OutputKind.Application, project.ProjectFile.OutputKind);
        Assert.Equal("O2", project.ProjectFile.Optimization);
        Assert.Equal(TargetProfile.Windows.Target, Assert.Single(project.ProjectFile.Targets));
        Assert.True(await solution.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project, TargetProfile.Windows);
        Assert.True(File.Exists(paths.Ir));
        Assert.True(File.Exists(paths.Manifest));
        Assert.False(File.Exists(paths.Record));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".kimiproj")));
        Assert.Equal(Path.Combine(this.directory, "bin", TargetProfile.Windows.Target, "Single.O2.exe"), paths.Executable);
    }

    [Fact]
    public void ImplicitHostDefaultDoesNotChangeExplicitProjectSettings()
    {
        var source = this.Write("Single.kimi", "()");
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        solution.LoadForBuild(null, new(), [source]);
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            solution.PrepareProject(null);
            Assert.Equal(TargetProfile.Windows.Target, Assert.Single(Assert.Single(solution.Projects).Value.ProjectFile.Targets));
        }
        else
        {
            Assert.Throws<CommandInputException>(() => solution.PrepareProject(null));
        }

        var configured = this.Write("Configured.kimiproj", "Targets={\"aarch64-pc-windows-msvc\"} OutputKind=\"Library\" Optimization=\"O0\"");
        var project = Assert.Single(this.Load(configured).Projects).Value;
        Assert.Equal("aarch64-pc-windows-msvc", Assert.Single(project.ProjectFile.Targets));
        Assert.Equal(OutputKind.Library, project.ProjectFile.OutputKind);
        Assert.Equal("O0", project.ProjectFile.Optimization);
    }

    // A run's later steps, on a host that runs its Windows target; on any other the host rule refuses it first, as
    // CommandExecutionTest.RunRefusesATargetThisHostDoesNotRunBeforeAnyBuild reads on every host (the Tatara plan's B7b8, C-575 B1).
    [Fact]
    public async Task RunPreparesImplicitSettingsWithoutDecodingOrCompilingSource()
    {
        Assert.SkipUnless(TargetProfile.Host == TargetProfile.Windows, "The run needs a host that runs the Windows target.");
        var source = Path.Combine(this.directory, "Single.kimi");
        File.WriteAllBytes(source, [0xff]);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        var solution = new Solution(new Kimigayo(console));
        solution.LoadForBuild(null, new() { Target = TargetProfile.Windows.Target }, [source]);
        solution.PrepareProject(null);
        Assert.True(solution.PrepareRun());
        Assert.Equal(1, await solution.Run(TestContext.Current.CancellationToken)); // The toolchain's failure before launch (IMPL 20.3).
        Assert.Contains("Run 'kimi build' first", console.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
    }

    [Fact]
    public void MultipleSourcesRemainIndependentAndCannotBeRunAsOneApplication()
    {
        var first = this.Write("First.kimi", "()");
        var second = this.Write("Second.kimi", "()");
        var solution = this.Load(first, second, first);
        Assert.Equal(2, solution.Projects.Count);
        var projects = solution.Projects.Values.ToArray();
        Assert.NotSame(projects[0].ProjectFile, projects[1].ProjectFile);
        projects[0].ProjectFile.Optimization = "O0";
        Assert.Equal("O2", projects[1].ProjectFile.Optimization);
        Assert.Throws<CommandInputException>(() => solution.PrepareRun());
        Assert.Throws<CommandInputException>(() => this.Load(first, Path.Combine(this.directory, "missing")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckDoesNotReadTestOnlyFilesOrPublishArtifacts(bool testExists)
    {
        var path = this.Write("Check.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} TestSources={\"Broken.kimi\"} TestDependencies={Missing={PackageId=\"example.missing\" PackageVersion=\"1\" Project=\"missing.kimiproj\"}}");
        this.Write("Main.kimi", "Console.writeLine(\"checked without running\")");
        if (testExists)
        {
            File.WriteAllBytes(Path.Combine(this.directory, "Broken.kimi"), [0xff]);
        }

        var solution = this.Load(path);
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
        Assert.False(File.Exists(Path.ChangeExtension(path, "kimi.lock.json")));
    }

    [Fact]
    public async Task CheckRejectsDuplicateResolvedTestPaths()
    {
        var path = this.Write("Check.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} TestSources={\"tests/../Test.kimi\" \"Test.kimi\"}");
        this.Write("Main.kimi", "()");
        var solution = this.Load(path);
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDependencyProjectsStillValidateExistingLocks(bool locked)
    {
        var path = this.Write("Check.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"}");
        this.Write("Main.kimi", "()");
        var lockPath = DependencyLock.PathForProject(path);
        File.WriteAllText(lockPath, "corrupt lock");
        var solution = this.Load(path);
        solution.KimiOptions.Locked = locked;
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        Assert.False(await solution.Generate(TestContext.Current.CancellationToken));
        Assert.Equal("corrupt lock", File.ReadAllText(lockPath));
        var resolution = DependencyResolver.Resolve(path, TargetProfile.Windows.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken);
        DependencyLock.Update(lockPath, resolution, TestContext.Current.CancellationToken);
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        var bytes = File.ReadAllBytes(lockPath);
        this.Write("Main.kimi", "Console.writeLine(\"source edit\")");
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        Assert.Equal(bytes, File.ReadAllBytes(lockPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductGraphRequiresCurrentLockAndChecksLiveSources(bool locked)
    {
        Directory.CreateDirectory(Path.Combine(this.directory, "Library"));
        var path = this.Write("Root.kimiproj", "Targets={\"x86_64-pc-windows-msvc\"} Dependencies={Lib={PackageId=\"example.library\" PackageVersion=\"1\" Project=\"Library/Library.kimiproj\"}} TestDependencies={Missing={PackageId=\"missing\" PackageVersion=\"1\" Project=\"missing.kimiproj\"}}");
        this.Write("Main.kimi", "public func main() => Lib.Api.value()");
        this.Write("Library/Library.kimiproj", "OutputKind=\"Library\" PackageId=\"example.library\" PackageVersion=\"1\" Targets={\"x86_64-pc-windows-msvc\"}");
        var librarySource = this.Write("Library/Library.kimi", "public group Api\n    public func value() => ()");
        var solution = this.Load(path);
        solution.KimiOptions.Locked = locked;
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        var lockPath = DependencyLock.PathForProject(path);
        Assert.False(File.Exists(lockPath));
        var resolution = DependencyResolver.Resolve(path, TargetProfile.Windows.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken);
        Assert.True(resolution.Product.IsResolved);
        Assert.False(resolution.Test.IsResolved);
        DependencyLock.Update(lockPath, resolution, TestContext.Current.CancellationToken);
        var lockBytes = File.ReadAllBytes(lockPath);
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
        Assert.True(await solution.Generate(TestContext.Current.CancellationToken));
        var ir = Path.Combine(this.directory, "bin", TargetProfile.Windows.Target, "Root.ll");
        Assert.True(File.Exists(ir));
        Assert.False(File.Exists(Path.ChangeExtension(ir, ".link.build.json")));
        File.AppendAllText(librarySource, "\npublic func unused() => missing()");
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        Assert.Equal(lockBytes, File.ReadAllBytes(lockPath));
        File.WriteAllText(librarySource, "public group Api\n    public func value() => ()");
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        File.WriteAllBytes(librarySource, [0xff]);
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        Assert.Equal(lockBytes, File.ReadAllBytes(lockPath));
    }

    private string Write(string name, string contents)
    {
        var path = Path.Combine(this.directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private Solution Load(params string[] paths)
    {
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        solution.LoadForBuild(null, new() { Target = TargetProfile.Windows.Target }, paths);
        solution.PrepareProject(null);
        return solution;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi;
using Kimi.Command;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class SolutionInputTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-input-" + Guid.NewGuid().ToString("N"));

    public SolutionInputTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void ResolvesExactPathThenProjectThenSource()
    {
        var stem = Path.Combine(this.directory, "A");
        var source = this.Write("A.kimi", "::Core.writeLine(\"single\")");
        Assert.Equal(source, Solution.ResolveInputPath(stem));
        var project = this.Write("A.kimiproj", "invalid project contents");
        Assert.Equal(project, Solution.ResolveInputPath(stem));
        Assert.Equal(source, Solution.ResolveInputPath(source));
        Assert.Equal(project, Solution.ResolveInputPath(Path.GetRelativePath(Directory.GetCurrentDirectory(), stem)));
        Directory.CreateDirectory(stem);
        Assert.Equal(stem, Solution.ResolveInputPath(stem));
        Assert.Throws<InvalidDataException>(() => this.Load(stem));
    }

    [Fact]
    public void ExistingUnsupportedFileDoesNotFallBack()
    {
        this.Write("A.kimiproj", "{}");
        this.Write("A.kimi", "()");
        var path = this.Write("A", "()");
        Assert.Throws<InvalidDataException>(() => Solution.ResolveInputPath(path));
    }

    [Theory]
    [InlineData("A.kimi")]
    [InlineData("A.kimiproj")]
    [InlineData("A.kimisln")]
    [InlineData("A.other")]
    public void ExplicitExtensionIsNeverAppendedAgain(string name)
    {
        this.Write(name + ".kimiproj", "{}");
        this.Write(name + ".kimi", "()");
        Assert.Throws<FileNotFoundException>(() => Solution.ResolveInputPath(Path.Combine(this.directory, name)));
    }

    [Fact]
    public void MissingNameReportsAllCandidates()
    {
        var path = Path.Combine(this.directory, "absent");
        var exception = Assert.Throws<FileNotFoundException>(() => Solution.ResolveInputPath(path));
        Assert.Contains(path, exception.Message);
        Assert.Contains(path + ".kimiproj", exception.Message);
        Assert.Contains(path + ".kimi", exception.Message);
    }

    [Fact]
    public async Task InvalidSelectedProjectDoesNotFallBackToValidSource()
    {
        this.Write("A.kimiproj", "OutputKind=\"Invalid\"");
        this.Write("A.kimi", "::Core.writeLine(\"single\")");
        var solution = this.Load(Path.Combine(this.directory, "A"));
        Assert.Empty(solution.Projects);
        Assert.False(await solution.Generate(TestContext.Current.CancellationToken));
        Assert.False(await solution.Build(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => solution.Run(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
    }

    [Fact]
    public void InvalidDiscoveredSolutionDoesNotFallBackToProjects()
    {
        this.Write("A.kimisln", "Projects = true");
        this.Write("A.kimiproj", "Targets=\"x86_64-pc-windows-msvc\" OutputKind=\"Application\"");
        Assert.Throws<InvalidDataException>(() => this.Load(this.directory));
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

    [Fact]
    public async Task ImplicitProjectEmitsOnlyTheSelectedSourceAndUsesExplicitTarget()
    {
        var source = this.Write("Single.kimi", "::Core.writeLine(\"single\")");
        this.Write("BrokenSibling.kimi", "let broken =");
        var solution = this.Load(Path.Combine(this.directory, "Single"));
        var project = Assert.Single(solution.Projects).Value;
        Assert.Equal(source, Assert.Single(solution.Projects).Key);
        Assert.Equal("Single", project.Name);
        Assert.Equal(this.directory, project.Directory);
        Assert.Equal(OutputKind.Application, project.ProjectFile.OutputKind);
        Assert.Equal("O2", project.ProjectFile.Optimization);
        Assert.Equal(WindowsProfile.Target, Assert.Single(project.ProjectFile.Targets));
        Assert.True(await solution.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        Assert.True(File.Exists(paths.Ir));
        Assert.True(File.Exists(paths.Manifest));
        Assert.False(File.Exists(paths.Record));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".kimiproj")));
        Assert.Equal(Path.Combine(this.directory, "bin", WindowsProfile.Target, "Single.O2.exe"), paths.Executable);
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
            Assert.Equal(WindowsProfile.Target, Assert.Single(Assert.Single(solution.Projects).Value.ProjectFile.Targets));
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => solution.PrepareProject(null));
        }

        var configured = this.Write("Configured.kimiproj", "Targets={\"aarch64-pc-windows-msvc\"} OutputKind=\"Library\" Optimization=\"O0\"");
        var project = Assert.Single(this.Load(configured).Projects).Value;
        Assert.Equal("aarch64-pc-windows-msvc", Assert.Single(project.ProjectFile.Targets));
        Assert.Equal(OutputKind.Library, project.ProjectFile.OutputKind);
        Assert.Equal("O0", project.ProjectFile.Optimization);
    }

    [Fact]
    public async Task RunPreparesImplicitSettingsWithoutDecodingOrCompilingSource()
    {
        var source = Path.Combine(this.directory, "Single.kimi");
        File.WriteAllBytes(source, [0xff]);
        var solution = this.Load(source);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => solution.Run(TestContext.Current.CancellationToken));
        Assert.Contains("Run 'kimi build' first", error.Message);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
    }

    [Fact]
    public async Task MultipleSourcesRemainIndependentAndCannotBeRunAsOneApplication()
    {
        var first = this.Write("First.kimi", "()");
        var second = this.Write("Second.kimi", "()");
        var solution = this.Load(first, second, first);
        Assert.Equal(2, solution.Projects.Count);
        var projects = solution.Projects.Values.ToArray();
        Assert.NotSame(projects[0].ProjectFile, projects[1].ProjectFile);
        projects[0].ProjectFile.Optimization = "O0";
        Assert.Equal("O2", projects[1].ProjectFile.Optimization);
        await Assert.ThrowsAsync<InvalidDataException>(() => solution.Run(TestContext.Current.CancellationToken));
        Assert.Throws<FileNotFoundException>(() => this.Load(first, Path.Combine(this.directory, "missing")));
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
        solution.LoadForBuild(null, new() { Target = WindowsProfile.Target }, paths);
        solution.PrepareProject(null);
        return solution;
    }
}

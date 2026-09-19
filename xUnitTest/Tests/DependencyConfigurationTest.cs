// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class DependencyConfigurationTest
{
    [Theory]
    [InlineData("PackageId=\"Example.math\" PackageVersion=\"1\"")]
    [InlineData("PackageId=\"example..math\" PackageVersion=\"1\"")]
    [InlineData("PackageId=\"example.math\"")]
    [InlineData("PackageId=\"example.math\" PackageVersion=\"*\"")]
    [InlineData("Dependencies={Math={PackageId=\"example.math\" PackageVersion=\"1\" Project=\"M.kimiproj\" Package=\"M.kimipkg\"}}")]
    [InlineData("Dependencies={Kimi={PackageId=\"example.core\" PackageVersion=\"1\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"example.math\" PackageVersion=\"1\"} Math={PackageId=\"example.math\" PackageVersion=\"2\"}}")]
    [InlineData("KotonohaArray={{Name=\"Math\" Version=\"1\"}}")]
    [InlineData("PackageId=\"a-\" PackageVersion=\"1\"")]
    [InlineData("PackageId=\"1a\" PackageVersion=\"1\"")]
    [InlineData("PackageId=\"a\" PackageVersion=\"v 1\"")]
    [InlineData("Dependencies={\"not-a-name\"={PackageId=\"a\" PackageVersion=\"1\"}}")]
    [InlineData("Dependencies={\"e\\u0301\"={PackageId=\"a\" PackageVersion=\"1\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\" Project=\"M.kimi\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\" Package=\"\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\"}} Dependencies={\"Ma\\u0074h\"={PackageId=\"a\" PackageVersion=\"2\"}}")]
    [InlineData("TestDependencies={Math={PackageId=\"a\" PackageVersion=\"1\"} Math={PackageId=\"a\" PackageVersion=\"2\"}}")]
    [InlineData("PackageSources={{Store=\"packages\" PackageId=\"a\"}}")]
    [InlineData("PackageSources={{PackageId=\"a\" PackageVersion=\"1\"}}")]
    [InlineData("TestSources={\"test.kimi\" \"test.kimi\"}")]
    [InlineData("TestSources={\"tests/*.kimi\"}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={ContractId=\"c\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"dynamic\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\" ContractId=\"\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\" Sha256=\"abc\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\" Sha256=\"gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg\"}}}")]
    [InlineData("NativeLibraries={\"x86_64-pc-windows-msvc\"={codec={Input=\"codec.lib\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\"}}} NativeLibraries={\"x86_64-pc-windows-msvc\"={codec={Kind=\"import\" Input=\"codec.lib\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\" ContractId=\"a\"}}} NativeLibraries={\"x86_64-pc-windows-msvc\"={codec={ContractId=\"b\" Input=\"codec.lib\"}}}")]
    [InlineData("NativeLibraries={\"x86_64-pc-windows-msvc\"={kernel32={Kind=\"import\" Input=\"kernel32.lib\"}}}")]
    [InlineData("NativeRequirements={t={codec={Kind=\"static\"} codec={Kind=\"import\"}}}")]
    [InlineData("NativeRequirements={t={codec={Kind=\"static\"}} t={codec={Kind=\"import\"}}}")]
    [InlineData("NativeRequirements={t={codec={Kind=\"static\"}}} NativeRequirements={t={codec={Kind=\"import\"}}}")]
    [InlineData("NativeLibraries={t={codec={Kind=\"static\" Input=\"a.lib\"} codec={Kind=\"static\" Input=\"b.lib\"}}}")]
    public void InvalidDependencyDeclarationsCannotBeSilentlyLoaded(string source)
        => Assert.Throws<TinyhandException>(() => ProjectFile.Load(Encoding.UTF8.GetBytes(source)));

    [Theory]
    [InlineData("NativeBindings={}")]
    [InlineData("NativeBindings={\"x86_64-pc-windows-msvc\"={codec=\"codec.lib\"}}")]
    public void SupersededNativeBindingsRequireMigration(string source)
        => Assert.Equal(NativeConfiguration.SupersededBindings, Assert.Throws<TinyhandException>(() => ProjectFile.Load(Encoding.UTF8.GetBytes(source))).Message);

    [Fact]
    public void IndentedNativeRequirementIsRead()
    {
        var file = ProjectFile.Load(Encoding.UTF8.GetBytes("NativeRequirements=\n  \"x86_64-pc-windows-msvc\"=\n    codec={ Kind=\"static\" ContractId=\"example.codec.v1\" }\n"))!;
        var requirement = file.NativeRequirements[WindowsProfile.Target]["codec"];
        Assert.Equal(("static", "example.codec.v1", (string?)null), (requirement.Kind, requirement.ContractId, requirement.Sha256));
    }

    [Fact]
    public void InvalidProgrammaticNativeRequirementsCannotPrepare()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() };
        Assert.False(c.Prepare(WindowsProfile.Target));
        Assert.Contains(c.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.InvalidDependencyConfiguration_Kd));
    }

    [Fact]
    public void LegacyProgrammaticReferencesCannotBeSilentlyIgnored()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.KotonohaArray = [new() { Name = "Math", Version = "1" }];
        Assert.False(c.Prepare(WindowsProfile.Target));
        Assert.Contains(c.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.InvalidDependencyConfiguration_Kd));
    }

    [Fact]
    public async Task CheckingNoLoadedProjectsFails()
    {
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckingNoConfiguredTargetsFails()
    {
        var project = Compilation.CreateForTest().Project;
        project.ProjectFile.Targets = [];
        Assert.False(await project.Check(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("PackageId=\"example.math\" PackageVersion=\"V1.0-RC+ABC\"")]
    [InlineData("PackageId=\"a.1-b2\" PackageVersion=\"1\"")]
    [InlineData("Dependencies={数学={PackageId=\"example.math\" PackageVersion=\"1\" Project=\"../Math/Math.kimiproj\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\"} math={PackageId=\"a\" PackageVersion=\"2\"}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\"}} TestDependencies={Math={PackageId=\"a\" PackageVersion=\"1\"}}")]
    [InlineData("PackageSources={{Store=\"packages\"} {PackageId=\"a\" PackageVersion=\"1\" Package=\"a.kimipkg\"}}")]
    [InlineData("KotonohaArray={} Dependencies={} TestSources={\"missing.kimi\"}")]
    [InlineData("Dependencies=\n  Math={PackageId=\"example.math\" PackageVersion=\"1\" Project=\"../Math/Math.kimiproj\"}\n")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"static\" ContractId=\"example.codec.v1\"}}}")]
    [InlineData("NativeRequirements={\"x86_64-pc-windows-msvc\"={codec={Kind=\"import\" Sha256=\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}}} NativeLibraries={\"x86_64-pc-windows-msvc\"={codec={Input=\"native/codec.lib\" Sha256=\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}}")]
    [InlineData("NativeLibraries={\"x86_64-pc-windows-msvc\"={observer={Kind=\"static\" ContractId=\"o\" Input=\"native/observer.lib\"}}}")]
    [InlineData("NativeRequirements=\n  \"x86_64-pc-windows-msvc\"=\n    codec={ Kind=\"static\" ContractId=\"example.codec.v1\" }\n")]
    [InlineData("NativeRequirements={a={codec={Kind=\"static\"}} b={codec={Kind=\"import\"}}} NativeLibraries={a={codec={Input=\"codec.lib\"}}}")]
    [InlineData("NativeLibraries=\n  \"x86_64-pc-windows-msvc\"=\n    observer={ Kind=\"static\" Input=\"native/observer.lib\" }\n")]
    public void ConfigurationRoundTripsWithoutReadingSources(string source)
    {
        var file = ProjectFile.Load(Encoding.UTF8.GetBytes(source));
        Assert.NotNull(file);
        var bytes = TinyhandSerializer.SerializeToUtf8(file);
        var restored = ProjectFile.Load(bytes);
        Assert.Equal(bytes, TinyhandSerializer.SerializeToUtf8(restored));
    }

    [Fact]
    public void UnresolvedProductGraphsCannotCertifyPreparation()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.Dependencies.Add("Math", new() { PackageId = "example.math", PackageVersion = "1", Project = "Math.kimiproj" });
        Assert.False(c.Prepare(WindowsProfile.Target));
        Assert.Contains(c.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.UnresolvedDependencyGraph_Kd));
        Assert.Null(c.BuildMetadata);
    }

    [Theory]
    [InlineData("Math")]
    [InlineData("Geometry")]
    public void DocumentedSourceProjectsLoad(string name)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../examples/SourceDependencies", name, name + ".kimiproj"));
        Assert.NotNull(ProjectFile.Load(File.ReadAllBytes(path)));
    }

    [Fact]
    public void TestOnlyReferencesDoNotEnterProductPreparation()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.TestDependencies.Add("Math", new() { PackageId = "example.math", PackageVersion = "1", Project = "missing.kimiproj" });
        Assert.True(c.Prepare(WindowsProfile.Target));
    }

    [Fact]
    public async Task SemanticCheckHonorsCancellation()
    {
        var c = Compilation.CreateForTest();
        var solution = new Solution(c.Kimigayo);
        solution.Projects.Add("test", c.Project);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solution.Check(cancellation.Token));
        Assert.Empty(c.Project.BuildMetadata);
    }

    [Fact]
    public void WarmConfigurationValidationAllocatesNothing()
    {
        var file = ProjectFile.Load(Encoding.UTF8.GetBytes("PackageId=\"a\" PackageVersion=\"V1+build\" Dependencies={Math={PackageId=\"b\" PackageVersion=\"2\" Project=\"Math.kimiproj\"}} TestSources={\"tests/test.kimi\"}"))!;
        Assert.Null(DependencyConfiguration.Validate(file));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (DependencyConfiguration.Validate(file) is not null)
            {
                throw new InvalidOperationException("Dependency configuration validation failed.");
            }
        }));
    }
}

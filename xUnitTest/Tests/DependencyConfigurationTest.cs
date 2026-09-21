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
    [InlineData("NativeLibraries={t={codec={Kind=\"static\" Input=\"a.lib\"}} t={observer={Kind=\"static\" Input=\"b.lib\"}}}")]
    [InlineData("NativeLibraries={t={codec={Kind=\"static\" Input=\"a.lib\"}}} NativeLibraries={t={{Name=\"observer\" Kind=\"static\" Input=\"b.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Name=\"codec\" Kind=\"static\" Input=\"a.lib\"} {Name=\"codec\" Kind=\"static\" Input=\"b.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Kind=\"static\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Name=\"codec\" Kind=\"static\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"Example..codec\" PackageVersion=\"1\"} Name=\"codec\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\"} Name=\"codec\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Name=\"codec\" ContractId=\"\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Name=\"kernel32\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={codec={Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={codec={Name=\"other\" Kind=\"static\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={\"x\"}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"p\" PackageVersion=\"1\"} Name=\"codec\" ContractId=\"a\" Input=\"a.lib\"} {Package={PackageId=\"p\" PackageVersion=\"1\"} Name=\"codec\" ContractId=\"b\" Input=\"b.lib\"}}}")]
    [InlineData("NativeRequirements={t={{Kind=\"static\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1\"} Name=\"kimi_backend\" Input=\"a.lib\"}}}")]
    [InlineData("NativeLibraries={t={codec={Kind=\"static\" Input=\"a.lib\"}}} NativeLibraries={u={codec={Kind=\"static\" Input=\"b.lib\"}}}")]
    [InlineData("NativeRequirements={t={codec={Kind=\"static\"}}} NativeRequirements={u={codec={Kind=\"static\"}}}")]
    [InlineData("Dependencies={Math={PackageId=\"a\" PackageVersion=\"1\"}} Dependencies={Geometry={PackageId=\"b\" PackageVersion=\"1\"}}")]
    [InlineData("TestDependencies={Math={PackageId=\"a\" PackageVersion=\"1\"}} TestDependencies={Geometry={PackageId=\"b\" PackageVersion=\"1\"}}")]
    [InlineData("CompileTimeSettings={A={Bool=true}} CompileTimeSettings={B={Bool=true}}")]
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
    public void NativeLibraryRecordArraysSeparateSelfAndPackageTargets()
    {
        var file = ProjectFile.Load(Encoding.UTF8.GetBytes("NativeLibraries=\n  \"x86_64-pc-windows-msvc\"=\n    {\n      Package={ PackageId=\"example.codec\" PackageVersion=\"1.0.0\" }\n      Name=\"codec\"\n      ContractId=\"example.codec.v1\"\n      Input=\"native/codec.lib\"\n    }\n    { Name=\"observer\" Kind=\"static\" Input=\"native/observer.lib\" }\n"))!;
        var supplies = file.NativeLibraries[WindowsProfile.Target];
        var observer = Assert.Single(supplies);
        Assert.Equal(("observer", "static", (NativePackageTarget?)null), (observer.Key, observer.Value.Kind, observer.Value.Package));
        var codec = Assert.Single(supplies.Packaged);
        Assert.Equal(("codec", "example.codec", "1.0.0", "example.codec.v1", "native/codec.lib", (string?)null), (codec.Name, codec.Package!.PackageId, codec.Package.PackageVersion, codec.ContractId, codec.Input, codec.Kind));

        // Shorthand keys become record names, and a record array is written only when a package is targeted.
        var shorthand = ProjectFile.Load(Encoding.UTF8.GetBytes("NativeLibraries={t={codec={Kind=\"static\" Input=\"a.lib\"}}}"))!;
        Assert.Equal("codec", shorthand.NativeLibraries["t"]["codec"].Name);
        Assert.Contains("codec", Encoding.UTF8.GetString(TinyhandSerializer.SerializeToUtf8(shorthand)));
        Assert.Equal(file, ProjectFile.Load(TinyhandSerializer.SerializeToUtf8(file)), NativeSuppliesComparer.Instance);
        var clone = TinyhandSerializer.Clone(file)!.NativeLibraries[WindowsProfile.Target].Packaged[0];
        Assert.Equal(codec, clone);
        Assert.NotSame(codec.Package, clone.Package);
    }

    [Theory]
    [InlineData("codec", "1", "c.v1", null, false, true)]
    [InlineData("codec", "1", null, null, false, false)]
    [InlineData("codec", "1", "other", null, false, false)]
    [InlineData("codec", "1", "c.v1", "b", false, false)]
    [InlineData("codec", "1", "c.v1", "A", false, true)]
    [InlineData("codec", "1", null, null, true, false)]
    [InlineData("unrequired", "1", null, null, false, true)]
    [InlineData("codec", "2", null, null, false, true)]
    public void PackageTargetedSuppliesMatchTheResolvedModuleRequirement(string name, string version, string? contractId, string? hashDigit, bool combined, bool valid)
    {
        var c = Compilation.CreateForTest();
        var root = c.Project.ProjectFile;
        root.Dependencies.Add("Lib", new() { PackageId = "library", PackageVersion = "1", Project = "library.kimiproj" });
        root.NativeLibraries[WindowsProfile.Target] = new() { Packaged = [new() { Name = name, Package = new() { PackageId = "library", PackageVersion = version }, ContractId = contractId, Sha256 = hashDigit is null ? null : new string(hashDigit[0], 64), Input = "codec.lib" }] };
        var library = new ProjectFile { OutputKind = OutputKind.Library, PackageId = "library", PackageVersion = "1" };
        var sha256 = new string('a', 64);
        if (combined)
        {
            library.NativeLibraries[WindowsProfile.Target] = new() { ["codec"] = new() { Kind = "static", ContractId = "c.v1", Input = "own.lib" } };
        }
        else
        {
            library.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static", ContractId = "c.v1", Sha256 = sha256 } };
        }

        var rootNode = new DependencyNode("root", new("root.kimiproj", root, [], []), -1, string.Empty);
        var libraryNode = new DependencyNode("library@1", new("library.kimiproj", library, [], []), 0, "Lib");
        rootNode.Edges.Add("Lib", libraryNode.Key);
        Assert.Equal(valid, c.Prepare(WindowsProfile.Target, new([rootNode, libraryNode])));
        Assert.Equal(!valid, c.Kotonoha.DiagnosticCollection.GetArray().Any(x => x.Entry.Name == nameof(DiagnosticCode.InvalidDependencyConfiguration_Kd)));
    }

    [Theory]
    [InlineData("x.v1", "x.v1", true)]
    [InlineData(null, "x.v1", true)]
    [InlineData("x.v2", "x.v1", false)]
    [InlineData("x.v2", null, true)]
    public void SuppliesOfOneNameMergeOnlyWithAgreeingContracts(string? rootContract, string? otherContract, bool valid)
    {
        // The library does not require "extra", so only supply agreement can reject the pair.
        var c = Compilation.CreateForTest();
        var root = c.Project.ProjectFile;
        root.Dependencies.Add("Lib", new() { PackageId = "library", PackageVersion = "1", Project = "library.kimiproj" });
        root.NativeLibraries[WindowsProfile.Target] = new() { Packaged = [new() { Name = "extra", Package = new() { PackageId = "library", PackageVersion = "1" }, ContractId = rootContract, Input = "root.lib" }] };
        var library = new ProjectFile { OutputKind = OutputKind.Library, PackageId = "library", PackageVersion = "1" };
        var other = new ProjectFile { OutputKind = OutputKind.Library, PackageId = "other", PackageVersion = "1" };
        other.NativeLibraries[WindowsProfile.Target] = new() { Packaged = [new() { Name = "extra", Package = new() { PackageId = "library", PackageVersion = "1" }, ContractId = otherContract, Input = "other.lib" }] };
        var rootNode = new DependencyNode("root", new("root.kimiproj", root, [], []), -1, string.Empty);
        var libraryNode = new DependencyNode("library@1", new("library.kimiproj", library, [], []), 0, "Lib");
        var otherNode = new DependencyNode("other@1", new("other.kimiproj", other, [], []), 1, "Other");
        rootNode.Edges.Add("Lib", libraryNode.Key);
        libraryNode.Edges.Add("Other", otherNode.Key);
        Assert.Equal(valid, c.Prepare(WindowsProfile.Target, new([rootNode, libraryNode, otherNode])));
    }

    private sealed class NativeSuppliesComparer : IEqualityComparer<ProjectFile?>
    {
        public static readonly NativeSuppliesComparer Instance = new();

        public bool Equals(ProjectFile? x, ProjectFile? y)
            => x is not null && y is not null && x.NativeLibraries.Count == y.NativeLibraries.Count && x.NativeLibraries.All(pair =>
                y.NativeLibraries.TryGetValue(pair.Key, out var other) && pair.Value.Count == other.Count &&
                pair.Value.All(entry => other.TryGetValue(entry.Key, out var record) && record == entry.Value) &&
                pair.Value.Packaged.SequenceEqual(other.Packaged));

        public int GetHashCode(ProjectFile? obj) => 0;
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
    [InlineData("NativeLibraries={t={{Name=\"codec\" Kind=\"static\" Input=\"codec.lib\"}}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"example.codec\" PackageVersion=\"1.0.0\"} Name=\"codec\" ContractId=\"example.codec.v1\" Input=\"native/codec.lib\"} {Name=\"observer\" Kind=\"import\" Input=\"observer.lib\"}}}")]
    [InlineData("NativeLibraries={t={}}")]
    [InlineData("NativeLibraries={t={{Package={PackageId=\"p\" PackageVersion=\"1\"} Name=\"codec\" ContractId=\"a\" Input=\"a.lib\"} {Package={PackageId=\"p\" PackageVersion=\"2\"} Name=\"codec\" ContractId=\"b\" Input=\"b.lib\"} {Package={PackageId=\"p\" PackageVersion=\"1\"} Name=\"codec\" Input=\"c.lib\"}}}")]
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

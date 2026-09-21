// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class DependencyResolutionTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-graph-" + Guid.NewGuid().ToString("N"));

    public DependencyResolutionTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void DiamondAndReferenceAliasesShareOneIdentity()
    {
        this.Write("Leaf", "leaf");
        this.Write("Left", "left", Edge("Shared", "Leaf", "leaf"));
        this.Write("Right", "right", Edge("Other", "Leaf", "leaf"));
        var root = this.Write("Root", "root", Edge("Left", "Left", "left") + Edge("Right", "Right", "right") + Edge("Again", "Left", "left"));
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved, result.Product.Diagnostic);
        Assert.True(result.Test.IsResolved, result.Test.Diagnostic);
        Assert.Equal(4, result.Product.Nodes.Length);
        var rootNode = result.Product.Nodes[0];
        Assert.Equal(rootNode.Edges["Left"], rootNode.Edges["Again"]);
        Assert.False(rootNode.Edges.ContainsKey("Shared"));
        foreach (var node in result.Product.Nodes)
        {
            Assert.Same(node.Input, result.Test.Nodes.Single(x => x.Key == node.Key).Input);
        }
    }

    [Theory]
    [InlineData("Missing", "missing", "1", "InvalidProjectInput")]
    [InlineData("Leaf", "other", "1", "IdentityMismatch")]
    [InlineData("Leaf", "leaf", "V1", "IdentityMismatch")]
    public void RequestedIdentityMustMatchTheLoadedProject(string folder, string id, string version, string reason)
    {
        this.Write("Leaf", "leaf");
        var root = this.Write("Root", "root", Edge("Use", folder, id, version));
        var result = Resolve(root);
        Assert.Equal(reason, result.Product.ReasonCode);
        Assert.Equal("ProductUnresolved", result.Test.ReasonCode);
    }

    [Theory]
    [InlineData("OutputKind=\"Application\"", "NotLibrary")]
    [InlineData("Targets={\"x86_64-unknown-linux-gnu\"}", "TargetMismatch")]
    [InlineData("LangVersion=\"future\"", "LanguageMismatch")]
    [InlineData("LangVersion=\"0.0.1\"", "LanguageMismatch")]
    public void EveryDependencyUsesTheRequiredEnvironment(string settings, string reason)
    {
        this.Write("Leaf", "leaf", settings: settings);
        var root = this.Write("Root", "root", Edge("Use", "Leaf", "leaf"));
        Assert.Equal(reason, Resolve(root).Product.ReasonCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelfAndIndirectCyclesAreRejected(bool self)
    {
        this.Write("A", "a", self ? Edge("SelfReference", "A", "a") : Edge("Next", "B", "b"));
        this.Write("B", "b", Edge("Back", "A", "a"));
        var root = this.Write("Root", "root", Edge("Start", "A", "a"));
        var result = Resolve(root);
        Assert.Equal("Cycle", result.Product.ReasonCode);
        Assert.Contains("root -> Start", result.Product.Diagnostic);
        Assert.Contains(self ? "a@1 -> a@1" : "b@1 -> a@1 -> b@1", result.Product.Diagnostic);
    }

    [Fact]
    public void ReferenceBackToRootReportsItsPath()
    {
        var root = this.Write("Root", "root", Edge("Again", "Root", "root"));
        var result = Resolve(root);
        Assert.Equal("Cycle", result.Product.ReasonCode);
        Assert.Contains("root -> Again", result.Product.Diagnostic);
    }

    [Fact]
    public void TwoVersionsRemainDistinct()
    {
        this.Write("V1", "library");
        this.Write("V2", "library", version: "2");
        var root = this.Write("Root", "root", Edge("Old", "V1", "library") + Edge("New", "V2", "library", "2"));
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved, result.Product.Diagnostic);
        Assert.Equal(3, result.Product.Nodes.Length);
        Assert.NotEqual(result.Product.Nodes[0].Edges["Old"], result.Product.Nodes[0].Edges["New"]);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("setting")]
    [InlineData("edge")]
    public void SameReleaseConflictsIdentifyBothPaths(string change)
    {
        this.Write("A", "library", source: "first");
        this.Write("B", "library", dependencies: change == "edge" ? Edge("Extra", "Leaf", "leaf") : string.Empty, settings: change == "setting" ? "CompileTimeSettings={Feature={Bool=true}}" : string.Empty, source: change == "source" ? "second" : "first");
        var root = this.Write("Root", "root", Edge("First", "A", "library") + Edge("Second", "B", "library"));
        var result = Resolve(root);
        Assert.Equal("ConflictingInput", result.Product.ReasonCode);
        Assert.Contains("root -> First", result.Product.Diagnostic);
        Assert.Contains("root -> Second", result.Product.Diagnostic);
    }

    [Fact]
    public void EquivalentRelocatedInputsMergeAndStillCheckTheirChildren()
    {
        this.Write("Leaf1", "leaf", source: "same");
        this.Write("Leaf2", "leaf", source: "same");
        this.Write("A", "library", Edge("Child", "Leaf1", "leaf"), source: "same");
        this.Write("B", "library", Edge("Child", "Leaf2", "leaf"), settings: "Optimization=\"O0\" OutputPath=\"elsewhere.ll\"", source: "same");
        var root = this.Write("Root", "root", Edge("First", "A", "library") + Edge("Second", "B", "library"));
        Assert.True(Resolve(root).Product.IsResolved);
        File.WriteAllText(Path.Combine(this.directory, "Leaf2", "main.kimi"), "changed");
        Assert.Equal("ConflictingInput", Resolve(root).Product.ReasonCode);
    }

    [Fact]
    public void TestOnlyFailureDoesNotInvalidateProductResolution()
    {
        var root = this.Write("Root", "root", settings: "TestDependencies={Missing={PackageId=\"missing\" PackageVersion=\"1\" Project=\"missing.kimiproj\"}}");
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved);
        Assert.Equal("InvalidProjectInput", result.Test.ReasonCode);
    }

    [Fact]
    public void DependencyTestsDoNotPropagate()
    {
        this.Write("Leaf", "leaf", settings: "TestDependencies={Missing={PackageId=\"missing\" PackageVersion=\"1\" Project=\"missing.kimiproj\"}}");
        var root = this.Write("Root", "root", Edge("Leaf", "Leaf", "leaf"));
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved, result.Product.Diagnostic);
        Assert.True(result.Test.IsResolved, result.Test.Diagnostic);
    }

    [Fact]
    public void TestReferencesCannotReassignProductNames()
    {
        this.Write("A", "a");
        this.Write("B", "b");
        var root = this.Write("Root", "root", Edge("Use", "A", "a"), settings: "TestDependencies={" + Edge("Use", "B", "b") + "}");
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved);
        Assert.Equal("ReferenceReassignment", result.Test.ReasonCode);
    }

    [Fact]
    public void PackageInputsStayExplicitlyUnsupported()
    {
        var root = this.Write("Root", "root", "Use={PackageId=\"library\" PackageVersion=\"1\" Package=\"library.kimipkg\"}");
        Assert.Equal("UnsupportedPackage", Resolve(root).Product.ReasonCode);
    }

    [Fact]
    public void ProductSnapshotsExcludeTestsAndRetainTheReadBytes()
    {
        var root = this.Write("Root", "root", settings: "TestSources={\"test.kimi\"}", source: "original");
        File.WriteAllBytes(Path.Combine(this.directory, "Root", "test.kimi"), [0xff]);
        var result = Resolve(root);
        Assert.True(result.Product.IsResolved, result.Product.Diagnostic);
        var source = Assert.Single(result.Product.Nodes[0].Input.Sources);
        File.WriteAllText(Path.Combine(this.directory, "Root", "main.kimi"), "changed");
        Assert.Equal("original", System.Text.Encoding.UTF8.GetString(source.Bytes));
        Assert.Equal("main.kimi", source.LogicalPath);
    }

    [Fact]
    public void DeepGraphsUseAnIterativeTraversal()
    {
        const int Count = 128;
        for (var i = 0; i < Count; i++)
        {
            this.Write("N" + i, "n" + i, i + 1 < Count ? Edge("Next", "N" + (i + 1), "n" + (i + 1)) : string.Empty);
        }

        var result = Resolve(Path.Combine(this.directory, "N0", "N0.kimiproj"));
        Assert.True(result.Product.IsResolved, result.Product.Diagnostic);
        Assert.Equal(Count, result.Product.Nodes.Length);
    }

    [Fact]
    public void ProductOnlyResolutionDoesNotOpenTestDependencyInputs()
    {
        var root = this.Write("Root", "root", settings: "TestDependencies={Tests={PackageId=\"tests\" PackageVersion=\"1\" Project=\"../Tests/Tests.kimiproj\"}}");
        var tests = this.Write("Tests", "tests");
        using var exclusive = new FileStream(tests, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = DependencyResolver.Resolve(root, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken, includeTests: false);
        Assert.True(result.Product.IsResolved);
        Assert.Equal("NotRequested", result.Test.ReasonCode);
    }

    private static DependencyResolution Resolve(string path)
        => DependencyResolver.Resolve(path, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken);

    private static string Edge(string name, string folder, string id, string version = "1")
        => $"{name}={{PackageId=\"{id}\" PackageVersion=\"{version}\" Project=\"../{folder}/{folder}.kimiproj\"}} ";

    private string Write(string name, string id, string dependencies = "", string settings = "", string source = "", string version = "1")
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Library\" PackageId=\"{id}\" PackageVersion=\"{version}\" Targets={{\"{WindowsProfile.Target}\"}} Dependencies={{{dependencies}}} {settings}");
        File.WriteAllText(Path.Combine(folder, "main.kimi"), source);
        return path;
    }
}

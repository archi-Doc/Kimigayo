// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class DependencyLockTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-lock-" + Guid.NewGuid().ToString("N"));

    public DependencyLockTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void MissingLockIsImplicitOnlyForAnEmptyRequiredPartition()
    {
        var empty = this.Resolution();
        var path = Path.Combine(this.directory, "missing.kimi.lock.json");
        Assert.Null(DependencyLock.Validate(path, empty, false));
        Assert.NotNull(DependencyLock.Validate(path, this.Resolution(dependency: true), false));
    }

    [Fact]
    public void LockContainsIdentityAndStructureWithoutHostPathsOrSourceBytes()
    {
        var result = this.Resolution(dependency: true);
        var bytes = DependencyLock.Serialize(result);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(this.directory, text);
        Assert.DoesNotContain("SECRET_SOURCE_BYTES", text);
        Assert.Contains("library@1", text);
        Assert.Contains("\"root\"", text);
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        Assert.True(DependencyLock.Update(path, result, TestContext.Current.CancellationToken));
        Assert.Null(DependencyLock.Validate(path, result, true));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void UnchangedRestoreDoesNotRewriteTheLock()
    {
        var result = this.Resolution(dependency: true);
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        Assert.True(DependencyLock.Update(path, result, TestContext.Current.CancellationToken));
        var oldTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, oldTime);
        Assert.False(DependencyLock.Update(path, result, TestContext.Current.CancellationToken));
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + ".writing"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"product\":null}")]
    public void ExistingCorruptLocksNeverBecomeImplicitEmptyResolution(string text)
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        File.WriteAllText(path, text);
        Assert.NotNull(DependencyLock.Validate(path, this.Resolution(), false));
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void ADirectoryAtTheLockPathIsAnError()
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        Directory.CreateDirectory(path);
        Assert.NotNull(DependencyLock.Validate(path, this.Resolution(), false));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("reference")]
    [InlineData("empty")]
    public void ChangedRequiredStructureNeedsRestore(string change)
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        DependencyLock.Update(path, this.Resolution(dependency: true), TestContext.Current.CancellationToken);
        var changed = this.Resolution(dependency: change != "empty", version: change == "version" ? "2" : "1", reference: change == "reference" ? "Renamed" : "Use");
        Assert.NotNull(DependencyLock.Validate(path, changed, false));
    }

    [Fact]
    public void SourceEditsAndInputRelocationDoNotChangeLockStructure()
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        DependencyLock.Update(path, this.Resolution(dependency: true), TestContext.Current.CancellationToken);
        var moved = this.Resolution(dependency: true);
        moved.Product.Nodes[1].Input.Sources[0].Bytes[0] ^= 1;
        Assert.Null(DependencyLock.Validate(path, moved, false));
        Assert.False(DependencyLock.Update(path, moved, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TestOnlyFailureIsRecordedWithoutBlockingProductValidation()
    {
        var good = this.Resolution();
        var result = new DependencyResolution(good.Product, new([], "InvalidProjectInput", "host path must not enter lock"));
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        DependencyLock.Update(path, result, TestContext.Current.CancellationToken);
        Assert.Null(DependencyLock.Validate(path, good, false));
        Assert.NotNull(DependencyLock.Validate(path, good, true));
        Assert.DoesNotContain("host path", File.ReadAllText(path));
    }

    [Fact]
    public void FailedProductRestoreReplacesOldSuccessInBothPartitions()
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        var good = this.Resolution();
        DependencyLock.Update(path, good, TestContext.Current.CancellationToken);
        var failed = new DependencyResolution(new([], "Cycle", "cycle path"), new([], "ProductUnresolved"));
        DependencyLock.Update(path, failed, TestContext.Current.CancellationToken);
        Assert.NotNull(DependencyLock.Validate(path, good, false));
        Assert.Contains("ProductUnresolved", File.ReadAllText(path));
    }

    [Fact]
    public void ActiveWriterPreventsReplacement()
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        var good = this.Resolution();
        DependencyLock.Update(path, good, TestContext.Current.CancellationToken);
        var prior = File.ReadAllBytes(path);
        using (var guard = new FileStream(path + ".writing", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => DependencyLock.Update(path, this.Resolution(dependency: true), TestContext.Current.CancellationToken));
        }

        Assert.Equal(prior, File.ReadAllBytes(path));
    }

    [Fact]
    public void CancelledRestoreLeavesPriorLockUntouched()
    {
        var path = Path.Combine(this.directory, "Root.kimi.lock.json");
        DependencyLock.Update(path, this.Resolution(), TestContext.Current.CancellationToken);
        var prior = File.ReadAllBytes(path);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DependencyLock.Update(path, this.Resolution(dependency: true), cancellation.Token));
        Assert.Equal(prior, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(this.directory, "*.tmp"));
    }

    private DependencyResolution Resolution(bool dependency = false, string version = "1", string reference = "Use")
    {
        var root = new DependencyNode("root", new(Path.Combine(this.directory, "Root.kimiproj"), new(), [], []), -1, string.Empty);
        var nodes = new[] { root };
        if (dependency)
        {
            var input = new DependencyInput(Path.Combine(this.directory, Guid.NewGuid().ToString("N"), "Library.kimiproj"), new() { PackageId = "library", PackageVersion = version }, [], [new("main.kimi", Encoding.UTF8.GetBytes("SECRET_SOURCE_BYTES"))]);
            var child = new DependencyNode("library@" + version, input, 0, reference);
            root.Edges.Add(reference, child.Key);
            nodes = [root, child];
        }

        return new(new(nodes), new(nodes));
    }
}

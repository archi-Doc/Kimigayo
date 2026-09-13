// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class ToolchainResolverTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-toolchain-" + Guid.NewGuid().ToString("N"));

    public ToolchainResolverTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void ExplicitRootOverridesEnvironmentAndMissingSelectionsDoNotFallBack()
    {
        var executable = Path.Combine(this.directory, "compiler");
        Directory.CreateDirectory(Path.Combine(executable, "toolchain"));
        Assert.Equal(Path.Combine(this.directory, "chosen tools"), ToolchainResolver.ResolveRoot("chosen tools", "environment", executable, this.directory));
        Assert.Equal(Path.Combine(this.directory, "environment"), ToolchainResolver.ResolveRoot(null, "environment", executable, this.directory));
        Assert.Throws<InvalidDataException>(() => ToolchainResolver.ResolveRoot(string.Empty, "environment", executable, this.directory));
    }

    [Fact]
    public void SourceBuildFindsRepositoryFromExecutableAndAdjacentToolsTakePrecedence()
    {
        File.WriteAllText(Path.Combine(this.directory, "Kimigayo.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(this.directory, "Kimi"));
        File.WriteAllText(Path.Combine(this.directory, "Kimi", "Kimi.csproj"), string.Empty);
        Directory.CreateDirectory(Path.Combine(this.directory, "backend", "windows-x64"));
        File.WriteAllText(Path.Combine(this.directory, "backend", "windows-x64", "profile.json"), "{}");
        var executable = Path.Combine(this.directory, "Kimi", "bin", "Release", "net10.0");
        Directory.CreateDirectory(executable);
        var unrelated = Path.Combine(this.directory, "unrelated project");
        Assert.Equal(Path.Combine(this.directory, "toolchain"), ToolchainResolver.ResolveRoot(null, null, executable, unrelated));
        Directory.CreateDirectory(Path.Combine(executable, "toolchain"));
        Assert.Equal(Path.Combine(executable, "toolchain"), ToolchainResolver.ResolveRoot(null, null, executable, unrelated));
    }

    [Fact]
    public void DistributionDefaultsToExecutableDirectoryWithoutInstalledTools()
    {
        var executable = Path.Combine(this.directory, "distribution");
        Assert.Equal(Path.Combine(executable, "toolchain"), ToolchainResolver.ResolveRoot(null, null, executable, this.directory));
    }

    [Fact]
    public void BackendResolutionIsRelocatableAndLegacyInputsRemainManifestRelative()
    {
        using var automatic = JsonDocument.Parse("""{"resolution":"toolchain"}""");
        using var explicitInput = JsonDocument.Parse("""{"input":"custom.lib"}""");
        var root = Path.Combine(this.directory, "tools with spaces");
        var manifest = Path.Combine(this.directory, "project", "out");
        Assert.Equal(Path.Combine(root, "windows_x64", WindowsProfile.BackendFile), ToolchainResolver.ResolveBackend(automatic.RootElement, root, manifest));
        Assert.Equal(Path.Combine(manifest, "custom.lib"), ToolchainResolver.ResolveBackend(explicitInput.RootElement, root, manifest));
    }

    [Theory]
    [InlineData("""{"resolution":"toolchain","input":"override.lib"}""")]
    [InlineData("""{"resolution":"other"}""")]
    [InlineData("""{"input":"-override.lib"}""")]
    public void AmbiguousOrInvalidBackendLocationsAreRejected(string json)
    {
        using var entry = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => ToolchainResolver.ResolveBackend(entry.RootElement, this.directory, this.directory));
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;

namespace Kimi.Compiler;

/// <summary>Resolves compiler tools and profile libraries independently of project locations.</summary>
internal static class ToolchainResolver
{
    internal static string ResolveRoot(string? configured)
        => ResolveRoot(configured, Environment.GetEnvironmentVariable("KIMI_TOOLCHAIN_ROOT"), AppContext.BaseDirectory, Directory.GetCurrentDirectory());

    internal static string ResolveRoot(string? configured, string? environment, string executableDirectory, string workingDirectory)
    {
        var selected = configured ?? environment;
        if (selected is not null)
        {
            return ArtifactFiles.ResolvePath(selected, workingDirectory);
        }

        var adjacent = Path.Combine(executableDirectory, "toolchain");
        if (Directory.Exists(adjacent))
        {
            return Path.GetFullPath(adjacent);
        }

        // A source build can execute from Kimi/bin/... or a test host. Never search from the user's project or cwd.
        for (var directory = new DirectoryInfo(executableDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "Kimi", "Kimi.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "backend", "windows-x64", "profile.json")))
            {
                return Path.Combine(directory.FullName, "toolchain");
            }
        }

        return Path.GetFullPath(adjacent);
    }

    internal static string ResolveBackend(JsonElement entry, string root, string manifestDirectory)
    {
        if (entry.TryGetProperty("resolution", out var resolution))
        {
            if (resolution.GetString() != "toolchain" || entry.TryGetProperty("input", out _))
            {
                throw new InvalidDataException("Invalid backend toolchain resolution: an input path is not permitted.");
            }

            return Path.Combine(root, "windows_x64", WindowsProfile.BackendFile);
        }

        return ArtifactFiles.ResolvePath(entry.GetProperty("input").GetString()!, manifestDirectory);
    }
}

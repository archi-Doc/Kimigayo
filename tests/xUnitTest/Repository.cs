// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

/// <summary>Files of the repository checkout that contains the test assembly.</summary>
internal static class Repository
{
    /// <summary>Gets the repository root: the nearest ancestor of the test assembly that holds <c>Kimigayo.slnx</c>.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>Reads a repository text file with LF line endings.</summary>
    /// <param name="path">The path segments below the repository root.</param>
    /// <returns>The file text.</returns>
    internal static string ReadText(params string[] path)
        => File.ReadAllText(Path.Combine([Root, .. path])).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Kimigayo.slnx was not found above the test assembly.");
    }
}

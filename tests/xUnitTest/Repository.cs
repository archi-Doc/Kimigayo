// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

/// <summary>Files of the repository checkout that contains the test assembly.</summary>
internal static class Repository
{
    /// <summary>The skip reason of a row that needs <see cref="NativeBuilds"/>.</summary>
    internal const string NoNativeBuilds = "Native builds need a Windows host and the checkout's toolchain (tatara.dll beside the tests).";

    /// <summary>Gets the repository root: the nearest ancestor of the test assembly that holds <c>Kimigayo.slnx</c>.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>Gets a value indicating whether this process runs native builds: a Windows host whose test output holds the tatara.dll
    /// a build copies from the checkout's toolchain (README, Toolchain). Off Windows, and in a checkout without a toolchain such as a
    /// fresh clone, the rows that load Tatara or run the toolchain's tools skip; scripts/verify.ps1 runs the tests with -failSkips, so
    /// there such a skip fails.</summary>
    internal static bool NativeBuilds { get; } = OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "tatara.dll"));

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

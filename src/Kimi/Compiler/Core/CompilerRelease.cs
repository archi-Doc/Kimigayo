// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;

namespace Kimi.Compiler;

/// <summary>The compiler and backend package release, supplied by Directory.Build.props.</summary>
public static class CompilerRelease
{
    public static string Version { get; } = typeof(CompilerRelease).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(x => x.Key == "KimigayoVersion").Value!;
}

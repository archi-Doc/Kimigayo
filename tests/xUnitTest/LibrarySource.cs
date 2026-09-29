// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Locations inside the embedded Kimi library sources, for Abort messages reported where library code aborts.</summary>
internal static class LibrarySource
{
    /// <summary>Gets the source location of the only occurrence of <paramref name="anchor"/> in a Kimi library file.</summary>
    /// <param name="file">The library file name, such as <c>Core.kimi</c>.</param>
    /// <param name="anchor">Text that occurs exactly once in the file; the location is its first character.</param>
    /// <param name="context">Optional text that occurs once and precedes the anchor, when the anchor alone repeats.</param>
    /// <returns>The <c>compiler://</c> location with line and column.</returns>
    internal static string Location(string file, string anchor, string? context = null)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Kimi/Library", file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = 0;
        if (context is not null)
        {
            start = source.IndexOf(context, StringComparison.Ordinal);
            Assert.True(start >= 0);
            Assert.Equal(start, source.LastIndexOf(context, StringComparison.Ordinal));
        }

        var offset = source.IndexOf(anchor, start, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        if (context is null)
        {
            Assert.Equal(offset, source.LastIndexOf(anchor, StringComparison.Ordinal));
        }

        var line = source.AsSpan(0, offset).Count('\n') + 1;
        var column = offset - source.LastIndexOf('\n', offset);
        return FormattableString.Invariant($"compiler://Kimi/{Compilation.CurrentLanguageVersion}/{file}:{line}:{column}");
    }
}

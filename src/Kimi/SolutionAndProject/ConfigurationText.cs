// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Tinyhand.IO;

namespace Kimi;

/// <summary>
/// The parse of a user's configuration text, a project or solution file (IMPL 20.1, 20.3.1), shared by every reader of one
/// (<see cref="ProjectFile.Load"/>, <see cref="Solution.TryReadFile"/>). Tinyhand's text reader is one library call over the user's bytes
/// with no compiler code inside, so whatever it raises is the file's failure: it surfaces as a <see cref="TinyhandException"/>, as the
/// reader's own failures do, and each reader reports it as that file's load failure. The catch spans that one call only, so it hides no
/// compiler defect (PLAN G984).
/// </summary>
internal static class ConfigurationText
{
    /// <summary>Converts the text to Tinyhand's binary form.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="writer">The writer that receives the binary form.</param>
    /// <exception cref="TinyhandException">The text is not a valid configuration text.</exception>
    internal static void ToBinary(ReadOnlySpan<byte> utf8, ref TinyhandWriter writer)
    {
        try
        {
            TinyhandTreeConverter.FromUtf8ToBinary(utf8, ref writer, true);
        }
        catch (Exception ex) when (ex is not TinyhandException)
        {
            // Tinyhand's reader raises InvalidOperationException for a nesting deeper than it reads, for example.
            throw new TinyhandException(ex.Message);
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1401 // Fields should be private

namespace Kimi.Compiler;

/// <summary>
/// Controls how Koto nodes are written as source text.
/// </summary>
[Flags]
public enum KotoWriteOptions : byte
{
    /// <summary>
    /// Uses the default output format and appends nothing.
    /// </summary>
    None = 0,

    /// <summary>
    /// Appends a single space after the output.
    /// </summary>
    AppendSpace = 1 << 0,

    /// <summary>
    /// Appends a line feed after the output.
    /// </summary>
    AppendLineFeed = 1 << 1,

    /// <summary>
    /// Writes names in fully qualified form.
    /// </summary>
    FullyQualified = 1 << 2,
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The document and its parts are one unit.

using System.Security.Cryptography;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>The JSON document that <c>kimi check --Format json</c> writes (SPEC 23.3.6.8), version <c>kimi.check/2</c>: one check unit's
/// outcome, acceptance, source table with the hashes of the inputs that were read, and diagnostic records with their repair candidates.</summary>
/// <param name="Schema">The schema name and version, <c>kimi.check/2</c>.</param>
/// <param name="Compiler">The compiler build identity.</param>
/// <param name="Unit">The check unit.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Accepted">Whether the result is accepted.</param>
/// <param name="TestPresence">The test presence.</param>
/// <param name="Sources">The source table, with the SHA-256 of every recorded input that was read for this output.</param>
/// <param name="Diagnostics">The records in result order.</param>
public sealed record CheckDocument(string Schema, string Compiler, CheckUnitDescription Unit, CheckOutcome Outcome, bool Accepted, TestPresence TestPresence, CheckSourceEntry[] Sources, CheckDiagnostic[] Diagnostics)
{
    /// <summary>The schema name and version of the document.</summary>
    public const string SchemaName = "kimi.check/2";

    public bool Equals(CheckDocument? other)
        => other is not null && this.Schema == other.Schema && this.Compiler == other.Compiler && this.Unit == other.Unit && this.Outcome == other.Outcome &&
            this.Accepted == other.Accepted && this.TestPresence == other.TestPresence && this.Sources.AsSpan().SequenceEqual(other.Sources) && this.Diagnostics.AsSpan().SequenceEqual(other.Diagnostics);

    public override int GetHashCode()
        => HashCode.Combine(this.Schema, this.Compiler, this.Unit, this.Outcome, this.Accepted, this.Sources.Length, this.Diagnostics.Length);

    /// <summary>Forms the document of one check output.</summary>
    /// <param name="output">The check output.</param>
    /// <param name="project">The project file, or the implicit source.</param>
    /// <param name="target">The target.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="debug">The unit's <c>Debug</c> setting.</param>
    /// <param name="hashes">The hashes of the inputs read for this output, by full path.</param>
    /// <returns>The document.</returns>
    internal static CheckDocument Create(CheckOutput output, string project, string target, CheckMode mode, bool debug, IReadOnlyDictionary<string, string> hashes)
    {
        var sources = new CheckSourceEntry[output.Sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            var source = output.Sources[i];
            sources[i] = new(source.Path, source.IsInput, source.IsInput && hashes.TryGetValue(source.Path, out var hash) ? hash : null);
        }

        return new(SchemaName, Compilation.CompilerVersion, new(project, target, mode, debug), output.Outcome, output.Accepted, output.Presence, sources, output.Diagnostics);
    }
}

/// <summary>The check unit of a document (SPEC 23.3.1).</summary>
/// <param name="Project">The project file, or the implicit source.</param>
/// <param name="Target">The target.</param>
/// <param name="Mode">The mode.</param>
/// <param name="Debug">The unit's <c>Debug</c> setting.</param>
public sealed record CheckUnitDescription(string Project, string Target, CheckMode Mode, bool Debug);

/// <summary>One entry of a document's source table (SPEC 23.3.6.3).</summary>
/// <param name="Path">The display path.</param>
/// <param name="IsInput">Whether the source is an input the check read.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 of the input's bytes as read for this output, so an edit is applied to the same bytes; <see langword="null"/> when the input was not read.</param>
public sealed record CheckSourceEntry(string Path, bool IsInput, string? Sha256);

/// <summary>An input source that reads through another and records the SHA-256 of every file it reads, for the source table of a document.</summary>
/// <param name="inner">The source that supplies the bytes.</param>
internal sealed class HashingInputSource(CheckInputSource inner) : CheckInputSource
{
    /// <summary>Gets the hashes by full path, as lowercase hexadecimal.</summary>
    public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);

    public override byte[] ReadAllBytes(string path)
    {
        var bytes = inner.ReadAllBytes(path);
        this.Hashes[path] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return bytes;
    }

    public override SourceContent ReadSource(string path)
        => SourceContent.FromBytes(this.ReadAllBytes(path));

    public override string[] GetFiles(string directory, string pattern)
        => inner.GetFiles(directory, pattern);
}

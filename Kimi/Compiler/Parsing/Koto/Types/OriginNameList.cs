// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Preserves Origin declaration locations and optional outlives bounds without allocating storage on Origin-free nodes.</summary>
internal sealed class OriginNameList : List<string>
{
    // Allocated only when a declaration writes `name : target` (SPEC 15.3); indexed like the names.
    private List<(string? Name, SourceSpan Span)>? bounds;

    internal List<SourceSpan> Spans { get; } = new();

    /// <summary>Gets a declared bound target, or null when the parameter has none.</summary>
    /// <param name="origins">Declared Origin parameters.</param>
    /// <param name="index">The parameter index.</param>
    /// <param name="span">The bound target span.</param>
    /// <returns>The bound target name.</returns>
    internal static string? GetBound(IReadOnlyList<string> origins, int index, out SourceSpan span)
    {
        if (origins is OriginNameList { bounds: { } bounds } && index < bounds.Count)
        {
            span = bounds[index].Span;
            return bounds[index].Name;
        }

        span = default;
        return null;
    }

    /// <summary>Compares declared names and bounds, as fragments must repeat both (SPEC 6.1.2, 15.3).</summary>
    /// <param name="a">The first parameter list.</param>
    /// <param name="b">The second parameter list.</param>
    /// <returns>Whether both lists declare the same binders and bounds.</returns>
    internal static bool SameParameters(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i] || GetBound(a, i, out _) != GetBound(b, i, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes <c> origin a, b : a</c>, or nothing for an empty list.</summary>
    /// <param name="origins">Declared Origin parameters.</param>
    /// <param name="builder">The destination builder.</param>
    internal static void WriteTo(IReadOnlyList<string> origins, ref IndentedStringBuilder builder)
    {
        if (origins.Count == 0)
        {
            return;
        }

        builder.Append(" origin ");
        for (var i = 0; i < origins.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            builder.Append(origins[i]);
            if (GetBound(origins, i, out _) is { } bound)
            {
                builder.Append(" : ");
                builder.Append(bound);
            }
        }
    }

    internal void Add(string name, SourceSpan span)
    {
        this.Add(name);
        this.Spans.Add(span);
        this.bounds?.Add(default);
    }

    /// <summary>Records the bound of the most recently added Origin parameter.</summary>
    /// <param name="target">The outlived Origin name or <c>static</c>.</param>
    /// <param name="span">The target's source span.</param>
    internal void SetLastBound(string target, SourceSpan span)
    {
        if (this.bounds is null)
        {
            this.bounds = new(this.Count);
            for (var i = 0; i < this.Count; i++)
            {
                this.bounds.Add(default);
            }
        }

        this.bounds[^1] = (target, span);
    }

    /// <summary>Appends parsed parameters into this exposed list, keeping spans and bounds.</summary>
    /// <param name="source">The parsed parameters.</param>
    internal void AppendFrom(IReadOnlyList<string> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            this.Add(source[i], source is OriginNameList located && i < located.Spans.Count ? located.Spans[i] : default);
            if (GetBound(source, i, out var span) is { } bound)
            {
                this.SetLastBound(bound, span);
            }
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Preserves Origin declaration locations without allocating storage on Origin-free nodes.</summary>
internal sealed class OriginNameList : List<string>
{
    internal List<SourceSpan> Spans { get; } = new();

    /// <summary>Compares names and order, which fragments must repeat (SPEC 6.1.2, 15.3).</summary>
    /// <param name="a">The first parameter list.</param>
    /// <param name="b">The second parameter list.</param>
    /// <returns>Whether both lists declare the same binders.</returns>
    internal static bool SameParameters(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes <c> {a, b}</c>, retaining explicitly empty headers.</summary>
    /// <param name="origins">Declared Origin parameters.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="explicitHeader">Whether an empty closed header must be written.</param>
    internal static void WriteTo(IReadOnlyList<string> origins, ref IndentedStringBuilder builder, bool explicitHeader = false)
    {
        if (origins.Count == 0 && !explicitHeader)
        {
            return;
        }

        builder.Append(" {");
        for (var i = 0; i < origins.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            builder.Append(origins[i]);
        }

        builder.Append('}');
    }

    internal void Add(string name, SourceSpan span)
    {
        this.Add(name);
        this.Spans.Add(span);
    }

    /// <summary>Appends parsed parameters into this exposed list, keeping spans.</summary>
    /// <param name="source">The parsed parameters.</param>
    internal void AppendFrom(IReadOnlyList<string> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            this.Add(source[i], source is OriginNameList located && i < located.Spans.Count ? located.Spans[i] : default);
        }
    }
}

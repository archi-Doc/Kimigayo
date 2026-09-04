// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents an array literal expression.</summary>
[TinyhandObject]
public sealed partial class ArrayLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.ArrayLiteral;

    /// <summary>Gets the array elements in source order.</summary>
    [Key(1)]
    public List<Koto> Elements { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="ArrayLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete literal span.</param>
    /// <param name="elements">The array elements.</param>
    public ArrayLiteralKoto(ref TokenReader reader, SourceSpan range, List<Koto> elements)
        : base(ref reader, range)
    {
        this.Elements = elements;
        this.Adopt(elements);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.OpenBracketChar);
        for (var i = 0; i < this.Elements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.Elements[i].WriteTo(ref builder);
        }

        builder.Append(Constants.CloseBracketChar);
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.Elements;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.Elements, oldKoto, newKoto);
}

/// <summary>Represents one key-value pair in a dictionary literal.</summary>
[TinyhandObject]
public sealed partial class DictionaryLiteralEntry
{
    /// <summary>Gets the key expression.</summary>
    [Key(0)]
    public Koto Key { get; internal set; }

    /// <summary>Gets the value expression.</summary>
    [Key(1)]
    public Koto Value { get; internal set; }

    /// <summary>Initializes a new instance of the <see cref="DictionaryLiteralEntry"/> class.</summary>
    /// <param name="key">The key expression.</param>
    /// <param name="value">The value expression.</param>
    public DictionaryLiteralEntry(Koto key, Koto value)
    {
        this.Key = key;
        this.Value = value;
    }
}

/// <summary>Represents a dictionary literal expression.</summary>
[TinyhandObject]
public sealed partial class DictionaryLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.DictionaryLiteral;

    /// <summary>Gets the dictionary entries in source order.</summary>
    [Key(1)]
    public List<DictionaryLiteralEntry> Entries { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="DictionaryLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete literal span.</param>
    /// <param name="entries">The dictionary entries.</param>
    public DictionaryLiteralKoto(ref TokenReader reader, SourceSpan range, List<DictionaryLiteralEntry> entries)
        : base(ref reader, range)
    {
        this.Entries = entries;
        foreach (var entry in entries)
        {
            entry.Key.Parent = this;
            entry.Value.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.OpenBracketChar);
        if (this.Entries.Count == 0)
        {
            builder.Append(Constants.ColonChar);
        }
        else
        {
            for (var i = 0; i < this.Entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                var entry = this.Entries[i];
                entry.Key.WriteTo(ref builder);
                builder.Append(": ");
                entry.Value.WriteTo(ref builder);
            }
        }

        builder.Append(Constants.CloseBracketChar);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var entry in this.Entries)
        {
            yield return entry.Key;
            yield return entry.Value;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        foreach (var entry in this.Entries)
        {
            if (entry.Key == oldKoto)
            {
                entry.Key = newKoto;
                return true;
            }

            if (entry.Value == oldKoto)
            {
                entry.Value = newKoto;
                return true;
            }
        }

        return false;
    }
}

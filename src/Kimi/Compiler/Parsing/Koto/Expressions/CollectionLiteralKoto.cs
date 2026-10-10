// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents an array literal expression.</summary>
public sealed class ArrayLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.ArrayLiteral;

    /// <summary>Gets the array elements in source order.</summary>
    public List<Koto> Elements { get; private set; }

    /// <summary>Gets the compile-time length for fill construction, whose sole element is evaluated once.</summary>
    public Koto? FillLength { get; private set; }

    internal BoundLength? FillCount { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ArrayLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete literal span.</param>
    /// <param name="elements">The array elements.</param>
    /// <param name="fillLength">The optional fill length; elements then contains exactly one value.</param>
    public ArrayLiteralKoto(ref TokenReader reader, SourceSpan range, List<Koto> elements, Koto? fillLength = null)
        : base(ref reader, range)
    {
        this.Elements = elements;
        this.FillLength = fillLength;
        this.Adopt(fillLength);
        this.Adopt(elements);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.OpenBracketChar);
        if (this.FillLength is { } length)
        {
            length.WriteTo(ref builder);
            builder.Append(" of ");
        }

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

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        this.FillLength = slots.Slot(this.FillLength);
        slots.List(this.Elements);
    }
}

/// <summary>Represents one key-value pair in a dictionary literal.</summary>
[TinyhandObject]
public sealed partial class DictionaryLiteralEntry
{
    /// <summary>Gets the key expression.</summary>
    [IgnoreMember]
    public Koto Key { get; internal set; }

    /// <summary>Gets the value expression.</summary>
    [IgnoreMember]
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
public sealed class DictionaryLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.DictionaryLiteral;

    /// <summary>Gets the dictionary entries in source order.</summary>
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

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        foreach (var entry in this.Entries)
        {
            entry.Key = slots.Slot(entry.Key);
            entry.Value = slots.Slot(entry.Value);
        }
    }
}

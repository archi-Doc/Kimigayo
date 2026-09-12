// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

/// <summary>Selects the readable symbol prefix of a pooled constant; sharing ignores the kind.</summary>
internal enum LlvmConstantKind : byte
{
    Text,
    Location,
}

/// <summary>
/// Private module constants that share identical UTF-8 byte sequences (SPEC 21.5.6).
/// Entries, names and encodings are retained across preparations so warm modules reuse them.
/// </summary>
internal sealed class LlvmConstantPool
{
    private static readonly string[] Prefixes = ["__kimi_text", "__kimi_location"];

    private readonly List<Entry> entries = new();
    private readonly StringBuilder scratch = new();
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    private readonly List<string>[] names = [new(), new()];
    private readonly int[] kindCounts = new int[2];
    private int count;

    internal int Count => this.count;

    internal Entry this[int index]
        => (uint)index < (uint)this.count ? this.entries[index] : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Clear()
    {
        this.count = 0;
        this.indices.Clear();
        this.kindCounts.AsSpan().Clear();
    }

    /// <summary>Returns the constant holding the UTF-8 encoding of a nonempty string.</summary>
    /// <param name="value">The string; the empty string has no backing constant.</param>
    /// <param name="kind">The symbol prefix used when the bytes are new to this module.</param>
    /// <returns>The constant index.</returns>
    internal int Intern(string value, LlvmConstantKind kind)
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("An empty string is Static/null/length zero and has no backing constant.", nameof(value));
        }

        if (this.indices.TryGetValue(value, out var index))
        {
            return index;
        }

        index = this.count++;
        if (index == this.entries.Count)
        {
            this.entries.Add(new());
        }

        this.entries[index].Set(value, this.GetName(kind, this.kindCounts[(int)kind]++), this.scratch);
        this.indices.Add(value, index);
        return index;
    }

    private string GetName(LlvmConstantKind kind, int ordinal)
    {
        var list = this.names[(int)kind];
        while (list.Count <= ordinal)
        {
            list.Add(list.Count == 0 ? Prefixes[(int)kind] : Prefixes[(int)kind] + "." + list.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return list[ordinal];
    }

    /// <summary>One named constant and its retained escaped definition.</summary>
    internal sealed class Entry
    {
        internal string Name { get; private set; } = string.Empty;

        internal string Value { get; private set; } = string.Empty;

        internal int ByteLength { get; private set; }

        /// <summary>Gets the complete <c>@name = private unnamed_addr constant ...</c> line.</summary>
        internal string Definition { get; private set; } = string.Empty;

        internal void Set(string value, string name, StringBuilder text)
        {
            if (ReferenceEquals(this.Name, name) && this.Value == value)
            {
                return;
            }

            this.Name = name;
            this.Value = value;
            this.ByteLength = Encoding.UTF8.GetByteCount(value);
            text.Clear();
            text.Append('@').Append(name).Append(" = private unnamed_addr constant [").Append(this.ByteLength).Append(" x i8] c\"");
            Span<byte> bytes = stackalloc byte[4];
            const string Hex = "0123456789ABCDEF";
            foreach (var rune in value.EnumerateRunes())
            {
                // Escape every byte: raw source text never enters IR (SPEC 21.5.2); NUL is data.
                var length = rune.EncodeToUtf8(bytes);
                for (var i = 0; i < length; i++)
                {
                    text.Append('\\').Append(Hex[bytes[i] >> 4]).Append(Hex[bytes[i] & 15]);
                }
            }

            this.Definition = text.Append("\", align 1\n").ToString();
        }
    }
}

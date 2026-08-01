// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1202 // Elements should be ordered by access

[StructLayout(LayoutKind.Explicit)]
public readonly struct LimitedValue : IEquatable<LimitedValue>
{
    private static readonly object I64Tag = new();
    private static readonly object DoubleTag = new();

    // null      : Bool
    // I64Tag    : I64
    // DoubleTag : Double
    // string    : Text
    [FieldOffset(0)]
    private readonly object? tagOrText;

    [FieldOffset(8)]
    public readonly bool Bool;

    [FieldOffset(8)]
    public readonly long I64;

    [FieldOffset(8)]
    public readonly double Double;

    public string Text => (this.tagOrText as string) ?? string.Empty;

    public LimitedValueKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var tagOrText = this.tagOrText;

            if (tagOrText is null)
            {
                return LimitedValueKind.Bool;
            }
            else if (ReferenceEquals(tagOrText, I64Tag))
            {
                return LimitedValueKind.I64;
            }
            else if (ReferenceEquals(tagOrText, DoubleTag))
            {
                return LimitedValueKind.Double;
            }

            return LimitedValueKind.Text;
        }
    }

    public LimitedValue(bool value)
    {
        this.tagOrText = null;
        this.Bool = value;
    }

    public LimitedValue(long value)
    {
        this.tagOrText = I64Tag;
        this.I64 = value;
    }

    public LimitedValue(double value)
    {
        this.tagOrText = DoubleTag;
        this.Double = value;
    }

    public LimitedValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        this.tagOrText = value;
    }

    public bool Equals(LimitedValue other)
    {
        var tagOrText = this.tagOrText;

        if (tagOrText is null)
        {
            return other.tagOrText is null && this.Bool == other.Bool;
        }
        else if (ReferenceEquals(tagOrText, I64Tag))
        {
            return ReferenceEquals(other.tagOrText, I64Tag) &&
                this.I64 == other.I64;
        }
        else if (ReferenceEquals(tagOrText, DoubleTag))
        {
            return ReferenceEquals(other.tagOrText, DoubleTag) &&
                this.Double.Equals(other.Double);
        }

        return other.tagOrText is string otherText &&
            string.Equals((string)tagOrText, otherText, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
        => obj is LimitedValue other && this.Equals(other);

    public override int GetHashCode()
    {
        var tagOrText = this.tagOrText;

        if (tagOrText is null)
        {
            return HashCode.Combine(LimitedValueKind.Bool, this.Bool);
        }
        else if (ReferenceEquals(tagOrText, I64Tag))
        {
            return HashCode.Combine(LimitedValueKind.I64, this.I64);
        }
        else if (ReferenceEquals(tagOrText, DoubleTag))
        {
            return HashCode.Combine(LimitedValueKind.Double, this.Double);
        }

        return HashCode.Combine(LimitedValueKind.Text, StringComparer.Ordinal.GetHashCode((string)tagOrText));
    }

    public static bool operator ==(LimitedValue left, LimitedValue right)
        => left.Equals(right);

    public static bool operator !=(LimitedValue left, LimitedValue right)
        => !left.Equals(right);

    public override string ToString() => this.Kind switch
    {
        LimitedValueKind.Bool => this.Bool.ToString(),
        LimitedValueKind.I64 => this.I64.ToString(),
        LimitedValueKind.Double => this.Double.ToString(),
        LimitedValueKind.Text => this.Text,
        _ => string.Empty,
    };
}

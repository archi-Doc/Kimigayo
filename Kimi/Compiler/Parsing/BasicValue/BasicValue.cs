// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1202 // Elements should be ordered by access

[StructLayout(LayoutKind.Explicit)]
public readonly struct BasicValue : IEquatable<BasicValue>
{
    private static readonly object BoolTag = new();
    private static readonly object I64Tag = new();
    private static readonly object F64Tag = new();

    // null    : Invalid
    // BoolTag : Bool
    // I64Tag  : I64
    // F64Tag  : F64
    // string  : String
    [FieldOffset(0)]
    private readonly object? tagOrString;

    [FieldOffset(8)]
    public readonly bool Bool;

    [FieldOffset(8)]
    public readonly long I64;

    [FieldOffset(8)]
    public readonly double F64;

    public string String => (this.tagOrString as string) ?? string.Empty;

    public BasicValueKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var tagOrString = this.tagOrString;
            if (tagOrString is null)
            {
                return BasicValueKind.Invalid;
            }

            if (ReferenceEquals(tagOrString, BoolTag))
            {
                return BasicValueKind.Bool;
            }

            if (ReferenceEquals(tagOrString, I64Tag))
            {
                return BasicValueKind.I64;
            }

            if (ReferenceEquals(tagOrString, F64Tag))
            {
                return BasicValueKind.F64;
            }

            return BasicValueKind.String;
        }
    }

    public BasicValue(bool value)
    {
        this = default;
        this.tagOrString = BoolTag;
        this.Bool = value;
    }

    public BasicValue(long value)
    {
        this = default;
        this.tagOrString = I64Tag;
        this.I64 = value;
    }

    public BasicValue(double value)
    {
        this = default;
        this.tagOrString = F64Tag;
        this.F64 = value;
    }

    public BasicValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        this = default;
        this.tagOrString = value;
    }

    public bool Equals(BasicValue other)
    {
        var tagOrString = this.tagOrString;
        if (tagOrString is null)
        {
            return other.tagOrString is null;
        }

        if (ReferenceEquals(tagOrString, BoolTag))
        {
            return ReferenceEquals(other.tagOrString, BoolTag) && this.Bool == other.Bool;
        }

        if (ReferenceEquals(tagOrString, I64Tag))
        {
            return ReferenceEquals(other.tagOrString, I64Tag) && this.I64 == other.I64;
        }

        if (ReferenceEquals(tagOrString, F64Tag))
        {
            return ReferenceEquals(other.tagOrString, F64Tag) && this.F64.Equals(other.F64);
        }

        return other.tagOrString is string otherString &&
            string.Equals((string)tagOrString, otherString, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
        => obj is BasicValue other && this.Equals(other);

    public override int GetHashCode()
    {
        var tagOrString = this.tagOrString;
        if (tagOrString is null)
        {
            return BasicValueKind.Invalid.GetHashCode();
        }

        if (ReferenceEquals(tagOrString, BoolTag))
        {
            return HashCode.Combine(BasicValueKind.Bool, this.Bool);
        }

        if (ReferenceEquals(tagOrString, I64Tag))
        {
            return HashCode.Combine(BasicValueKind.I64, this.I64);
        }

        if (ReferenceEquals(tagOrString, F64Tag))
        {
            return HashCode.Combine(BasicValueKind.F64, this.F64);
        }

        return HashCode.Combine(BasicValueKind.String, StringComparer.Ordinal.GetHashCode((string)tagOrString));
    }

    public static bool operator ==(BasicValue left, BasicValue right)
        => left.Equals(right);

    public static bool operator !=(BasicValue left, BasicValue right)
        => !left.Equals(right);

    public override string ToString() => this.Kind switch
    {
        BasicValueKind.Bool => this.Bool.ToString(),
        BasicValueKind.I64 => this.I64.ToString(CultureInfo.InvariantCulture),
        BasicValueKind.F64 => this.F64.ToString(CultureInfo.InvariantCulture),
        BasicValueKind.String => this.String,
        _ => string.Empty,
    };
}

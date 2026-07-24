// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Parsing;

[StructLayout(LayoutKind.Explicit)]
public readonly record struct LimitedValue : IEquatable<LimitedValue>
{
    [FieldOffset(0)]
    public readonly LimitedValueKind Kind;

    // Primitive value union
    [FieldOffset(8)]
    public readonly bool Bool;

    [FieldOffset(8)]
    public readonly long I64;

    [FieldOffset(8)]
    public readonly double Double;

    // Managed references cannot overlap non-reference fields.
    [FieldOffset(16)]
    public readonly string? Text;

    public LimitedValue(bool value)
    {
        this = default;
        this.Kind = LimitedValueKind.Bool;
        this.Bool = value;
    }

    public LimitedValue(long value)
    {
        this = default;
        this.Kind = LimitedValueKind.I64;
        this.I64 = value;
    }

    public LimitedValue(double value)
    {
        this = default;
        this.Kind = LimitedValueKind.Double;
        this.Double = value;
    }

    public LimitedValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        this = default;
        this.Kind = LimitedValueKind.Text;
        this.Text = text;
    }

    public bool Equals(LimitedValue other)
    {
        if (this.Kind != other.Kind)
        {
            return false;
        }

        return this.Kind switch
        {
            LimitedValueKind.Bool =>
                this.Bool == other.Bool,

            LimitedValueKind.I64 =>
                this.I64 == other.I64,

            LimitedValueKind.Double =>
                this.Double.Equals(other.Double),

            LimitedValueKind.Text =>
                string.Equals(this.Text, other.Text, StringComparison.Ordinal),

            _ => true,
        };
    }

    public override int GetHashCode()
    {
        return this.Kind switch
        {
            LimitedValueKind.Bool =>
                HashCode.Combine(this.Kind, this.Bool),

            LimitedValueKind.I64 =>
                HashCode.Combine(this.Kind, this.I64),

            LimitedValueKind.Double =>
                HashCode.Combine(this.Kind, this.Double),

            LimitedValueKind.Text =>
                HashCode.Combine(
                    this.Kind,
                    this.Text is null ? 0 : StringComparer.Ordinal.GetHashCode(this.Text)),

            _ => this.Kind.GetHashCode(),
        };
    }
}

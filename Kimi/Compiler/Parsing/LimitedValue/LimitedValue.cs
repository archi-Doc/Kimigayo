// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1202 // Elements should be ordered by access

[StructLayout(LayoutKind.Explicit)]
public readonly struct LimitedValue : IEquatable<LimitedValue>
{
    private const string I64Text = "i";
    private const string DoubleText = "d";

    [FieldOffset(0)]
    public readonly string? Text;

    [FieldOffset(8)]
    public readonly bool Bool; // Text = null

    [FieldOffset(8)]
    public readonly long I64; // Text = I64Text

    [FieldOffset(8)]
    public readonly double Double; // Text = DoubleText

    public LimitedValueKind Kind
    {
        get
        {
            if (this.Text is null)
            {// Bool
                return LimitedValueKind.Bool;
            }
            else if (object.ReferenceEquals(this.Text, I64Text))
            {// I64
                return LimitedValueKind.I64;
            }
            else if (object.ReferenceEquals(this.Text, DoubleText))
            {// Double
                return LimitedValueKind.Double;
            }

            return LimitedValueKind.Text;
        }
    }

    public LimitedValue(bool value)
    {
        this.Text = null;
        this.Bool = value;
    }

    public LimitedValue(long value)
    {
        this.Text = I64Text;
        this.I64 = value;
    }

    public LimitedValue(double value)
    {
        this.Text = DoubleText;
        this.Double = value;
    }

    public LimitedValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        this.Text = text;
    }

    public bool Equals(LimitedValue other)
    {
        if (this.Text is null)
        {// Bool
            if (other.Text is null)
            {
                return this.Bool == other.Bool;
            }
        }
        else if (object.ReferenceEquals(this.Text, I64Text))
        {// I64
            if (object.ReferenceEquals(other.Text, I64Text))
            {
                return this.I64 == other.I64;
            }
        }
        else if (object.ReferenceEquals(this.Text, DoubleText))
        {// Double
            if (object.ReferenceEquals(other.Text, DoubleText))
            {
                return this.Double.Equals(other.Double);
            }
        }
        else
        {// Text
            return other.Kind == LimitedValueKind.Text &&
                string.Equals(this.Text, other.Text, StringComparison.Ordinal);
        }

        return false;
    }

    public override int GetHashCode()
    {
        if (this.Text is null)
        {// Bool
            return HashCode.Combine(this.Kind, this.Bool);
        }
        else if (object.ReferenceEquals(this.Text, I64Text))
        {// I64
            return HashCode.Combine(this.Kind, this.I64);
        }
        else if (object.ReferenceEquals(this.Text, DoubleText))
        {// Double
            return HashCode.Combine(this.Kind, this.Double);
        }
        else
        {// Text
            return HashCode.Combine(this.Kind, StringComparer.Ordinal.GetHashCode(this.Text));
        }
    }
}

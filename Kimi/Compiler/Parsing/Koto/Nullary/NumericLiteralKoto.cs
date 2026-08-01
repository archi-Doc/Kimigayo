// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Tinyhand.Tree;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class NumericLiteralKoto : Koto
{
    [Key(1)]
    public string Literal { get; private set; }

    [Key(2)]
    public NumericLiteralKind Kind { get; private set; }

    [Key(3)]
    private UInt128 uv;

    public NumericLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Literal = token.Text.ToString();
    }

    public bool TryGetI64(out long value)
    {
        this.PrepareNumericLiteral();

        if (this.Kind is >= NumericLiteralKind.Integer and <= NumericLiteralKind.USize &&
            this.uv <= long.MaxValue)
        {
            value = (long)this.uv;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetF32(out float value)
    {
        this.PrepareNumericLiteral();

        if (this.Kind == NumericLiteralKind.F32)
        {
            value = BitConverter.UInt32BitsToSingle((uint)this.uv);
            return float.IsFinite(value);
        }
        else if (this.Kind == NumericLiteralKind.Float ||
            this.Kind == NumericLiteralKind.F64)
        {
            var doubleValue = BitConverter.UInt64BitsToDouble((ulong)this.uv);
            value = (float)doubleValue;
            return double.IsFinite(doubleValue) && float.IsFinite(value);
        }

        value = default;
        return false;
    }

    public bool TryGetF64(out double value)
    {
        this.PrepareNumericLiteral();

        if (this.Kind == NumericLiteralKind.F32)
        {
            value = BitConverter.UInt32BitsToSingle((uint)this.uv);
            return double.IsFinite(value);
        }
        else if (this.Kind == NumericLiteralKind.Float ||
            this.Kind == NumericLiteralKind.F64)
        {
            value = BitConverter.UInt64BitsToDouble((ulong)this.uv);
            return double.IsFinite(value);
        }

        value = default;
        return false;
    }

    public bool TryGetLimitedValue(out LimitedValue limitedValue)
    {
        this.PrepareNumericLiteral();

        if (this.Kind is >= NumericLiteralKind.Integer and <= NumericLiteralKind.USize)
        {// Integer
            if (this.uv <= long.MaxValue)
            {
                limitedValue = new((long)this.uv);
                return true;
            }
        }
        else if (this.Kind == NumericLiteralKind.F32)
        {
            var value = BitConverter.UInt32BitsToSingle((uint)this.uv);
            if (double.IsFinite(value))
            {
                limitedValue = new(value);
                return true;
            }
        }
        else if (this.Kind == NumericLiteralKind.Float ||
            this.Kind == NumericLiteralKind.F64)
        {
            var value = BitConverter.UInt64BitsToDouble((ulong)this.uv);
            if (double.IsFinite(value))
            {
                limitedValue = new(value);
                return true;
            }
        }

        limitedValue = default;
        return false;
    }

    public override string ToString()
        => $"{this.Literal}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.Literal);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Literal})", default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PrepareNumericLiteral()
    {
        if (this.Kind == NumericLiteralKind.Invalid)
        {
            NumericLiteralParser.TryParse(this.Literal, out var kind, out var uv);
            this.Kind = kind;
            this.uv = uv;
        }
    }
}

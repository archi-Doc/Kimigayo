// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class NumericLiteralKoto : Koto
{//
    [Key(1)]
    public string Literal { get; private set; }

    [Key(2)]
    public NumericLiteralKind NumericKind { get; private set; }

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
        NumericLiteralParser.IsIntegerInRange(this.NumericKind, this.uv, IntPtr.Size);
        if (this.NumericKind >= NumericLiteralKind.Integer &&
            this.NumericKind <= NumericLiteralKind.U128)
        {
        }
    }

    public override string ToString()
        => $"{this.Literal}";

    public override void WriteTo(IndentWriter writer)
    {
        writer.Write(this.Literal);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Literal})", default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PrepareNumericLiteral()
    {
        if (this.NumericKind == NumericLiteralKind.Invalid)
        {
            NumericLiteralParser.TryParse(this.Literal, out var kind, out var uv);
            this.NumericKind = kind;
            this.uv = uv;
        }
    }
}

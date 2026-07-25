// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

public enum NumericLiteraKind
{
    Invalid,
    I8,
    I16,
    I32,
    I64,
    I128,
    ISize,
    U8,
    U16,
    U32,
    U64,
    U128,
    USize,
}

[TinyhandObject]
public sealed partial class NumericLiteralKoto : Koto
{//
    [Key(1)]
    public string Literal { get; private set; }

    [Key(2)]
    public NumericLiteraKind NumericKind { get; private set; }

    [Key(3)]
    private UInt128 numericValue;

    public NumericLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Literal = token.Text.ToString();
    }

    public bool TryGetI64(out long value)
    {
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
}

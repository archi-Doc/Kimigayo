// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class EqualsEqualsKoto : BinaryKoto
{// A == B
    public EqualsEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left.ToString()} == {this.Right.ToString()}";
}

// [TinyhandObject]
public abstract partial class BinaryKoto : Koto
{
    // [Key(1)]
    // public TokenKind Kind { get; private set; }

    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Right { get; private set; }

    public BinaryKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range)
    {
        // this.Kind = token.Kind;
        this.Left = left;
        this.Right = right;
        this.Left.Parent = this;
        this.Right.Parent = this;
    }

    internal BinaryKoto(CodeContext codeContext)
        : base(codeContext)
    {
        this.Left = default!;
        this.Right = default!;
    }

    public override string ToString()
        => $"BinaryKoto: {this.Right.ToString()}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Left, this.Right,]);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Left)
        {
            this.Left = newKoto;
            return true;
        }
        else if (oldKoto == this.Right)
        {
            this.Right = newKoto;
            return true;
        }

        return false;
    }

    [TinyhandOnDeserialized]
    protected void OnDeserialized()
    {
        this.Left.Parent = this;
        this.Left.CodeContext = this.CodeContext;
        this.Right.Parent = this;
        this.Right.CodeContext = this.CodeContext;
    }
}

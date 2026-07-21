// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class AttributeKoto : UnaryKoto
{// #A
    public AttributeKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"#{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class MacroKoto : UnaryKoto
{// $A
    public MacroKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"${this.Operand.ToString()}";
}

[TinyhandObject]
public partial class HeapKoto : UnaryKoto
{// &A
    public HeapKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"&{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class UnwrapKoto : UnaryKoto
{// *A
    public UnwrapKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"*{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class CaretKoto : UnaryKoto
{// ^A
    public CaretKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"^{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PrefixPlusKoto : UnaryKoto
{// +A
    public PrefixPlusKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"+{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PrefixPlusPlusKoto : UnaryKoto
{// ++A
    public PrefixPlusPlusKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"++{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PrefixMinusKoto : UnaryKoto
{// -A
    public PrefixMinusKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"-{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PrefixMinusMinusKoto : UnaryKoto
{// --A
    public PrefixMinusMinusKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"--{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class NotKoto : UnaryKoto
{// not A
    public NotKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"not {this.Operand.ToString()}";
}

[TinyhandObject]
public partial class ParenthesizedKoto : UnaryKoto
{// (A)
    public ParenthesizedKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"({this.Operand.ToString()})";
}

[TinyhandObject]
public partial class PrefixIncrementKoto : UnaryKoto
{// ++A
    public PrefixIncrementKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"++{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PostfixIncrementKoto : UnaryKoto
{// A++
    public PostfixIncrementKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"{this.Operand.ToString()}++";
}

[TinyhandObject]
public partial class PostfixDecrementKoto : UnaryKoto
{// A--
    public PostfixDecrementKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"{this.Operand.ToString()}--";
}

[TinyhandObject]
public partial class UnaryKoto : Koto
{// + - not ^ ++ -- * &
    // [Key(1)]
    // public TokenKind Kind { get; private set; }

    [Key(1)]
    public Koto Operand { get; protected set; }

    public UnaryKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range)
    {
        // this.Kind = token.Kind;
        this.Operand = operand;
        operand.Parent = this;
    }

    public override string ToString()
        => $"Unary:{this.Operand.ToString()}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Operand,]);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Operand)
        {
            this.Operand = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    [TinyhandOnDeserialized]
    protected void OnDeserialized()
    {
        this.Operand.Parent = this;
        this.Operand.CodeContext = this.CodeContext;
    }
}

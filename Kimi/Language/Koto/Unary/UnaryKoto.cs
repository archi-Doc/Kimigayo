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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.SharpChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.DollarChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.AmpersandChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.AsteriskChar);
        writer.Write(this.Operand.ToString());
    }
}

[TinyhandObject]
public partial class PrefixCaretKoto : UnaryKoto
{// ^A
    public PrefixCaretKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"^{this.Operand.ToString()}";

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.CaretChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.PlusChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.PlusChar);
        writer.Write(Constants.PlusChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.MinusChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.MinusChar);
        writer.Write(Constants.MinusChar);
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(this.Operand.ToString());
        writer.Write(Constants.PlusChar);
        writer.Write(Constants.PlusChar);
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(this.Operand.ToString());
        writer.Write(Constants.MinusChar);
        writer.Write(Constants.MinusChar);
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write("not ");
        writer.Write(this.Operand.ToString());
    }
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

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(Constants.OpenParenthesisChar);
        writer.Write(this.Operand.ToString());
        writer.Write(Constants.CloseParenthesisChar);
    }
}

// [TinyhandObject]
public abstract partial class UnaryKoto : Koto
{// + - not ^ ++ -- * &
    [Key(1)]
    public Koto Operand { get; protected set; }

    public UnaryKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range)
    {
        // this.Kind = token.Kind;
        this.Operand = operand;
        operand.Parent = this;
    }

    internal UnaryKoto(CodeContext codeContext)
        : base(codeContext)
    {
        this.Operand = default!;
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

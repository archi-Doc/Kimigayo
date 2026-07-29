// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class AttributeKoto : UnaryKoto
{// #A
    [IgnoreMember]
    public Koto IdentifierKoto { get; private set; }

    [IgnoreMember]
    public List<Koto> Arguments { get; private set; } = [];

    public bool IsIfAttribute
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.IdentifierKoto is UnresolvedKoto unresolvedKoto)
            {
                return unresolvedKoto.Identifier.SequenceEqual(Constants.IfAttribute) == true;
            }
            else
            {
                return false;
            }
        }
    }

    public AttributeKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
        if (operand is InvocationKoto invocationKoto &&
            invocationKoto.Method is UnresolvedKoto unresolvedKoto)
        {// #Attribute(arguments)
            this.IdentifierKoto = unresolvedKoto;
            this.Arguments = invocationKoto.Arguments;
        }
        else
        {
            this.IdentifierKoto = operand;
        }

        /*else
        {
            this.AddDiagnostic(Hashed.Kimi.InvalidAttributeKoto);

            this.IdentifierKoto = UnresolvedKoto.Error;
            this.Operand = new ErrorKoto(ref reader, range);
            // this.IdentifierKoto = this.Operand;
        }*/
    }

    public override string ToString()
        => $"#{this.Operand.ToString()}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.SharpChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.DollarChar);
        builder.Append(this.Operand.ToString());
    }
}

[TinyhandObject]
public partial class ReferenceKoto : UnaryKoto
{// &A
    public ReferenceKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"&{this.Operand.ToString()}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.AmpersandChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.AsteriskChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.CaretChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.PlusChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.PlusChar);
        builder.Append(Constants.PlusChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MinusChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MinusChar);
        builder.Append(Constants.MinusChar);
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.PlusChar);
        builder.Append(Constants.PlusChar);
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.MinusChar);
        builder.Append(Constants.MinusChar);
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("not ");
        builder.Append(this.Operand.ToString());
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

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.OpenParenthesisChar);
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.CloseParenthesisChar);
    }
}

// [TinyhandObject]
public abstract partial class UnaryKoto : Koto
{
    [Key(1)]
    public Koto Operand { get; protected set; }

    public UnaryKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range)
    {
        this.Operand = operand;
        operand.Parent = this;
    }

    internal UnaryKoto(CodeContext codeContext)
        : base(codeContext, default)
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

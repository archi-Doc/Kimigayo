// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class AttributeKoto : UnaryKoto
{// #A
    public override KotoKind Akind => KotoKind.Attribute;

    [IgnoreMember]
    public Koto IdentifierKoto { get; private set; }

    [IgnoreMember]
    public List<Koto> Arguments { get; private set; } = [];

    public bool IsIfAttribute
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.IdentifierKoto is IdentifierNameKoto unresolvedKoto)
            {
                return unresolvedKoto.IdentifierName.SequenceEqual(Constants.IfAttribute) == true;
            }
            else
            {
                return false;
            }
        }
    }

    public AttributeKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
        if (operand is InvocationKoto invocationKoto &&
            invocationKoto.Method is IdentifierNameKoto unresolvedKoto)
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
            this.AddDiagnostic(KimiDiagnostic.InvalidAttributeKoto);

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

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        if (this.Operand is InvocationKoto invocationKoto &&
            invocationKoto.Method is IdentifierNameKoto identifierKoto)
        {
            this.IdentifierKoto = identifierKoto;
            this.Arguments = invocationKoto.Arguments;
        }
        else
        {
            this.IdentifierKoto = this.Operand;
            this.Arguments = [];
        }
    }
}

[TinyhandObject]
public partial class MacroKoto : UnaryKoto
{// $A
    public override KotoKind Akind => KotoKind.Macro;

    public MacroKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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

/*[TinyhandObject]
public partial class ReferenceKoto : UnaryKoto
{// &A
    public override KotoKind Akind => KotoKind.Reference;

    [Key(2)]
    public ReferenceKind ReferenceKind { get; private set; }

    public ReferenceKoto(ref TokenReader reader, TextSpan range, Koto operand, ReferenceKind referenceKind)
        : base(ref reader, range, operand)
    {// &, &owner
        this.ReferenceKind = referenceKind;
    }

    public override string ToString()
        => $"&{this.Operand.ToString()}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.ReferenceKind.ToText(true));
        builder.Append(this.Operand.ToString());
    }
}*/

[TinyhandObject]
public partial class UnwrapKoto : UnaryKoto
{// *A
    public override KotoKind Akind => KotoKind.Unwrap;

    public UnwrapKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PrefixCaret;

    public PrefixCaretKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PrefixPlus;

    public PrefixPlusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PrefixPlusPlus;

    public PrefixPlusPlusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PrefixMinus;

    public PrefixMinusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PrefixMinusMinus;

    public PrefixMinusMinusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PostfixIncrement;

    public PostfixIncrementKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.PostfixDecrement;

    public PostfixDecrementKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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
    public override KotoKind Akind => KotoKind.Not;

    public NotKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public override string ToString()
        => $"{Constants.NotKeyword} {this.Operand.ToString()}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.NotKeyword);
        builder.AppendSpace();
        builder.Append(this.Operand.ToString());
    }
}

[TinyhandObject]
public partial class ParenthesizedKoto : UnaryKoto
{// (A)
    public override KotoKind Akind => KotoKind.Parenthesized;

    public ParenthesizedKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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

[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class UnaryKoto : Koto
{
    [Key(1)]
    public Koto Operand { get; protected set; }

    public UnaryKoto(ref TokenReader reader, SourceSpan range, Koto operand)
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

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Operand.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    protected void OnDeserialized()
    {
        this.Operand.Parent = this;
        this.Operand.CodeContext = this.CodeContext;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class MemberAccessKoto : BinaryKoto
{// A.B
    public override KotoKind Akind => KotoKind.MemberAccess;

    public Koto Accessor => this.Right;

    public MemberAccessKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left.ToString()}{Constants.DotChar}{this.Accessor.ToString()}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append(Constants.DotChar);
        this.Right.WriteTo(ref builder);
    }
}

[TinyhandObject]
public partial class IndexKoto : BinaryKoto
{// A[B]
    public override KotoKind Akind => KotoKind.Index;

    public Koto Index => this.Right;

    public IndexKoto(ref TokenReader reader, SourceRange range, Koto left, Koto index)
        : base(ref reader, range, left, index)
    {
    }

    public override string ToString()
        => $"{this.Left.ToString()}[{this.Index.ToString()}]";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append("[");
        this.Right.WriteTo(ref builder);
        builder.Append("]");
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Left, this.Index,]);
    }
}

[TinyhandObject]
public partial class AsteriskKoto : BinaryKoto
{// A * B
    public override KotoKind Akind => KotoKind.Asterisk;

    public AsteriskKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} * {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " * ");
    }
}

[TinyhandObject]
public partial class SlashKoto : BinaryKoto
{// A / B
    public override KotoKind Akind => KotoKind.Slash;

    public SlashKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} / {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " / ");
    }
}

[TinyhandObject]
public partial class PercentKoto : BinaryKoto
{// A % B
    public override KotoKind Akind => KotoKind.Percent;

    public PercentKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} % {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " % ");
    }
}

[TinyhandObject]
public partial class PlusKoto : BinaryKoto
{// A + B
    public override KotoKind Akind => KotoKind.Plus;

    public PlusKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} + {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " + ");
    }
}

[TinyhandObject]
public partial class MinusKoto : BinaryKoto
{// A - B
    public override KotoKind Akind => KotoKind.Minus;

    public MinusKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} - {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " - ");
    }
}

[TinyhandObject]
public partial class LessThanLessThanKoto : BinaryKoto
{// A << B
    public override KotoKind Akind => KotoKind.LessThanLessThan;

    public LessThanLessThanKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} << {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " << ");
    }
}

[TinyhandObject]
public partial class GreaterThanGreaterThanKoto : BinaryKoto
{// A >> B
    public override KotoKind Akind => KotoKind.GreaterThanGreaterThan;

    public GreaterThanGreaterThanKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} >> {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >> ");
    }
}

[TinyhandObject]
public partial class LessThanKoto : BinaryKoto
{// A < B
    public override KotoKind Akind => KotoKind.LessThan;

    public LessThanKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} < {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " < ");
    }
}

[TinyhandObject]
public partial class LessThanEqualsKoto : BinaryKoto
{// A <= B
    public override KotoKind Akind => KotoKind.LessThanEquals;

    public LessThanEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} <= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " <= ");
    }
}

[TinyhandObject]
public partial class GreaterThanKoto : BinaryKoto
{// A > B
    public override KotoKind Akind => KotoKind.GreaterThan;

    public GreaterThanKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} > {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " > ");
    }
}

[TinyhandObject]
public partial class GreaterThanEqualsKoto : BinaryKoto
{// A >= B
    public override KotoKind Akind => KotoKind.GreaterThanEquals;

    public GreaterThanEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} >= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >= ");
    }
}

[TinyhandObject]
public partial class AsKoto : BinaryKoto
{// A as B
    public override KotoKind Akind => KotoKind.As;

    public AsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} as {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " as ");
    }
}

[TinyhandObject]
public partial class IsKoto : BinaryKoto
{// A is B
    public override KotoKind Akind => KotoKind.Is;

    public IsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} is {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " is ");
    }
}

[TinyhandObject]
public partial class EqualsEqualsKoto : BinaryKoto
{// A == B
    public override KotoKind Akind => KotoKind.EqualsEquals;

    public EqualsEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} == {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " == ");
    }
}

[TinyhandObject]
public partial class ExclamationEqualsKoto : BinaryKoto
{// A != B
    public override KotoKind Akind => KotoKind.ExclamationEquals;

    public ExclamationEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} != {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " != ");
    }
}

[TinyhandObject]
public partial class AmpersandKoto : BinaryKoto
{// A & B
    public override KotoKind Akind => KotoKind.Ampersand;

    public AmpersandKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} & {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " & ");
    }
}

[TinyhandObject]
public partial class CaretKoto : BinaryKoto
{// A ^ B
    public override KotoKind Akind => KotoKind.Caret;

    public CaretKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} ^ {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " ^ ");
    }
}

[TinyhandObject]
public partial class BarKoto : BinaryKoto
{// A | B
    public override KotoKind Akind => KotoKind.Bar;

    public BarKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} | {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " | ");
    }
}

[TinyhandObject]
public partial class AndKoto : BinaryKoto
{// A and B
    public override KotoKind Akind => KotoKind.And;

    public AndKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} and {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " and ");
    }
}

[TinyhandObject]
public partial class OrKoto : BinaryKoto
{// A or B
    public override KotoKind Akind => KotoKind.Or;

    public OrKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} or {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " or ");
    }
}

[TinyhandObject]
public partial class EqualsKoto : BinaryKoto
{// A = B
    public override KotoKind Akind => KotoKind.Equals;

    public EqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} = {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " = ");
    }
}

[TinyhandObject]
public partial class PlusEqualsKoto : BinaryKoto
{// A += B
    public override KotoKind Akind => KotoKind.PlusEquals;

    public PlusEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} += {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " += ");
    }
}

[TinyhandObject]
public partial class MinusEqualsKoto : BinaryKoto
{// A -= B
    public override KotoKind Akind => KotoKind.MinusEquals;

    public MinusEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} -= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " -= ");
    }
}

[TinyhandObject]
public partial class AsteriskEqualsKoto : BinaryKoto
{// A *= B
    public override KotoKind Akind => KotoKind.AsteriskEquals;

    public AsteriskEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} *= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " *= ");
    }
}

[TinyhandObject]
public partial class SlashEqualsKoto : BinaryKoto
{// A /= B
    public override KotoKind Akind => KotoKind.SlashEquals;

    public SlashEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} /= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " /= ");
    }
}

[TinyhandObject]
public partial class PercentEqualsKoto : BinaryKoto
{// A %= B
    public override KotoKind Akind => KotoKind.PercentEquals;

    public PercentEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} %= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " %= ");
    }
}

[TinyhandObject]
public partial class AmpersandEqualsKoto : BinaryKoto
{// A &= B
    public override KotoKind Akind => KotoKind.AmpersandEquals;

    public AmpersandEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} &= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " &= ");
    }
}

[TinyhandObject]
public partial class CaretEqualsKoto : BinaryKoto
{// A ^= B
    public override KotoKind Akind => KotoKind.CaretEquals;

    public CaretEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} ^= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " ^= ");
    }
}

[TinyhandObject]
public partial class BarEqualsKoto : BinaryKoto
{// A |= B
    public override KotoKind Akind => KotoKind.BarEquals;

    public BarEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} |= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " |= ");
    }
}

[TinyhandObject]
public partial class LessThanLessThanEqualsKoto : BinaryKoto
{// A <<= B
    public override KotoKind Akind => KotoKind.LessThanLessThanEquals;

    public LessThanLessThanEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} <<= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " <<= ");
    }
}

[TinyhandObject]
public partial class GreaterThanGreaterThanEqualsKoto : BinaryKoto
{// A >>= B
    public override KotoKind Akind => KotoKind.GreaterThanGreaterThanEquals;

    public GreaterThanGreaterThanEqualsKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    public override string ToString()
        => $"{this.Left} >>= {this.Right}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >>= ");
    }
}

// [TinyhandObject]
public abstract partial class BinaryKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Right { get; private set; }

    public BinaryKoto(ref TokenReader reader, SourceRange range, Koto left, Koto right)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Right = right;
        this.Left.Parent = this;
        this.Right.Parent = this;
    }

    internal BinaryKoto(CodeContext codeContext)
        : base(codeContext, default)
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

    protected void WriteBinaryKoto(ref IndentedStringBuilder builder, string infix)
    {
        this.Left.WriteTo(ref builder);

        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.None);
        }

        builder.Append(infix);

        this.Right.WriteTo(ref builder);
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

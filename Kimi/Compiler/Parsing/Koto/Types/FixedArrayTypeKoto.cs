// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Retains a fixed-array length expression and element type.</summary>
public sealed class FixedArrayTypeKoto : TypeKoto
{
    internal FixedArrayTypeKoto(ref TokenReader reader, SourceSpan span, Koto length, Koto element)
        : base(ref reader, span)
    {
        this.Length = length;
        this.ElementType = element;
        this.Adopt(length);
        this.Adopt(element);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.FixedArrayType;

    /// <summary>Gets the unevaluated length.</summary>
    public Koto Length { get; private set; }

    /// <summary>Gets the element type, including an explicit inference placeholder.</summary>
    public Koto ElementType { get; private set; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append('[');
        this.Length.WriteTo(ref builder);
        builder.Append(" of ");
        this.ElementType.WriteTo(ref builder);
        builder.Append(']');
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Length);
        visitor.Visit(this.ElementType);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Length;
        yield return this.ElementType;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Length == oldKoto)
        {
            this.Length = newKoto;
        }
        else if (this.ElementType == oldKoto)
        {
            this.ElementType = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}

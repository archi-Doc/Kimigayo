// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Evaluates a Value Context operand and explicitly discards its result.</summary>
public sealed class DiscardKoto : Koto
{
    internal DiscardKoto(ref TokenReader reader, SourceSpan span, Koto operand)
        : base(ref reader, span)
    {
        this.Operand = operand;
        this.Adopt(operand);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Discard;

    /// <summary>Gets the independently inferred operand.</summary>
    public Koto Operand { get; private set; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("_ = ");
        this.Operand.WriteTo(ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor) => visitor.Visit(this.Operand);

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Operand;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Operand != oldKoto)
        {
            return false;
        }

        this.Operand = newKoto;
        return true;
    }
}

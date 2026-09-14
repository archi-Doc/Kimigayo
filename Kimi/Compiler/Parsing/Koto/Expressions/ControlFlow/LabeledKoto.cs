// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Attaches a lexical Label to a control-flow construct.</summary>
public sealed class LabeledKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Labeled;

    /// <summary>Gets the Label name.</summary>
    public string Label { get; private set; }

    /// <summary>Gets the labeled control-flow construct.</summary>
    public Koto Target { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="LabeledKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The complete source span.</param>
    /// <param name="label">The Label name.</param>
    /// <param name="target">The labeled control-flow construct.</param>
    public LabeledKoto(ref TokenReader reader, SourceSpan span, string label, Koto target)
        : base(ref reader, span)
    {
        this.Label = label;
        this.Target = target;
        this.Adopt(target);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Label);
        builder.Append(':');
        builder.AppendSpace();
        this.Target.WriteTo(ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Target);
    }

    protected override IEnumerable<Koto> GetChildNodes() => [this.Target];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Target != oldKoto || newKoto is not (DoKoto or IfKoto or MatchKoto or ForKoto or WhileKoto or LoopKoto or ErrorKoto))
        {
            return false;
        }

        this.Target = newKoto;
        return true;
    }
}

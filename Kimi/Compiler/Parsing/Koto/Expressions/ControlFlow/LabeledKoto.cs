// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Attaches a lexical Label to a Block or Loop.</summary>
[TinyhandObject]
public sealed partial class LabeledKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Labeled;

    /// <summary>Gets the Label name.</summary>
    [Key(1)]
    public string Label { get; private set; }

    /// <summary>Gets the labeled Block or Loop.</summary>
    [Key(2)]
    public Koto Target { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="LabeledKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The complete source span.</param>
    /// <param name="label">The Label name.</param>
    /// <param name="target">The labeled Block or Loop.</param>
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
        if (this.Target is CodeBlockKoto block)
        {
            block.WriteIndentedTo(ref builder);
        }
        else
        {
            builder.AppendSpace();
            this.Target.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes() => [this.Target];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Target != oldKoto || newKoto is not (CodeBlockKoto or ForKoto or WhileKoto or LoopKoto or ErrorKoto))
        {
            return false;
        }

        this.Target = newKoto;
        return true;
    }
}

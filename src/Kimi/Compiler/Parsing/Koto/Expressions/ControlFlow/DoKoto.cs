// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A scoped expression that executes its common Body once.</summary>
public sealed class DoKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Do;

    /// <summary>Gets the single-item or indented body.</summary>
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="DoKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The complete expression span.</param>
    /// <param name="body">The executable body.</param>
    public DoKoto(ref TokenReader reader, SourceSpan span, CodeBlockKoto body)
        : base(ref reader, span)
    {
        this.Body = body;
        this.Adopt(body);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("do");
        this.Body.WriteBranchTo(ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor) => visitor.Visit(this.Body);

    protected override IEnumerable<Koto> GetChildNodes() => [this.Body];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto != this.Body || newKoto is not CodeBlockKoto body)
        {
            return false;
        }

        this.Body = body;
        return true;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a Never-valued <c>break</c> expression.</summary>
[TinyhandObject]
public sealed partial class BreakKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Break;

    /// <summary>Gets the break value, if present.</summary>
    [Key(1)]
    public Koto? Expression { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BreakKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The break value, if present.</param>
    public BreakKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range)
    {
        this.Expression = expression;
        if (expression is not null)
        {
            expression.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Expression?.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.BreakKeyword);
        if (this.Expression is not null)
        {
            builder.AppendSpace();
            this.Expression.WriteTo(ref builder);
        }
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression != oldKoto)
        {
            return false;
        }

        this.Expression = newKoto;
        newKoto.Parent = this;
        oldKoto.Parent = default;
        return true;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Expression?.RestoreAfterDeserialization(codeContext, this);
    }
}

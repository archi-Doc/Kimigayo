// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a Never-valued <c>return</c> expression.</summary>
[TinyhandObject]
public sealed partial class ReturnKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Return;

    /// <summary>Gets the returned expression, if present.</summary>
    [Key(1)]
    public Koto? Expression { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="ReturnKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The returned expression, if present.</param>
    public ReturnKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
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
        builder.Append(Constants.ReturnKeyword);
        if (this.Expression is not null)
        {
            builder.AppendSpace();
            this.Expression.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Expression is not null)
        {
            yield return this.Expression;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression != oldKoto)
        {
            return false;
        }

        this.Expression = newKoto;
        return true;
    }
}

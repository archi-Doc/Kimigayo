// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a Never-valued <c>yield</c> expression.</summary>
[TinyhandObject]
public sealed partial class YieldKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Yield;

    /// <summary>Gets the value supplied to the target value-producing construct.</summary>
    [Key(1)]
    public Koto Expression { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="YieldKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The value supplied to the target construct.</param>
    public YieldKoto(ref TokenReader reader, SourceSpan range, Koto expression)
        : base(ref reader, range)
    {
        this.Expression = expression;
        expression.Parent = this;
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Expression.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.YieldKeyword);
        builder.AppendSpace();
        this.Expression.WriteTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Expression;
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

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.Expression.Parent = this;
}

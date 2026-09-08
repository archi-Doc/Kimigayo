// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a <c>while</c> expression whose value is Unit.</summary>
public sealed class WhileKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.While;

    /// <summary>Gets the loop condition.</summary>
    public Koto Condition { get; private set; }

    /// <summary>Gets the loop body.</summary>
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="WhileKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="condition">The loop condition.</param>
    /// <param name="body">The loop body.</param>
    public WhileKoto(ref TokenReader reader, SourceSpan range, Koto condition, CodeBlockKoto body)
        : base(ref reader, range)
    {
        this.Condition = condition;
        this.Body = body;
        condition.Parent = this;
        body.Parent = this;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.WhileKeyword);
        builder.AppendSpace();
        this.Condition.WriteTo(ref builder);
        this.Body.WriteIndentedTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => [this.Condition, this.Body];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
            return true;
        }

        if (this.Body == oldKoto && newKoto is CodeBlockKoto block)
        {
            this.Body = block;
            return true;
        }

        return false;
    }
}

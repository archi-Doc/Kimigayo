// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a <c>while</c> expression whose value is Unit.</summary>
[TinyhandObject]
public sealed partial class WhileKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.While;

    /// <summary>Gets the loop condition.</summary>
    [Key(1)]
    public Koto Condition { get; private set; }

    /// <summary>Gets the loop body.</summary>
    [Key(2)]
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
    public override void Bind(Compilation compilation)
    {
        this.Condition.Bind(compilation);
        this.Body.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.WhileKeyword);
        builder.AppendSpace();
        this.Condition.WriteTo(ref builder);
        this.Body.WriteIndentedTo(ref builder);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        if (this.Body == oldKoto && newKoto is CodeBlockKoto block)
        {
            this.Body = block;
            block.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Condition.RestoreAfterDeserialization(codeContext, this);
        this.Body.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Condition.Parent = this;
        this.Body.Parent = this;
    }
}

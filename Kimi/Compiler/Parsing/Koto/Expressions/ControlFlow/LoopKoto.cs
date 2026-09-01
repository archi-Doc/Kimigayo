// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents an unconditional <c>loop</c> expression.</summary>
[TinyhandObject]
public sealed partial class LoopKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Loop;

    /// <summary>Gets the loop body.</summary>
    [Key(1)]
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="LoopKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="body">The loop body.</param>
    public LoopKoto(ref TokenReader reader, SourceSpan range, CodeBlockKoto body)
        : base(ref reader, range)
    {
        this.Body = body;
        body.Parent = this;
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Body.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.LoopKeyword);
        this.Body.WriteIndentedTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Body;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Body != oldKoto || newKoto is not CodeBlockKoto block)
        {
            return false;
        }

        this.Body = block;
        return true;
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.Body.Parent = this;
}

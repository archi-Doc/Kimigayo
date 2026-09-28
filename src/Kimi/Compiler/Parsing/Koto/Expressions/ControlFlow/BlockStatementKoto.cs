// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A statement with an independently scoped inline or indented body.</summary>
public abstract class BlockStatementKoto : Koto
{
    /// <summary>Gets the body, including the scope of a single-item body.</summary>
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Gets a value indicating whether the body is written on the header line.</summary>
    public bool IsInline { get; }

    /// <summary>Gets the contextual keyword introducing this statement.</summary>
    public abstract string Keyword { get; }

    /// <summary>Initializes a new instance of the <see cref="BlockStatementKoto"/> class.</summary>
    /// <param name="reader">The owning token reader.</param>
    /// <param name="span">The statement's complete span.</param>
    /// <param name="body">The parsed body.</param>
    /// <param name="isInline">Whether the body is a single-item body.</param>
    protected BlockStatementKoto(ref TokenReader reader, SourceSpan span, CodeBlockKoto body, bool isInline)
        : base(ref reader, span)
    {
        this.Body = body;
        this.IsInline = isInline;
        this.Adopt(body);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        builder.Append(this.Keyword);
        this.Body.WriteBranchTo(ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Body);
    }

    protected override IEnumerable<Koto> GetChildNodes() => [this.Body];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Body != oldKoto || newKoto is not CodeBlockKoto body)
        {
            return false;
        }

        this.Body = body;
        return true;
    }
}

/// <summary>A lexical unsafe-operation context, not an expression.</summary>
public sealed class UnsafeBlockKoto : BlockStatementKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.UnsafeBlock;

    /// <inheritdoc/>
    public override string Keyword => Constants.UnsafeKeyword;

    /// <summary>Initializes a new instance of the <see cref="UnsafeBlockKoto"/> class.</summary>
    /// <param name="reader">The owning reader.</param>
    /// <param name="span">The complete span.</param>
    /// <param name="body">The body.</param>
    /// <param name="isInline">Whether the body is inline.</param>
    public UnsafeBlockKoto(ref TokenReader reader, SourceSpan span, CodeBlockKoto body, bool isInline)
        : base(ref reader, span, body, isInline)
    {
    }
}

/// <summary>A registration of deferred cleanup, not an expression.</summary>
public sealed class DeferredBlockKoto : BlockStatementKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.DeferredBlock;

    /// <inheritdoc/>
    public override string Keyword => "defer";

    /// <summary>Initializes a new instance of the <see cref="DeferredBlockKoto"/> class.</summary>
    /// <param name="reader">The owning reader.</param>
    /// <param name="span">The complete span.</param>
    /// <param name="body">The body.</param>
    /// <param name="isInline">Whether the body is inline.</param>
    public DeferredBlockKoto(ref TokenReader reader, SourceSpan span, CodeBlockKoto body, bool isInline)
        : base(ref reader, span, body, isInline)
    {
    }
}

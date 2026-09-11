// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Stores small syntax forms using shared child ownership and writing.</summary>
public sealed class SyntaxFormKoto : ExpressionKoto
{
    private readonly KotoKind kind;
    private readonly string prefix;
    private readonly string separator;
    private readonly string suffix;
    private readonly Koto[] children;

    internal SyntaxFormKoto(ref TokenReader reader, SourceSpan span, KotoKind kind, string prefix, Koto[] children, string separator = ", ", string suffix = "")
        : base(ref reader, span)
    {
        this.kind = kind;
        this.prefix = prefix;
        this.separator = separator;
        this.suffix = suffix;
        this.children = children;
        this.Adopt(children);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => this.kind;

    /// <summary>Gets the ordered operands without allocating an iterator.</summary>
    public ReadOnlySpan<Koto> Operands => this.children;

    internal bool IsMutablePattern => this.kind == KotoKind.BindingPattern && this.prefix == "var ";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.kind == KotoKind.ConditionalConformance && this.children is [IsKoto, _, CodeBlockKoto block])
        {
            this.children[0].WriteTo(ref builder);
            builder.Append(" when ");
            this.children[1].WriteTo(ref builder);
            block.WriteIndentedTo(ref builder);
            return;
        }

        builder.Append(this.prefix);
        for (var i = 0; i < this.children.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(this.separator);
            }

            this.children[i].WriteTo(ref builder);
        }

        builder.Append(this.suffix);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.VisitMany(this.children);
    }

    protected override IEnumerable<Koto> GetChildNodes() => this.children;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.children, oldKoto, newKoto);
}

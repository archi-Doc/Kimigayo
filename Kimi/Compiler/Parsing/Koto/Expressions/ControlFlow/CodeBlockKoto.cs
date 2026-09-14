// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Retains an indented item sequence or a common single-item Body.
/// </summary>
public sealed class CodeBlockKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CodeBlock;

    private IReadOnlyList<Koto> items;

    /// <summary>Gets the declaration context of a compile-time directive body.</summary>
    public TokenKind DeclarationContext { get; internal set; }

    /// <summary>Gets a value indicating whether this node wraps an explicitly introduced single-item body.</summary>
    public bool IsExpressionBody { get; internal set; }

    /// <summary>Gets a value indicating whether this single-item body has one item; its owner determines use or discard.</summary>
    public bool HasTrailingExpression => this.IsExpressionBody && this.items.Count == 1;

    /// <summary>Gets the block items in source order.</summary>
    public IReadOnlyList<Koto> Items => this.items;

    /// <summary>
    /// Gets the single body item before context/result classification, or <see langword="null"/> when there is no implicit result.
    /// </summary>
    public Koto? TrailingExpression => this.HasTrailingExpression && this.items.Count > 0 ? this.items[^1] : null;

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete block span.</param>
    /// <param name="items">The parsed block items.</param>
    public CodeBlockKoto(ref TokenReader reader, SourceSpan range, IReadOnlyList<Koto> items)
        : base(ref reader, range)
    {
        this.items = items;
        this.Adopt(items);
    }

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class for generated syntax.</summary>
    /// <param name="codeContext">The owning code context.</param>
    internal CodeBlockKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
        this.items = [];
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        for (var i = 0; i < this.items.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            if (this.items[i] is CodeBlockKoto nested)
            {
                nested.WriteIndentedTo(ref builder);
            }
            else if (this.items[i] is DeclarationContainerKoto container)
            {
                container.WriteAsBlockItem(ref builder);
            }
            else
            {
                this.items[i].WriteTo(ref builder);
            }
        }
    }

    internal void WriteIndentedTo(ref IndentedStringBuilder builder)
    {
        builder.AppendLine();
        builder.IncrementIndent();
        if (this.items.Count == 0 && this.DeclarationContext == TokenKind.Invalid)
        {
            // Preserve a body emptied by directive selection without emitting invalid source.
            builder.Append("#if false");
            builder.AppendLine();
            builder.IncrementIndent();
            builder.Append("()");
            builder.DecrementIndent();
        }
        else
        {
            this.WriteTo(ref builder);
        }

        builder.DecrementIndent();
    }

    internal void WriteBranchTo(ref IndentedStringBuilder builder)
    {
        if (this.IsExpressionBody)
        {
            builder.Append(" => ");
            this.WriteTo(ref builder);
        }
        else
        {
            this.WriteIndentedTo(ref builder);
        }
    }

    /// <summary>Adds an item to a compiler-generated block.</summary>
    /// <param name="item">The item to add.</param>
    internal void AddLast(Koto item)
    {
        if (this.items is not List<Koto> list)
        {
            // A generated block typically receives many top-level items; avoid the first few regrowths.
            list = this.items.Count == 0 ? new List<Koto>(16) : new List<Koto>(this.items);
            this.items = list;
        }

        list.Add(item);
        item.Parent = this;
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.VisitMany(this.items);
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.items;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.items, oldKoto, newKoto);
}

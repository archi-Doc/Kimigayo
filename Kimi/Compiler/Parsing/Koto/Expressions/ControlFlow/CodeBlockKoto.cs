// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an indentation-delimited expression block.
/// </summary>
public sealed class CodeBlockKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CodeBlock;

    private IReadOnlyList<Koto> items;

    private bool hasUnterminatedExpression;

    /// <summary>Gets a value indicating whether the last item has an explicit semicolon.</summary>
    public bool HasTrailingSemicolon { get; private set; }

    /// <summary>Gets the declaration context of a compile-time directive body.</summary>
    public TokenKind DeclarationContext { get; internal set; }

    /// <summary>Gets a value indicating whether this node wraps an explicitly introduced branch Expression body.</summary>
    public bool IsExpressionBody { get; internal set; }

    /// <summary>Gets a value indicating whether this explicit Expression body supplies an implicit result.</summary>
    public bool HasTrailingExpression => this.IsExpressionBody && this.items.Count == 1;

    /// <summary>Gets the block items in source order.</summary>
    public IReadOnlyList<Koto> Items => this.items;

    /// <summary>
    /// Gets the implicit branch result, or <see langword="null"/> when there is no implicit result.
    /// </summary>
    public Koto? TrailingExpression => this.HasTrailingExpression && this.items.Count > 0 ? this.items[^1] : null;

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete block span.</param>
    /// <param name="items">The parsed block items.</param>
    /// <param name="hasTrailingExpression">Whether the final item is an expression without a semicolon.</param>
    /// <param name="hasTrailingSemicolon">Whether the final item has an explicit semicolon.</param>
    public CodeBlockKoto(ref TokenReader reader, SourceSpan range, IReadOnlyList<Koto> items, bool hasTrailingExpression, bool hasTrailingSemicolon = false)
        : base(ref reader, range)
    {
        this.items = items;
        this.hasUnterminatedExpression = hasTrailingExpression;
        this.HasTrailingSemicolon = hasTrailingSemicolon;
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
                if (i == this.items.Count - 1 && this.HasTrailingSemicolon && ParenthesizedKoto.NeedsMultilineGrouping(this.items[i]))
                {
                    ParenthesizedKoto.WriteGroupedTo(this.items[i], ref builder);
                }
                else
                {
                    this.items[i].WriteTo(ref builder);
                }
            }

            if (i == this.items.Count - 1 && this.HasTrailingSemicolon)
            {
                builder.Append(';');
            }
        }
    }

    internal void WriteIndentedTo(ref IndentedStringBuilder builder)
    {
        builder.AppendLine();
        builder.IncrementIndent();
        this.WriteTo(ref builder);
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
    /// <param name="hasTrailingExpression">Whether the item is an expression without a semicolon.</param>
    internal void AddLast(Koto item, bool hasTrailingExpression)
    {
        if (this.items is not List<Koto> list)
        {
            list = new List<Koto>(this.items);
            this.items = list;
        }

        list.Add(item);
        item.Parent = this;
        this.hasUnterminatedExpression = hasTrailingExpression;
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.items;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.items, oldKoto, newKoto);
}

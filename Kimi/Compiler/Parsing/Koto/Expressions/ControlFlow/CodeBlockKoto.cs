// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an indentation-delimited expression block.
/// </summary>
[TinyhandObject]
public sealed partial class CodeBlockKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CodeBlock;

    [Key(1)]
    private List<Koto> items = [];

    /// <summary>
    /// Gets a value indicating whether the final item is the block's trailing expression.
    /// </summary>
    [Key(2)]
    public bool HasTrailingExpression { get; private set; }

    /// <summary>Gets the block items in source order.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> Items => this.items;

    /// <summary>
    /// Gets the trailing expression, or <see langword="null"/> when this block evaluates to Unit.
    /// </summary>
    [IgnoreMember]
    public Koto? TrailingExpression => this.HasTrailingExpression && this.items.Count > 0 ? this.items[^1] : null;

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete block span.</param>
    /// <param name="items">The parsed block items.</param>
    /// <param name="hasTrailingExpression">Whether the final item supplies the block value.</param>
    public CodeBlockKoto(ref TokenReader reader, SourceSpan range, List<Koto> items, bool hasTrailingExpression)
        : base(ref reader, range)
    {
        this.items = items;
        this.HasTrailingExpression = hasTrailingExpression;

        foreach (var item in items)
        {
            item.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        foreach (var item in this.items)
        {
            item.Bind(compilation);
        }
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

            if (this.items[i] is CollectionKoto group)
            {
                group.WriteAsBlockItem(ref builder);
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
        this.WriteTo(ref builder);
        builder.DecrementIndent();
    }

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class for generated syntax.</summary>
    /// <param name="codeContext">The owning code context.</param>
    internal CodeBlockKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
    }

    /// <summary>Adds an item to a compiler-generated block.</summary>
    /// <param name="item">The item to add.</param>
    /// <param name="hasTrailingExpression">Whether the item supplies the block value.</param>
    internal void AddLast(Koto item, bool hasTrailingExpression)
    {
        this.items.Add(item);
        item.Parent = this;
        this.HasTrailingExpression = hasTrailingExpression;
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.items;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        for (var i = 0; i < this.items.Count; i++)
        {
            if (this.items[i] == oldKoto)
            {
                this.items[i] = newKoto;
                return true;
            }
        }

        return false;
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        foreach (var item in this.items)
        {
            item.Parent = this;
        }
    }
}

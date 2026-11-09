// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Stores small syntax forms using shared child ownership and writing.</summary>
public sealed class SyntaxFormKoto : ExpressionKoto, IOriginClauseOwner
{
    List<OriginRelationKoto>? IOriginClauseOwner.OriginClauses { get; set; }

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

    internal SyntaxFormKoto(FunctionKoto constructor)
        : base(constructor.CodeContext, constructor.SignatureSpan)
    {
        this.kind = KotoKind.ConstructorReference;
        this.prefix = TokenKind.Base.ToText();
        this.separator = ", ";
        this.suffix = string.Empty;
        this.children = [];
    }

    /// <inheritdoc/>
    public override KotoKind Akind => this.kind;

    /// <inheritdoc/>
    internal override int TypeComponentCount => this.kind == KotoKind.RootName && this.children.Length == 1 ? 1 : 0;

    /// <inheritdoc/>
    internal override Koto TypeComponent(int index) => this.children[0];

    /// <summary>Gets the ordered operands without allocating an iterator.</summary>
    public ReadOnlySpan<Koto> Operands => this.children;

    internal bool IsMutablePattern => this.kind == KotoKind.BindingPattern && this.prefix == TokenKind.Var.ToText();

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

        // A keyword prefix (let, var, associate, base, init, _) or separator (for) is a table spelling, set off by spaces.
        var space = TokenHelper.GetKeywordOrIdentifierKind(this.separator) != TokenKind.Identifier ? " " : string.Empty;
        builder.Append(this.prefix);
        for (var i = 0; i < this.children.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(space);
                builder.Append(this.separator);
                builder.Append(space);
            }
            else if (TokenHelper.GetKeywordOrIdentifierKind(this.prefix) != TokenKind.Identifier)
            {
                builder.Append(' ');
            }

            this.children[i].WriteTo(ref builder);
        }

        builder.Append(this.suffix);
        OriginClauses.Write(this, ref builder);
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        slots.List(this.children);
    }
}

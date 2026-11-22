// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an alias declaration.
/// </summary>
public sealed class AliasKoto : DeclarationKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Alias;

    /// <summary>Gets the optional source-local qualifier name.</summary>
    public string? Name { get; }

    /// <summary>Gets the target, a ContainerReference read as a Type (SPEC F.6.1, F.5).</summary>
    public Koto TargetSyntax { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="AliasKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="targetSyntax">The target.</param>
    /// <param name="name">The optional qualifier name.</param>
    /// <param name="span">The declaration location.</param>
    public AliasKoto(ref TokenReader reader, Koto targetSyntax, string? name, SourceSpan span)
        : base(ref reader, span)
    {
        this.Name = name;
        this.TargetSyntax = targetSyntax;
        targetSyntax.Parent = this;
    }

    /// <inheritdoc/>
    public override bool IsToplevel => true;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        builder.Append(TokenKind.Alias.ToText());
        builder.AppendSpace();
        if (this.Name is { } name)
        {
            builder.Append(name);
            builder.Append(" => ");
        }

        this.TargetSyntax.WriteTo(ref builder);
        OriginClauses.Write(this, ref builder);
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        this.TargetSyntax = slots.Slot(this.TargetSyntax);
    }
}

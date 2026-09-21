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

    /// <summary>Gets the segments of the aliased qualified name.</summary>
    public List<string> QualifiedName { get; private set; }

    /// <summary>Gets the optional source-local qualifier name.</summary>
    public string? Name { get; }

    /// <summary>Gets a Container path requiring Type arguments or Origin bindings.</summary>
    public Koto? TargetSyntax { get; }

    /// <summary>Initializes a new instance of the <see cref="AliasKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="alias">The qualified name segments.</param>
    /// <param name="name">The optional qualifier name.</param>
    /// <param name="span">The declaration location.</param>
    /// <param name="targetSyntax">The optional bound Container path.</param>
    public AliasKoto(ref TokenReader reader, List<string> alias, string? name = null, SourceSpan span = default, Koto? targetSyntax = null)
        : base(ref reader, span)
    {
        this.QualifiedName = alias;
        this.Name = name;
        this.TargetSyntax = targetSyntax;
        if (targetSyntax is not null)
        {
            targetSyntax.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override bool IsToplevel => true;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        builder.Append(Constants.AliasKeyword);
        builder.AppendSpace();
        if (this.Name is { } name)
        {
            builder.Append(name);
            builder.Append(" => ");
        }

        if (this.TargetSyntax is { } target)
        {
            target.WriteTo(ref builder);
            OriginClauses.Write(this, ref builder);
            return;
        }

        for (var i = 0; i < this.QualifiedName.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(Constants.DotChar);
            }

            builder.Append(this.QualifiedName[i]);
        }

        OriginClauses.Write(this, ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        base.VisitChildrenCore(visitor);
        if (this.TargetSyntax is { } target)
        {
            visitor.Visit(target);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var child in base.GetChildNodes())
        {
            yield return child;
        }

        if (this.TargetSyntax is { } target)
        {
            yield return target;
        }
    }
}

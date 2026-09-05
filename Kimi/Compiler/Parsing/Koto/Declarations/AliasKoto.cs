// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

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

    /// <summary>Initializes a new instance of the <see cref="AliasKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="alias">The qualified name segments.</param>
    public AliasKoto(ref TokenReader reader, List<string> alias)
        : base(ref reader, default)
    {
        this.QualifiedName = alias;
    }

    /// <inheritdoc/>
    public override bool IsToplevel => true;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        builder.Append(Constants.AliasKeyword);
        builder.AppendSpace();
        for (var i = 0; i < this.QualifiedName.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(Constants.DotChar);
            }

            builder.Append(this.QualifiedName[i]);
        }
    }
}

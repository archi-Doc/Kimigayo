// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an alias declaration.
/// </summary>
[TinyhandObject]
public sealed partial class AliasKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Alias;

    /// <summary>Gets the segments of the aliased qualified name.</summary>
    [Key(1)]
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
    public override string ToString()
        => $"{Constants.AliasKeyword} {string.Join(Constants.DotChar, this.QualifiedName)}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        builder.Append(Constants.AliasKeyword);
        builder.AppendSpace();
        for (var i = 0; i < this.QualifiedName.Count; i++)
        {
            builder.Append(this.QualifiedName[i]);
            if (i < (this.QualifiedName.Count - 1))
            {
                builder.Append(Constants.DotChar);
            }
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Provides a stable identifier for a named Koto node.
/// </summary>
[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class IdentifiableKoto : DeclarationKoto
{
    /// <summary>Gets or sets the identifier derived from this node and its containing declarations.</summary>
    [Key(1)]
    public ulong KotoId
    {
        get
        {
            if (field == 0)
            {
                // Include every identifiable ancestor so equal local names remain distinct.
                var hash = XxHash3Slim.Hash64(this.GetIdentifier());

                var parent = this.Parent;
                while (parent is not null)
                {
                    if (parent is IdentifiableKoto identifiableKoto)
                    {
                        hash = XxHash3Slim.Combine(identifiableKoto.KotoId, hash);
                    }

                    parent = parent.Parent;
                }

                field = hash;
            }

            return field;
        }

        protected set
        {
            field = value;
        }
    }

    /// <summary>Gets the source identifier used to compute <see cref="KotoId"/>.</summary>
    /// <returns>The identifier text.</returns>
    public abstract ReadOnlySpan<char> GetIdentifier();

    /// <summary>Initializes a new instance of the <see cref="IdentifiableKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The node span.</param>
    public IdentifiableKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IdentifiableKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="range">The node span.</param>
    public IdentifiableKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }
}

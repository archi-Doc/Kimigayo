// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class IdentifiableKoto : Koto
{
    [Key(1)]
    public ulong KotoId
    {
        get
        {
            if (field == 0)
            {
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

    public abstract ReadOnlySpan<char> GetIdentifier();

    public IdentifiableKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    public IdentifiableKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }
}

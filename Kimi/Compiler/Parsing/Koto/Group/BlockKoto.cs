// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;
#pragma warning disable SA1202 // Elements should be ordered by access

[TinyhandObject(ReservedKeyCount = 3)]
public abstract partial class BlockKoto : IdentifiableKoto, ITokenParser
{
    [Key(2)]
    public Koto.GoshujinClass Children { get; protected set; } = new();

    public BlockKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    public BlockKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }

    public virtual void Clear()
    {
        foreach (var x in this.Children)
        {
            if (x is BlockKoto blockKoto)
            {
                blockKoto.Clear();
            }
        }

        this.Children.ClearAll();
    }

    public void Parse(ref TokenReader reader)
    {
    }
}

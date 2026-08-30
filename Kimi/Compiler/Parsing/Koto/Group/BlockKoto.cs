// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;
#pragma warning disable SA1202 // Elements should be ordered by access

/// <summary>
/// Provides a base for named nodes that contain child nodes.
/// </summary>
[TinyhandObject(ReservedKeyCount = 3)]
public abstract partial class BlockKoto : IdentifiableKoto, ITokenParser
{
    /// <summary>Gets or sets the child node collection.</summary>
    [Key(2)]
    public Koto.GoshujinClass Children { get; protected set; } = new();

    /// <summary>Initializes a new instance of the <see cref="BlockKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The node span.</param>
    public BlockKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BlockKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="range">The node span.</param>
    public BlockKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }

    /// <summary>Recursively removes all child nodes.</summary>
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

    /// <summary>Parses the block body.</summary>
    /// <param name="reader">The token reader.</param>
    public void Parse(ref TokenReader reader)
    {
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a generic name with type arguments.
/// </summary>
[TinyhandObject]
public partial class GenericsKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Generics;

    /// <summary>Gets the generic identifier.</summary>
    [Key(1)]
    public Koto? Identifier { get; private set; }

    [Key(2)]
    private readonly List<Koto> typeList = [];

    /// <summary>Initializes a new instance of the <see cref="GenericsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="identifier">The generic identifier.</param>
    /// <param name="typeList">The generic type arguments.</param>
    public GenericsKoto(ref TokenReader reader, SourceSpan range, Koto identifier, List<Koto> typeList)
        : base(ref reader, range)
    {
        this.Identifier = identifier;
        this.typeList = typeList;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Identifier?.WriteTo(ref builder);
        builder.Append(Constants.LessThanChar);

        for (var i = 0; i < this.typeList.Count; i++)
        {
            this.typeList[i].WriteTo(ref builder);

            if (i != (this.typeList.Count - 1))
            {
                builder.AppendCommaAndSpace();
            }
        }

        builder.Append(Constants.GreaterThanChar);
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Identifier?.RestoreAfterDeserialization(codeContext, this);
        foreach (var type in this.typeList)
        {
            type.RestoreAfterDeserialization(codeContext, this);
        }
    }
}

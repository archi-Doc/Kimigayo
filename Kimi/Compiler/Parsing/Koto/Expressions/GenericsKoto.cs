// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a generic name with type arguments.
/// </summary>
[TinyhandObject]
public sealed partial class GenericsKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Generics;

    /// <summary>Gets the generic identifier.</summary>
    [Key(1)]
    public Koto? Identifier { get; private set; }

    [Key(2)]
    private List<Koto> typeList;

    /// <summary>Gets the generic type arguments.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> TypeArguments => this.typeList;

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
        identifier.Parent = this;
        this.Adopt(typeList);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Identifier?.WriteTo(ref builder);
        builder.Append(Constants.LessThanChar);

        for (var i = 0; i < this.typeList.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.typeList[i].WriteTo(ref builder);
        }

        builder.Append(Constants.GreaterThanChar);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Identifier is not null)
        {
            yield return this.Identifier;
        }

        foreach (var type in this.typeList)
        {
            yield return type;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Identifier == oldKoto)
        {
            this.Identifier = newKoto;
            return true;
        }

        return ReplaceInList(this.typeList, oldKoto, newKoto);
    }
}

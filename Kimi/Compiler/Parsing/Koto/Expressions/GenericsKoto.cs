// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a generic name with type arguments.
/// </summary>
public sealed class GenericsKoto : ApplicationKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Generics;

    /// <summary>Gets the generic identifier.</summary>
    public Koto? Identifier => this.Target;

    /// <summary>Gets the generic type arguments.</summary>
    public IReadOnlyList<Koto> TypeArguments => this.ArgumentNodes;

    /// <summary>Initializes a new instance of the <see cref="GenericsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="identifier">The generic identifier.</param>
    /// <param name="typeList">The generic type arguments.</param>
    public GenericsKoto(ref TokenReader reader, SourceSpan range, Koto identifier, IReadOnlyList<Koto> typeList)
        : base(ref reader, range, identifier, typeList)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Identifier?.WriteTo(ref builder);
        this.WriteArgumentsTo(ref builder, Constants.LessThanChar, Constants.GreaterThanChar);
    }
}

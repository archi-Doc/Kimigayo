// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Provides the base representation of a type-syntax node.</summary>
[TinyhandObject]
[TinyhandUnion(0, typeof(TypeSemanticsKoto))]
[TinyhandUnion(1, typeof(TupleTypeKoto))]
[TinyhandUnion(2, typeof(FunctionTypeKoto))]
public abstract partial class TypeKoto : Koto
{
    /// <summary>Gets the ownership semantics applied to this type.</summary>
    [IgnoreMember]
    public virtual SemanticsKind SemanticsKind => SemanticsKind.Owner;

    /// <summary>Gets the custom semantics parameter, if present.</summary>
    [IgnoreMember]
    public virtual string? SemanticsParameter => default;

    /// <summary>Gets the type identifier when this node has a simple name.</summary>
    [IgnoreMember]
    public virtual string Identifier => string.Empty;

    /// <summary>Gets the origin associated with this type, if present.</summary>
    [IgnoreMember]
    public virtual string? OriginName => default;

    /// <summary>Initializes a new instance of the <see cref="TypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The type source span.</param>
    protected TypeKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TypeKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="range">The type source span.</param>
    protected TypeKoto(CodeContext codeContext, SourceSpan range)
        : base(codeContext, range)
    {
    }
}

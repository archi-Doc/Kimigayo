// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Provides the base for collections whose members are all static.</summary>
[TinyhandObject]
public abstract partial class StaticCollectionKoto : CollectionKoto
{
    /// <inheritdoc/>
    public sealed override bool IsInstantiable => false;

    /// <inheritdoc/>
    public sealed override bool HasStaticMembersOnly => true;

    /// <summary>Initializes a new instance of the <see cref="StaticCollectionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected StaticCollectionKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StaticCollectionKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    protected StaticCollectionKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}

/// <summary>Provides the base for collections that produce runtime values.</summary>
[TinyhandObject]
public abstract partial class InstantiableCollectionKoto : CollectionKoto
{
    /// <inheritdoc/>
    public sealed override bool IsInstantiable => true;

    /// <summary>Initializes a new instance of the <see cref="InstantiableCollectionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected InstantiableCollectionKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InstantiableCollectionKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    protected InstantiableCollectionKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}

/// <summary>Provides the base for instantiable collections with generic parameters and origins.</summary>
[TinyhandObject]
public abstract partial class GenericCollectionKoto : InstantiableCollectionKoto
{
    /// <inheritdoc/>
    public sealed override bool SupportsGenerics => true;

    /// <inheritdoc/>
    public sealed override bool SupportsOrigins => true;

    /// <summary>Initializes a new instance of the <see cref="GenericCollectionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected GenericCollectionKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GenericCollectionKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    protected GenericCollectionKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>A compiler-synthesized node that is already resolved: the pinned callee of a range construction, or one of
/// its explicit Type arguments (SPEC 4.6.3). It has no source spelling of its own.</summary>
internal sealed class SyntheticKoto(Koto root) : ExpressionKoto(root.CodeContext, root.Span)
{
    public override KotoKind Akind => KotoKind.Invalid;

    /// <summary>Gets or sets the Type whose generic environment a pinned Type function is selected in.</summary>
    internal BoundType? DeclaringType { get; set; }

    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("<synthetic>");

    /// <summary>Resolves the node to a declaration or a Type.</summary>
    /// <param name="symbol">The pinned declaration, or null for a Type argument.</param>
    /// <param name="type">The Type of a Type argument, or null for a callee.</param>
    /// <param name="declaringType">The declaring Type of a pinned Type function.</param>
    internal void Resolve(BindingSymbol? symbol, BoundType? type, BoundType? declaringType)
    {
        this.BoundSymbol = symbol;
        this.BoundType = type;
        this.DeclaringType = declaringType;
        this.BindingFailure = BindingFailure.None;
        this.BindingState = BindingState.Resolved;
    }
}

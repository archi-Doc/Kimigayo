// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// A synthesized operand that denotes a value its enclosing desugaring has already evaluated, such as the receiver whose
/// length a range key reads (SPEC 4.6.4) or the receiver a property update locates. A desugaring never lowers source syntax
/// a second time: it refers to the value through this node, which has the Type of <see cref="Source"/> (or of the operand
/// the desugaring registers, such as a borrow of it), and every later phase reads the operand the desugaring registered for
/// that source. The source is not a child, so no traversal evaluates it here.
/// </summary>
internal sealed class EvaluatedKoto(Koto source) : ExpressionKoto(source.CodeContext, source.Span)
{
    public override KotoKind Akind => KotoKind.Invalid;

    /// <summary>Gets the syntax whose value the enclosing desugaring evaluated.</summary>
    internal Koto Source { get; } = source;

    public override void WriteTo(ref IndentedStringBuilder builder) => this.Source.WriteTo(ref builder);
}

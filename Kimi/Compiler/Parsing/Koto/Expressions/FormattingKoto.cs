// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

// Compiler-only expressions retain the source root as their parent. They introduce
// neither a scope nor a temporary boundary, and never adopt an embedded expression.
internal sealed class FormattingKoto(Koto root, FormattingOperation operation)
    : ExpressionKoto(root.CodeContext, root.Span)
{
    public override KotoKind Akind => KotoKind.Invalid;

    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("<formatting>");

    internal FormattingOperation Operation { get; } = operation;

    internal BoundType? DeclaringType { get; set; }

    internal BoundFormatting Plan { get; set; } = null!;

    internal void Resolve(BoundType type)
    {
        this.BoundType = type;
        this.BindingState = BindingState.Resolved;
    }
}

internal enum FormattingOperation : byte
{
    Callee,
    Storage,
    Capacity,
    Hint,
    Check,
    Finish,
}

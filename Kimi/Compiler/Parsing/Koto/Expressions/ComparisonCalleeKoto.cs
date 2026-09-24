// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

// A non-owning call view keeps operand parents and temporary/control-transfer boundaries intact.
internal sealed class ComparisonCalleeKoto(BinaryKoto root) : ExpressionKoto(root.CodeContext, root.Span)
{
    public override KotoKind Akind => KotoKind.Invalid;

    internal BoundType Self { get; set; } = null!;

    internal BoundCall? RequirementStorage { get; set; }

    internal BoundCall? ImplementationStorage { get; set; }

    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("<comparison>");
}

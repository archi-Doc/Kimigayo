// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>A condition whose validation must survive early directive selection.</summary>
/// <param name="Condition">The complete condition, including its source and diagnostic context.</param>
/// <param name="Scope">The enclosing syntax scope retained for validation diagnostics.</param>
/// <remarks>
/// This is a validation obligation, not executable syntax. The condition may also belong to a retained
/// directive node. Resolve Names only in the prepared Compilation environment, never in source Type or
/// Value scopes. Consumers must not bind an excluded target to validate it. Pending is not a generic dependency.
/// </remarks>
public sealed record PendingDirectiveCondition(Koto Condition, Koto Scope)
{
    /// <summary>Gets the source document captured before the diagnostic collection can be reused for another source.</summary>
    public SourceDocument? SourceDocument { get; } = Condition.CodeContext.SourceDocument ?? Condition.DiagnosticCollection?.SourceDocument;
}

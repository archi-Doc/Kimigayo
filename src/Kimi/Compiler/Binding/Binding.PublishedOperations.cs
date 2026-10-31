// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 4.9.6: the verification of a published operation's own body against its row. Responsibility: summarize the Kimi body of a library
// operation that publishes its summary, under its own premises with abstract Type parameters, as EffectSummary summarizes a witness.
// Inputs: the operation; its whole row from the row's one owner (TyCtxt.Published over KimiLibraryCatalog.Row). Outputs: whether the body
// stays within confined, and the first effect that does not. Invariant: only the effects the row publishes are covered, the requirement it
// runs and the calls of the Callable input it names; every other effect of the body counts as at any witness. Prohibition: callers and
// bound verification never read this body; they use the row (DECISIONS BU-70).
public sealed partial class Binding
{
    /// <summary>SPEC 4.9.6: summarizes the body of a Kimi operation that publishes its summary, under its own premises with abstract
    /// Type parameters, as its published row permits: the parameter-dependent requirement the row names and the calls through the
    /// Callable input it names (`PublishedRow.Calls`) are covered.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>Whether the body stays within confined, and the first effect that does not.</returns>
    internal (bool Valid, EffectViolation Violation, Koto? Node) SummarizePublishedOperation(BindingSymbol operation)
    {
        var summary = this.effectSummary ??= new(this);
        var valid = summary.CheckPublished(operation, this.types.Published(operation), this.ConstraintScope(operation.Declaration));
        return (valid, summary.Violation, summary.ViolationNode);
    }

    private sealed partial class EffectSummary
    {
        private BindingSymbol? published; // SPEC 4.9.6: the requirement whose effects the checked operation's row publishes.
        private BoundType? publishedCallable; // SPEC 4.9.6: the Callable input whose calls the checked operation's row publishes.

        // The operation's own body with its row's effects covered, which hold only for this check.
        internal bool CheckPublished(BindingSymbol operation, (BindingSymbol? Runs, int On, int Destroys, bool InPlace, int Calls)? row, BindingScope scope)
        {
            this.published = row?.Runs;
            this.publishedCallable = row is { Calls: >= 0 and var calls } ? TyCtxt.Signature(operation, null, calls, out _) : null;
            try
            {
                return this.CheckBody(operation, scope, destructions: false);
            }
            finally
            {
                this.published = null;
                this.publishedCallable = null;
            }
        }
    }
}

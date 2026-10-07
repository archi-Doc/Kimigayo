// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private List<PairBinder>? adaptationBinders;
    private Dictionary<Koto, (Koto Declaration, long Cases, bool LowerBound)>? caseLimits;
    private Dictionary<Koto, Koto>? caseLimitCauses;

    private long AdaptationCaseProduct(Koto operation)
    {
        var binders = this.adaptationBinders ??= new();
        this.PairBinders(operation, binders);
        return SemanticsCaseProduct(binders);
    }

    // Binding must not publish a complete operation plan after checking only a prefix of the body's required cases.
    private BoundType? FailCaseLimit(Koto operation, BindingScope scope, long cases, bool lowerBound = false)
    {
        Koto declaration = scope.Function ?? scope.Owner;
        if (this.caseLimitCauses?.TryGetValue(declaration, out var cause) == true && !ReferenceEquals(cause, operation))
        {
            return this.CompleteDependent(operation, cause);
        }

        if (operation.BindingFailure == BindingFailure.None)
        {
            (this.caseLimitCauses ??= new(ReferenceEqualityComparer.Instance))[declaration] = operation;
        }

        return this.FailExplained(ref this.caseLimits, operation, BindingFailure.CaseLimit, (declaration, cases, lowerBound));
    }

    private void ReportCaseLimit(Koto operation, DiagnosticRequirement requirement)
    {
        var fact = this.caseLimits![operation];
        var span = fact.Declaration is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : fact.Declaration.Span;
        operation.Report(
            requirement,
            DiagnosticCode.OwnershipCaseLimit_Kd,
            fact.Cases,
            (long)OwnershipAnalysis.CaseBound,
            at: fact.Declaration,
            span: span,
            note: fact.LowerBound ? "The case count is a lower bound; checking stopped before an unchecked operation plan could be committed" : null);
    }
}

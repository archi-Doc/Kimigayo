// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Converts retained SPEC 6.4 failures (declaration eligibility, overrides, direct base calls) to diagnostic facts and explanations.
// Inputs are the recorded causes, comparisons and locations (a declaration's eligibility cause, and an override's cause, related
// declarations and compared Types, are its problem's facts; the eligibility cause is published with the rule it breaks);
// outputs retain the original requirement, code and primary span. Never select a slot or repeat an eligibility, signature or premise check.

using System.Diagnostics;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 6.4.1: the closed cause and the rule it breaks, the code's Label; each rule fits DiagnosticLimits.ValueLength, so none is elided.
    private static void ReportVirtualDeclarationFailure(FunctionKoto function, DiagnosticCollection? store, in BindingProblem problem, VirtualDeclarationCause cause)
    {
        var rule = cause switch
        {
            VirtualDeclarationCause.NotStructFunction => "virtual and override require a direct struct instance function",
            VirtualDeclarationCause.OwnGenericParameters => "a virtual slot has no own generic parameters; enclosing Type parameters remain allowed",
            VirtualDeclarationCause.Receiver => "the receiver must be objref/Self or objuniq/Self; bare self means ref/Self",
            VirtualDeclarationCause.NoBody => "a virtual or override function requires a body",
            VirtualDeclarationCause.NotOpen => "an original virtual slot requires an open struct",
            VirtualDeclarationCause.Private => "an original virtual slot cannot be private; omitted accessibility is private",
            VirtualDeclarationCause.RedeclaredConstraints => "an override inherits constraints; do not redeclare them",
            _ => throw new UnreachableException(),
        };
        (string, Koto, string?)[] related = [("declaration", function, "the virtual or override declaration")];
        function.ReportIn(store, problem.Requirement, problem.Code, span: function.DispatchModifierSpan, evidence: [cause, rule], related: related);
    }

    // SPEC 6.4.2-6.4.4: an override problem from its facts (OverrideFacts): the cause and a condition's proof, the original or first
    // candidate and a conflicting declaration, the record's own location, a renamed external name (whose parameter's two names the adapter
    // reads from the two declarations at that position) and a result's two Types.
    private void ReportOverrideFailure(FunctionKoto function, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var (cause, proof) = (OverrideFailureCause.NoBase, ConstraintProof.Proven);
        (Koto? At, FunctionKoto? First, FunctionKoto? Second, SourceSpan? Span) facts = default;
        (BoundType? Expected, BoundType? Actual) types = default;
        foreach (ref readonly var fact in this.report.Facts(index))
        {
            switch (fact.Kind)
            {
                case ProblemFactKind.Reason:
                    (cause, proof) = ((OverrideFailureCause)(byte)fact.Value, (ConstraintProof)(byte)(fact.Value >> 8));
                    break;
                case ProblemFactKind.Related when fact.Value == 0:
                    facts.At = this.compilation.SyntaxOf(fact.Site);
                    break;
                case ProblemFactKind.Related when fact.Value == 1:
                    facts.First = (FunctionKoto)this.compilation.SyntaxOf(fact.Site);
                    break;
                case ProblemFactKind.Related:
                    facts.Second = (FunctionKoto)this.compilation.SyntaxOf(fact.Site);
                    break;
                case ProblemFactKind.Span:
                    facts.Span = new((int)(fact.Value >> 32), (int)fact.Value);
                    break;
                case ProblemFactKind.Expected:
                    types.Expected = this.report.Value(fact);
                    break;
                case ProblemFactKind.Actual:
                    types.Actual = this.report.Value(fact);
                    break;
            }
        }

        var related = facts.First is null ? null : facts.Second is null
            ? new (string, Koto, string?)[] { ("declaration", facts.First, facts.First.IsVirtual ? "original virtual slot" : "inherited function") }
            : [("declaration", facts.First, "original virtual slot"), ("declaration", facts.Second, "conflicting declaration")];
        var parameter = cause == OverrideFailureCause.ExternalName ? NamedParameter(function, facts.Span!.Value) : -1;
        object[]? evidence = cause switch
        {
            OverrideFailureCause.ExternalName => [facts.First!.Parameters[parameter].ExternalName!, function.Parameters[parameter].ExternalName!, cause, parameter],
            OverrideFailureCause.ResultType => [DiagnosticTypeName(types.Expected!), DiagnosticTypeName(types.Actual!), cause],
            OverrideFailureCause.Condition when proof is ConstraintProof.Unknown or ConstraintProof.Refuted => [facts.At!, cause, proof],
            OverrideFailureCause.Condition => null,
            _ => [cause],
        };
        var span = facts.Span ?? (facts.At is null ? function.DispatchModifierSpan : null);
        if (evidence is null)
        {
            function.ReportIn(store, problem.Requirement, DiagnosticCode.InternalInvariant_Kd, at: facts.At, span: span);
            return;
        }

        function.ReportIn(store, problem.Requirement, problem.Code, at: facts.At, span: span, evidence: evidence, related: related, condition: problem.Condition);

        // The parameter whose external name the check recorded (its Span fact): a read of the recorded location, not the comparison again.
        static int NamedParameter(FunctionKoto function, SourceSpan span)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].ExternalNameSpan == span)
                {
                    return i;
                }
            }

            throw new UnreachableException();
        }
    }

    private void ReportBaseCallFailure(BaseReferenceKoto reference, DiagnosticCollection? store, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var cause = this.baseCallFailures[reference];
        var note = cause switch
        {
            BaseCallCause.NotCall => "base has no value; use base.name(arguments)",
            BaseCallCause.NoReceiver => "base requires a derived instance function; named locals have no implicit self",
            BaseCallCause.NoBase => "the lexical declaring struct has no direct base",
            BaseCallCause.OutsideBody => "base is available in an instance body, not a parameter default or declaration header",
            BaseCallCause.NoSelfCapture => "base in an anonymous function requires an explicit self capture at each function boundary",
            BaseCallCause.NotInstanceFunction => "base.name(arguments) must name an instance function; use Type qualification for receiverless functions",
            _ => throw new UnreachableException(),
        };
        reference.ReportIn(store, requirement, code, note: note, evidence: [cause]);
    }
}

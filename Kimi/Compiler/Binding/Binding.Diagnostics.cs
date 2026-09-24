// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Keep proof failures intact. Only diagnostic publication follows these recorded
    // missing-name causes; later validators must still see an invalid declaration.
    private Dictionary<Koto, Koto>? constraintDiagnosticCauses;
    private Dictionary<Koto, BindingSymbol>? objectPayloadCauses;
    private MissingConstraintNameVisitor? missingConstraintNameVisitor;
    private DiagnosticDependencyVisitor? diagnosticDependencyVisitor;
    private Dictionary<Koto, Koto>? diagnosticDependencies;

    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        (this.diagnosticDependencies ??= new(ReferenceEqualityComparer.Instance))[node] = cause;
        return Complete(node, null);
    }

    // This affects publication only: invalid/unresolved state is retained, so a suppressed
    // derivative diagnostic never becomes permission to emit code.
    private bool IsDependentDiagnostic(Koto use)
    {
        if (use.BindingFailure is not (BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported or BindingFailure.TypeMismatch))
        {
            return false;
        }

        var visitor = this.diagnosticDependencyVisitor ??= new(this);
        return visitor.HasCause(use);
    }

    // Called only after both value and Type lookup failed. A tentative Type path must not
    // publish access errors when a valid value path exists in the other namespace.
    private bool ReportUnavailableQualifier(Koto syntax, BindingScope scope)
    {
        if (syntax is GenericsKoto generic)
        {
            return this.ReportUnavailableQualifier(generic.Identifier!, scope);
        }

        if (syntax is not MemberAccessKoto member)
        {
            return false;
        }

        if (this.ReportUnavailableQualifier(member.Left, scope))
        {
            return true;
        }

        if (this.TypeName(member.Left, scope, false) is not { } qualifier || !this.scopes.TryGetValue(qualifier.Declaration, out var members) ||
            TypeSpelling(member.Right) is not { } name || !members.Types.TryGetValue(name, out var first))
        {
            return false;
        }

        for (var candidate = first; candidate is not null; candidate = candidate.Next)
        {
            if (this.Accessible(candidate, scope))
            {
                return false;
            }
        }

        Fail(member, BindingFailure.Access);
        return true;
    }

    /// <summary>Rejects an object form, creation, cast or runtime test over a Type that opts out of ObjectPayload, naming the declaring Type (SPEC 8.4.7.2).</summary>
    private BoundType? FailObjectPayload(Koto use, BindingSymbol renounced)
    {
        (this.objectPayloadCauses ??= new(ReferenceEqualityComparer.Instance))[use] = renounced;
        return Fail(use, BindingFailure.NotObjectPayload);
    }

    private void FailConstraint(Koto use, Koto? diagnosticCause = null)
    {
        if (use.BindingFailure != BindingFailure.None)
        {
            return;
        }

        Fail(use, BindingFailure.InvalidConstraint);
        if (diagnosticCause is null && use is IsKoto clause)
        {
            diagnosticCause = this.FindMissingConformanceName(clause);
        }

        if (diagnosticCause is not null)
        {
            (this.constraintDiagnosticCauses ??= new(ReferenceEqualityComparer.Instance))[use] = diagnosticCause;
        }
    }

    private Koto? ConformanceDiagnosticCause(Koto owner)
        => owner is StructKoto or EnumKoto && owner.BindingFailure == BindingFailure.InvalidConstraint &&
            this.constraintDiagnosticCauses?.TryGetValue(owner, out var cause) == true ? cause : null;

    private Koto? FindConformanceDiagnosticCause(Koto owner, BoundConstraint fact)
    {
        if (fact.Kind == ConstraintKind.Error && owner is DeclarationContainerKoto container)
        {
            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (ReferenceEquals(clause.BoundConstraint, fact) && this.FindMissingConformanceName(clause) is { } cause)
                {
                    return cause;
                }
            }
        }

        return null;
    }

    private Koto? FindMissingConformanceName(IsKoto clause)
    {
        if (clause.Parent is not (StructKoto or EnumKoto) || !IsSelfConstraint(clause) ||
            clause.BoundConstraint is null)
        {
            return null;
        }

        var visitor = this.missingConstraintNameVisitor ??= new();
        visitor.Visit(clause.Right);
        var cause = visitor.Cause;
        visitor.Cause = null;
        return cause;
    }

    private sealed class MissingConstraintNameVisitor : KotoVisitor
    {
        internal Koto? Cause { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Cause is not null)
            {
                return;
            }

            if (node.BoundSymbol is null && node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType)
            {
                this.Cause = node;
                return;
            }

            node.VisitChildren(this);
        }
    }

    private sealed class DiagnosticDependencyVisitor(Binding binding) : KotoVisitor
    {
        private readonly HashSet<Koto> seen = new(ReferenceEqualityComparer.Instance);
        private bool found;

        public override void Visit(Koto node)
        {
            if (this.found || !this.seen.Add(node))
            {
                return;
            }

            if (node.BindingFailure != BindingFailure.None)
            {
                this.found = true;
                return;
            }

            if (binding.diagnosticDependencies?.TryGetValue(node, out var cause) == true)
            {
                this.Visit(cause);
            }

            if (node.BoundSymbol?.Declaration is VariableKoto { InitializerKoto: { } initializer } declaration &&
                declaration.BindingState != BindingState.Resolved)
            {
                this.Visit(initializer);
            }

            node.VisitChildren(this);
        }

        internal bool HasCause(Koto node)
        {
            this.seen.Clear();
            this.seen.Add(node);
            this.found = false;
            node.VisitChildren(this);
            return this.found;
        }
    }
}

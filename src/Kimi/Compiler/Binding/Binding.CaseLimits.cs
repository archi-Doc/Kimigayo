// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Semantics case bound of a definition (SPEC 8.15, 23.1.1), the one place it is decided, and its having no case (8.3.4).
// Input: a function or Property definition whose signature is bound. Output: CaseLimit on the definition beyond PairBinder.CaseBound;
// with no case (a binder admits none, PairBinder.CaseProduct 0), the definition rests on the environment that emptied the set, as beyond
// the bound but without a record of its own, so every pair binder of a published part admits a case; a Semantics requirement read from
// that set (a case adaptation's rows, a follow, a selection through the layer: FollowablePair None) rests on the environment too
// (RestOnEmptiedSet).
// Decided once per definition and pass, before its parts (defaults, base initializer, bodies, initializer, accessor bodies) are bound;
// beyond the bound the definition fails with CaseLimit, only as its first failure. Its parts are still bound, so their independent
// records stay visible (SPEC 23.1.2), but no case of them is verified: a case adaptation in them depends on the failure, and the HIR
// publishes no part of an unresolved definition. Its signature stays valid (KeepsSignature), so its calls are checked normally. A
// definition without parts is never bounded; Closures, default templates and accessors share their definition's binders.
// Must not bound an operation or keep the case count in a table: the report recomputes it.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private List<PairBinder>? caseBinders;

    // A definition's parts: a function's defaults, base initializer and bodies; a Property's initializer and accessor bodies.
    private static bool HasParts(Koto definition)
    {
        if (definition is PropertyKoto property)
        {
            for (var i = 0; i < property.Accessors.Count; i++)
            {
                if (property.Accessors[i].Body is not null)
                {
                    return true;
                }
            }

            return property.InitializerKoto is not null;
        }

        var function = (FunctionKoto)definition;
        return HasParameterDefaults(function) || function.Body is not null || function.ExpressionBody is not null || function.BaseInitializer is not null;
    }

    // The definition whose case bound covers a node: the nearest named function or Property around it.
    private static Koto? Definition(Koto node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto { IsAnonymous: false } or PropertyKoto)
            {
                return current;
            }
        }

        return null;
    }

    // SPEC 8.3.4, 8.15.2: a binder that admits no case gives the definition no case; it rests on the environment whose premises
    // emptied the set (its failure is that set's one record), so its parts are bound but none is analyzed. A definition whose own
    // environment failed has failed itself.
    private void CheckCaseBound(Koto definition)
    {
        if (!HasParts(definition))
        {
            return;
        }

        var count = this.CaseCount(definition);
        if (count == 0)
        {
            if (this.EmptyingEnvironment(this.ConstraintScope(definition)) is not { } failed)
            {
                this.Fail(definition, BindingFailure.Internal);
            }
            else if (!ReferenceEquals(failed, definition))
            {
                this.AddPrerequisite(definition, failed);
                this.Fail(definition, BindingFailure.InvalidConstraint);
            }
        }
        else if (count > PairBinder.CaseBound)
        {
            this.Fail(definition, BindingFailure.CaseLimit);
        }
    }

    // SPEC 8.3.4: a Semantics requirement read from a set that admits no case (a case adaptation's rows, a pair layer's follow or
    // selection) is the Error of the environment that emptied it, its one record: the node rests on it, never a record of its own.
    private BoundType? RestOnEmptiedSet(Koto node, BindingScope scope)
        => this.EmptyingEnvironment(scope) is { } failed ? this.CompleteDependent(node, failed) : this.Fail(node, BindingFailure.Internal);

    // The owner of the failed environment on a scope chain: a set its premises empty is an Error proof, which fails that environment
    // (ValidateConstraintEnvironments), so a chain that admits no case has one; null is a broken contract.
    private Koto? EmptyingEnvironment(BindingScope? scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Failed: { } failed })
            {
                return failed;
            }
        }

        return null;
    }

    // The Semantics cases of a body: the product of the admitted sets of the binders in its scope, 0 when one admits none.
    private long CaseCount(Koto body)
    {
        var binders = this.caseBinders ??= new();
        this.PairBinders(body, binders);
        return PairBinder.CaseProduct(binders);
    }

    private void ReportCaseLimit(Koto definition, DiagnosticRequirement requirement)
    {
        var span = definition is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : definition.Span;
        definition.Report(requirement, DiagnosticCode.OwnershipCaseLimit_Kd, this.CaseCount(definition), (long)PairBinder.CaseBound, span: span);
    }
}

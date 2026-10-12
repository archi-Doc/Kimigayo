// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Semantics case bound of a definition (SPEC 8.10, 23.3.6.1), the one place it is decided.
// Input: a function or Property definition whose signature is bound. Output: CaseLimit on the definition beyond PairBinder.CaseBound.
// Decided once per definition and pass, before its parts (defaults, base initializer, bodies, initializer, accessor bodies) are bound;
// beyond the bound the definition fails with CaseLimit, only as its first failure. Its parts are still bound, so their independent
// records stay visible (SPEC 23.3.6.4), but no case of them is verified: a case adaptation in them depends on the failure, and the HIR
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

    private void CheckCaseBound(Koto definition)
    {
        if (HasParts(definition) && this.CaseCount(definition) > PairBinder.CaseBound)
        {
            this.Fail(definition, BindingFailure.CaseLimit);
        }
    }

    // The Semantics cases of a body: the product of the admitted sets of the resolved binders in its scope.
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

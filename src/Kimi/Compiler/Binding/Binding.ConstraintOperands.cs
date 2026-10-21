// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the facts of a constraint's operand Types: whether a part of an operand names a declaration whose context failed (Invalid) or is not
// yet resolved (Unresolved), which a proof reads before it judges the proposition (JudgeConstraintAtom, the capability requests), and
// whether a part is an associated projection (an interned constraint's HasAssociatedProjection). Each walk visits a distinct part once
// per query (DECISIONS BU-19). Reads binding states only; never binds or reports.
public sealed partial class Binding
{
    private static bool InvalidDeclarationContext(Koto declaration)
        => InvalidDeclarationContextCause(declaration) is not null;

    // The nearest enclosing declaration (or conditional conformance) that failed; a member's check in that context rests on it.
    // A declaration whose only failure keeps its signature valid (KeepsSignature: a form rule or the case bound) is no such context.
    private static Koto? InvalidDeclarationContextCause(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is DeclarationKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance }) && node.BindingState == BindingState.Invalid &&
                !KeepsSignature(node))
            {
                return node;
            }
        }

        return null;
    }

    private static bool UnresolvedTypeDeclarationContext(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is StructKoto or EnumKoto or ContractKoto && node.BindingState == BindingState.Unresolved) || node is ContractKoto { BoundSymbol.Contract.HasUnresolvedParents: true })
            {
                return true;
            }
        }

        return false;
    }

    // The declaration contexts a Type's parts name: whether one is in a failed context and whether one is not yet resolved, each distinct
    // part visited once (DECISIONS BU-19).
    private (bool Invalid, bool Unresolved) OperandContexts(BoundType type)
    {
        var found = (Invalid: false, Unresolved: false);
        this.OperandContexts(type, this.visitedParts.Begin(this.storageVersion), ref found);
        return found;
    }

    // An atom's operand contexts: its subject's and required Type's, and its Contract's own declaration.
    private (bool Invalid, bool Unresolved) OperandContexts(BoundConstraint proposition)
    {
        var subject = proposition.Subject is { } type ? this.OperandContexts(type) : default;
        var required = proposition.RequiredType is { } requiredType ? this.OperandContexts(requiredType) : default;
        var contract = proposition.Contract?.Declaration;
        return (subject.Invalid || required.Invalid || (contract is not null && InvalidDeclarationContext(contract)),
            subject.Unresolved || required.Unresolved || (contract is not null && UnresolvedTypeDeclarationContext(contract)));
    }

    // Whether a requirement clause rests on a failed context: its Contract's, a required Type's or a nested clause's.
    private bool InvalidConstraintRequirement(BoundConstraint constraint)
        => (constraint.Contract is { } contract && InvalidDeclarationContext(contract.Declaration)) ||
        (constraint.RequiredType is { } required && this.OperandContexts(required).Invalid) ||
        (constraint.Left is { } left && this.InvalidConstraintRequirement(left)) ||
        (constraint.Right is { } right && this.InvalidConstraintRequirement(right));

    // Whether a part of a Type is an associated projection (BoundConstraint.HasAssociatedProjection), each distinct part visited once; a
    // projection is abstract, so a part without an abstract part has none.
    private bool ContainsAssociatedProjection(BoundType? type)
        => type is { HasAbstractPart: true } && this.ProjectionPart(type, this.visitedParts.Begin(this.storageVersion));

    private void OperandContexts(BoundType type, uint query, ref (bool Invalid, bool Unresolved) found)
    {
        if (type.Symbol is { } symbol)
        {
            found.Invalid |= InvalidDeclarationContext(symbol.Declaration);
            found.Unresolved |= UnresolvedTypeDeclarationContext(symbol.Declaration);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query))
            {
                this.OperandContexts(type.Components[i], query, ref found);
            }
        }
    }

    private bool ProjectionPart(BoundType type, uint query)
    {
        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = type.Components[i];
            if (part.HasAbstractPart && this.FirstVisit(part, query) && this.ProjectionPart(part, query))
            {
                return true;
            }
        }

        return false;
    }

    // Whether an existence walk's query meets a part for the first time; a leaf is cheaper to answer again than to record.
    private bool FirstVisit(BoundType part, uint query)
    {
        if (part.Components.Count == 0)
        {
            return true;
        }

        if (this.visitedParts.TryGet(part, query, out _))
        {
            return false;
        }

        this.visitedParts.Set(part, query, true);
        return true;
    }
}

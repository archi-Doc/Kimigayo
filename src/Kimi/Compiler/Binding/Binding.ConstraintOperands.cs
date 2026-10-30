// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the facts of a constraint's operand Types: whether a part of an operand names a declaration whose context failed (Invalid) or is not
// yet resolved (Unresolved), which a proof reads before it judges the proposition (JudgeConstraintAtom, the capability requests), and
// whether a part is an associated projection (an interned constraint's HasAssociatedProjection). Each walk visits a distinct part once
// per query (DECISIONS BU-19). Reads binding states and their retained part causes; never binds or reports.
public sealed partial class Binding
{
    private static bool UnresolvedTypeDeclarationContext(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is StructKoto or EnumKoto or ContractKoto && node.BindingState == BindingState.Unresolved) ||
                node is ContractKoto { BoundSymbol.Contract.HasUnresolvedParents: true })
            {
                return true;
            }
        }

        return false;
    }

    private bool InvalidDeclarationContext(Koto declaration)
        => this.InvalidDeclarationContextCause(declaration) is not null;

    // The nearest enclosing declaration (or conditional conformance) that failed; a member's check in that context rests on it.
    // An independent failure can keep the signature valid; a failed declaration part still rejects that context.
    private Koto? InvalidDeclarationContextCause(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is DeclarationKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance }) && this.InvalidDeclarationCause(node) is { } cause)
            {
                return cause;
            }
        }

        return null;
    }

    // The failure of the first declaration a node's own selection calls (CallPlan.Called) that failed or lies in a failed context.
    private Koto? InvalidCalledDeclaration(Koto node)
    {
        for (var i = 0; CallPlan.Called(node, i) is { } called; i++)
        {
            if (this.InvalidDeclarationContextCause(called) is { } cause)
            {
                return cause;
            }
        }

        return null;
    }

    private Koto? InvalidDeclarationCause(Koto declaration)
    {
        if (declaration.BindingState != BindingState.Invalid)
        {
            return null;
        }

        return this.UnresolvedPart(declaration) ?? (KeepsSignature(declaration) ? null : declaration);
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
        return (subject.Invalid || required.Invalid || (contract is not null && this.InvalidDeclarationContext(contract)),
            subject.Unresolved || required.Unresolved || (contract is not null && UnresolvedTypeDeclarationContext(contract)));
    }

    // Whether a requirement clause rests on a failed context: its Contract's, a required Type's or a nested clause's.
    private bool InvalidConstraintRequirement(BoundConstraint constraint)
        => (constraint.Contract is { } contract && this.InvalidDeclarationContext(contract.Declaration)) ||
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
            found.Invalid |= this.InvalidDeclarationContext(symbol.Declaration);
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

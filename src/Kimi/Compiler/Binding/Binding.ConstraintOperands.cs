// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the declaration contexts of a constraint's operand Types: whether a part of an operand names a declaration whose context failed
// (Invalid) or is not yet resolved (Unresolved), which a proof reads before it judges the proposition (JudgeConstraintAtom, the
// capability requests). Reads binding states only; never binds or reports.
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

    private static bool InvalidConstraintType(BoundType type)
    {
        if (type.Symbol is { } symbol && InvalidDeclarationContext(symbol.Declaration))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (InvalidConstraintType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnresolvedConstraintType(BoundType type)
    {
        if (type.Symbol is { } symbol && UnresolvedTypeDeclarationContext(symbol.Declaration))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (UnresolvedConstraintType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnresolvedConstraintOperand(BoundConstraint constraint)
        => (constraint.Subject is { } subject && UnresolvedConstraintType(subject)) ||
        (constraint.RequiredType is { } required && UnresolvedConstraintType(required)) ||
        (constraint.Contract is { } contract && UnresolvedTypeDeclarationContext(contract.Declaration));
}

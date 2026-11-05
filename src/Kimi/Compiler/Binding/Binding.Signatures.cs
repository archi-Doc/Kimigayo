// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Signatures: a declaration's header and the signature rules of a member group.
// - Responsibility: BindHeader binds a function's, property's or annotated variable's header when first demanded (BindDeclarations'
//   header loop forces the rest), once per pass, except while a pending associated Type keeps a function's header open:
//   BindFunctionHeader then leaves it unbound, and a later BindHeader binds it again. A demand while the header is being bound returns,
//   and an anonymous function's header belongs to its expression context. ValidateSignatures checks a member group's one receiver
//   shape (SPEC 7.3), its duplicate overloads (SPEC 7.4, by SignatureEquals) and a Contract's same-name requirements with those they
//   inherit (SPEC 8.4.2).
// - Inputs: the declared symbols and scopes; the bound parameter Types. Outputs: each symbol's header state and Type, and the
//   ReceiverShapeMismatch and Duplicate failures; the parameter-shape checks are Binding.ParameterShapes.cs's.
// - Invariants: a header is its declaration's, never the body that demands it (BindHeader binds it outside any body).
// - Prohibitions: no call is selected and no overload ranked here; ValidateSignatures only fails declarations.
public sealed partial class Binding
{
    /// <summary>Gets the receiver shape of a function with a receiver (SPEC 7.3): its receiver's Semantics, or null.</summary>
    private static SemanticsKind? ReceiverShape(BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 && symbol.Declaration is FunctionKoto { IsSpecialization: false } function &&
            function.Parameters[symbol.ReceiverIndex].Type.BoundType is { } receiver ? receiver.Semantics : null;

    private static bool SignatureSlotEquals(BindingSymbol? a, BindingSymbol? b, Koto aBinder, Koto bBinder)
        => ReferenceEquals(a, b) || (a is not null && b is not null && ReferenceEquals(a.Scope.Owner, aBinder) && ReferenceEquals(b.Scope.Owner, bBinder) && a.Slot == b.Slot);

    private static bool SignatureEquals(BoundType a, BoundType b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind != b.Kind || a.Semantics != b.Semantics || a.Length != b.Length || !SameLengthSignature(a.LengthExpression, b.LengthExpression, aBinder, bBinder) || a.Components.Count != b.Components.Count || a.LengthArguments.Length != b.LengthArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < a.LengthArguments.Length; i++)
        {
            if (!SameLengthSignature(a.LengthArguments[i], b.LengthArguments[i], aBinder, bBinder))
            {
                return false;
            }
        }

        if (a.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            return SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder);
        }

        if (a.Components.Count == 0)
        {
            return a.Symbol is not null && ReferenceEquals(a.Symbol, b.Symbol);
        }

        if (a.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ? !SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder) : a.Symbol != b.Symbol)
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!SignatureEquals(a.Components[i], b.Components[i], aBinder, bBinder))
            {
                return false;
            }
        }

        return true;
    }

    private void BindHeader(BindingSymbol symbol)
    {
        if (symbol.HeaderBound || symbol.Resolving || symbol.Declaration is FunctionKoto { IsAnonymous: true })
        {
            // An anonymous header belongs to its fixed expression context, including on a later binding pass.
            return;
        }

        // A header is its declaration's, never the body that binds it lazily (SPEC 15.6.1; DECISIONS R4-M101 E3).
        var saved = this.EnterBody(default);
        try
        {
            symbol.Resolving = true;
            if (symbol.Declaration is FunctionKoto function)
            {
                if (!this.BindFunctionHeader(symbol, function))
                {
                    symbol.Resolving = false;
                    return;
                }
            }
            else if (symbol.Property is { } property)
            {
                this.BindPropertyHeader(property);
            }
            else if (symbol.Declaration is VariableKoto variable && variable.TypeKoto is { } type)
            {
                symbol.Type = this.BindType(type, symbol.Scope);
            }

            symbol.HeaderBound = true;
            symbol.Resolving = false;
        }
        finally
        {
            this.formingBody = saved;
        }
    }

    private void ValidateSignatures()
    {
        foreach (var scope in this.scopes.Values)
        {
            foreach (var first in scope.Values.Values)
            {
                // SPEC 7.4: the functions of one Name acquire corresponding parameters of overlapping Types in one mode; a Contract's
                // requirements are checked below with the ones they inherit.
                if (scope.Owner is not ContractKoto)
                {
                    this.ValidateParameterShapes(first);
                }

                for (var a = first; a is not null; a = a.Next)
                {
                    if (a.Declaration is not FunctionKoto fa || fa.IsSpecialization)
                    {
                        continue;
                    }

                    // SPEC 7.3: every function with a receiver in one member group shares one receiver shape.
                    if (ReceiverShape(a) is { } shape)
                    {
                        for (var b = a.Next; b is not null; b = b.Next)
                        {
                            if (ReceiverShape(b) is { } other && other != shape)
                            {
                                this.Fail(fa, BindingFailure.ReceiverShapeMismatch);
                                this.Fail(b.Declaration, BindingFailure.ReceiverShapeMismatch);
                            }
                        }
                    }

                    for (var b = a.Next; b is not null; b = b.Next)
                    {
                        if (b.Declaration is not FunctionKoto fb || fb.IsSpecialization || fa.GenericArguments.Count != fb.GenericArguments.Count || fa.Parameters.Count != fb.Parameters.Count)
                        {
                            continue;
                        }

                        var equal = true;
                        for (var i = 0; i < fa.Parameters.Count; i++)
                        {
                            var ta = fa.Parameters[i].Type.BoundType;
                            var tb = fb.Parameters[i].Type.BoundType;
                            if (ta is null || tb is null || !SignatureEquals(ta, tb, fa, fb))
                            {
                                equal = false;
                                break;
                            }
                        }

                        if (equal)
                        {
                            this.FailDuplicate(fa, fb);
                        }
                    }
                }
            }
        }

        // SPEC 8.4.2: a Contract's same-name requirements, including those inherited by refinement, share one receiver shape.
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not ContractKoto { BoundSymbol.Contract: { } shape } contract)
            {
                continue;
            }

            foreach (var members in shape.MembersByName.Values)
            {
                this.ValidateContractParameterShapes(contract, members);
                SemanticsKind? expected = null;
                for (var i = 0; i < members.Count; i++)
                {
                    if (ReceiverShape(members[i].Symbol) is not { } current)
                    {
                        continue;
                    }

                    if (expected is null)
                    {
                        expected = current;
                    }
                    else if (expected != current)
                    {
                        this.Fail(contract, BindingFailure.ReceiverShapeMismatch);
                        if (ReferenceEquals(members[i].Symbol.Declaration.Parent, contract))
                        {
                            this.Fail(members[i].Symbol.Declaration, BindingFailure.ReceiverShapeMismatch);
                        }
                    }
                }
            }
        }
    }
}

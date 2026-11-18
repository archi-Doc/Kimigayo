// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Signatures: a declaration's header, its declared signature (FnSig) and the signature rules of a member group.
// - Responsibility: BindHeader, the owner of Binding.DeclQuery's Signature kind, binds a function's, property's or annotated variable's
//   header when first demanded (BindDeclarations' header loop forces the rest), once per pass, except while a pending associated Type
//   keeps a function's header open: BindFunctionHeader then leaves it unbound (AbandonQuery), and a later BindHeader binds it again. A
//   demand while the header is being bound returns, and an anonymous function's header belongs to its expression context.
//   FnSig is the one read of a callable's declared signature (SPEC 9.1: generic count and ordered parameter Types, the receiver
//   included; with its result and binder): a view over what the header published, read by every signature comparison.
//   ValidateSignatures checks a member group's one receiver shape (SPEC 7.3), its duplicate overloads (SPEC 9.1, by SignatureEquals)
//   and a Contract's same-name requirements with those they inherit (SPEC 8.4.2).
// - Inputs: the declared symbols and scopes; the bound parameter Types. Outputs: each header's Type, its Signature state (BeginQuery,
//   CompleteQuery, AbandonQuery), FnSig, and the ReceiverShapeMismatch and Duplicate failures; the parameter-shape checks are
//   Binding.ParameterShapes.cs's.
// - Invariants: a header is its declaration's, never the body that demands it (BindHeader binds it outside any body). FnSig copies
//   nothing: a function's parameter Types are the ones BindType published on its parameters (each as soon as it is bound, also while a
//   pending associated Type keeps the header open), its result the symbol's Type, its receiver index the declaration's `self`
//   parameter (Binding.Indexing); an accessor's are its BoundAccessor's (BindPropertyHeader).
// - Prohibitions: no call is selected and no overload ranked here; ValidateSignatures only fails declarations; no signature
//   comparison reads a function's parameter Types, receiver index, result or generic count other than through FnSig.
public sealed partial class Binding
{
    /// <summary>Gets the receiver shape of a function with a receiver (SPEC 7.3): its receiver's Semantics, or null.</summary>
    private static SemanticsKind? ReceiverShape(BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 && symbol.Declaration is FunctionKoto { IsSpecialization: false } function &&
            new FnSig(function).Input(symbol.ReceiverIndex) is { } receiver ? receiver.Semantics : null;

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
        // Resolving is the variable binding's own Running flag (interim until R7-D2 d4, Binding.DeclQuery).
        if (symbol.Declaration is FunctionKoto { IsAnonymous: true } || symbol.Resolving ||
            this.BeginQuery(symbol, DeclQueryKind.Signature) != DeclState.NotStarted)
        {
            // An anonymous header belongs to its fixed expression context, including on a later binding pass.
            return;
        }

        // A header is its declaration's, never the body that binds it lazily (SPEC 15.6.1; DECISIONS R4-M101 E3).
        var saved = this.EnterBody(default);
        try
        {
            if (symbol.Declaration is FunctionKoto function)
            {
                if (!this.BindFunctionHeader(symbol, function))
                {
                    this.AbandonQuery(symbol, DeclQueryKind.Signature);
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

            this.CompleteQuery(symbol, DeclQueryKind.Signature);
        }
        finally
        {
            this.formingBody = saved;
        }
    }

    private void ValidateSignatures()
    {
        foreach (var scope in this.scopes.Values.In(this.pass))
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

                    var sa = new FnSig(fa);

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
                        if (b.Declaration is not FunctionKoto { IsSpecialization: false } fb)
                        {
                            continue;
                        }

                        var sb = new FnSig(fb);
                        if (sa.GenericCount != sb.GenericCount || sa.InputCount != sb.InputCount)
                        {
                            continue;
                        }

                        var equal = true;
                        for (var i = 0; i < sa.InputCount; i++)
                        {
                            var ta = sa.Input(i);
                            var tb = sb.Input(i);
                            if (ta is null || tb is null || !SignatureEquals(ta, tb, sa.Binder, sb.Binder))
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

    // A callable's declared signature (SPEC 9.1, 8.6.3): a view over a function's or an accessor's published header (no temporary
    // FunctionKoto or copied parameter lists). An accessor has no generic parameter and two inputs, its receiver and its value input.
    private readonly struct FnSig
    {
        private readonly FunctionKoto? function;
        private readonly BoundAccessor? accessor;

        internal FnSig(FunctionKoto function) => this.function = function;

        internal FnSig(BoundAccessor accessor) => this.accessor = accessor;

        internal Koto Binder => (Koto?)this.function ?? this.accessor!.Binder;

        internal int GenericCount => this.function?.GenericArguments.Count ?? 0;

        internal int OriginCount => this.function?.Origins.Count ?? this.accessor?.Declaration?.Origins.Count ?? 0;

        internal int InputCount => this.function?.Parameters.Count ?? 2;

        internal int ReceiverIndex => this.function is { } f ? f.BoundSymbol!.ReceiverIndex : this.accessor!.Receiver is null ? -1 : 0;

        internal BoundType? Result => this.function is { } f ? f.BoundSymbol?.Type : this.accessor!.Result;

        internal BoundType? Input(int index) => this.function is { } f ? f.Parameters[index].Type.BoundType : index == 0 ? this.accessor!.Receiver : this.accessor!.Input;
    }
}

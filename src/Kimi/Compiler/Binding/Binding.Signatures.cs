// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Signatures: a declaration's header, its declared signature (FnSig) and the signature rules of a member group.
// - Responsibility: BindHeader, the owner of Binding.DeclQuery's Signature kind, binds a function's, property's or annotated variable's
//   header when first demanded (BindDeclarations' header loop forces the rest), once per pass, except while a pending associated Type
//   keeps a function's header open: BindFunctionHeader then leaves it unbound (AbandonQuery), and a later BindHeader binds it again
//   (never while an associated-inference request is open: its runs read the header bound before it, Binding.AssociatedInference.Demand). A
//   demand while the header is being bound returns, and an anonymous function's header belongs to its expression context.
//   FnSig is the one read of a callable's declared signature (SPEC 9.1: generic count and ordered parameter Types, the receiver
//   included; with its result, Origin count and binder): a view over what the header published, read by every signature comparison,
//   the 7.3 receiver shape and the 7.4 checker's correspondence (Binding.ParameterShapes) included; the generic slots' kinds are the
//   bound Schema's (SameGenericShape) and the syntax's (SameExplicitArgumentShape, SameSlotKinds), read once their counts compared equal.
//   ValidateSignatures checks a member group's one receiver shape (SPEC 7.3), its duplicate overloads (SPEC 9.1, by SignatureEquals,
//   only between functions of one SignatureHash: a hash per function, so the check is linear in a group whose Signatures differ within
//   the top two layers of their parameter Types; Signatures that differ only deeper share a hash and are compared pairwise)
//   and a Contract's same-name requirements with those they inherit (SPEC 8.4.2).
// - Inputs: the declared symbols and scopes; the bound parameter Types. Outputs: each header's Type, its Signature state (BeginQuery,
//   CompleteQuery, AbandonQuery; FailQuery and the declaration's CyclicBinding_Kd by the kind's re-entry rule, ReenterHeader), FnSig,
//   and the ReceiverShapeMismatch and Duplicate failures; the parameter-shape checks are Binding.ParameterShapes.cs's.
// - Invariants: a header is its declaration's, never the body that demands it (BindHeader binds it outside any body). FnSig copies
//   nothing: a function's parameter Types are its parameter symbols' (BindFunctionHeader's: the Types BindType published on its
//   parameters, BindType's partial value while a pending associated Type keeps the header open), its result the symbol's Type (likewise),
//   its receiver index the declaration's `self` parameter (Binding.Indexing); an accessor's are its BoundAccessor's (BindPropertyHeader).
// - Prohibitions: no call is selected and no overload ranked here; ValidateSignatures only fails declarations; no signature
//   comparison reads a function's parameter Types, receiver index, result or generic count other than through FnSig; SignatureHash
//   reads nothing SignatureEquals does not compare, and both classify a Type's kind only by SignaturePartsCompared and
//   SignatureSymbolBySlot, so two functions of one Signature never have different hashes.
public sealed partial class Binding
{
    // SPEC 9.1: the member group being checked, its functions in chain order, each with its SignatureHash (null: an unbound parameter Type)
    // and the index of the next later function of that hash (-1: none). Cleared after the check, so no group stays alive between passes;
    // its capacity is reused, so a warm rebind allocates nothing.
    private readonly List<(BindingSymbol Symbol, FunctionKoto Function, int? Hash, int SameHash)> signatureGroup = [];

    // SignatureGroup's scratch: each hash's latest function in the group being linked; empty between groups (each removes its own).
    private readonly Dictionary<int, int> signatureHashes = [];

    // The hashes and comparisons of this run's duplicate check (SPEC 9.1).
    private int duplicateSteps;

    /// <summary>Gets the hashes and comparisons of the latest run's duplicate check (SPEC 9.1), which a scaling check reads.</summary>
    internal int DuplicateCheckWork => this.duplicateSteps;

    /// <summary>Gets the receiver shape of a function with a receiver (SPEC 7.3): its receiver's Semantics, or null.</summary>
    private static SemanticsKind? ReceiverShape(BindingSymbol symbol)
        => symbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto { IsSpecialization: false } function } &&
            new FnSig(function) is { ReceiverIndex: >= 0 } signature && signature.Input(signature.ReceiverIndex) is { } receiver ? receiver.Semantics : null;

    private static bool SignatureSlotEquals(BindingSymbol? a, BindingSymbol? b, Koto aBinder, Koto bBinder)
        => ReferenceEquals(a, b) || (a is not null && b is not null && ReferenceEquals(a.Scope.Owner, aBinder) && ReferenceEquals(b.Scope.Owner, bBinder) && a.Slot == b.Slot);

    // SPEC 9.1: whether SignatureEquals compares a Type's parts; a generic parameter or a target projection is its slot alone.
    private static bool SignaturePartsCompared(BoundTypeKind kind) => kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection);

    // SPEC 9.1: whether SignatureEquals compares a Type's symbol by slot number under the two binders (SignatureSlotEquals), not by identity.
    private static bool SignatureSymbolBySlot(BoundTypeKind kind)
        => !SignaturePartsCompared(kind) || kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation;

    // SPEC 9.1: a hash of a function's declared Signature (its generic count and ordered parameter Types), or null when a parameter Type
    // is unbound (such a Signature equals no other). Equal Signatures have equal hashes: it reads, of each parameter Type and of its
    // direct parts where SignaturePartsCompared, only what SignatureEquals compares before any part (SignatureLayer).
    private static int? SignatureHash(FnSig signature)
    {
        var hash = HashCode.Combine(signature.GenericCount, signature.InputCount);
        for (var i = 0; i < signature.InputCount; i++)
        {
            if (signature.Input(i) is not { } input)
            {
                return null;
            }

            hash = HashCode.Combine(hash, SignatureLayer(input));
            var parts = SignaturePartsCompared(input.Kind) ? input.Components.Count : 0;
            for (var p = 0; p < parts; p++)
            {
                hash = HashCode.Combine(hash, SignatureLayer(input.Components[p]));
            }
        }

        return hash;
    }

    // What SignatureEquals compares of a Type before its parts: Kind, Semantics, Length, the part count and the symbol, by slot number
    // where SignatureSymbolBySlot and by identity elsewhere.
    private static int SignatureLayer(BoundType type)
        => HashCode.Combine(type.Kind, type.Semantics, type.Length, type.Components.Count, SignatureSymbolBySlot(type.Kind) ? type.Symbol?.Slot ?? -1 : RuntimeHelpers.GetHashCode(type.Symbol));

    // SPEC 9.1: whether two Types are one under their binders (`a` under `aBinder`, `b` under `bBinder`): the same layer
    // (SameSignatureLayer) and, where SignaturePartsCompared, the same parts.
    private static bool SignatureEquals(BoundType a, BoundType b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (!SameSignatureLayer(a, b, aBinder, bBinder))
        {
            return false;
        }

        for (var i = 0; SignaturePartsCompared(a.Kind) && i < a.Components.Count; i++)
        {
            if (!SignatureEquals(a.Components[i], b.Components[i], aBinder, bBinder))
            {
                return false;
            }
        }

        return true;
    }

    // What SignatureEquals compares at one node before its parts (SignatureLayer's fields, the lengths under the binders, and the symbol:
    // by slot number where SignatureSymbolBySlot, by identity elsewhere; a Type without compared parts is decided here).
    private static bool SameSignatureLayer(BoundType a, BoundType b, Koto aBinder, Koto bBinder)
    {
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

        if (!SignaturePartsCompared(a.Kind))
        {
            return SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder);
        }

        if (a.Components.Count == 0)
        {
            return a.Symbol is not null && ReferenceEquals(a.Symbol, b.Symbol);
        }

        return SignatureSymbolBySlot(a.Kind) ? SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder) : a.Symbol == b.Symbol;
    }

    // SPEC 8.6.3, 8.5.5 (R7-C2e2 e2-5b, design R1.1, R2.3, R2.10 N2 and R3.1): the identification's relation of a requirement's Type `a`
    // with an implementation's `b` in `scope`, SignatureEquals' walk in three values. At each node a pair identical under the binders
    // (SignatureEquals) is Proven and not descended; a pending projection on either side (PendingNode: held, or no Type decides it yet)
    // is Unknown and binds nothing; a differing layer is Refuted; the parts are conjoined (CombineProof), so a mismatched sibling of a
    // pending part refutes the pair. Read by MatchesRequirement only; SignatureEquals stays the identity its other readers ask.
    private ConstraintProof SignatureRelation(BoundType a, BoundType b, Koto aBinder, Koto bBinder, BindingScope scope, QuerySite site)
    {
        if (SignatureEquals(a, b, aBinder, bBinder))
        {
            return ConstraintProof.Proven;
        }

        if (this.PendingNode(a, scope, site) || this.PendingNode(b, scope, site))
        {
            return ConstraintProof.Unknown;
        }

        if (!SameSignatureLayer(a, b, aBinder, bBinder))
        {
            return ConstraintProof.Refuted;
        }

        var proof = ConstraintProof.Proven;
        for (var i = 0; SignaturePartsCompared(a.Kind) && i < a.Components.Count && proof != ConstraintProof.Refuted; i++)
        {
            proof = CombineProof(proof, this.SignatureRelation(a.Components[i], b.Components[i], aBinder, bBinder, scope, site), true);
        }

        return proof;
    }

    // SignatureRelation's pending node: a projection on a receiver with no abstract part whose specification answers pending
    // (ProjectionOutcome.Pending: held, or not yet inferred). A projection on an abstract receiver is pending as itself (8.6.1), a fact
    // of its form that the relation's layers decide, so it is no pending node.
    private bool PendingNode(BoundType type, BindingScope scope, QuerySite site)
        => type.Kind == BoundTypeKind.AssociatedProjection && !type.Components[0].HasAbstractPart && this.ProjectionSpecification(type, scope, site).Pending;

    private void BindHeader(BindingSymbol symbol)
    {
        // Resolving is the variable binding's own Running flag (interim until R7-D2 d4, Binding.DeclQuery). While an associated-inference
        // request is open no header is bound: its runs read the headers bound before it (R7-C4 design R4, ruling 6 (ii)).
        if (this.inferenceRequest >= 0 || symbol.Declaration is FunctionKoto { IsAnonymous: true } || symbol.Resolving)
        {
            // An anonymous header belongs to its fixed expression context, including on a later binding pass.
            return;
        }

        switch (this.BeginQuery(symbol, DeclQueryKind.Signature))
        {
            case DeclState.Running:
                this.ReenterHeader(symbol);
                return;
            case not DeclState.NotStarted:
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

    // The Signature kind's re-entry rule (Binding.DeclQuery's row; SPEC 11.6 "Cycles between inferred initializers are errors", SPEC 9):
    // a demand of a Property's header while it is computed and its Type is not yet formed is CyclicBinding_Kd at the declaration, and the
    // query fails (Error), so a use of the Property in the pass, in the computation the re-entry interrupted too, rests on that record. The
    // cycle is recorded once per pass, at the header that started first (DECISIONS BU-105). Any other header's re-entry reads its output
    // as far as formed and records nothing (SPEC 10.5, 10.8).
    private void ReenterHeader(BindingSymbol symbol)
    {
        if (symbol.Property is not null && symbol.Type is null)
        {
            this.Fail(symbol.Declaration, BindingFailure.Cycle, true);
            this.FailQuery(symbol, DeclQueryKind.Signature);
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

                var group = this.SignatureGroup(first);
                foreach (var (a, fa, _, sameHash) in group)
                {
                    var sa = new FnSig(fa);

                    // SPEC 7.3: every function with a receiver in one member group shares one receiver shape.
                    if (ReceiverShape(a) is { } shape)
                    {
                        for (var b = a.Next; b is not null; b = b.Next)
                        {
                            if (ReceiverShape(b) is { } other && other != shape)
                            {
                                // SPEC 23.1.2: a declaration that already failed, such as one whose receiver forms no Type, keeps its first
                                // failure, the input this mismatch rests on.
                                this.Fail(fa, BindingFailure.ReceiverShapeMismatch);
                                this.Fail(b.Declaration, BindingFailure.ReceiverShapeMismatch);
                            }
                        }
                    }

                    // SPEC 9.1: a later function of the same Signature has the same SignatureHash; only those are compared, in chain order.
                    for (var k = sameHash; k >= 0; k = group[k].SameHash)
                    {
                        this.duplicateSteps++;
                        var fb = group[k].Function;
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

        this.signatureGroup.Clear();

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
                        // SPEC 23.1.2: as a member group's mismatch, a requirement that already failed keeps its first failure, the input
                        // this mismatch rests on.
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

    // SPEC 9.1: a member group's functions in chain order, each linked to the next later function of the same SignatureHash; a function
    // with an unbound parameter Type is linked to none. The hashes' scratch is emptied by removing the group's own hashes: a Clear would
    // cost the widest group's capacity again for every later group.
    private List<(BindingSymbol Symbol, FunctionKoto Function, int? Hash, int SameHash)> SignatureGroup(BindingSymbol first)
    {
        var group = this.signatureGroup;
        var latest = this.signatureHashes;
        group.Clear();
        for (var symbol = first; symbol is not null; symbol = symbol.Next)
        {
            if (symbol.Declaration is not FunctionKoto { IsSpecialization: false } function)
            {
                continue;
            }

            this.duplicateSteps++;
            var hash = SignatureHash(new FnSig(function));
            if (hash is { } key)
            {
                if (latest.TryGetValue(key, out var last))
                {
                    group[last] = group[last] with { SameHash = group.Count };
                }

                latest[key] = group.Count;
            }

            group.Add((symbol, function, hash, -1));
        }

        foreach (var row in group)
        {
            if (row.Hash is { } key)
            {
                latest.Remove(key);
            }
        }

        return group;
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

        // The function or the accessor viewed, for the signature's parts as written (WrittenSignaturePart).
        internal FunctionKoto? Function => this.function;

        internal BoundAccessor? Accessor => this.accessor;

        internal int GenericCount => this.function?.GenericArguments.Count ?? 0;

        internal int OriginCount => this.function?.Origins.Count ?? this.accessor?.Declaration?.Origins.Count ?? 0;

        internal int InputCount => this.function?.Parameters.Count ?? 2;

        internal int ReceiverIndex => this.function is { } f ? f.BoundSymbol!.ReceiverIndex : this.accessor!.Receiver is null ? -1 : 0;

        internal BoundType? Result => this.function is { } f ? f.BoundSymbol?.Type : this.accessor!.Result;

        // A function's input is its parameter symbol's Type (BindFunctionHeader's): the Type published on the parameter, or while a pending
        // associated Type keeps the header open its partial value, as Result reads the symbol's (R7-C4 design R4: a run reads the header
        // bound before its request).
        internal BoundType? Input(int index) => this.function is { } f ? DeclaredSymbols.Parameter(f, index).Type : index == 0 ? this.accessor!.Receiver : this.accessor!.Input;
    }
}

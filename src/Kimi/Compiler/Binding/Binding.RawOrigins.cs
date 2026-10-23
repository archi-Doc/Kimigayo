// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, R4-M105): the Origin a raw layer states, BoundType.RawOrigin. It states which
// storage a pointer reaches and where a call may store through it; it grants no access and decides no access error (the owner,
// 2026-10-22: no access through a raw pointer is a compile-time error; condition 3 bounds it at run time).
// - Responsibility: the written raw Origin in every position, the atom its name binds (WrittenRawOrigin), judged once after the last
//   requirement fixed point (InvalidRawOrigin, from ValidateOriginRequirements: a Phantom slot, also through a written projection; an
//   atom no borrow holds; a pair input admitting a non-borrow case); the step of a raw layer (StepOf, by its Origin's kind; StoredStep,
//   a Field's or Case payload's layer; SignatureStep, a signature's layer) and the borrows its Origin belongs to (RawMembers), which
//   retention and A5 read, each slot keeper walked once, a keeper cycle that changes its Type arguments an incomplete answer
//   (CallRetention.Limit), never a silent stop; the Origin a `during self` layer is read with at a Place (ReadHolder); a layer's
//   OwnedOrigins member (OwnedOrigin); the relations with an unstated end (UnstatedRelation, rulings S4); the allowlist of a `during self`
//   layer's Place reached through no dereference (HolderPlaceFailure, OQ-R1 (a)); the borrows keeping a declared slot (Keepers, D15);
//   and a Type with every raw layer's Origin dropped (WithoutRawOrigins): an instance's stored Types (Instance, design-final §15) and,
//   as the D5 exception (the owner's Q-O3, DECISIONS BU-24), the old analysis's view at its Type intake, removed with it at R6-check.
// - Inputs: bound Types; a declaration's schema and keepers after the requirement fixed point; a signature's input Types; PathOrigin.
// - Outputs: raw Origins, steps, member borrows, relation verdicts and the OriginRelation failure facts of the allowlist.
// - Invariants: Holder only in declared Field and payload Types, which every read replaces, never in an instance's stored Types;
//   Unstated only in relations and their records, never in a Type, so no premise, obligation or row names Holder or Unstated
//   (design-final §13 A2). A formed raw Origin is the Origin it is formed with; every step is total and never Shared by default, and
//   only retention and the TyCtxt views read one, after Binding's last pass.
// - Files: this one (relations, claim fits, the Types without raw Origins); .Steps.cs (RawStep and its readers); .Written.cs (the written
//   forms and their judgment); .Holder.cs (`during self` layers read and written at a Place); .Keepers.cs (member borrows and slot keepers).
// - Prohibitions: no region, Loan or retention facts beyond the member borrows, no Place walk of its own (PathOrigin is PlaceAuthority's),
//   no access verdict, no message text.
public sealed partial class Binding
{
    // The member borrows a step or a retention walk is reading (RawMembers), in stack order: each reader truncates to where it started.
    private readonly List<RawMember> rawMembers = new();

    // SPEC 5.4, 5.7: whether a conversion stating `claimed` is a fit: the premises prove that its operand's Origin, none for an
    // unstated pointer or an address, outlives the claimed one.
    internal bool ClaimFits(ConversionKoto claim, BoundOrigin claimed)
        => this.ProvesOriginOutlives(claim.Left.BoundType?.RawOrigin ?? BoundOrigin.Unstated, claimed, claim);

    // A Type with every raw layer's Origin dropped: an instance's stored Types (Instance; design-final §15), and the D5 exception (the
    // owner's Q-O3, 2026-10-22; DECISIONS BU-24): the old analysis and Emission compare Types by reference, so they read each Type
    // through this view at their Type intake (OwnershipAnalysis.Place, OwnershipBody.Resolve); removed with them at R6-check.
    internal BoundType? WithoutRawOrigins(BoundType? type)
    {
        if (type is null || !type.CarriesRawOrigin)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.WithoutRawOrigins(type.Components[i])!;
            }

            var parts = components.AsSpan(0, type.Components.Count);
            return type.Kind == BoundTypeKind.Semantics && type.Semantics == SemanticsKind.Raw
                ? this.types.Pointer(parts[0]) : this.types.Rebuild(type, parts, type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 15.2.3, 5.7: a layer's member of OwnedOrigins: its Origin, or its raw Origin other than `self`.
    private static BoundOrigin? OwnedOrigin(BoundType type) => type.Origin ?? (type.RawOrigin is { Kind: not OriginKind.Holder } raw ? raw : null);

    // SPEC 5.7 (rulings S4): a raw layer without `during` counts as stating an Origin every Origin outlives: `x outlives Unstated`
    // holds and `Unstated outlives x`, x another Origin, is Refuted; null for a relation without an Unstated end.
    private static bool? UnstatedRelation(BoundOrigin longer, BoundOrigin shorter) => (longer.Kind, shorter.Kind) switch
    {
        (_, OriginKind.Unstated) => true,
        (OriginKind.Unstated, _) => false,
        _ => null,
    };

    // Whether two Types are one Type but for the Origins their raw layers state, which a contract compares by subtyping (SPEC 15.3.7).
    private static bool SameExceptRawOrigins(BoundType a, BoundType b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Semantics == SemanticsKind.Raw && a.Kind == BoundTypeKind.Semantics
            ? b is not { Semantics: SemanticsKind.Raw, Kind: BoundTypeKind.Semantics }
            : !a.SameExceptOrigins(b) || !ReferenceEquals(a.Origin, b.Origin) || !a.OriginArguments.SequenceEqual(b.OriginArguments))
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!SameExceptRawOrigins(a.Components[i], b.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 5.6, 5.7 (D14): the raw Origin the argument of a callee's Within parameter (KimiLibraryCatalog.Within) states before its fit to
    // the parameter, with that argument; null when the callee has no such parameter or the argument states none.
    private BoundOrigin? StatedWithin(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, out Koto argument)
    {
        argument = null!;
        var within = KimiLibraryCatalog.Within(selected.BoundSymbol);
        for (var i = 0; i < operations.Length && within >= 0; i++)
        {
            if (operations[i] is { ParameterIndex: var parameter, Source: { } source, AdaptedType.RawOrigin: { } stated } && parameter == within)
            {
                argument = source;
                return stated;
            }
        }

        return null;
    }
}

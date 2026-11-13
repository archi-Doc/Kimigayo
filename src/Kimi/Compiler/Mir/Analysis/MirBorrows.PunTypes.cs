// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's pun rule, its judgement over Types (PLAN G667c; contract in MirBorrows.Puns.cs; SPEC 4.1, 4.4.2, 5.2, 5.3, 6.7.1, IMPL 21.1).
// Responsibility: what a write of a Type does at a Place of a Type and a Position. The leading chain of a Type: the Type, then the part
// the specification places at its own address, repeatedly (TyCtxt.Leading, Binding's one step: a fixed array's element, a C-layout
// struct's first Field); its grid rows: the Type and the element rows reached through element rows; its end: its last row where every
// step to it holds. One reading of a length (Sized; SPEC 4.1: a fixed array's size is N x stride; 5.2: a punned Place is
// positive-sized) serves both sides: a Place that holds no raw pointer's byte is never punned (Pointered), a written Type holds a
// Scalar by it (Scalar, Within), and a length that is no constant is not established, so no pun is decided over one on either side
// (SPEC 4.4.2). One judge (Designated): the Place's own Type (off the Place's address it leaves the Place's storage), or, on, a chain
// row's: typed. Any other write is a pun only where it must overlap a raw pointer of the Place (SPEC 15.6: a write through its address
// is in it): the Place's grid rows end at a raw pointer (every byte of it is a pointer's), or, on, its chain ends at one and the
// written Type's at a Scalar (the write's first byte is a Scalar's on the pointer's first), every length on the way a constant above
// 0; an abstract written part is a Scalar there (Scalar) and no other row decides it. Else it is undecided, a pun or none as the
// layout places it (IMPL 21.1). One step rule (Step) keeps or loses a Position.
// Two walks reach a Type's inline parts, by what a missing part would cost. Pointered and Scalar answer what bytes a Type has: a
// part they miss would remove a holder or a written Scalar, so they read only the stored Types a Type's own structure fixes
// (Components, AdtDef's Fields, base and Case payloads) with Sized. For Pointered every other Type answers "may" (Extent.Open), a
// Closure included (its environment is the body's reading, R4-M148); for Scalar an abstract part is a binding's, a Scalar of any
// size among them (SPEC 8.15.1), and a Type that is not punnable (SPEC 5.2, TyCtxt.Punnable) holds none. Within and Scalared read
// the raw Places within a written Type (SPEC 5.2: its Fields and elements; TyCtxt.RawPlaces, the one statement), an array of no
// element cut by Within's array row (Sized); a part they miss would drop a write, so where RawPlaces answers Empty, or Undecided with
// no row published, they add the written side (O-220 (a), O-231 (a)): its by-value parts (Holdings' Inline entries: an enum's Case
// payloads, a Closure's environment, an owner layer's target; an Unsupported row undecided, Unstated), each off; an abstract part's
// own entry adds nothing (Scalar's abstract row judged the part).
// Must not: test a Type's Kind or an AdtDef fact for the chain's step (TyCtxt.Leading's); read Binding's holdings for the bytes a Place
// has (they state the Loans and layers a value carries); decide SPEC 5.2's punnable set (TyCtxt.Punnable's).
internal sealed partial class MirBorrows
{
    private readonly List<Hold> punHolds = []; // Holdings' scratch (WrittenBeside, Scalared), a stack.
    private readonly List<RawPlace> punRaw = []; // RawPlaces' scratch (Within, Scalared), a stack.

    // How much of what is asked a Type holds, in rising order; a pun is decided only over Positive. For the chain's step from a row it is
    // the one reading of a length (Sized; SPEC 4.1: a fixed array's size is N x stride; 5.2: a punned Place is positive-sized), for
    // the holder and the written Type alike.
    private enum Extent : byte
    {
        // Nothing: a fixed array of a constant length 0 has no element, no byte.
        None,

        // Not established: a length that is no constant (SPEC 4.4.2: the body is checked for every binding that meets the public
        // conditions, and they may all make it 0: `N - N`, a bare `N` under ValidLength(0 - N)); for Pointered, also a part whose bytes
        // the Type's own structure does not state (it may hold a raw pointer's byte).
        Open,

        // Surely: a constant length above 0; a first Field (SPEC 6.7.1: C layout has no zero-sized Field).
        Positive,
    }

    private static Extent Sized(BoundType row)
    {
        if (row.Kind != BoundTypeKind.FixedArray || row is { LengthExpression: null, Length: > 0 })
        {
            return Extent.Positive;
        }

        return row.LengthExpression is null ? Extent.None : Extent.Open;
    }

    private static Extent Most(Extent a, Extent b) => a > b ? a : b;

    // What a Holdings entry that names no stored part writes, as the written side's fold reads it (Within; Scalared: whether it may
    // write): a form with no row undecided (Holdings' contract: each reader's undecided outcome); none for an erased environment (no raw
    // Place lies within a Function value, SPEC 5.2), a layer or an abstract part's own entry (Scalar's row reads the part); null for
    // an Inline part, which the fold reads by its Type.
    private static PunWrite? Unstated(in Hold entry) => entry.Step == HoldStep.Unsupported ? PunWrite.Undecided : null;

    // How much of a Scalar a punnable Type holds inline (A5's own fact; SPEC 5.2's punnable set is TyCtxt.Punnable's): a Scalar surely, and
    // so an abstract part, a binding's (SPEC 8.15.1: every binding, a Scalar of any size among them), judged as one at its Position
    // (Designated); an array its element's, by its own extent at most (Sized); a Tuple its components' most where it is punnable
    // (TyCtxt.Punnable: every component is, decided once per Type, so a write reads it by one lookup per Tuple level). None for a raw
    // pointer and for any Type that is not punnable.
    private Extent Scalar(BoundType type)
    {
        if (ScalarTypes.Supports(type) || type.IsAbstract)
        {
            return Extent.Positive;
        }

        switch (type.Kind)
        {
            case BoundTypeKind.Semantics when type.Semantics == SemanticsKind.Owner && type.Components.Count > 0:
                return this.Scalar(type.Components[0]);
            case BoundTypeKind.FixedArray:
                var scalar = this.Scalar(type.Components[0]);
                return scalar < Sized(type) ? scalar : Sized(type);
            case BoundTypeKind.Tuple:
                var most = Extent.None;

                // By index: a foreach over the interface would allocate an enumerator per call.
                for (var i = 0; i < type.Components.Count; i++)
                {
                    most = Most(most, this.Scalar(type.Components[i]));
                }

                return most != Extent.None && types.Punnable(type) ? most : Extent.None;
            default:
                return Extent.None;
        }
    }

    // How much of a raw pointer's byte a Place of a Type holds inline, by the Type's own structure and the one reading of a length
    // (Sized): the bytes a Place has (SPEC 4.1, 5.2), which is not what its value carries: Binding's holdings state the Loans and layers
    // carried, and an array of no element keeps its element's (SPEC 4.3.3: `[0 of v]` keeps v's Loans), so RawInline is true of a Place
    // with no byte. None: a Scalar. Positive (it surely has one): a raw pointer. A fixed array has its element's, by its own extent at
    // most; a Tuple, a struct (its Fields and its base, AdtDef) and an enum (its Cases' payloads) their parts' most. Open (it may have
    // one: Binding's layer fact stands, nothing establishes the byte): any other Type (an abstract part, a Closure, an own step), a part
    // Binding did not bind and a declaration on an inline cycle (an error at its container).
    private Extent Pointered(BoundType type)
    {
        if (ScalarTypes.Supports(type))
        {
            return Extent.None;
        }

        var most = Extent.None;
        if (type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple)
        {
            // By index: a foreach over the interface would allocate an enumerator per call.
            for (var i = 0; most != Extent.Positive && i < type.Components.Count; i++)
            {
                most = Most(most, this.Pointered(cases.Type(type.Components[i])));
            }

            return most < Sized(type) ? most : Sized(type);
        }

        if (!(AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) || type.Symbol!.Adt is not { InlineCycle: false })
        {
            return type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ? Extent.Positive : Extent.Open;
        }

        for (var i = 0; most != Extent.Positive && i < AdtDef.Count(type); i++)
        {
            most = Most(most, AdtDef.FieldType(type, i) is { } field ? this.Pointered(cases.Type(field)) : Extent.Open);
        }

        var payloads = AdtDef.CaseTypes(type);
        for (var i = 0; most != Extent.Positive && payloads is not null && i < payloads.Length; i++)
        {
            most = Most(most, payloads[i] is { } payload ? this.Pointered(cases.Type(payload)) : Extent.Open);
        }

        return most != Extent.Positive && AdtDef.Base(type) is { } parent ? Most(most, this.Pointered(cases.Type(parent))) : most;
    }

    // The leading chain's one step under the case view (TyCtxt.Leading, Binding's): the part the specification places at the address of
    // a value of the row's Type, `element` whether it is a fixed array's element (SPEC 4.1: element i at offset i * stride), else a
    // C-layout struct's first Field (IMPL 21.1.2); null where it places none. A row has no path of its own (a Place's case Type, a
    // written or stride Type, a part of one), so it steps as a root's key: the step is a function of the Type. It reads no length:
    // Sized does.
    private BoundType? Lead(BoundType row, out bool element)
        => types.Leading(PlaceKey.Root(row), out element).Type is { } part ? cases.Type(part) : null;

    // The chain's next row; `grid`: through an element step only.
    private BoundType? Next(BoundType row, bool grid) => this.Lead(row, out var element) is { } part && (element || !grid) ? part : null;

    // Whether `type` is a row of the leading chain of `place`: the Type itself, then the part at its own address, repeatedly. `grid`:
    // element rows only, the rows whose stride keeps a displaced pointer on addresses where the chain starts (SPEC 5.3).
    private bool Leads(BoundType place, BoundType type, bool grid)
    {
        for (var row = place; row is not null; row = this.Next(row, grid))
        {
            if (types.SameStructure(row, type))
            {
                return true;
            }
        }

        return false;
    }

    // The chain's end: its last row (`grid`: of its grid rows), what is at the first byte of a value of the Type (grid: at every element
    // start of it, and nothing else in it), where every step to it is established to hold (Sized: Positive); else nothing (null):
    // through an array of a length that is no constant (or of no element) the judge decides no pun.
    private BoundType? End(BoundType type, bool grid)
    {
        while (this.Next(type, grid) is { } next)
        {
            if (Sized(type) != Extent.Positive)
            {
                return null;
            }

            type = next;
        }

        return type;
    }

    // The one step rule (SPEC 4.1, 5.3; IMPL 21.1.2): whether a step keeps a Position on the leading chain of `place`: a displacement of
    // a pointer of Type `of`, or an element step of a raw Place of Type `of` (a fixed array), where the stride's Type is a grid row of
    // `place`; the first Field (`field` 0) of a C-layout `of`: its address is its base's; no other step (`field` -1: none of a Field).
    private bool Step(BoundType place, BoundType of, int field)
    {
        var part = this.Lead(of, out var element);
        if (element)
        {
            return this.Leads(place, part!, grid: true);
        }

        return of is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var to, ..] }
            ? this.Leads(place, cases.Type(to), grid: true) : field == 0 && part is not null;
    }

    // The one designation judge (SPEC 5.2, 5.2.1 condition 1, 5.3, 4.1; IMPL 21.1): what a write of Type `write`, punnable and holding a
    // Scalar, does at a Place of Type `place`, `on` whether its address is one where the Place's leading chain starts. The Place's own
    // Type (off the Place's address it leaves the Place's storage, undefined), or, on, a chain row's: typed. Else a pun only where the
    // write must overlap a raw pointer of the Place, which holds a byte (Holder): every byte of the Place is a pointer's (its grid rows
    // end at a raw pointer), or the write's first byte is a Scalar's on a pointer's first (on, the Place's chain ends at a raw pointer and
    // the written Type's at a Scalar), every length on the way a constant above 0 and the written Type surely holding a Scalar
    // (Extent.Positive; an abstract part is a Scalar of a binding's size, Scalar, so it is a pun only where these rows establish the
    // overlap, the owner's O-205 (a)). Else undecided: the layout, or a length that is no constant, decides what it covers.
    private PunWrite Designated(BoundType place, BoundType write, bool on)
    {
        if (types.SameStructure(place, write) || (on && this.Leads(place, write, grid: false)))
        {
            return PunWrite.Typed;
        }

        if (this.Scalar(write) != Extent.Positive)
        {
            return PunWrite.Undecided;
        }

        // On, at the chain's end: the written Type's first byte is a Scalar's.
        var first = on && Pointer(this.End(place, grid: false)) && this.End(write, grid: false) is { Kind: not BoundTypeKind.Tuple } led &&
            this.Scalar(led) == Extent.Positive;
        return first || Pointer(this.End(place, grid: true)) ? PunWrite.Pun : PunWrite.Undecided;

        static bool Pointer(BoundType? row) => row is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
    }

    // What a write of a raw Place of Type `type` at Position `on`, or of a raw Place within it (SPEC 5.2: its Fields and elements are raw
    // Places of their own Types), does at a Place of Type `place`: nothing where the Place designates it; else each punnable Type holding
    // a Scalar there, at its Position (Step; a part of a Type with no decided layout is off).
    private PunWrite Within(BoundType place, BoundType type, bool on)
    {
        if (on && this.Leads(place, type, grid: false))
        {
            return PunWrite.Typed;
        }

        var result = this.Scalar(type) != Extent.None ? this.Designated(place, type, on) : PunWrite.Typed;
        if (result == PunWrite.Pun)
        {
            return result;
        }

        // The raw Places within (TyCtxt.RawPlaces), each at its Position: a lead row at the Type's (Step), every other row off. By the
        // written array's extent: its elements at their Positions; of a length that is no constant, their writes are no decided pun; of
        // no element, no raw Place within. Listed: the rows. Empty: the written side (WrittenBeside). Undecided (O-231 (a)): today's
        // written parts, the rows Binding's storage states where any is published (a struct, Tuple or fixed array at a Limit or with an
        // unbound part), else the written side; never nothing written.
        var start = this.punRaw.Count;
        var answer = types.RawPlaces(PlaceKey.Root(type), this.punRaw);
        if (answer == RawPlacesAnswer.Empty || (answer == RawPlacesAnswer.Undecided && this.punRaw.Count == start))
        {
            return this.WrittenBeside(place, type, result);
        }

        for (var i = start; result != PunWrite.Pun && i < this.punRaw.Count; i++)
        {
            if (this.punRaw[i] is not { Key.Type: { } row } raw)
            {
                continue;
            }

            var part = cases.Type(row);
            result = !raw.Element ? Worse(result, this.Within(place, part, on && raw.Lead)) : Sized(type) switch
            {
                Extent.Positive => Worse(result, this.Within(place, part, on && this.Step(place, type, -1))),
                Extent.Open => this.Within(place, part, on: false) == PunWrite.Typed ? result : Worse(result, PunWrite.Undecided),
                _ => result,
            };
        }

        this.punRaw.RemoveRange(start, this.punRaw.Count - start);
        return result;
    }

    // The written side beyond the raw Places (O-220 (a)): a whole-value write of a Type with no raw Place within (an enum's Case payload,
    // a Closure's environment, an owner layer's target) writes its by-value parts' bytes (Holdings' Inline entries), each off.
    private PunWrite WrittenBeside(BoundType place, BoundType type, PunWrite result)
    {
        var start = this.punHolds.Count;
        _ = types.Holdings(type, null, null, this.punHolds);
        for (var i = start; result != PunWrite.Pun && i < this.punHolds.Count; i++)
        {
            if (this.punHolds[i] is { Step: HoldStep.Inline, Part: { } part })
            {
                result = Worse(result, this.Within(place, cases.Type(part), on: false));
            }
            else if (Unstated(this.punHolds[i]) is { } unstated)
            {
                result = Worse(result, unstated);
            }
        }

        this.punHolds.RemoveRange(start, this.punHolds.Count - start);
        return result;
    }

    // Whether a raw Place of a Type, or one within it, has a punnable Type holding a Scalar.
    private bool Scalared(BoundType type)
    {
        if (this.Scalar(type) != Extent.None)
        {
            return true;
        }

        // The raw Places within, read by Within's three classes: Listed or Undecided with published rows, the rows; else the written side.
        var found = false;
        var start = this.punRaw.Count;
        var answer = types.RawPlaces(PlaceKey.Root(type), this.punRaw);
        if (answer == RawPlacesAnswer.Listed || (answer == RawPlacesAnswer.Undecided && this.punRaw.Count > start))
        {
            for (var i = start; !found && i < this.punRaw.Count; i++)
            {
                found = this.punRaw[i] is { Key.Type: { } row } && this.Scalared(cases.Type(row));
            }

            this.punRaw.RemoveRange(start, this.punRaw.Count - start);
            return found;
        }

        // The written side beyond the raw Places (WrittenBeside's parts).
        var held = this.punHolds.Count;
        _ = types.Holdings(type, null, null, this.punHolds);
        for (var i = held; !found && i < this.punHolds.Count; i++)
        {
            found = this.punHolds[i] is { Step: HoldStep.Inline, Part: { } part } ? this.Scalared(cases.Type(part))
                : Unstated(this.punHolds[i]) is not null;
        }

        this.punHolds.RemoveRange(held, this.punHolds.Count - held);
        return found;
    }
}

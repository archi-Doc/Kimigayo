// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What a call that writes a value's storage may write past it (A3 E16, DECISIONS R4-M73, R4-M147), from HoldsLoans' walk:
/// the guard of a value that may hold a Loan, part by part, three ways; Through wins over Limit, Limit over None.</summary>
internal enum MirWrites : byte
{
    /// <summary>Nothing past it: no exclusive reference to storage that may hold a Loan.</summary>
    None,

    /// <summary>Some part gives the write fact: past no shared layer, an exclusive reference to storage that may hold a Loan (a raw
    /// layer whose step lets a call write past it counting as one), an abstract part, or a Function or Closure value or a part past the
    /// walk's bound, which may be one (the guard).</summary>
    Through,

    /// <summary>No part gives the write fact, and a stored raw layer stepping Exclusive whose members Binding does not publish (a Type
    /// argument supplies it, or the member walk is incomplete) leaves it undecided: a Call whose W reaches the value is the located
    /// Unsupported_Kd at the call (DECISIONS R4-M147).</summary>
    Limit,
}

// What a value may hold: raw authority (HoldsRaw) and Loans with what a call may write past them (HoldsLoans), each a walk of its parts.
internal static partial class MirTypes
{
    // The holding walk's modes, by where a part lies: in the value's own storage or past an exclusive reference from it; with LoanOnly,
    // in a struct's or enum's Type argument, which gives Loan facts only (SPEC 15.3.5: a Type argument has no address and grants no
    // access; the owner's Q2 (a)): its storage counts only through Fields, payloads, the base or past an Own layer.
    private const int Own = 0;
    private const int Exclusive = 1;
    private const int LoanOnly = 2;

    // The facts the holding walk finds.
    private const int Loan = 1;
    private const int Write = 2;
    private const int Limit = 4;

    // The deepest part the holding walks enter: past it a part may be anything (SPEC 15.3.5's growing recursion).
    private const int Bound = 32;

    /// <summary>Gets whether a value of a Type holds a raw pointer, an rc handle or a Weak of one at any depth, so that reading it from an
    /// immutable static obtains authority from the environment (SPEC 8.11.2); a referent reached through the value counts too. A Field or
    /// an enum's Case Binding did not substitute (null; <see cref="AdtDef.CaseTypes"/>) holds nothing, a dependent of Binding's rejection.
    /// Each Type is walked once per query, as HoldsLoans' are, so a repeating recursive Type ends exactly; past depth 32 the answer is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="seen">Scratch for the Types walked, cleared first.</param>
    /// <returns>Whether the value holds raw authority.</returns>
    internal static bool HoldsRaw(BoundType? type, HashSet<BoundType> seen)
    {
        seen.Clear();
        return Raw(type, seen, 0);
    }

    /// <summary>Gets whether a value of a Type may hold a Loan (SPEC 15.6), so that A5 tracks it: a part of it is a borrow, pair or raw
    /// layer (a raw pointer carries the holders it addresses and the Loans it was formed through, SPEC 15.6 Addresses), a Slice, a Function
    /// or Closure value, or an abstract Type, whose instance may store a borrow its Origins do not show. Its parts are its own storage, a
    /// struct's Fields and base and an enum's payloads (a Field or Case Binding did not substitute holds nothing, a dependent of Binding's
    /// rejection; <see cref="AdtDef.CaseTypes"/>), and its components: elements, an owning handle's payload, and a Type argument, which its
    /// storage may hold behind a raw pointer (a library collection, its iterators) or as a Loan or a Weak (SPEC 3.2.2, 15.3.5); a function
    /// item holds none. The same walk gives what a call may write past the value (A3 E16): its guard, whether it may hold, past no shared
    /// layer, an exclusive reference (a pair layer's exclusive case and an abstract part counting, as do a Function or Closure value and a
    /// part past the bound) to storage that may hold a Loan; a struct's or enum's Type argument, which has no address and grants no access
    /// (SPEC 15.3.5), adds only whether the value may hold one. A struct's or enum's own storage is its stored parts as the holding value's
    /// substitution makes them, raw Origins kept (TyCtxt.StoredParts, DECISIONS BU-41), so a raw layer below the holder is read on the
    /// instance that stores it. Past a raw layer of the value's own storage (DECISIONS R4-M147; SPEC 5.7 Calls, 15.6.4), a layer a struct's
    /// Field or an enum's Case payload stores, at inline positions, by its step on that instance (TyCtxt.RawStep): `self`, the pointee and
    /// the holding instance's Type arguments by their actual Types in the value's own storage, the write fact where the instance has an
    /// Origin argument (the owner's 「進める (推奨)」); a slot kept only by `ref`, nothing; `static`, an exclusive reference to its pointee;
    /// another Origin, an exclusive reference to its pointee and to each exclusive member's referent (TyCtxt.RawMembers), the Limit where
    /// the members are unpublished; none, SPEC 5.7 Calls' per-parameter rule: an exclusive reference to its pointee where the parameter's
    /// Type expression writes the layer, a Type argument's or a pair slot's argument's included (TyCtxt.RawSupplied on the holding
    /// instance), and the write fact where a declaration states it in its own Fields, Case payloads or base; a layer a declaration writes
    /// inside a nested instance's Type argument, Field payload or base clause reads as supplied until Binding publishes its provenance
    /// relative to the holder (DECISIONS R4-M147, Known limits). Any other raw layer (a local's or parameter's own Type, an element or a
    /// Tuple outside an instance) is an exclusive reference to its pointee (DECISIONS R4-M120). Each Type is walked once per query and
    /// mode, a raw layer and the inline Tuples and fixed arrays of a stored part once per part, so a repeating recursive Type ends exactly;
    /// past depth 32, where one grows (SPEC 15.3.5), the answer is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose stored parts and stored raw layers' facts the walk reads.</param>
    /// <param name="seen">Scratch for the modes each Type was walked in, cleared first.</param>
    /// <param name="members">Scratch for a stored layer's members, used in stack order and left as given.</param>
    /// <param name="writes">What a call may write past the value.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, TyCtxt types, Dictionary<BoundType, byte> seen, List<RawMember> members, out MirWrites writes)
    {
        seen.Clear();
        var walk = new Holding(types, seen, members, stackalloc int[Bound + 2]);
        var found = walk.Holds(type, Own, 0);
        writes = ((found & Loan) != 0, (found & Write) != 0, (found & Limit) != 0) switch
        {
            (true, true, _) => MirWrites.Through,
            (true, false, true) => MirWrites.Limit,
            _ => MirWrites.None,
        };
        return (found & Loan) != 0;
    }

    // HoldsRaw's walk: a Type seen before adds nothing; its components, a struct's Fields and base and an enum's payload Tuples.
    private static bool Raw(BoundType? type, HashSet<BoundType> seen, int depth)
    {
        if (type is null || !seen.Add(type))
        {
            return false;
        }

        if (depth > Bound || type.Semantics is SemanticsKind.Raw or SemanticsKind.Rc)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (Raw(type.Components[i], seen, depth + 1))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Raw(AdtDef.FieldType(type, i), seen, depth + 1))
            {
                return true;
            }
        }

        foreach (var payload in AdtDef.CaseTypes(type) ?? [])
        {
            if (Raw(payload, seen, depth + 1))
            {
                return true;
            }
        }

        return Raw(AdtDef.Base(type), seen, depth + 1);
    }

    // HoldsLoans' walk, with the stored part it is inside: a struct's Field or an enum's Case payload, whose inline positions (a Tuple's
    // components, a fixed array's element) from `start` the walk is at, so a raw layer there reads that part's step (TyCtxt.RawStep).
    private ref struct Holding(TyCtxt types, Dictionary<BoundType, byte> seen, List<RawMember> members, Span<int> positions)
    {
        private readonly Span<int> positions = positions; // The inline positions of the stored parts the walk is inside, outermost first.
        private BoundType? instance; // The struct or enum whose stored part the walk is inside (null: none).
        private int part;
        private int start; // The stored part's first inline position.
        private int top; // The positions in use.

        // The facts a part adds in its mode, each (Type, mode) once per query (a LoanOnly mode not after its full one), a raw layer and an
        // inline Tuple or fixed array of a stored part once per part. A part that may hold a Loan (a borrow, pair or raw layer, a Slice, a
        // Function or Closure value, an abstract part, a part past the bound) gives Loan in the value's own storage, and Write past an
        // exclusive reference from it, but in a LoanOnly mode. In the value's own storage an exclusive reference, a UniqSlice's elements, a
        // pair layer's exclusive case and what lies past a raw layer by its step (Past) are walked past, and an abstract part (the owner's
        // Q1 (a)), a callable and a part past the bound may be such a reference; a struct's or enum's Type arguments are walked LoanOnly. A
        // mode's walk stops once it has the facts no other part changes.
        internal int Holds(BoundType? type, int mode, int depth)
        {
            if (type is null || type.Kind == BoundTypeKind.FunctionItem)
            {
                return 0;
            }

            var own = (mode & ~LoanOnly) == Own;
            var exclusive = (mode & LoanOnly) != 0 ? 0 : Write;
            var met = own ? Loan : exclusive;
            if (type.Semantics == SemanticsKind.Raw)
            {
                return met | (own && exclusive != 0 && type.Components.Count > 0 ? this.Past(type.Components[0], depth + 1) : 0);
            }

            var inline = this.instance is not null && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
            ref var modes = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, type, out _);
            if (!inline && (modes & ((1 << mode) | (1 << (mode & ~LoanOnly)))) != 0)
            {
                return 0;
            }

            modes |= (byte)(inline ? 0 : 1 << mode);
            if (depth > Bound || type.Kind is BoundTypeKind.Function or BoundTypeKind.Closure)
            {
                return met | (own ? exclusive : 0);
            }

            if (TyCtxt.Layer(type, out var below, out _) == HirPlaceKind.PairLayer)
            {
                return met | (own && exclusive != 0 ? this.Part(below, Exclusive, depth + 1) : 0);
            }

            if (type.IsAbstract)
            {
                return met | (own ? exclusive : 0);
            }

            var referent = own && exclusive != 0 && (IsExclusive(type) || View(type) == SemanticsKind.Uniq) && type.Components.Count > 0
                ? this.Part(type.Components[0], Exclusive, depth + 1) : 0;
            if (IsReference(type) || type.Kind == BoundTypeKind.Slice)
            {
                return met | referent;
            }

            var found = referent;
            var done = own ? Loan | exclusive : met;
            var arguments = own && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ? mode | LoanOnly : mode;
            for (var i = 0; i < type.Components.Count && (found & done) != done; i++)
            {
                found |= inline ? this.Inline(i, type.Components[i], arguments, depth + 1) : this.Part(type.Components[i], arguments, depth + 1);
            }

            var parts = types.StoredParts(type, out var parent); // A struct's Fields or an enum's Case payloads, raw Origins kept (BU-41).
            for (var i = 0; i < parts.Length && (found & done) != done; i++)
            {
                found |= this.Stored(type, i, parts[i], mode, depth + 1);
            }

            return found | ((found & done) != done ? this.Part(parent, mode, depth + 1) : 0);
        }

        // What lies past a raw layer of the value's own storage (DECISIONS R4-M147): for a layer a stored part holds, by its step on the
        // instance holding it (TyCtxt.RawStep): `self`, the pointee and the instance's Type arguments by their actual Types in the value's
        // own storage, the write fact where the instance has an Origin argument; a slot kept only by `ref`, nothing; `static`, an
        // exclusive reference to the pointee; another Origin, an exclusive reference to the pointee and to each exclusive member's
        // referent, the Limit where the members are unpublished; none, an exclusive reference to the pointee where the parameter's Type
        // expression writes the layer (RawSupplied, R4-M147's gap aside), the write fact where a declaration states it. Any other layer: an
        // exclusive reference to the pointee.
        private int Past(BoundType pointee, int depth)
        {
            if (this.instance is not { } stored)
            {
                return this.Part(pointee, Exclusive, depth);
            }

            var at = this.positions[this.start..this.top];
            return types.RawStep(stored, this.part, at) switch
            {
                RawStep.Own => stored.OriginArguments.Count > 0 ? Write : this.Admitted(stored, pointee, depth),
                RawStep.Shared => 0,
                RawStep.Static => this.Part(pointee, Exclusive, depth),
                RawStep.Exclusive => this.Members(stored, at, depth) | this.Part(pointee, Exclusive, depth),
                _ => types.RawSupplied(stored, this.part, at) ? this.Part(pointee, Exclusive, depth) : Write, // Unstated.
            };
        }

        // What an Exclusive layer's members admit past it (TyCtxt.RawMembers): each exclusive member's referent past an exclusive
        // reference, or the Limit where the members are unpublished (an incomplete walk, a layer a Type argument supplies).
        private int Members(BoundType stored, ReadOnlySpan<int> at, int depth)
        {
            var first = members.Count;
            var found = types.RawMembers(stored, this.part, at, members) ? 0 : Limit;
            for (var i = first; found == 0 && i < members.Count; i++)
            {
                found |= members[i].Exclusive ? this.Part(members[i].Referent, Exclusive, depth) : 0;
            }

            members.RemoveRange(first, members.Count - first);
            return found;
        }

        // What a `during self` layer admits past it (SPEC 5.7 Calls, "for self, the holding value's Type"; the owner's Q2 (a)): its pointee
        // and the holding instance's Type arguments by their actual Types, in the value's own storage.
        private int Admitted(BoundType stored, BoundType pointee, int depth)
        {
            var found = this.Part(pointee, Own, depth);
            for (var i = 0; i < stored.Components.Count; i++)
            {
                found |= this.Part(stored.Components[i], Own, depth);
            }

            return found;
        }

        // A part outside the stored part: a referent, a pointee, a Type argument or a base.
        private int Part(BoundType? type, int mode, int depth)
        {
            var stored = this.instance;
            this.instance = null;
            var found = this.Holds(type, mode, depth);
            this.instance = stored;
            return found;
        }

        // A stored part of an instance: a struct's Field `part`, or an enum's Case `part` whose payload Tuple's components are its first
        // inline positions.
        private int Stored(BoundType instance, int part, BoundType? type, int mode, int depth)
        {
            var (stored, at, first) = (this.instance, this.part, this.start);
            (this.instance, this.part, this.start) = (instance, part, this.top);
            var found = this.Holds(type, mode, depth);
            (this.instance, this.part, this.start) = (stored, at, first);
            return found;
        }

        // An inline position of the stored part: a Tuple's component or a fixed array's element.
        private int Inline(int position, BoundType? type, int mode, int depth)
        {
            this.positions[this.top++] = position;
            var found = this.Holds(type, mode, depth);
            this.top--;
            return found;
        }
    }
}

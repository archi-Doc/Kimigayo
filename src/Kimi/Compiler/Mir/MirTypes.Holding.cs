// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What a call that writes a value's storage may write past it (A3 E16, DECISIONS R4-M73, R4-M147, R4-M153), from HoldsLoans'
/// walk: the guard of a value that may hold a Loan, read off the walk's one answer (<see cref="MirOutcome"/>: decided, Limit, Deep or
/// none), which rule W reads.</summary>
internal enum MirWrites : byte
{
    /// <summary>Nothing past it: no exclusive reference to storage that may hold a Loan.</summary>
    None,

    /// <summary>Some part gives the write fact (decided): past no shared layer, an exclusive reference to storage that may hold a Loan (a
    /// raw layer whose step lets a call write past it counting as one), an abstract part, or a Function value, which may be one (the
    /// guard); a Closure value by its environment's parts (DECISIONS R4-M148).</summary>
    Through,

    /// <summary>No part gives the write fact, and a stored raw layer stepping Exclusive whose members Binding does not publish (a Type
    /// argument supplies it, or the member walk is incomplete) leaves it undecided: a Call whose W reaches the value is the located
    /// Unsupported_Kd at the call (DECISIONS R4-M147).</summary>
    Limit,

    /// <summary>No part gives the write fact or the Limit, and a part past the walk's bound, where the write fact would be read, leaves it
    /// undecided: a Call whose W from the value would change what W writes is OwnershipDepthLimit_Kd at the call (DECISIONS
    /// R4-M153).</summary>
    Deep,
}

/// <summary>The holding walk's slots for one Type (<see cref="MirTypes.HoldsLoans"/>, <see cref="MirDepths"/>), by mode: Own, Exclusive and
/// Own with LoanOnly (Exclusive with LoanOnly never occurs).</summary>
[InlineArray(3)]
internal struct MirModeDepths
{
    private byte mode;
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
    /// value, or an abstract Type, whose instance may store a borrow its Origins do not show. Its parts are its own storage, a
    /// struct's Fields and base and an enum's payloads (a Field or Case Binding did not substitute holds nothing, a dependent of Binding's
    /// rejection; <see cref="AdtDef.CaseTypes"/>), and its components: elements, a Closure's environment bindings (SPEC 15.8.2, a Tuple of
    /// them, IMPL 21.1.2; DECISIONS R4-M148), an owning handle's payload, and a Type argument, which its
    /// storage may hold behind a raw pointer (a library collection, its iterators) or as a Loan or a Weak (SPEC 3.2.2, 15.3.5); a function
    /// item holds none. The same walk gives what a call may write past the value (A3 E16): its guard, whether it may hold, past no shared
    /// layer, an exclusive reference (a pair layer's exclusive case and an abstract part counting, as does a Function value) to storage that
    /// may hold a Loan, a part past the bound there being the walk's Deep fact, never the write fact (DECISIONS R4-M153); a struct's or
    /// enum's Type argument, which has no address and grants no access
    /// (SPEC 15.3.5), adds only whether the value may hold one. A struct's or enum's own storage is its stored parts as the holding value's
    /// substitution makes them, raw Origins kept (TyCtxt.StoredParts, DECISIONS BU-41), so a raw layer below the holder is read on the
    /// instance that stores it. Past a raw layer of the value's own storage (DECISIONS R4-M147; SPEC 5.7 Calls, 15.6.4), a layer a struct's
    /// Field or an enum's Case payload stores, at inline positions, by its step on that instance (TyCtxt.RawStep): `self`, the pointee and
    /// the holding instance's Type arguments by their actual Types in the value's own storage, the write fact where the instance has an
    /// Origin argument (the owner's 「進める (推奨)」); a slot kept only by `ref`, nothing; `static`, an exclusive reference to its pointee;
    /// another Origin, an exclusive reference to its pointee and to each exclusive member's referent (TyCtxt.RawMembers), the Limit where
    /// the members are unpublished; none, SPEC 5.7 Calls' per-parameter rule: the write fact where a declaration writes the layer, at any
    /// depth below the holder, which its kept part states (TyCtxt.RawStated; DECISIONS BU-46, R4-M151), else an exclusive reference to its
    /// pointee, the parameter's Type expression writing the layer (a Type argument's or a pair slot's argument's included). Any other raw
    /// layer (a local's or parameter's own Type, an element or a Tuple outside an instance) is read by the same rule: the write fact where
    /// a declaration wrote it (an Array element of a Field, a `self` pointee's content), else an exclusive reference to its pointee
    /// (DECISIONS R4-M120). Each Type is entered once per query and mode at the shallowest depth the walk meets it at, again where it is
    /// met shallower (<see cref="MirDepths"/>, at most once per depth), a raw layer and the inline Tuples and fixed arrays of a stored part
    /// once per part, so a repeating recursive Type ends exactly and the answer is the same in every order of the parts; a part past depth
    /// 32, where one grows (SPEC 15.3.5), gives yes for tracking and the guard's Deep where its mode reads the write fact, only where its
    /// Type is never entered within the bound in that mode (for a LoanOnly part, nor in its full mode).</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose stored parts and stored raw layers' facts the walk reads.</param>
    /// <param name="seen">Scratch for the depth each Type was entered at in each mode (<see cref="MirModeDepths"/>), cleared first.</param>
    /// <param name="members">Scratch for a stored layer's members, used in stack order and left as given.</param>
    /// <param name="writes">What a call may write past the value.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, TyCtxt types, Dictionary<BoundType, MirModeDepths> seen, List<RawMember> members, out MirWrites writes)
    {
        seen.Clear();
        var walk = new Holding(types, seen, members, stackalloc int[Bound + 2]);
        var found = walk.Holds(type, Own, 0);
        foreach (var modes in seen.Values)
        {
            for (var mode = Own; mode <= LoanOnly; mode++)
            {
                var past = modes[mode] == MirDepths.Past && (mode != LoanOnly || !MirDepths.Entered(modes[Own], Bound));
                found = past ? found.Join(Beyond(mode)) : found;
            }
        }

        writes = found.Loan ? Guard(found.Writes) : MirWrites.None;
        return found.Loan;
    }

    // What a part past the bound gives in a mode (DECISIONS R4-M153): Loan in the value's own storage, and Deep where the mode reads the
    // write fact. The modes are Own, Exclusive and Own|LoanOnly (LoanOnly's value), the slots of MirModeDepths.
    private static Facts Beyond(int mode) => new((mode & ~LoanOnly) == Own, (mode & LoanOnly) == 0 ? MirOutcome.Deep : MirOutcome.None);

    // The guard rule W reads, read off the walk's answer: decided is the write fact (Through), and Limit and Deep leave it undecided.
    private static MirWrites Guard(MirOutcome writes)
    {
        if (writes.IsDecided)
        {
            return MirWrites.Through;
        }

        if (writes.IsLimit)
        {
            return MirWrites.Limit;
        }

        return writes.IsDeep ? MirWrites.Deep : MirWrites.None;
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
    private ref struct Holding(TyCtxt types, Dictionary<BoundType, MirModeDepths> seen, List<RawMember> members, Span<int> positions)
    {
        private readonly Span<int> positions = positions; // The inline positions of the stored parts the walk is inside, outermost first.
        private BoundType? instance; // The struct or enum whose stored part the walk is inside (null: none).
        private int part;
        private int start; // The stored part's first inline position.
        private int top; // The positions in use.

        // The facts a part adds in its mode, each (Type, mode) entered at the shallowest depth it is met at (MirDepths; a LoanOnly mode not
        // where its full one was entered as shallow), a raw layer and an inline Tuple or fixed array of a stored part once per part. A part
        // past the bound is met, not entered: its slot gives its facts after the walk where it stays Past (HoldsLoans), but an inline Tuple
        // or fixed array, whose positions decide it, gives them at once. A part that may hold a Loan (a borrow, pair or raw layer, a Slice, a
        // Function value, an abstract part) gives Loan in the value's own storage, and the write fact (decided) past an exclusive
        // reference from it, but in a LoanOnly mode. In the value's own storage an exclusive reference, a UniqSlice's elements, a pair
        // layer's exclusive case and what lies past a raw layer by its step (Past) are walked past, and an abstract part (the owner's Q1
        // (a)) and a callable may be such a reference; a struct's or enum's Type arguments are walked LoanOnly. Parts compose by Join, and a
        // mode's walk stops only once it has the facts no other part changes (the write fact being the outcome's top), so a part past the
        // bound it skips adds nothing the answer lacks.
        internal Facts Holds(BoundType? type, int mode, int depth)
        {
            if (type is null || type.Kind == BoundTypeKind.FunctionItem)
            {
                return default;
            }

            var own = (mode & ~LoanOnly) == Own;
            var writes = (mode & LoanOnly) == 0; // The mode reads the write fact.
            var exclusive = writes ? MirOutcome.Decided : MirOutcome.None;
            var met = new Facts(own, own ? MirOutcome.None : exclusive);
            if (type.Semantics == SemanticsKind.Raw)
            {
                return met.Join(own && writes && type.Components.Count > 0 ? this.Past(type, depth + 1) : default);
            }

            var inline = this.instance is not null && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
            if (depth > Bound && inline)
            {
                return Beyond(mode);
            }

            if (!inline)
            {
                ref var modes = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, type, out _); // Used before any recursion moves it.
                if (depth > Bound)
                {
                    MirDepths.Meet(ref modes[mode]);
                    return default;
                }

                if ((mode == LoanOnly && MirDepths.Entered(modes[Own], depth)) || !MirDepths.Enter(ref modes[mode], depth))
                {
                    return default;
                }
            }

            if (type.Kind == BoundTypeKind.Function || type.IsAbstract)
            {
                return new(own, exclusive);
            }

            if (TyCtxt.Layer(type, out var below, out _) == HirPlaceKind.PairLayer)
            {
                return met.Join(own && writes ? this.Part(below, Exclusive, depth + 1) : default);
            }

            var referent = own && writes && (IsExclusive(type) || View(type) == SemanticsKind.Uniq) && type.Components.Count > 0
                ? this.Part(type.Components[0], Exclusive, depth + 1) : default;
            if (IsReference(type) || type.Kind == BoundTypeKind.Slice)
            {
                return met.Join(referent);
            }

            var found = referent;
            var done = own ? new Facts(true, exclusive) : met;
            var arguments = own && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ? mode | LoanOnly : mode;
            for (var i = 0; i < type.Components.Count && !found.Covers(done); i++)
            {
                var component = type.Components[i];
                found = found.Join(inline ? this.Inline(i, component, arguments, depth + 1) : this.Part(component, arguments, depth + 1));
            }

            var parts = types.StoredParts(type, out var parent); // A struct's Fields or an enum's Case payloads, raw Origins kept (BU-41).
            for (var i = 0; i < parts.Length && !found.Covers(done); i++)
            {
                found = found.Join(this.Stored(type, i, parts[i], mode, depth + 1));
            }

            return found.Covers(done) ? found : found.Join(this.Part(parent, mode, depth + 1));
        }

        // What lies past a raw layer of the value's own storage (DECISIONS R4-M147): for a layer a stored part holds, by its step on the
        // instance holding it (TyCtxt.RawStep): `self`, the pointee and the instance's Type arguments by their actual Types in the value's
        // own storage, the write fact where the instance has an Origin argument; a slot kept only by `ref`, nothing; `static`, an
        // exclusive reference to the pointee; another Origin, an exclusive reference to the pointee and to each exclusive member's
        // referent, the Limit where the members are unpublished; none, and any layer outside a stored part, one rule (DECISIONS R4-M151):
        // the write fact where a declaration wrote the layer, which the kept parts state (TyCtxt.RawStated, DECISIONS BU-46), else an
        // exclusive reference to the pointee (the walk's own Type wrote it, or it states an Origin outside a stored part).
        private Facts Past(BoundType layer, int depth)
        {
            var stored = this.instance;
            var pointee = layer.Components[0];
            ReadOnlySpan<int> at = stored is null ? default : this.positions[this.start..this.top];
            return (stored is null ? RawStep.Unstated : types.RawStep(stored, this.part, at)) switch
            {
                RawStep.Own => this.Admitted(stored!, pointee, depth),
                RawStep.Shared => default,
                RawStep.Static => this.Part(pointee, Exclusive, depth),
                RawStep.Exclusive => this.Members(stored!, at, depth).Join(this.Part(pointee, Exclusive, depth)),
                _ => TyCtxt.RawStated(layer) ? new(false, MirOutcome.Decided) : this.Part(pointee, Exclusive, depth),
            };
        }

        // What an Exclusive layer's members admit past it (TyCtxt.RawMembers): each exclusive member's referent past an exclusive
        // reference, or the Limit where the members are unpublished (an incomplete walk, a layer a Type argument supplies).
        private Facts Members(BoundType stored, ReadOnlySpan<int> at, int depth)
        {
            var first = members.Count;
            var found = types.RawMembers(stored, this.part, at, members) ? default : new Facts(false, MirOutcome.Limit);
            for (var i = first; !found.Writes.IsDecided && i < members.Count; i++)
            {
                found = found.Join(members[i].Exclusive ? this.Part(members[i].Referent, Exclusive, depth) : default);
            }

            members.RemoveRange(first, members.Count - first);
            return found;
        }

        // What a `during self` layer admits past it (TyCtxt.SelfAdmitted, SPEC 5.7 Calls; the owner's Q2 (a)): the write fact where the
        // holding instance has an Origin argument, else its pointee and the instance's Type arguments by their actual Types, in the value's
        // own storage.
        private Facts Admitted(BoundType stored, BoundType pointee, int depth)
        {
            if (TyCtxt.SelfAdmitted(stored, out var arguments))
            {
                return new(false, MirOutcome.Decided);
            }

            var found = this.Part(pointee, Own, depth);
            for (var i = 0; i < arguments.Count; i++)
            {
                found = found.Join(this.Part(arguments[i], Own, depth));
            }

            return found;
        }

        // A part outside the stored part: a referent, a pointee, a Type argument or a base.
        private Facts Part(BoundType? type, int mode, int depth)
        {
            var stored = this.instance;
            this.instance = null;
            var found = this.Holds(type, mode, depth);
            this.instance = stored;
            return found;
        }

        // A stored part of an instance: a struct's Field `part`, or an enum's Case `part` whose payload Tuple's components are its first
        // inline positions.
        private Facts Stored(BoundType instance, int part, BoundType? type, int mode, int depth)
        {
            var (stored, at, first) = (this.instance, this.part, this.start);
            (this.instance, this.part, this.start) = (instance, part, this.top);
            var found = this.Holds(type, mode, depth);
            (this.instance, this.part, this.start) = (stored, at, first);
            return found;
        }

        // An inline position of the stored part: a Tuple's component or a fixed array's element.
        private Facts Inline(int position, BoundType? type, int mode, int depth)
        {
            this.positions[this.top++] = position;
            var found = this.Holds(type, mode, depth);
            this.top--;
            return found;
        }
    }

    // What the holding walk finds in a part: Loan (in the value's own storage) and the guard's outcome where the mode reads the write
    // fact, composed only by Join (MirOutcome's).
    private readonly struct Facts(bool loan, MirOutcome writes)
    {
        internal bool Loan => loan;

        internal MirOutcome Writes => writes;

        internal Facts Join(Facts other) => new(loan || other.Loan, writes.Join(other.Writes));

        // Whether these facts include `done`'s: Loan, and the write fact (the outcome's top) where `done` has it.
        internal bool Covers(Facts done) => (loan || !done.Loan) && (writes.IsDecided || !done.Writes.IsDecided);
    }
}

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

    /// <summary>No part gives the write fact, and an abstract part in the value's own storage writes by provenance (SPEC 15.6.4; DECISIONS
    /// R4-M170): past it only the holders its exclusive Loans and addresses name, and E where its state carries Φ_W (MirBorrows' named
    /// step).</summary>
    Named,

    /// <summary>Some part gives the write fact (decided): past no shared layer, an exclusive reference to storage that may hold a Loan (a
    /// raw layer whose step lets a call write past it counting as one), an abstract part past an exclusive reference, a pair layer, or a
    /// Function value, which may be one (the guard); a Closure value by its environment's parts (DECISIONS R4-M148). Past it the holders
    /// its exclusive Loans and addresses name, never one only a shared Loan names, and E where its state carries Φ_E (SPEC 15.6.4 item 1;
    /// DECISIONS R4-M177).</summary>
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

/// <summary>The holding walk's slots for one key (<see cref="MirHoldingKey"/>; <see cref="MirTypes.HoldsLoans"/>,
/// <see cref="MirDepths"/>), by mode: Own, Exclusive and Own with LoanOnly (Exclusive with LoanOnly never occurs).</summary>
[InlineArray(3)]
internal struct MirModeDepths
{
    private byte mode;
}

/// <summary>What a part's facts in the holding walk are a function of (DECISIONS R4-M157, BU-58): its Type, and for an inline Tuple or
/// fixed array of a stored part (Stored) the Type the declaration writes there with the holding instance, which
/// <see cref="TyCtxt.Holdings"/> and <see cref="TyCtxt.RawMembers"/> read beside the kept layer. Where the declaration writes nothing
/// there (Written null), each raw layer below steps by its own Origin with no member, so the part's facts are its Type's in a stored part
/// (Holder null). Any other part is its Type alone.</summary>
/// <param name="Type">The part's Type.</param>
/// <param name="Stored">Whether it is an inline Tuple or fixed array of a stored part.</param>
/// <param name="Written">The Type the declaration writes at its positions, or null.</param>
/// <param name="Holder">The instance holding the stored part where <paramref name="Written"/> is not null, else null.</param>
internal readonly record struct MirHoldingKey(BoundType Type, bool Stored, BoundType? Written, BoundType? Holder);

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

    /// <summary>Gets whether a value of a Type may hold a Loan (SPEC 15.6), so that A5 tracks it, and what a call may write past it (A3 E16),
    /// one walk of what the Type holds (<see cref="TyCtxt.Holdings"/>, SPEC 3.3.4) in three modes: a part of its own storage that may hold a
    /// Loan is a borrow, pair case or raw layer (a raw pointer carries the holders it addresses and the Loans it was formed through, SPEC
    /// 15.6 Addresses), a Slice, a Function value or an abstract Type, whose instance may store a borrow its Origins do not show. The guard
    /// is whether it may hold, past no shared layer, an exclusive reference (an abstract part past an exclusive reference counting, as does
    /// a Function value) to storage that may hold a Loan; an abstract part in the value's own storage gives the named fact instead (SPEC
    /// 15.6.4, DECISIONS R4-M170), a part past the bound there the walk's Deep fact, never the write fact (DECISIONS R4-M153); a struct's or
    /// enum's Type argument adds only whether the value may hold one (SPEC 15.3.5). A stored raw layer is read by its entry on the instance
    /// that stores it (SPEC 5.7 Calls, 15.6.4; DECISIONS R4-M147, BU-41, BU-46). Each part is entered by its key
    /// (<see cref="MirHoldingKey"/>) once per query and mode at the shallowest depth the walk meets it at, again where it is met shallower
    /// (<see cref="MirDepths"/>, at most once per depth), and a raw layer is read at each meeting of its keyed parent. So a repeating
    /// recursive Type ends exactly, a Type shared in a DAG is walked once, and the answer is the same in every order of the parts. A level
    /// is one entry: a Case's payload component is one level below its enum and a Dictionary's K and V two, through its entry, so a Dictionary
    /// nest reaches the bound at half the depth of a Tuple's, a named limit (the owner's O-47, PLAN G408), located at the call where a check
    /// rests on the guard. A part past
    /// depth 32, where one grows (SPEC 15.3.5), gives yes for tracking and the guard's Deep where its mode reads the write fact, only where
    /// its key is never entered within the bound in that mode (for a LoanOnly part, nor in its full mode).</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose holdings and stored raw layers' members the walk reads.</param>
    /// <param name="seen">Scratch for the depth each key was entered at in each mode (<see cref="MirModeDepths"/>), cleared first.</param>
    /// <param name="members">Scratch for a stored layer's members, used in stack order and left as given.</param>
    /// <param name="entries">Scratch for the entries the walk is reading, used in stack order and left as given.</param>
    /// <param name="writes">What a call may write past the value.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, TyCtxt types, Dictionary<MirHoldingKey, MirModeDepths> seen, List<RawMember> members, List<Hold> entries, out MirWrites writes)
    {
        seen.Clear();
        var found = new Holding(types, seen, members, entries).Holds(type, null, null, Own, 0);
        foreach (var modes in seen.Values)
        {
            for (var mode = Own; mode <= LoanOnly; mode++)
            {
                var past = modes[mode] == MirDepths.Past && (mode != LoanOnly || !MirDepths.Entered(modes[Own], Bound));
                found = past ? found.Join(Beyond(mode)) : found;
            }
        }

        writes = found.Loan ? Guard(found) : MirWrites.None;
        return found.Loan;
    }

    // What a part past the bound gives in a mode (DECISIONS R4-M153): Loan in the value's own storage, and Deep where the mode reads the
    // write fact. The modes are Own, Exclusive and Own|LoanOnly (LoanOnly's value), the slots of MirModeDepths.
    private static Facts Beyond(int mode) => new((mode & ~LoanOnly) == Own, (mode & LoanOnly) == 0 ? MirOutcome.Deep : MirOutcome.None);

    // What the walk finds at a part that may hold a Loan in a mode: Loan in the value's own storage, the write fact past an exclusive
    // reference from it, but in a LoanOnly mode.
    private static Facts Met(int mode)
        => new((mode & ~LoanOnly) == Own, mode == Exclusive ? MirOutcome.Decided : MirOutcome.None);

    // The guard rule W reads, read off the walk's answer: decided is the write fact (Through), Limit and Deep leave it undecided, else the
    // named fact (Named) where an abstract part gave it. Deep's step check compares W's full step, which covers the named one.
    private static MirWrites Guard(Facts found)
    {
        var writes = found.Writes;
        if (writes.IsDecided)
        {
            return MirWrites.Through;
        }

        if (writes.IsLimit)
        {
            return MirWrites.Limit;
        }

        return writes.IsDeep ? MirWrites.Deep : found.Named ? MirWrites.Named : MirWrites.None;
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

    // HoldsLoans' walk over what a value holds (TyCtxt.Holdings, SPEC 3.3.4), one level at a time, each entry read on with its Part. A part
    // is entered once per key and mode at the shallowest depth it is met at (Enters); a raw layer at each meeting. Past any step but `Inline`
    // the walk leaves the stored part (null, null), where a raw layer reads its own Origin. Binding's storage Limit is no fact here: its
    // entries stay published and the walk's own bound covers the growth (DECISIONS R4-M153), so the walk ignores Holdings' answer. Parts
    // compose by Join, and a mode's walk stops only once it has the facts no other part changes (the write fact being the outcome's top),
    // so a part past the bound it skips adds nothing the answer lacks. `entries` is the walk's scratch, in stack order.
    private readonly struct Holding(TyCtxt types, Dictionary<MirHoldingKey, MirModeDepths> seen, List<RawMember> members, List<Hold> entries)
    {
        // The facts a part adds in its mode: the Join of its entries'.
        internal Facts Holds(BoundType? type, BoundType? written, BoundType? holder, int mode, int depth)
        {
            if (type is null || type.Kind == BoundTypeKind.FunctionItem || (type.Semantics != SemanticsKind.Raw && !this.Enters(type, written, holder, mode, depth)))
            {
                return default;
            }

            var first = entries.Count;
            _ = types.Holdings(type, written, holder, entries);
            var last = entries.Count;
            var found = default(Facts);
            var done = (mode & ~LoanOnly) == Own ? new Facts(true, mode == Own ? MirOutcome.Decided : MirOutcome.None) : Met(mode);
            for (var i = first; i < last && !found.Covers(done); i++)
            {
                found = found.Join(this.Entry(entries[i], type, written, holder, mode, depth + 1));
            }

            entries.RemoveRange(first, last - first);
            return found;
        }

        // Whether the walk enters a part, by what its facts read (DECISIONS R4-M157): an inline Tuple or fixed array of a stored part by the
        // Type its declaration writes there with the holding instance, or by its Type in a stored part where the declaration writes none
        // (Hold.Written is as declared, so another written Type is none here); any other part by its Type. A part past the bound is met,
        // not entered: its slot gives its facts after the walk where it stays Past (HoldsLoans). A LoanOnly mode is not entered where its
        // full mode was as shallow.
        private bool Enters(BoundType type, BoundType? written, BoundType? holder, int mode, int depth)
        {
            var stored = holder is not null && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
            var at = stored && written?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray ? written : null;
            var key = new MirHoldingKey(type, stored, at, at is null ? null : holder);
            ref var modes = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, key, out _); // Used before any recursion moves it.
            if (depth > Bound)
            {
                MirDepths.Meet(ref modes[mode]);
                return false;
            }

            return (mode != LoanOnly || !MirDepths.Entered(modes[Own], depth)) && MirDepths.Enter(ref modes[mode], depth);
        }

        // SPEC 3.3.4's rows as A5 reads them: what one entry adds in a mode. `Inline`, a safe `Own` and `Loan` pass the walk on: a Type
        // argument has no address and grants no access (SPEC 15.3.5), so in the value's own storage it gives Loan facts only, while past an
        // exclusive reference a call may replace the whole value, and it stays Exclusive. `Abstract` writes by provenance in the value's own
        // storage (SPEC 15.6.4; DECISIONS R4-M170). Every other step is a part that may hold a Loan: its mode's fact (Met), and in the value's
        // own storage what lies past it (Past).
        private Facts Entry(in Hold entry, BoundType layer, BoundType? written, BoundType? holder, int mode, int depth) => entry.Step switch
        {
            HoldStep.Inline => this.Holds(entry.Part, entry.Written, entry.Holder, mode, depth),
            HoldStep.Own when (entry.Flags & HoldFlags.Raw) == 0 => this.Holds(entry.Part, null, null, mode, depth),
            HoldStep.Loan => this.Holds(entry.Part, null, null, (mode & ~LoanOnly) == Own ? mode | LoanOnly : mode, depth),
            HoldStep.Abstract => mode == Own ? new(true, MirOutcome.None, named: true) : Met(mode),
            HoldStep.Unsupported => new((mode & ~LoanOnly) == Own, (mode & LoanOnly) == 0 ? MirOutcome.Limit : MirOutcome.None),
            HoldStep.Own or HoldStep.Address or HoldStep.Static or HoldStep.Shared or HoldStep.Exclusive or HoldStep.Erased
                => Met(mode).Join(mode == Own ? this.Past(entry, layer, written, holder, depth) : default),
            _ => throw new InvalidOperationException($"HoldStep {entry.Step} has no row (InternalInvariant)."),
        };

        // What lies past a layer of the value's own storage (DECISIONS R4-M147, R4-M151): by what the entry holds, the write fact where
        // that is storage the Type does not state (a Function value's environment; a layer a declaration wrote without an Origin; a
        // `during self` layer's values of Origins that may keep an exclusive Loan); by its step, nothing past a shared one, its Part in the
        // value's own storage past an own step, and past any other an exclusive reference to its Part, with a stored layer's exclusive
        // members. `layer`, `written` and `holder` are the walk's key at the Type holding the entry, which RawMembers reads; the entry's
        // are its Part's.
        private Facts Past(in Hold entry, BoundType layer, BoundType? written, BoundType? holder, int depth) => (entry.Step, entry.Holds) switch
        {
            (HoldStep.Erased, _) or (_, HoldContent.Any) => new(false, MirOutcome.Decided),
            (_, HoldContent.AnyOf) => (entry.Flags & HoldFlags.ExclusiveOrigin) != 0 ? new(false, MirOutcome.Decided) : default,
            (HoldStep.Shared, _) => default,
            (HoldStep.Own, _) => this.Holds(entry.Part, null, null, Own, depth),
            (_, HoldContent.Members) when holder is not null
                => this.Members(layer, written, holder, depth).Join(this.Holds(entry.Part, null, null, Exclusive, depth)),
            _ => this.Holds(entry.Part, null, null, Exclusive, depth),
        };

        // What a stored layer's members admit past it (TyCtxt.RawMembers): each exclusive member's referent past an exclusive reference, or
        // the Limit where the members are unpublished (an incomplete walk, a layer a Type argument supplies).
        private Facts Members(BoundType layer, BoundType? written, BoundType holder, int depth)
        {
            var first = members.Count;
            var found = types.RawMembers(layer, written, holder, members) ? default : new Facts(false, MirOutcome.Limit);
            for (var i = first; !found.Writes.IsDecided && i < members.Count; i++)
            {
                found = found.Join(members[i].Exclusive ? this.Holds(members[i].Referent, null, null, Exclusive, depth) : default);
            }

            members.RemoveRange(first, members.Count - first);
            return found;
        }
    }

    // What the holding walk finds in a part: Loan (in the value's own storage), the guard's outcome where the mode reads the write fact,
    // and Named, an abstract part in the value's own storage there (SPEC 15.6.4), composed only by Join (MirOutcome's).
    private readonly struct Facts(bool loan, MirOutcome writes, bool named = false)
    {
        internal bool Loan => loan;

        internal MirOutcome Writes => writes;

        internal bool Named => named;

        internal Facts Join(Facts other) => new(loan || other.Loan, writes.Join(other.Writes), named || other.Named);

        // Whether these facts include `done`'s: Loan, and the write fact (the outcome's top) where `done` has it.
        internal bool Covers(Facts done) => (loan || !done.Loan) && (writes.IsDecided || !done.Writes.IsDecided);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What a call that writes a value's storage may write past it (A3 E16, DECISIONS R4-M73), from HoldsLoans' walk: the guard
/// of a value that may hold a Loan, three ways.</summary>
internal enum MirWrites : byte
{
    /// <summary>Nothing past it: no exclusive reference to storage that may hold a Loan.</summary>
    None,

    /// <summary>It may hold, past no shared layer, an exclusive reference to storage that may hold a Loan (a raw layer whose step lets a
    /// call write its pointee counting as one), or a Function or Closure value or a part past the walk's bound, which may (the
    /// guard).</summary>
    Through,

    /// <summary>It passes the guard only through a Type argument of a struct or enum, a pair layer, an abstract part or a stored raw layer
    /// whose Origin steps Exclusive, which may be such a reference (P13's interim, DECISIONS R4-M76, until the guard reads each
    /// part).</summary>
    ThroughArgument,
}

// What a value may hold: raw authority (HoldsRaw) and Loans with what a call may write past them (HoldsLoans), each a walk of its parts.
internal static partial class MirTypes
{
    // The holding walk's modes, by where a part lies: in the value's own storage or past an exclusive reference from it; with Argument,
    // the exclusive reference lies in a struct's or enum's Type argument, a pair layer, an abstract part or past a raw layer stepping
    // Exclusive.
    private const int Own = 0;
    private const int Exclusive = 1;
    private const int Argument = 2;

    // The facts the holding walk finds.
    private const int Loan = 1;
    private const int Write = 2;
    private const int WriteByArgument = 4;

    // The deepest part the holding walks enter: past it a part may be anything (SPEC 15.3.5's growing recursion).
    private const int Bound = 32;

    /// <summary>Gets whether a value of a Type holds a raw pointer, an rc handle or a Weak of one at any depth, so that reading it from an
    /// immutable static obtains authority from the environment (SPEC 8.11.2); a referent reached through the value counts too. Each Type
    /// is walked once per query, as HoldsLoans' are, so a repeating recursive Type ends exactly; past depth 32 the answer is yes.</summary>
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
    /// struct's Fields and base and an enum's payloads, and its components: elements, an owning handle's payload, and a Type argument,
    /// which its storage may hold behind a raw pointer (a library collection, its iterators) or as a Loan or a Weak (SPEC 3.2.2, 15.3.5);
    /// a function item holds none. The same walk gives what a call may write past the value (A3 E16): its guard, whether it may hold, past
    /// no shared layer, an exclusive reference (a pair layer's exclusive case and an abstract part counting, as do a Function or Closure
    /// value and a part past the bound) to storage that may hold a Loan, concretely or only through a struct's or enum's Type argument, a
    /// pair layer or an abstract part. Past a raw layer of the value's own storage (DECISIONS R4-M116, design B′ §4.4): a layer a struct's
    /// Field or an enum's Case payload stores, at inline positions, by its step (TyCtxt.RawStep): `self`, the holding value's own storage;
    /// a slot kept only by `ref`, nothing; another Origin, its pointee by argument; none or `static`, an exclusive reference to its pointee;
    /// any other layer (a local's or parameter's own Type, a Type argument's) an exclusive reference to its pointee. Each Type is walked
    /// once per query and mode, a raw layer and the inline Tuples and fixed arrays of a stored part once per part, so a repeating recursive
    /// Type ends exactly; past depth 32, where one grows (SPEC 15.3.5), the answer is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose stored raw layers' steps the walk reads.</param>
    /// <param name="seen">Scratch for the modes each Type was walked in, cleared first.</param>
    /// <param name="writes">What a call may write past the value.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, TyCtxt types, Dictionary<BoundType, byte> seen, out MirWrites writes)
    {
        seen.Clear();
        var walk = new Holding(types, seen, stackalloc int[Bound + 2]);
        var found = walk.Holds(type, Own, 0);
        writes = ((found & Loan) != 0, (found & Write) != 0, (found & WriteByArgument) != 0) switch
        {
            (true, true, _) => MirWrites.Through,
            (true, false, true) => MirWrites.ThroughArgument,
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
    private ref struct Holding(TyCtxt types, Dictionary<BoundType, byte> seen, Span<int> positions)
    {
        private readonly Span<int> positions = positions; // The inline positions of the stored parts the walk is inside, outermost first.
        private BoundType? instance; // The struct or enum whose stored part the walk is inside (null: none).
        private int part;
        private int start; // The stored part's first inline position.
        private int top; // The positions in use.

        // The facts a part adds in its mode, each (Type, mode) once per query (an Argument mode not after its concrete one), a raw layer and
        // an inline Tuple or fixed array of a stored part once per part. A part that may hold a Loan (a borrow, pair or raw layer, a Slice, a
        // Function or Closure value, an abstract part, a part past the bound) gives Loan in the value's own storage, and Write past an
        // exclusive reference from it, or WriteByArgument in an Argument mode. In the value's own storage an exclusive reference, a
        // UniqSlice's elements, a pair layer's exclusive case and a raw layer's pointee by its step (Pointee) are walked past, and an
        // abstract part, a callable and a part past the bound may be such a reference; a struct's or enum's Type arguments lead to an
        // Argument mode. A mode's walk stops once it has the facts no other part changes.
        internal int Holds(BoundType? type, int mode, int depth)
        {
            if (type is null || type.Kind == BoundTypeKind.FunctionItem)
            {
                return 0;
            }

            var own = (mode & ~Argument) == Own;
            var exclusive = (mode & Argument) != 0 ? WriteByArgument : Write;
            var met = own ? Loan : exclusive;
            if (type.Semantics == SemanticsKind.Raw)
            {
                return met | (own && type.Components.Count > 0 ? this.Pointee(type.Components[0], mode, depth + 1) : 0);
            }

            var inline = this.instance is not null && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
            ref var modes = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, type, out _);
            if (!inline && (modes & ((1 << mode) | (1 << (mode & ~Argument)))) != 0)
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
                return met | (own ? this.Part(below, Exclusive | Argument, depth + 1) : 0);
            }

            if (type.IsAbstract)
            {
                return met | (own ? WriteByArgument : 0);
            }

            var referent = own && (IsExclusive(type) || View(type) == SemanticsKind.Uniq) && type.Components.Count > 0
                ? this.Part(type.Components[0], Exclusive | (mode & Argument), depth + 1) : 0;
            if (IsReference(type) || type.Kind == BoundTypeKind.Slice)
            {
                return met | referent;
            }

            var found = referent;
            var done = own ? Loan | exclusive : met;
            var arguments = own && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ? mode | Argument : mode;
            for (var i = 0; i < type.Components.Count && (found & done) != done; i++)
            {
                found |= inline ? this.Inline(i, type.Components[i], arguments, depth + 1) : this.Part(type.Components[i], arguments, depth + 1);
            }

            for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type) && (found & done) != done; i++)
            {
                found |= this.Stored(type, i, AdtDef.FieldType(type, i), mode, depth + 1);
            }

            var payloads = AdtDef.CaseTypes(type) ?? [];
            for (var c = 0; c < payloads.Length && (found & done) != done; c++)
            {
                found |= this.Stored(type, c, payloads[c], mode, depth + 1);
            }

            return found | ((found & done) != done ? this.Part(AdtDef.Base(type), mode, depth + 1) : 0);
        }

        // What lies past a raw layer of the value's own storage (B′ §4.4): for a layer the stored part holds, by its step: `self`, the holding
        // value's own storage, walked in the mode; a slot kept only by `ref`, nothing; another Origin, its pointee by argument (R-N1); none or
        // `static`, an exclusive reference to the pointee; for any other layer, an exclusive reference to the pointee.
        private int Pointee(BoundType pointee, int mode, int depth)
            => (this.instance is { } stored ? types.RawStep(stored, this.part, this.positions[this.start..this.top]) : RawStep.Unstated) switch
            {
                RawStep.Own => this.Part(pointee, mode, depth),
                RawStep.Shared => 0,
                RawStep.Exclusive => this.Part(pointee, Exclusive | Argument, depth),
                _ => this.Part(pointee, Exclusive | (mode & Argument), depth),
            };

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

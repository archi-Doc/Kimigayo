// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What a call that writes a value's storage may write past it (A3 E16, DECISIONS R4-M73), from HoldsLoans' walk: the guard
/// of a value that may hold a Loan, three ways, and for one that holds none whether its raw pointers lead to storage that may.</summary>
internal enum MirWrites : byte
{
    /// <summary>Nothing past it: no exclusive reference to storage that may hold a Loan, or no Loan and no such raw pointer.</summary>
    None,

    /// <summary>It holds no Loan but, past no reference, a raw pointer whose pointee may: storage the body does not own (E).</summary>
    Raw,

    /// <summary>It may hold, past no shared layer or raw pointer, an exclusive reference to storage that may hold a Loan or a raw pointer
    /// whose pointee may, or a Function or Closure value or a part past the walk's bound, which may (the guard).</summary>
    Through,

    /// <summary>It passes the guard only through a Type argument of a struct or enum, a pair layer or an abstract part, which may be such
    /// a reference (P13's interim, DECISIONS R4-M76, until the guard reads each part).</summary>
    ThroughArgument,
}

// What a value may hold: raw authority (HoldsRaw) and Loans with what a call may write past them (HoldsLoans), each a walk of its parts.
internal static partial class MirTypes
{
    // The holding walk's modes, by where a part lies: in the value's own storage, past a raw pointer from it, past an exclusive reference
    // from it, past a raw pointer from there; with Argument, the exclusive reference lies in a struct's or enum's Type argument, a pair
    // layer or an abstract part.
    private const int Own = 0;
    private const int RawPointee = 1;
    private const int Exclusive = 2;
    private const int Argument = 4;

    // The facts the holding walk finds.
    private const int Loan = 1;
    private const int RawLoan = 2;
    private const int Write = 4;
    private const int WriteByArgument = 8;

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

    /// <summary>Gets whether a value of a Type may hold a Loan (SPEC 15.6), so that A5 tracks it: a part of it is a borrow or pair layer, a
    /// Slice, a Function or Closure value, or an abstract Type, whose instance may store a borrow its Origins do not show. Its parts are its
    /// own storage, a struct's Fields and base and an enum's payloads, and its components: elements, an owning handle's payload, and a
    /// Type argument, which its storage may hold behind a raw pointer (a library collection, its iterators) or as a Loan or a Weak (SPEC
    /// 3.2.2, 15.3.5). A raw pointer holds none (SPEC 5.2.2: its borrow ends at once), nor a function item. The same walk gives what a call
    /// may write past the value (A3 E16): its guard, whether it may hold, past no shared layer or raw pointer, an exclusive reference
    /// (a pair layer's exclusive case and an abstract part counting, as do a Function or Closure value and a part past the bound) to
    /// storage that may hold a Loan or a raw pointer whose pointee may, concretely or only through a struct's or enum's Type argument, a
    /// pair layer or an abstract part; and, for a value that holds no Loan, whether such a raw pointer lies past no reference. Each Type is
    /// walked once per query and mode, so a repeating recursive Type ends exactly; past depth 32, where one grows (SPEC 15.3.5), the answer
    /// is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="seen">Scratch for the modes each Type was walked in, cleared first.</param>
    /// <param name="writes">What a call may write past the value.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, Dictionary<BoundType, byte> seen, out MirWrites writes)
    {
        seen.Clear();
        var found = Holds(type, seen, Own, 0);
        writes = ((found & Loan) != 0, (found & Write) != 0, (found & WriteByArgument) != 0, (found & RawLoan) != 0) switch
        {
            (true, true, _, _) => MirWrites.Through,
            (true, false, true, _) => MirWrites.ThroughArgument,
            (false, _, _, true) => MirWrites.Raw,
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

        if (depth > 32 || type.Semantics is SemanticsKind.Raw or SemanticsKind.Rc)
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

    // HoldsLoans' walk: the facts a part adds in its mode, each (Type, mode) once per query (an Argument mode not after its concrete one).
    // A part that may hold a Loan (a borrow or pair layer, a Slice, a Function or Closure value, an abstract part, a part past the bound)
    // gives Loan in the value's own storage, RawLoan past a raw pointer from it, and Write past an exclusive reference from it, or
    // WriteByArgument in an Argument mode. In the value's own storage an exclusive reference, a UniqSlice's elements and a pair layer's
    // exclusive case are walked past, and an abstract part, a callable and a part past the bound may be such a reference; a struct's or
    // enum's Type arguments lead to an Argument mode. A mode's walk stops once it has the facts no other part changes.
    private static int Holds(BoundType? type, Dictionary<BoundType, byte> seen, int mode, int depth)
    {
        if (type is null || type.Kind == BoundTypeKind.FunctionItem)
        {
            return 0;
        }

        ref var modes = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, type, out _);
        if ((modes & ((1 << mode) | (1 << (mode & ~Argument)))) != 0)
        {
            return 0;
        }

        modes |= (byte)(1 << mode);
        var place = mode & ~Argument;
        var exclusive = (mode & Argument) != 0 ? WriteByArgument : Write;
        var met = place switch
        {
            Own => Loan,
            RawPointee => RawLoan,
            _ => exclusive,
        };
        if (type.Semantics == SemanticsKind.Raw)
        {
            return place is Own or Exclusive && type.Components.Count > 0 ? Holds(type.Components[0], seen, mode + RawPointee, depth + 1) : 0;
        }

        var own = place == Own;
        if (depth > 32 || type.Kind is BoundTypeKind.Function or BoundTypeKind.Closure)
        {
            return met | (own ? exclusive : 0);
        }

        if (TyCtxt.Layer(type, out var below, out _) == HirPlaceKind.PairLayer)
        {
            return met | (own ? Holds(below, seen, Exclusive | Argument, depth + 1) : 0);
        }

        if (type.IsAbstract)
        {
            return met | (own ? WriteByArgument : 0);
        }

        var referent = own && (IsExclusive(type) || View(type) == SemanticsKind.Uniq) && type.Components.Count > 0
            ? Holds(type.Components[0], seen, Exclusive | (mode & Argument), depth + 1) : 0;
        if (IsReference(type) || type.Kind == BoundTypeKind.Slice)
        {
            return met | referent;
        }

        var found = referent;
        var done = own ? Loan | exclusive : met;
        var arguments = own && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ? mode | Argument : mode;
        for (var i = 0; i < type.Components.Count && (found & done) != done; i++)
        {
            found |= Holds(type.Components[i], seen, arguments, depth + 1);
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type) && (found & done) != done; i++)
        {
            found |= Holds(AdtDef.FieldType(type, i), seen, mode, depth + 1);
        }

        foreach (var payload in AdtDef.CaseTypes(type) ?? [])
        {
            found |= (found & done) != done ? Holds(payload, seen, mode, depth + 1) : 0;
        }

        return found | ((found & done) != done ? Holds(AdtDef.Base(type), seen, mode, depth + 1) : 0);
    }
}

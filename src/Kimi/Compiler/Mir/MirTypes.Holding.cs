// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// What a value may hold: raw authority (HoldsRaw) and Loans (HoldsLoans), each a walk of its parts.
internal static partial class MirTypes
{
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
    /// 3.2.2, 15.3.5). A raw pointer holds none (SPEC 5.2.2: its borrow ends at once), nor a function item. Each Type is walked once per
    /// query, so a repeating recursive Type ends exactly; past depth 32, where one grows (SPEC 15.3.5), the answer is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="seen">Scratch for the Types walked, cleared first.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, HashSet<BoundType> seen)
    {
        seen.Clear();
        return Holds(type, seen, 0);
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

    // HoldsLoans' walk: a Type seen before adds nothing.
    private static bool Holds(BoundType? type, HashSet<BoundType> seen, int depth)
    {
        if (type is null || type.Semantics == SemanticsKind.Raw || type.Kind == BoundTypeKind.FunctionItem || !seen.Add(type))
        {
            return false;
        }

        if (depth > 32 || type.IsAbstract || type.Kind is BoundTypeKind.Slice or BoundTypeKind.Function or BoundTypeKind.Closure || IsReference(type))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (Holds(type.Components[i], seen, depth + 1))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Holds(AdtDef.FieldType(type, i), seen, depth + 1))
            {
                return true;
            }
        }

        foreach (var payload in AdtDef.CaseTypes(type) ?? [])
        {
            if (Holds(payload, seen, depth + 1))
            {
                return true;
            }
        }

        return Holds(AdtDef.Base(type), seen, depth + 1);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>The Type facts MIR decides by, each one walk of a Type's inline storage: its Tuple, fixed-array and Closure components,
/// a struct's Fields and base and an enum's Case payloads (plan 5.5).</summary>
internal static class MirTypes
{
    /// <summary>Gets whether a value of a Type may own what its destruction releases (SPEC 16.3), the one rule by which the builder drops
    /// an old value: an owning handle, a managed sequence or string, a Function value's environment, a destructor, or a Type its
    /// instance decides, anywhere in its inline storage; references, raw pointers, Slices and scalars own nothing.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The walk's depth; past 32 the answer is yes.</param>
    /// <returns>Whether drop elaboration has to consider the value.</returns>
    internal static bool MayNeedDrop(BoundType? type, int depth = 0) => type is not null && (depth > 32 || type.IsAbstract || type.Kind switch
    {
        BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Function => true,
        BoundTypeKind.Semantics => type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc,
        BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure => Any(type.Components, depth, false),
        _ => ReferenceEquals(type, BoundType.String) || AdtDef.Destructor(type) is not null || Stored(type, depth, false),
    });

    /// <summary>Gets whether a value of a Type is a reference whose Loans an EndBorrow ends: a ref, uniq, objref or objuniq, or a pair layer.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the value is a reference.</returns>
    internal static bool IsReference(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } ||
            (TyCtxt.Layer(type, out _, out var binder) is var _ && binder is not null);

    /// <summary>Gets whether a value of a Type holds a raw pointer, an rc handle or a Weak of one at any depth, so that reading it from an
    /// immutable static obtains authority from the environment (SPEC 8.4.10.2); a referent reached through the value counts too.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The walk's depth; past 32 the answer is yes.</param>
    /// <returns>Whether the value holds raw authority.</returns>
    internal static bool HoldsRaw(BoundType? type, int depth = 0)
        => type is not null && (depth > 32 || type.Semantics is SemanticsKind.Raw or SemanticsKind.Rc || Any(type.Components, depth, true) || Stored(type, depth, true));

    private static bool Any(IReadOnlyList<BoundType> types, int depth, bool raw)
    {
        for (var i = 0; i < types.Count; i++)
        {
            if (raw ? HoldsRaw(types[i], depth + 1) : MayNeedDrop(types[i], depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // A struct's Fields and base, an enum's payload Tuples.
    private static bool Stored(BoundType type, int depth, bool raw)
    {
        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (raw ? HoldsRaw(AdtDef.FieldType(type, i), depth + 1) : MayNeedDrop(AdtDef.FieldType(type, i), depth + 1))
            {
                return true;
            }
        }

        return (AdtDef.Base(type) is { } parent && (raw ? HoldsRaw(parent, depth + 1) : MayNeedDrop(parent, depth + 1))) ||
            (AdtDef.CaseTypes(type) is { } cases && Any(cases, depth, raw));
    }
}

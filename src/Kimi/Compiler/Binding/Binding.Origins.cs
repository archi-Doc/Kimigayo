// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Origin expressions (SPEC 15.3.1, 15.3.6): atoms interned per owner (a Borrow's root occurrence, else the binder) and AtomKey
// (OriginAtom, kept in the owner's module's NodeColumns: OriginAtomRows, DECISIONS BU-220), meets interned in one canonical operand
// order (Meet, CompareOrigins; over the library layer's frozen level and the compilation's, PassPart.Levels, PLAN G949 Stage 2 U4) and the
// context-free outlives facts (OriginOutlives); an obligation's record is
// Binding.Obligations.cs's. Declaration schemas are Binding.Origins.Schemas.cs's; a Type position's written and omitted Origins
// Binding.Origins.Annotations.cs's.
public sealed partial class Binding
{
    // An owner-keyed interner's row key (KeyedRow: originAtoms, openOrigins): an atom's kind, its slot, then whether it names one Borrow
    // (ruling G922-19). With its owner it is the atom's whole identity: a row's atoms are its owner's own (the owner their binder) or name
    // the Borrow rooted at it (BorrowRoot, their binder that root's declaration: Binding.PlaceAtom).
    private static readonly Func<BoundOrigin, (OriginKind Kind, int Slot, bool HasBorrowRoot)> AtomKey = static x => (x.Kind, x.Slot, x.BorrowRoot is not null);

    private readonly OriginAtomRows originAtoms;
    private readonly Dictionary<int, List<BoundOrigin>> originExpressions = new();
    private long originRowProbes;

    // The program's atoms at binders of the frozen library table (OriginAtomRows; ruling 203 (4)): the family's column form over the
    // library's ids (DECISIONS BU-220), outside the frozen layer; emptied where the item contexts are (Invalidate), whose memoized plans
    // hold its atoms, never per pass, so one key keeps one atom object across Binds until an edit (G949 ruling 205 N4).
    private PagedColumn<BoundOrigin?[]?> programAtoms;

    /// <summary>Gets the entries the owner-keyed Origin interners' row searches (KeyedRow: originAtoms, openOrigins) probed over the
    /// Binding's lifetime, which a scaling check reads.</summary>
    internal long OriginRowWork => this.originRowProbes;

    internal static bool IsBorrow(SemanticsKind semantics) => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    private static bool IsExclusive(SemanticsKind semantics) => semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    private static int CompareOrigins(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        var order = ((int)a.Kind).CompareTo((int)b.Kind);
        if (order != 0)
        {
            return order;
        }

        if ((a.BorrowRoot ?? a.Binder) is { } left && (b.BorrowRoot ?? b.Binder) is { } right)
        {
            order = CompareNodes(left, right);
            if (order != 0)
            {
                return order;
            }
        }

        order = a.Slot.CompareTo(b.Slot);
        if (order != 0)
        {
            return order;
        }

        order = a.Operands.Count.CompareTo(b.Operands.Count);
        for (var i = 0; order == 0 && i < a.Operands.Count; i++)
        {
            order = CompareOrigins(a.Operands[i], b.Operands[i]);
        }

        // The binder last (ruling G922-19): an atom naming a Borrow and its root's own atom share kind, owner and slot.
        return order != 0 || a.Binder is not { } x || b.Binder is not { } y ? order : CompareNodes(x, y);
    }

    // A declaration node's place among every source: its module, its source path, its span and, for a source-less generated fragment,
    // its immutable source snapshot. Never order by addresses.
    private static int CompareNodes(Koto left, Koto right)
    {
        var order = StringComparer.Ordinal.Compare(left.CodeContext.Kotonoha.Name, right.CodeContext.Kotonoha.Name);
        if (order == 0)
        {
            order = StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.Path, right.CodeContext.SourceDocument?.Path);
        }

        if (order == 0)
        {
            order = left.Span.Start.CompareTo(right.Span.Start);
        }

        if (order == 0)
        {
            order = left.Span.End.CompareTo(right.Span.End);
        }

        return order != 0 ? order : StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.SourceText, right.CodeContext.SourceDocument?.SourceText);
    }

    /// <summary>Proves only context-independent outlives facts; lack of proof is not equality.</summary>
    private static bool OriginOutlives(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b) || a.Kind is OriginKind.Static or OriginKind.Anchor)
        {
            return true;
        }

        if (UnstatedRelation(a, b) is { } decided)
        {
            return decided;
        }

        if (a.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < a.Operands.Count; i++)
            {
                if (!OriginOutlives(a.Operands[i], b))
                {
                    return false;
                }
            }

            return true;
        }

        if (b.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < b.Operands.Count; i++)
            {
                if (OriginOutlives(a, b.Operands[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnchor(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Anchor)
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (origin.Operands[i].Kind == OriginKind.Anchor)
            {
                return true;
            }
        }

        return false;
    }

    // The atom an owner-keyed interner puts at its owner's key (KeyedRow: originAtoms, openOrigins), which is its whole identity: its owner
    // (its BorrowRoot, else its binder) and its row key (AtomKey).
    private static BoundOrigin Interned(BoundOrigin atom, Koto owner, (OriginKind Kind, int Slot, bool HasBorrowRoot) key)
        => ReferenceEquals(atom.BorrowRoot ?? atom.Binder, owner) && AtomKey(atom) == key ? atom
            : throw new InvalidOperationException($"{atom} was interned at ({owner}, {key}) (InternalInvariant; DECISIONS BU-220).");

    // The atom of `binder`'s slot, owned by `borrowRoot` when it names one Borrow (PlaceAtom alone passes it) and else by its binder.
    private BoundOrigin OriginAtom(Koto binder, OriginKind kind, int slot, string? name = null, Koto? borrowRoot = null)
    {
        var key = (borrowRoot ?? binder, kind, slot, borrowRoot is not null);
        if (!this.originAtoms.TryGetValue(key, out var origin) || (name is not null && origin.Name != name))
        {
            // An edited declaration may reuse a slot with a different name. Keep old expressions immutable while
            // rebuilding the current signature; unchanged passes retain the same interned atom.
            this.originAtoms[key] = origin = new(kind, binder, slot, name) { Body = this.formingBody, BorrowRoot = borrowRoot };
        }

        return origin;
    }

    private BoundOrigin Meet(BoundOrigin a, BoundOrigin b)
    {
        // An anchor outlives every Origin but is no lifetime bound: it names the Loan of a raw Place borrow, so a meet keeps it.
        if (ReferenceEquals(a, b))
        {
            return a;
        }

        if (a.Kind == OriginKind.Static || (OriginOutlives(a, b) && !HasAnchor(a)))
        {
            return b;
        }

        if (b.Kind == OriginKind.Static || (OriginOutlives(b, a) && !HasAnchor(b)))
        {
            return a;
        }

        var capacity = (a.Kind == OriginKind.Intersection ? a.Operands.Count : 1) + (b.Kind == OriginKind.Intersection ? b.Operands.Count : 1);
        var scratch = this.originScratch.Rent(capacity);
        try
        {
            var count = 0;
            Add(a);
            Add(b);
            // Small Origin sets: insertion sort avoids a comparison delegate or boxed enumerator.
            for (var i = 1; i < count; i++)
            {
                var item = scratch[i];
                var j = i;
                while (j > 0 && CompareOrigins(item, scratch[j - 1]) < 0)
                {
                    scratch[j] = scratch[j - 1];
                    j--;
                }

                scratch[j] = item;
            }

            var hash = default(HashCode);
            for (var i = 0; i < count; i++)
            {
                hash.Add(RuntimeHelpers.GetHashCode(scratch[i]));
            }

            // The one find over the two levels (PassPart.Levels; PLAN G949 Stage 2 U4).
            var key = hash.ToHashCode();
            var levels = PassPart.Interned(this.Library, this.Library.Layer.Intersections, this.originExpressions);
            foreach (var level in levels)
            {
                if (level.TryGetValue(key, out var bucket) && Match(bucket, scratch, count) is { } found)
                {
                    return found;
                }
            }

            var open = levels.Open;
            if (!open.TryGetValue(key, out var into))
            {
                open.Add(key, into = new(1));
            }

            var created = new BoundOrigin(OriginKind.Intersection, operands: scratch.AsSpan(0, count).ToArray());
            into.Add(created);
            return created;

            static BoundOrigin? Match(List<BoundOrigin> bucket, BoundOrigin[] scratch, int count)
            {
                for (var i = 0; i < bucket.Count; i++)
                {
                    var candidate = bucket[i];
                    var equal = candidate.Operands.Count == count;
                    for (var j = 0; equal && j < count; j++)
                    {
                        equal = ReferenceEquals(candidate.Operands[j], scratch[j]);
                    }

                    if (equal)
                    {
                        return candidate;
                    }
                }

                return null;
            }

            void Add(BoundOrigin origin)
            {
                if (origin.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < origin.Operands.Count; i++)
                    {
                        Add(origin.Operands[i]);
                    }

                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    if (ReferenceEquals(scratch[i], origin))
                    {
                        return;
                    }
                }

                scratch[count++] = origin;
            }
        }
        finally
        {
            this.originScratch.Return(scratch, clearArray: true);
        }
    }

    /// <summary>The `originAtoms` family's one accessor (DECISIONS BU-220): a kept interner, an owner's atoms (a Borrow's root occurrence,
    /// else the binder) in its module's NodeColumns at its SyntaxId, one KeyedRow of them by AtomKey, never reset; a renumbering
    /// moves the row with its owner. From the freeze (PLAN G949 S1-1; ruling 203 (4)) an atom a later step interns at an owner of the
    /// frozen library table, such as a library requirement member's Input atoms that an Item's call context interns at MIR time
    /// (RequirementItemContext, `Body` the forming body), is a program fact: a row of the program's (programAtoms: the same KeyedRow form
    /// at the owner's library id, emptied with the item contexts at an edit), found after the library's column, so the frozen layer holds
    /// no row a later step made. Its members keep the names and the tuple key its readers use.</summary>
    private readonly struct OriginAtomRows(Binding binding)
    {
        internal BoundOrigin this[(Koto Owner, OriginKind Kind, int Slot, bool HasBorrowRoot) key]
        {
            set
            {
                var atom = Interned(value, key.Owner, (key.Kind, key.Slot, key.HasBorrowRoot));
                if (key.Owner.SyntaxOwner is { Frozen: true } library)
                {
                    binding.programAtoms.Grow(library.Count); // A frozen table's count is final.
                    KeyedRow.Put(ref binding.programAtoms.Ref(key.Owner.SyntaxId), atom, AtomKey, ref binding.originRowProbes);
                }
                else
                {
                    KeyedRow.Put(ref HirTables.Of(key.Owner).Families.OriginAtoms.Ref(key.Owner.SyntaxId), atom, AtomKey, ref binding.originRowProbes);
                }
            }
        }

        internal bool TryGetValue((Koto Owner, OriginKind Kind, int Slot, bool HasBorrowRoot) key, [NotNullWhen(true)] out BoundOrigin? origin)
        {
            var table = key.Owner.SyntaxOwner;
            origin = table is null ? null : KeyedRow.Find(table.Hir.Families.OriginAtoms.Get(key.Owner.SyntaxId), (key.Kind, key.Slot, key.HasBorrowRoot), AtomKey, ref binding.originRowProbes);
            if (origin is null && table is { Frozen: true })
            {
                origin = KeyedRow.Find(binding.programAtoms.Get(key.Owner.SyntaxId), (key.Kind, key.Slot, key.HasBorrowRoot), AtomKey, ref binding.originRowProbes);
            }

            return origin is not null;
        }

        internal void Add((Koto Owner, OriginKind Kind, int Slot, bool HasBorrowRoot) key, BoundOrigin origin)
        {
            if (this.TryGetValue(key, out _))
            {
                throw new ArgumentException($"An atom is already interned at {key}.", nameof(key));
            }

            this[key] = origin;
        }
    }
}

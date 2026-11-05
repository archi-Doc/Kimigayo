// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The C1-F4 view's atom table (2a0-D §3, D4, D7; DECISIONS R4-M101 E4, E5): an atom's identity (resolved at its use, a meet canonical),
// its class in a body by §3's precedence, first match deciding, the part of a constructor's slice it belongs to (E4), and the index MIR
// reads an atom by (OriginAtomIn, at a MIR site).
// Input: an atom, the slice it is interned in and the use or MIR site it is resolved at. Output: the atom's index in the slice's table and
// its part (OriginRowSet.InLayer).
// Invariants: an atom is classed once, on its first appearance in a slice; a class no precedence row gives, and a Dependent atom (an end a
// Binding failure left Unbound, DECISIONS R4-M118, or an unclassified Closure's pending atom, R4-M126 §2.5 (c)), is a broken contract in
// a complete compilation (OriginFinding); roots by the forming body for non-declaration binders and by slot role for declaration binders;
// an atom's part is its rows' part, or for an atom MIR reads its forming body's (DECISIONS R4-M114), and only a Fixed atom is in both
// parts (else a finding).
// Must not: let a slot or binder leave the view (only ProjectionRole does, D7).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // An atom MIR reads (a Ref's or a dereferenced layer's Origin) at a MIR site: resolved at the site's syntax, classed in the body's table
    // with the part its forming body gives, and returned as its index there.
    internal int OriginAtomIn(ExecutableBody body, BoundOrigin origin, SourceRef site)
    {
        var slice = this.OriginRows(body);
        var use = this.compilation.SyntaxOf(site);
        var atom = this.CanonicalOrigin(this.OriginAtUse(origin, use));
        return this.InternAtom(slice, atom, use, PartOf(atom, slice));
    }

    // E4 for an atom MIR reads (DECISIONS R4-M114): in a constructor's slice, the part whose unit formed it (1 the body's own, 2 its
    // layer's, the Field initializers'), a meet its operands' parts; a declaration atom names no part, as Fixed atoms are shared.
    private static byte PartOf(BoundOrigin atom, OriginRowSet slice)
    {
        if (atom.Kind == OriginKind.Intersection)
        {
            byte part = 0;
            for (var i = 0; i < atom.Operands.Count; i++)
            {
                part |= PartOf(atom.Operands[i], slice);
            }

            return part;
        }

        return !slice.Layer.IsBody || !atom.Body.IsBody ? (byte)0 : atom.Body == slice.Layer ? (byte)2 : atom.Body == slice.Body ? (byte)1 : (byte)0;
    }

    // A Projection slot's role (2a0-D D7: only the view decodes slots; DECISIONS R4-M101 E5): a declaration binder (a function, accessor
    // or Property) by its slot, any other binder its own place.
    private static ProjectionRole RoleOf(BoundOrigin atom) => atom.Kind != OriginKind.Projection ? ProjectionRole.None : atom.Binder switch
    {
        FunctionKoto when atom.Slot == ClosureValueSlot => ProjectionRole.ClosureValue,
        FunctionKoto when atom.Slot == CallResultSlot => ProjectionRole.CallResult,
        FunctionKoto when atom.Slot == ReceiverSlot => ProjectionRole.Receiver,
        FunctionKoto when atom.Slot == HelperSlot => ProjectionRole.Helper,
        FunctionKoto when atom.Slot <= EnvironmentSlot(0) => ProjectionRole.Environment,
        FunctionKoto or PropertyAccessorKoto => ProjectionRole.Parameter,
        PropertyKoto => ProjectionRole.Storage,
        _ => ProjectionRole.Place,
    };

    // Whether an atom is a root of a body, by the body that formed it (a non-declaration binder) or by its slot's role (E5): parameter j
    // of f is a root of f's body and of each template (f, i) with j < i; a receiver and a helper borrow of their own body; an environment
    // binding and a call result of their Closure's; a Closure literal's value of the body that evaluates it; an accessor's self and value
    // of the accessor's; a Property's storage of each of its accessors' (one atom, whichever formed it first).
    private static bool RootOf(BoundOrigin atom, ProjectionRole role, ExecutableBody body) => role switch
    {
        ProjectionRole.None or ProjectionRole.Place => atom.Body == body,
        ProjectionRole.Storage => body.Definition?.Declaration is PropertyAccessorKoto { Parent: var property } && ReferenceEquals(property, atom.Binder),
        ProjectionRole.ClosureValue => body.IsBody && EvaluatingBody(new(atom.Binder!.BoundSymbol)) == body,
        ProjectionRole.Parameter when atom.Binder is FunctionKoto => DeclaredBy(atom, body) && (body.Index < 0 || (uint)atom.Slot < (uint)body.Index),
        ProjectionRole.Receiver or ProjectionRole.Helper => DeclaredBy(atom, body) && body.Index < 0,
        _ => DeclaredBy(atom, body),
    };

    private static bool DeclaredBy(BoundOrigin atom, ExecutableBody body) => body.Definition is { } symbol && ReferenceEquals(symbol.Declaration, atom.Binder);

    // SPEC 15.3.6: a meet in its canonical form, operands flattened, deduplicated and ordered, static dropped (Meet).
    private BoundOrigin CanonicalOrigin(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return origin;
        }

        var result = BoundOrigin.Static;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            result = this.Meet(result, origin.Operands[i]);
        }

        return result;
    }

    // An atom's index in the slice's table, classed on its first appearance; a class no precedence row gives, or a Dependent one, is a
    // broken contract (OriginFinding records it in a complete compilation only).
    private int InternAtom(OriginRowSet slice, BoundOrigin atom, Koto site, byte part)
    {
        IOriginRowSetBuilder builder = slice;
        if (!builder.Index.TryGetValue(atom, out var index))
        {
            index = builder.Atoms.Count;
            builder.Index.Add(atom, index);
            var entry = this.ClassifyAtom(atom, slice);
            if (entry is null or { Kind: OriginAtomKind.Dependent })
            {
                this.OriginFinding(SourceRef.Of(site));
            }

            builder.Atoms.Add(entry ?? new(atom, OriginAtomKind.Unclassed, RoleOf(atom)));
            builder.Parts.Add(0);
        }

        // E4: the parts share Fixed atoms only, so a chain never crosses from one oracle's part to the other's.
        var before = builder.Parts[index];
        builder.Parts[index] |= part;
        if (before != 3 && builder.Parts[index] == 3 && builder.Atoms[index].Kind != OriginAtomKind.Fixed)
        {
            this.OriginFinding(SourceRef.Of(site));
        }

        return index;
    }

    // SPEC 15.6.5 and 2a0-D §3's precedence (E4, E5): an atom's class in body B, the first row matching; null when none does (a broken
    // contract). Roots, their order given by role (rows 3, 4, 5, 7, 7', 8), and atoms of an enclosing body (row 6) exclude each other.
    private OriginAtomEntry? ClassifyAtom(BoundOrigin atom, OriginRowSet slice)
    {
        switch (atom.Kind)
        {
            case OriginKind.Static:
                return new(atom, OriginAtomKind.Fixed, ProjectionRole.None);
            case OriginKind.Parameter or OriginKind.Input:
                // Rows 1 and 9: B's contract (its or an enclosing declaration's), or another declaration's universal atom (PLAN G195).
                return new(atom, this.InContract(atom.Binder, slice.Body) ? OriginAtomKind.Fixed : OriginAtomKind.Unclassed, ProjectionRole.None);
            case OriginKind.Intersection:
                return this.ClassifyMeet(atom, slice);
            case OriginKind.Unbound:
                return new(atom, OriginAtomKind.Dependent, ProjectionRole.None); // Only a Binding failure leaves one (DECISIONS R4-M118).
        }

        var role = RoleOf(atom);
        if (role is ProjectionRole.None or ProjectionRole.Place && !atom.Body.IsBody)
        {
            return null; // E5: a non-declaration atom first formed outside every body.
        }

        if (PendingAtom(atom))
        {
            // DECISIONS R4-M126 §2.5 (c), D-3: an unclassified Closure's receiver or environment atom, Dependent (R4-M118); only a Closure
            // whose body no binding bound, so a failed one, leaves it after Bind.
            return new(atom, OriginAtomKind.Dependent, role);
        }

        if (OwnCallAtom(atom, slice.Body))
        {
            return new(atom, OriginAtomKind.Fixed, role); // Row 5: B's own call receiver and call result.
        }

        if (RootOf(atom, role, slice.Body) || (slice.Layer.IsBody && RootOf(atom, role, slice.Layer)))
        {
            return new(atom, this.RootKind(atom, role), role);
        }

        if (EnclosingAtom(atom, slice.Body))
        {
            return new(atom, OriginAtomKind.Fixed, role); // Row 6: an enclosing body's atom.
        }

        return new(atom, OriginAtomKind.Unclassed, role); // Row 9: another body's root seen from B.
    }

    // A root's class: an inference or anchor of B Inferred (row 8); a Borrow of static storage formed in B, a site's or an accessor's
    // `storage`, Finite when mutable, Inferred when immutable (rows 7, 7'); a place read through a reference Inferred (row 3's referent
    // clause: a Borrow through a referent, or a stored accessor's instance `storage` through its receiver); any other root Finite (rows 3-5).
    private OriginAtomKind RootKind(BoundOrigin atom, ProjectionRole role)
    {
        if (role == ProjectionRole.None)
        {
            return OriginAtomKind.Inferred;
        }

        if (role is not (ProjectionRole.Place or ProjectionRole.Storage))
        {
            return OriginAtomKind.Finite;
        }

        if (atom.Binder!.BoundSymbol is { } symbol && IsStatic(symbol))
        {
            return IsMutableDeclaration(symbol.Declaration) ? OriginAtomKind.Finite : OriginAtomKind.Inferred;
        }

        return role == ProjectionRole.Storage || this.ReadsReferent(atom.Binder) ? OriginAtomKind.Inferred : OriginAtomKind.Finite;
    }

    // Rows 2, 2' and 9: a meet is one Fixed node when every operand is Fixed, a Dependent node when an operand is Dependent (DECISIONS
    // R4-M118), an Inferred meet node otherwise, and forms no edge when an operand is Unclassed, a located limit that decides first.
    private OriginAtomEntry? ClassifyMeet(BoundOrigin meet, OriginRowSet slice)
    {
        var kind = OriginAtomKind.Fixed;
        for (var i = 0; i < meet.Operands.Count; i++)
        {
            if (this.ClassifyAtom(meet.Operands[i], slice) is not { } operand)
            {
                return null;
            }

            if (operand.Kind == OriginAtomKind.Unclassed)
            {
                return new(meet, OriginAtomKind.Unclassed, ProjectionRole.None);
            }

            kind = kind == OriginAtomKind.Dependent || operand.Kind == OriginAtomKind.Dependent ? OriginAtomKind.Dependent
                : operand.Kind == OriginAtomKind.Fixed ? kind : OriginAtomKind.Inferred;
        }

        return new(meet, kind, ProjectionRole.None);
    }

    // SPEC 15.3.4: a declaration atom is B's contract when its binder owns B's definition or a scope enclosing it.
    private bool InContract(Koto? binder, ExecutableBody body)
    {
        if (this.BodyRoot(body) is not { } root)
        {
            return false;
        }

        for (var scope = this.scopes.GetValueOrDefault(root) ?? root.BoundSymbol?.Scope; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope.Owner, binder))
            {
                return true;
            }
        }

        return ReferenceEquals(root, binder);
    }
}

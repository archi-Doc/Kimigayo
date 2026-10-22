// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The C1-F4 view's atom table (2a0-D §3, D4, D7; DECISIONS R4-M101 E4, E5): an atom's identity (resolved at its use, a meet canonical),
// its class in a body by §3's precedence, first match deciding, and the index MIR reads an atom by (OriginAtomIn, at a MIR site).
// Input: an atom, the slice it is interned in and the use or MIR site it is resolved at. Output: the atom's index in the slice's table.
// Invariants: an atom is classed once, on its first appearance in a slice; a class no precedence row gives is a broken contract
// (OriginFinding); roots by the forming body for non-declaration binders and by slot role for declaration binders.
// Must not: let a slot or binder leave the view (only ProjectionRole does, D7).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // An atom MIR reads (a Ref's or a dereferenced layer's Origin) at a MIR site: resolved at the site's syntax, classed in the body's table,
    // and returned as its index there.
    internal int OriginAtomIn(ExecutableBody body, BoundOrigin origin, SourceRef site)
    {
        var slice = this.OriginRows(body);
        var use = this.compilation.SyntaxOf(site);
        return this.InternAtom(slice, this.CanonicalOrigin(this.OriginAtUse(origin, use)), use, 0);
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

    // An atom's index in the slice's table, classed on its first appearance; a class no precedence row gives is a broken contract.
    private int InternAtom(OriginRowSet slice, BoundOrigin atom, Koto site, byte part)
    {
        IOriginRowSetBuilder builder = slice;
        if (!builder.Index.TryGetValue(atom, out var index))
        {
            index = builder.Atoms.Count;
            builder.Index.Add(atom, index);
            if (this.ClassifyAtom(atom, slice) is not { } entry)
            {
                this.OriginFinding(SourceRef.Of(site));
                entry = new(atom, OriginAtomKind.Unclassed, RoleOf(atom));
            }

            builder.Atoms.Add(entry);
            builder.Parts.Add(0);
        }

        builder.Parts[index] |= part;
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
                return new(atom, OriginAtomKind.Unclassed, ProjectionRole.None);
        }

        var role = RoleOf(atom);
        if (role is ProjectionRole.None or ProjectionRole.Place && !atom.Body.IsBody)
        {
            return null; // E5: a non-declaration atom first formed outside every body.
        }

        if (role is ProjectionRole.ClosureValue or ProjectionRole.CallResult && DeclaredBy(atom, slice.Body))
        {
            return new(atom, OriginAtomKind.Fixed, role); // Row 5: B's own call receiver and call result.
        }

        if (RootOf(atom, role, slice.Body) || (slice.Layer.IsBody && RootOf(atom, role, slice.Layer)))
        {
            return new(atom, this.RootKind(atom, role), role);
        }

        for (var body = EvaluatingBody(slice.Body); body.IsBody; body = EvaluatingBody(body))
        {
            if (RootOf(atom, role, body))
            {
                return new(atom, OriginAtomKind.Fixed, role); // Row 6: an enclosing body's atom.
            }
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

    // Rows 2, 2' and 9: a meet is one Fixed node when every operand is Fixed, an Inferred meet node otherwise, and forms no edge when an
    // operand forms none.
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

            kind = operand.Kind == OriginAtomKind.Fixed ? kind : OriginAtomKind.Inferred;
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

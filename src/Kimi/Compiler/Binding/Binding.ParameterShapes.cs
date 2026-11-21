// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>What a parameter requires of a bare owned Place argument, judged on the outer Semantics of its normalized Type (SPEC 7.4).</summary>
public enum AcquisitionMode : byte
{
    /// <summary><c>owner</c>, <c>obj</c>, <c>rc</c>, <c>arc</c> or <c>raw</c>: the argument is acquired by value.</summary>
    ByValue,

    /// <summary><c>ref</c> or <c>objref</c>: a new shared borrow.</summary>
    Shared,

    /// <summary><c>uniq</c> or <c>objuniq</c>: an exclusive borrow.</summary>
    Exclusive,
}

public sealed partial class Binding
{
    // SPEC 7.4: the check's scratch, one conflicting parameter's parties while they are gathered and its facts while they are recorded
    // (RecordParameterShapes empties both): reused across passes, so a warm rebind of the failure allocates nothing.
    private readonly List<(FunctionKoto Function, int Index, int Role)> parameterShapeParties = [];
    private readonly List<ProblemFact> parameterShapeFacts = [];
    private readonly List<BindingSymbol> parameterShapeScratch = [];

    [Flags]
    private enum AcquisitionModes : byte
    {
        None = 0,
        ByValue = 1,
        Shared = 2,
        Exclusive = 4,
        All = ByValue | Shared | Exclusive,
    }

    // The matching key of a parameter (SPEC 7.4): Value(U), Object(U), either family over U for a pair application, or any key.
    private enum ParameterKey : byte
    {
        Value,
        Object,
        Either,
        Any,
    }

    private static AcquisitionModes ModesOf(SemanticsMask admitted)
    {
        var modes = AcquisitionModes.None;
        if ((admitted & (SemanticsMask.Owning | SemanticsMask.Raw)) != 0)
        {
            modes |= AcquisitionModes.ByValue;
        }

        if ((admitted & (SemanticsMask.Ref | SemanticsMask.ObjRef)) != 0)
        {
            modes |= AcquisitionModes.Shared;
        }

        if ((admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0)
        {
            modes |= AcquisitionModes.Exclusive;
        }

        return modes;
    }

    private static AcquisitionModes ModeFlag(AcquisitionMode mode) => (AcquisitionModes)(1 << (int)mode);

    private static AcquisitionMode FirstMode(AcquisitionModes modes)
        => (modes & AcquisitionModes.ByValue) != 0 ? AcquisitionMode.ByValue : (modes & AcquisitionModes.Shared) != 0 ? AcquisitionMode.Shared : AcquisitionMode.Exclusive;

    private static bool IsSingleMode(AcquisitionModes modes) => modes is AcquisitionModes.ByValue or AcquisitionModes.Shared or AcquisitionModes.Exclusive;

    // A pair slot's whole Type and a pair application name the pair's target symbol; an ordinary slot names a Type parameter.
    private static bool IsPairWhole(BoundType type) => type.Kind == BoundTypeKind.Parameter && type.Symbol?.Kind == BindingSymbolKind.SemanticsTarget;

    // SPEC 7.4: a key that still depends on a slot or a non-constant length, which the check does not resolve (the Note).
    private static bool IsConservative(BoundType type)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
            (type.Kind == BoundTypeKind.FixedArray && type.LengthExpression is not null))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (IsConservative(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // Equality of a normalized outer layer is unconditional only for fixed bindings;
    // inferred slots contribute the explicit correlation retained by JudgeParameterShapes.
    private static bool SameBinding(BoundType a, BoundType b, FunctionKoto fa, FunctionKoto fb, bool sameGenerics)
        => a.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection &&
            a.Kind == b.Kind && (ReferenceEquals(a, b) || (sameGenerics && PermittedSlotSharing(fa, fb) && SignatureEquals(a, b, CallSlotOwner(fa), CallSlotOwner(fb))));

    // SPEC 7.3, 7.4: a shape violation is an error of the group, not of the declaration's own signature or conditions, so the
    // declaration stays a candidate and the calls that select it are checked normally. SPEC 9.4: a misplaced protected form is
    // an error of the modifier alone, so the declaration and its uses are checked as written too. SPEC 8.15, 23.1.1: a definition
    // beyond the case bound keeps its signature, and its parts are bound but not verified per case (Binding.CaseLimits). SPEC 15.8.2,
    // 23.1.2 (DECISIONS R5-M63 (1), R4-M152): a Closure literal whose creation failed after its definition was bound, such as a
    // returned value P8 fails, keeps its definition and so its signature.
    private static bool KeepsSignature(Koto declaration)
        => declaration is DeclarationKoto { BindingState: BindingState.Invalid } &&
            (declaration.BindingFailure is BindingFailure.ProtectedPlacement or BindingFailure.CaseLimit ||
            declaration is FunctionKoto { BindingFailure: BindingFailure.ParameterShapeMismatch or BindingFailure.ReceiverShapeMismatch } ||
            declaration is FunctionKoto { IsAnonymous: true, ClosureDefinition: not null });

    // SPEC 7.4, 10.1, 10.8: explicit Type arguments reach only the declarations whose generic parameter lists have the same
    // length and the same kinds position by position; a length slot takes no explicit Type argument.
    private static bool SameExplicitArgumentShape(FunctionKoto a, FunctionKoto b)
    {
        var count = new FnSig(a).GenericCount;
        if (count != new FnSig(b).GenericCount)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (a.GenericArguments[i] is LengthParameterKoto != b.GenericArguments[i] is LengthParameterKoto)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 7.4: a party's ground of correspondence, its Related role (RecordParameterShapes), read from its parameter's syntax.
    private static string Ground(FunctionKoto function, int index, long role)
        => role == 1 ? $"the external name {function.Parameters[index].ExternalName}"
            : $"position {PositionOf(function, index, role == 2 ? new FnSig(function).ReceiverIndex : -1)} of {(role == 2 ? "a bound" : "an unbound")} call";

    // A parameter's position among the positional parameters of its function, the receiver excepted when it is given.
    private static int PositionOf(FunctionKoto function, int index, int receiver)
    {
        var position = 0;
        for (var i = 0; i < index; i++)
        {
            if (i != receiver && AllowsPositionalArgument(function, i))
            {
                position++;
            }
        }

        return position;
    }

    // The function and index of a parameter whose Type syntax a party's fact names.
    private static (FunctionKoto Function, int Index) PartyParameter(Koto type)
    {
        var node = type.Parent;
        while (node is not (null or FunctionKoto))
        {
            node = node.Parent;
        }

        if (node is FunctionKoto function)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(function.Parameters[i].Type, type))
                {
                    return (function, i);
                }
            }
        }

        throw new InvalidOperationException("A parameter shape party names no parameter Type (InternalInvariant).");
    }

    // Source order, with the declarations of the owning Contract after those it inherits. The lists are tiny, and an insertion
    // sort needs no comparer delegate, so a warm bind allocates nothing.
    private static void OrderDeclarations(List<BindingSymbol> symbols, Koto? owner)
    {
        for (var i = 1; i < symbols.Count; i++)
        {
            var current = symbols[i];
            var j = i - 1;
            while (j >= 0 && Compare(symbols[j], current) > 0)
            {
                symbols[j + 1] = symbols[j];
                j--;
            }

            symbols[j + 1] = current;
        }

        int Compare(BindingSymbol x, BindingSymbol y)
        {
            var own = (ReferenceEquals(x.Declaration.Parent, owner) ? 1 : 0) - (ReferenceEquals(y.Declaration.Parent, owner) ? 1 : 0);
            return owner is not null && own != 0 ? own : CompareDeclarations(x.Declaration, y.Declaration);
        }
    }

    // SPEC 7.4: the acquisition modes a parameter may require (one per admitted case) and its matching key.
    private ParameterShape ShapeOf(BoundType type, Koto function, bool sameGenerics)
    {
        switch (type.Kind)
        {
            case BoundTypeKind.Parameter when IsPairWhole(type):
                return new(ModesOf(this.PairSlotAdmitted(type, function)), ParameterKey.Any, type);
            case BoundTypeKind.Parameter:
                // A function's own slot binds a Semantics other than owner for a bare Place only through explicit Type arguments,
                // which reach the declarations with the same generic count alone (SPEC 10.1, 10.8).
                var own = ReferenceEquals(type.Symbol?.Scope.Owner, function);
                return new(own && !sameGenerics ? AcquisitionModes.ByValue : AcquisitionModes.All, ParameterKey.Any, type);
            case BoundTypeKind.TargetProjection:
                return new(AcquisitionModes.ByValue, ParameterKey.Any, type);
            case BoundTypeKind.AssociatedProjection:
                return new(AcquisitionModes.All, ParameterKey.Any, type);
            case BoundTypeKind.SemanticsApplication:
                return new(ModesOf(type.Symbol?.WholeType is { } whole ? this.PairSlotAdmitted(whole, function) : SemanticsMask.All), ParameterKey.Either, type.Components[0]);
            case BoundTypeKind.Semantics when type.Components.Count == 1:
                return type.Semantics switch
                {
                    SemanticsKind.Ref => new(AcquisitionModes.Shared, ParameterKey.Value, type.Components[0]),
                    SemanticsKind.Uniq => new(AcquisitionModes.Exclusive, ParameterKey.Value, type.Components[0]),
                    SemanticsKind.ObjRef => new(AcquisitionModes.Shared, ParameterKey.Object, type.Components[0]),
                    SemanticsKind.ObjUniq => new(AcquisitionModes.Exclusive, ParameterKey.Object, type.Components[0]),
                    SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc => new(AcquisitionModes.ByValue, ParameterKey.Object, type.Components[0]),
                    _ => new(AcquisitionModes.ByValue, ParameterKey.Value, type),
                };
            default:
                return new(AcquisitionModes.ByValue, ParameterKey.Value, type);
        }
    }

    private SemanticsMask PairSlotAdmitted(BoundType whole, Koto function)
        => this.scopes.TryGetValue(function, out var scope) ? this.AdmittedSemantics(whole, scope) : SemanticsMask.All;

    // SPEC 7.4: two keys overlap when steps 2-4 of the collision test of SPEC 8.9.1 unify them; Value and Object never meet.
    private bool KeysOverlap(in ParameterShape a, in ParameterShape b, FunctionKoto? left = null, FunctionKoto? right = null, bool shared = false)
    {
        if (a.Key == ParameterKey.Any || b.Key == ParameterKey.Any)
        {
            return true;
        }

        if (a.Key != ParameterKey.Either && b.Key != ParameterKey.Either && a.Key != b.Key)
        {
            return false;
        }

        return this.MayUnify(a.Target, b.Target, left, right, shared);
    }

    // SPEC 7.4: whether the parameters i of `earlier` and j of `later` are invalid together, with the colliding case of each.
    private bool ParameterShapesConflict(FunctionKoto earlier, int i, FunctionKoto later, int j, out AcquisitionMode mode, out AcquisitionMode other, out ParameterShape shape, out bool conservative)
    {
        mode = other = default;
        shape = default;
        conservative = false;
        if (new FnSig(earlier).Input(i) is not { } ta || new FnSig(later).Input(j) is not { } tb)
        {
            return false;
        }

        var result = this.JudgeParameterShapes(new(earlier, i, later, j), ta, tb);
        mode = result.Mode;
        other = result.Other;
        shape = result.Shape;
        conservative = result.Conservative;
        return result.Conflict;
    }

    private bool ParameterShapesConflict(BoundType ta, BoundType tb, ParameterShape sa, ParameterShape sb, out AcquisitionMode mode, out AcquisitionMode other, out ParameterShape shape, out bool conservative, bool? overlap = null)
    {
        mode = other = default;
        shape = default;
        conservative = false;
        var union = sa.Modes | sb.Modes;
        if (union == AcquisitionModes.None || IsSingleMode(union) || !(overlap ?? this.KeysOverlap(sa, sb)))
        {
            return false;
        }

        // The colliding case: a mode of the later parameter the earlier one lacks where there is one, and a differing earlier mode.
        var here = sb.Modes & ~sa.Modes;
        mode = FirstMode(here != AcquisitionModes.None ? here : sb.Modes);
        var there = sa.Modes & ~ModeFlag(mode);
        other = FirstMode(there != AcquisitionModes.None ? there : sa.Modes);
        shape = sb;
        conservative = IsConservative(ta) || IsConservative(tb);
        return true;
    }

    // SPEC 7.4: appends to the party store every parameter of `earlier` that corresponds to parameter j of `later` and conflicts
    // with it, in the unbound sequence, the bound sequence and by external name. The first conflict supplies the record's facts.
    private void CollectParameterShapeParties(FunctionKoto earlier, FunctionKoto later, int j, ref ParameterShapeConflict facts, ref bool found)
    {
        var earlierReceiver = new FnSig(earlier).ReceiverIndex;
        var laterReceiver = new FnSig(later).ReceiverIndex;
        var receivers = earlierReceiver >= 0 && laterReceiver >= 0;
        if (AllowsPositionalArgument(later, j))
        {
            // The bound sequence of two instance functions omits the receivers; it is the usual call form, so it is recorded first.
            if (receivers && j != laterReceiver)
            {
                var bound = PositionOf(later, j, laterReceiver);
                var at = ParameterAt(earlier, bound, earlierReceiver);
                if (at >= 0)
                {
                    this.AddParameterShapeParty(earlier, at, later, j, 2, ref facts, ref found);
                }
            }

            // The unbound sequence includes the receiver at its written position; two receivers follow the receiver-shape rule.
            var position = PositionOf(later, j, -1);
            var i = ParameterAt(earlier, position, -1);
            if (i >= 0 && (i != earlierReceiver || j != laterReceiver))
            {
                this.AddParameterShapeParty(earlier, i, later, j, 3, ref facts, ref found);
            }
        }

        if (j != laterReceiver && later.Parameters[j].ExternalName is { Length: > 0 } name)
        {
            for (var i = 0; i < earlier.Parameters.Count; i++)
            {
                if (i != earlierReceiver && earlier.Parameters[i].ExternalName == name)
                {
                    this.AddParameterShapeParty(earlier, i, later, j, 1, ref facts, ref found);
                    break;
                }
            }
        }

        static int ParameterAt(FunctionKoto function, int position, int receiver)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != receiver && AllowsPositionalArgument(function, i) && position-- == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // A party: the earlier parameter's Type syntax as a Related fact whose role is its ground (1 by external name, 2 by position in a bound
    // call, 3 in an unbound call), once per earlier parameter; the first conflict supplies the problem's other facts.
    private void AddParameterShapeParty(FunctionKoto earlier, int i, FunctionKoto later, int j, int role, ref ParameterShapeConflict facts, ref bool found)
    {
        foreach (var party in this.parameterShapeParties)
        {
            if (ReferenceEquals(party.Function, earlier) && party.Index == i)
            {
                return; // A pair that corresponds both by position and by name is recorded once.
            }
        }

        if (!this.ParameterShapesConflict(earlier, i, later, j, out var mode, out var other, out var shape, out _))
        {
            return;
        }

        this.parameterShapeParties.Add((earlier, i, role));
        if (!found)
        {
            found = true;
            facts = new(mode, other, shape.Key, shape.Target);
        }
    }

    // SPEC 7.4: one conflicting parameter's problem (or a gathered use's): a Reason fact packing its two modes and its matching key, the
    // conflicting parameter or use (Related, role 0), its parties (AddParameterShapeParty) and the Type the keys overlap at (Key) when the
    // key has one. Its condition is the report's (a further problem at the subject, such as a refining Contract's next inherited conflict).
    private void RecordParameterShapes(Koto subject, Koto at, in ParameterShapeConflict conflict, bool unresolved)
    {
        var facts = this.parameterShapeFacts;
        facts.Add(new(ProblemFactKind.Reason, SourceRef.Of(subject), (long)conflict.Mode | ((long)conflict.Other << 8) | ((long)conflict.Key << 16)));
        facts.Add(new(ProblemFactKind.Related, this.FactSite(at), 0));
        foreach (var (function, index, role) in this.parameterShapeParties)
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(function.Parameters[index].Type), role));
        }

        if (conflict.Target is { } target)
        {
            facts.Add(new(ProblemFactKind.Key, facts[1].Site, this.report.Keep(target)));
        }

        var recorded = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(facts);
        var requirement = DiagnosticRequirement.Binding(BindingFailure.ParameterShapeMismatch);
        var condition = this.report.Condition(new(DiagnosticCode.ParameterShapeMismatch_Kd, requirement, SourceRef.Of(subject), 0), recorded);
        this.Record(subject, BindingFailure.ParameterShapeMismatch, recorded, condition, unresolved);
        facts.Clear();
        this.parameterShapeParties.Clear();
    }

    // SPEC 7.4: the declarations of one group in source order; one problem per corresponding parameter of each later declaration, with
    // the overlapping earlier parameters as its parties. `subject` chooses the failed node for a declaration outside `owner`: a
    // requirement inherited by refinement is reported at the refining Contract, one problem per conflicting inherited requirement.
    private void ValidateParameterShapes(IReadOnlyList<BindingSymbol> members, Koto? owner)
    {
        for (var b = 0; b < members.Count; b++)
        {
            if (members[b].Declaration is not FunctionKoto later || later.IsSpecialization)
            {
                continue;
            }

            var subject = owner is not null && !ReferenceEquals(later.Parent, owner) ? owner : later;
            for (var j = 0; j < later.Parameters.Count; j++)
            {
                this.parameterShapeParties.Clear();
                var facts = default(ParameterShapeConflict);
                var found = false;
                for (var a = 0; a < b; a++)
                {
                    if (members[a].Declaration is FunctionKoto earlier && !earlier.IsSpecialization)
                    {
                        this.CollectParameterShapeParties(earlier, later, j, ref facts, ref found);
                    }
                }

                if (found)
                {
                    this.RecordParameterShapes(subject, later.Parameters[j].Type, facts, false);
                }
            }
        }

        this.parameterShapeParties.Clear();
    }

    // The declarations of one scope's Name chain, in source order; a Contract's requirements are checked with their inherited ones.
    private void ValidateParameterShapes(BindingSymbol first)
    {
        var scratch = this.parameterShapeScratch;
        scratch.Clear();
        for (var symbol = first; symbol is not null; symbol = symbol.Next)
        {
            if (symbol.Declaration is FunctionKoto { IsSpecialization: false })
            {
                scratch.Add(symbol);
            }
        }

        if (scratch.Count > 1)
        {
            OrderDeclarations(scratch, null);
            this.ValidateParameterShapes(scratch, null);
        }

        scratch.Clear();
    }

    // SPEC 8.4.3: a Contract's requirements with those inherited by refinement; an inherited one precedes the Contract's own,
    // so a conflict between them is reported at the refining declaration.
    private void ValidateContractParameterShapes(ContractKoto contract, List<BoundRequirement> members)
    {
        var scratch = this.parameterShapeScratch;
        scratch.Clear();
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Symbol.Declaration is FunctionKoto { IsSpecialization: false })
            {
                scratch.Add(members[i].Symbol);
            }
        }

        if (scratch.Count > 1)
        {
            OrderDeclarations(scratch, contract);
            this.ValidateParameterShapes(scratch, contract);
        }

        scratch.Clear();
    }

    // SPEC 7.4, 9.5.1, 9.6: a group gathered at a use from several declaration sources is checked there, with the use as the
    // subject and one record relating every overlapping declaration; its arguments play no part.
    private bool CheckGatheredParameterShapes(Koto use, in FunctionGroup members)
    {
        this.parameterShapeParties.Clear();
        var facts = default(ParameterShapeConflict);
        var found = false;
        var b = -1;
        foreach (var laterMember in members)
        {
            if (++b == 0 || laterMember.Declaration is not FunctionKoto later)
            {
                continue;
            }

            for (var j = 0; j < later.Parameters.Count; j++)
            {
                var a = 0;
                foreach (var earlierMember in members)
                {
                    if (a++ == b)
                    {
                        break; // Each pair once: the members before the later one.
                    }

                    if (earlierMember.Declaration is FunctionKoto earlier)
                    {
                        this.CollectParameterShapeParties(earlier, later, j, ref facts, ref found);
                    }
                }
            }
        }

        if (!found)
        {
            this.parameterShapeParties.Clear();
            return false;
        }

        this.RecordParameterShapes(use, use is MemberAccessKoto access ? access.Right : use, facts, true);
        return true;
    }

    // SPEC 7.4: a conflicting parameter's record, located at it, with both modes, the ground of correspondence and the matching key as
    // facts and the overlapping earlier parameters as related locations (RecordParameterShapes' facts).
    private void ReportParameterShapes(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var reason = facts[0].Value;
        var keyed = facts[^1].Kind == ProblemFactKind.Key;
        var parties = facts[2..(keyed ? facts.Length - 1 : facts.Length)];
        var related = new (string Role, Koto At, string? Label)[parties.Length];
        string? ground = null;
        for (var p = 0; p < related.Length; p++)
        {
            var type = this.compilation.SyntaxOf(parties[p].Site);
            var (function, i) = PartyParameter(type);
            var shown = Ground(function, i, parties[p].Value);
            ground ??= shown;
            related[p] = ("declaration", type, $"{function.Name} takes {shown} as {DiagnosticTypeName(type.BoundType!)}");
        }

        var target = keyed ? DiagnosticTypeName(this.report.Value(facts[^1])) : null;
        var key = ((ParameterKey)(byte)(reason >> 16)) switch
        {
            ParameterKey.Any => "any Type",
            ParameterKey.Either => $"Value or Object({target})",
            ParameterKey.Object => $"Object({target})",
            _ => $"Value({target})",
        };
        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            (AcquisitionMode)(byte)reason,
            (AcquisitionMode)(byte)(reason >> 8),
            at: this.compilation.SyntaxOf(facts[1].Site),
            evidence: [ground, key],
            related: related,
            condition: problem.Condition);
    }

    private void ResetParameterShapes()
    {
        this.shapeCallCandidates.Clear();
        this.shapeJudgments.Clear();
        this.shapeContracts.Clear();
        this.shapeDependencies.Clear();
        this.shapeSharedSlots.Clear();
        this.collisionLeft = this.collisionRight = null;
        this.parameterShapeParties.Clear();
        this.parameterShapeFacts.Clear();
    }

    private readonly record struct ParameterShape(AcquisitionModes Modes, ParameterKey Key, BoundType Target);

    // A conflicting parameter's (or use's) first conflict: its colliding modes and its matching key.
    private readonly record struct ParameterShapeConflict(AcquisitionMode Mode, AcquisitionMode Other, ParameterKey Key, BoundType? Target);
}

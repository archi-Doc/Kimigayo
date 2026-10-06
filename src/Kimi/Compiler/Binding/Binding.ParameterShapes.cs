// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>What a parameter requires of a bare owned Place argument, judged on the outer Semantics of its normalized Type (SPEC 7.3.1).</summary>
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
    private const string ParameterShapeAdvice = "Give the operations different names by SPEC 4.7.1 (sorted/sort, index/indexUniq), or give both parameters one acquisition mode; a rename reaches the callers, so no edit is applied automatically";
    private const string ParameterShapeUseAdvice = "Qualify the Name with its Container, such as A.f, to select one declaration source";
    private const string ParameterShapeNote = "The overlap comes from a generic slot or a non-constant length: the check unifies the Types without Constraints, enclosing Type arguments or length values";

    // SPEC 7.3.1: the conflicting parameters of a later declaration, or of a group gathered at a use, recorded only when the check
    // fails. The stores are reused across passes, so a warm rebind of the failure allocates nothing.
    private readonly Dictionary<Koto, (int Start, int Count)> parameterShapeConflicts = new(ReferenceEqualityComparer.Instance);
    private readonly List<ParameterShapeConflict> parameterShapeStore = [];
    private readonly List<ParameterShapeParty> parameterShapePartyStore = [];
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

    // The matching key of a parameter (SPEC 7.3.1): Value(U), Object(U), either family over U for a pair application, or any key.
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

    // SPEC 7.3.1: a key that still depends on a slot or a non-constant length, which the check does not resolve (the Note).
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

    // SPEC 7.3.1: two outer layers that are the same binding always bind to one Type and plan one acquisition, so they are not
    // compared: the same enclosing slot (one interned instance), or the same-numbered own slot of declarations with equal generic
    // counts, including the same application of it.
    private static bool SameBinding(BoundType a, BoundType b, FunctionKoto fa, FunctionKoto fb, bool sameGenerics)
    {
        if (a.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) || a.Kind != b.Kind)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (!sameGenerics || a.Symbol is not { } sa || b.Symbol is not { } sb || !ReferenceEquals(sa.Scope.Owner, fa) || !ReferenceEquals(sb.Scope.Owner, fb) || sa.Slot != sb.Slot)
        {
            return false;
        }

        return a.Kind != BoundTypeKind.SemanticsApplication || SignatureEquals(a.Components[0], b.Components[0], fa, fb);
    }

    // SPEC 7.3, 7.3.1: a shape violation is an error of the group, not of the declaration's own signature or conditions, so the
    // declaration stays a candidate and the calls that select it are checked normally. SPEC 9.3: a misplaced protected form is
    // an error of the modifier alone, so the declaration and its uses are checked as written too.
    private static bool IsFormFailure(Koto declaration)
        => declaration is DeclarationKoto { BindingState: BindingState.Invalid } &&
            (declaration.BindingFailure == BindingFailure.ProtectedPlacement ||
            declaration is FunctionKoto { BindingFailure: BindingFailure.ParameterShapeMismatch or BindingFailure.ReceiverShapeMismatch });

    // SPEC 7.3.1, 10.1, 10.8: explicit Type arguments reach only the declarations whose generic parameter lists have the same
    // length and the same kinds position by position; a length slot takes no explicit Type argument.
    private static bool SameExplicitArgumentShape(FunctionKoto a, FunctionKoto b)
    {
        if (a.GenericArguments.Count != b.GenericArguments.Count)
        {
            return false;
        }

        for (var i = 0; i < a.GenericArguments.Count; i++)
        {
            if (a.GenericArguments[i] is LengthParameterKoto != b.GenericArguments[i] is LengthParameterKoto)
            {
                return false;
            }
        }

        return true;
    }

    private static string Ground(in ParameterShapeParty party)
        => party.Name is { } name ? $"the external name {name}" : $"position {party.Position} of {(party.BoundCall ? "a bound" : "an unbound")} call";

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

    // SPEC 7.3.1: the acquisition modes a parameter may require (one per admitted case) and its matching key.
    private ParameterShape ShapeOf(BoundType type, FunctionKoto function, bool sameGenerics)
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

    private SemanticsMask PairSlotAdmitted(BoundType whole, FunctionKoto function)
        => this.scopes.TryGetValue(function, out var scope) ? this.AdmittedSemantics(whole, scope) : SemanticsMask.All;

    // SPEC 7.3.1: two keys overlap when steps 2-4 of the collision test of SPEC 8.4.9.1 unify them; Value and Object never meet.
    private bool KeysOverlap(in ParameterShape a, in ParameterShape b)
    {
        if (a.Key == ParameterKey.Any || b.Key == ParameterKey.Any)
        {
            return true;
        }

        if (a.Key != ParameterKey.Either && b.Key != ParameterKey.Either && a.Key != b.Key)
        {
            return false;
        }

        return this.MayUnify(a.Target, b.Target);
    }

    // SPEC 7.3.1: whether the parameters i of `earlier` and j of `later` are invalid together, with the colliding case of each.
    private bool ParameterShapesConflict(FunctionKoto earlier, int i, FunctionKoto later, int j, out AcquisitionMode mode, out AcquisitionMode other, out ParameterShape shape, out bool conservative)
    {
        mode = other = default;
        shape = default;
        conservative = false;
        if (earlier.Parameters[i].Type.BoundType is not { } ta || later.Parameters[j].Type.BoundType is not { } tb)
        {
            return false;
        }

        var sameGenerics = SameExplicitArgumentShape(earlier, later);
        if (SameBinding(ta, tb, earlier, later, sameGenerics))
        {
            return false;
        }

        var sa = this.ShapeOf(ta, earlier, sameGenerics);
        var sb = this.ShapeOf(tb, later, sameGenerics);
        var union = sa.Modes | sb.Modes;
        if (union == AcquisitionModes.None || IsSingleMode(union) || !this.KeysOverlap(sa, sb))
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

    // SPEC 7.3.1: appends to the party store every parameter of `earlier` that corresponds to parameter j of `later` and conflicts
    // with it, in the unbound sequence, the bound sequence and by external name. The first conflict supplies the record's facts.
    private void CollectParameterShapeParties(FunctionKoto earlier, BindingSymbol earlierSymbol, FunctionKoto later, BindingSymbol laterSymbol, int j, ref ParameterShapeConflict facts, ref bool found)
    {
        var earlierReceiver = earlierSymbol.ReceiverIndex;
        var laterReceiver = laterSymbol.ReceiverIndex;
        var receivers = earlierReceiver >= 0 && laterReceiver >= 0;
        if (later.AllowsPositionalArgument(j))
        {
            // The bound sequence of two instance functions omits the receivers; it is the usual call form, so it is recorded first.
            if (receivers && j != laterReceiver)
            {
                var bound = PositionOf(later, j, laterReceiver);
                var at = ParameterAt(earlier, bound, earlierReceiver);
                if (at >= 0)
                {
                    this.AddParameterShapeParty(earlier, at, later, j, true, bound, null, ref facts, ref found);
                }
            }

            // The unbound sequence includes the receiver at its written position; two receivers follow the receiver-shape rule.
            var position = PositionOf(later, j, -1);
            var i = ParameterAt(earlier, position, -1);
            if (i >= 0 && (i != earlierReceiver || j != laterReceiver))
            {
                this.AddParameterShapeParty(earlier, i, later, j, false, position, null, ref facts, ref found);
            }
        }

        if (j != laterReceiver && later.Parameters[j].ExternalName is { Length: > 0 } name)
        {
            for (var i = 0; i < earlier.Parameters.Count; i++)
            {
                if (i != earlierReceiver && earlier.Parameters[i].ExternalName == name)
                {
                    this.AddParameterShapeParty(earlier, i, later, j, false, -1, name, ref facts, ref found);
                    break;
                }
            }
        }

        static int PositionOf(FunctionKoto function, int index, int receiver)
        {
            var position = 0;
            for (var i = 0; i < index; i++)
            {
                if (i != receiver && function.AllowsPositionalArgument(i))
                {
                    position++;
                }
            }

            return position;
        }

        static int ParameterAt(FunctionKoto function, int position, int receiver)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != receiver && function.AllowsPositionalArgument(i) && position-- == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private void AddParameterShapeParty(FunctionKoto earlier, int i, FunctionKoto later, int j, bool boundCall, int position, string? name, ref ParameterShapeConflict facts, ref bool found)
    {
        // One party per earlier parameter: a pair that corresponds both by position and by name is recorded once.
        for (var p = this.parameterShapePartyStore.Count - 1; p >= facts.PartyStart; p--)
        {
            var party = this.parameterShapePartyStore[p];
            if (ReferenceEquals(party.Function, earlier) && party.Index == i)
            {
                return;
            }
        }

        if (!this.ParameterShapesConflict(earlier, i, later, j, out var mode, out var other, out var shape, out var conservative))
        {
            return;
        }

        this.parameterShapePartyStore.Add(new(earlier, i, boundCall, position, name));
        if (!found)
        {
            found = true;
            facts = facts with { Mode = mode, Other = other, Key = shape.Key, Target = shape.Target, Conservative = conservative };
        }
    }

    // SPEC 7.3.1: the declarations of one group in source order; one record per corresponding parameter of each later declaration,
    // with the overlapping earlier parameters as its parties. `subject` chooses the failed node for a declaration outside `owner`.
    private void ValidateParameterShapes(IReadOnlyList<BindingSymbol> members, Koto? owner)
    {
        for (var b = 0; b < members.Count; b++)
        {
            if (members[b].Declaration is not FunctionKoto later || later.IsSpecialization)
            {
                continue;
            }

            var start = this.parameterShapeStore.Count;
            for (var j = 0; j < later.Parameters.Count; j++)
            {
                var facts = new ParameterShapeConflict(later.Parameters[j].Type, default, default, default, null, false, false, false, this.parameterShapePartyStore.Count, 0);
                var found = false;
                for (var a = 0; a < b; a++)
                {
                    if (members[a].Declaration is FunctionKoto earlier && !earlier.IsSpecialization)
                    {
                        this.CollectParameterShapeParties(earlier, members[a], later, members[b], j, ref facts, ref found);
                    }
                }

                if (found)
                {
                    this.parameterShapeStore.Add(facts with { PartyCount = this.parameterShapePartyStore.Count - facts.PartyStart });
                }
            }

            if (this.parameterShapeStore.Count == start)
            {
                continue;
            }

            // A requirement inherited by refinement is reported at the refining Contract, which keeps the first explanation.
            var subject = owner is not null && !ReferenceEquals(later.Parent, owner) ? owner : later;
            if (ReferenceEquals(subject, later) || !this.parameterShapeConflicts.ContainsKey(subject))
            {
                this.parameterShapeConflicts[subject] = (start, this.parameterShapeStore.Count - start);
            }

            this.Fail(subject, BindingFailure.ParameterShapeMismatch);
        }
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

    // SPEC 8.4.2: a Contract's requirements with those inherited by refinement; an inherited one precedes the Contract's own,
    // so a conflict between them is reported at the refining declaration.
    private void ValidateContractParameterShapes(ContractKoto contract, List<BindingSymbol> members)
    {
        var scratch = this.parameterShapeScratch;
        scratch.Clear();
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Declaration is FunctionKoto { IsSpecialization: false })
            {
                scratch.Add(members[i]);
            }
        }

        if (scratch.Count > 1)
        {
            OrderDeclarations(scratch, contract);
            this.ValidateParameterShapes(scratch, contract);
        }

        scratch.Clear();
    }

    // SPEC 7.3.1, 9.4.1, 9.5: a group gathered at a use from several declaration sources is checked there, with the use as the
    // subject and one record relating every overlapping declaration; its arguments play no part.
    private bool CheckGatheredParameterShapes(Koto use, IReadOnlyList<BindingSymbol> members, bool aliasStage)
    {
        var facts = new ParameterShapeConflict(use is MemberAccessKoto access ? access.Right : use, default, default, default, null, false, true, aliasStage, this.parameterShapePartyStore.Count, 0);
        var found = false;
        for (var b = 1; b < members.Count; b++)
        {
            if (members[b].Declaration is not FunctionKoto later || later.IsSpecialization)
            {
                continue;
            }

            for (var j = 0; j < later.Parameters.Count; j++)
            {
                for (var a = 0; a < b; a++)
                {
                    if (members[a].Declaration is FunctionKoto earlier && !earlier.IsSpecialization)
                    {
                        this.CollectParameterShapeParties(earlier, members[a], later, members[b], j, ref facts, ref found);
                    }
                }
            }
        }

        if (!found)
        {
            return false;
        }

        this.parameterShapeConflicts[use] = (this.parameterShapeStore.Count, 1);
        this.parameterShapeStore.Add(facts with { PartyCount = this.parameterShapePartyStore.Count - facts.PartyStart });
        this.Fail(use, BindingFailure.ParameterShapeMismatch, true);
        return true;
    }

    // SPEC 7.3.1: one record per conflicting parameter, located at its Type (or at the use), with both modes, the ground of
    // correspondence and the unified key as facts and the overlapping earlier parameters as related locations.
    private void ReportParameterShapes(Koto node, DiagnosticRequirement requirement, (int Start, int Count) range)
    {
        for (var k = 0; k < range.Count; k++)
        {
            var conflict = this.parameterShapeStore[range.Start + k];
            var related = new (string Role, Koto At, string? Label)[conflict.PartyCount];
            for (var p = 0; p < related.Length; p++)
            {
                var party = this.parameterShapePartyStore[conflict.PartyStart + p];
                var parameter = party.Function.Parameters[party.Index];
                related[p] = ("declaration", parameter.Type, $"{party.Function.Name} takes {Ground(party)} as {DiagnosticTypeName(parameter.Type.BoundType!)}");
            }

            var target = conflict.Target is { } shown ? DiagnosticTypeName(shown) : null;
            var key = conflict.Key switch
            {
                ParameterKey.Any => "any Type",
                ParameterKey.Either => $"Value or Object({target})",
                ParameterKey.Object => $"Object({target})",
                _ => $"Value({target})",
            };
            var first = this.parameterShapePartyStore[conflict.PartyStart];
            node.Report(
                requirement,
                DiagnosticCode.ParameterShapeMismatch_Kd,
                conflict.Mode,
                conflict.Other,
                note: conflict.Conservative ? ParameterShapeNote : null,
                at: conflict.At,
                evidence: [Ground(first), key],
                advice: conflict.AtUse ? conflict.AliasStage ? ParameterShapeUseAdvice : null : ParameterShapeAdvice,
                related: related,
                condition: (ushort)k);
        }
    }

    private void ResetParameterShapes()
    {
        this.parameterShapeConflicts.Clear();
        this.parameterShapeStore.Clear();
        this.parameterShapePartyStore.Clear();
    }

    private readonly record struct ParameterShape(AcquisitionModes Modes, ParameterKey Key, BoundType Target);

    // A conflicting parameter (or use) with its colliding modes, its matching key and the range of its parties.
    private readonly record struct ParameterShapeConflict(Koto At, AcquisitionMode Mode, AcquisitionMode Other, ParameterKey Key, BoundType? Target, bool Conservative, bool AtUse, bool AliasStage, int PartyStart, int PartyCount);

    // An earlier parameter that corresponds to the conflicting one, by position in a bound or unbound call or by external name.
    private readonly record struct ParameterShapeParty(FunctionKoto Function, int Index, bool BoundCall, int Position, string? Name);
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Persist declaration storage across provisional/final binding passes. No environment is
    // allocated for declarations with neither written relations nor binding-set names.
    private readonly OriginDeclarationRows originDeclarations;

    private readonly DiscoveredOriginRows discoveredOrigins;
    private OriginRewriteVisitor? originRewriteVisitor;

    private static Koto? OriginOwner(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (IsOriginOwner(current))
            {
                // SPEC 15.3.3, 15.3.4: a Case's payload list is an inner form of the same kind; the Case, which the clauses attach to,
                // owns the sets of all its payloads.
                return current is { Akind: KotoKind.EnumCase, Parent: { Akind: KotoKind.EnumCase } outer } ? outer : current;
            }
        }

        return null;
    }

    // SPEC 15.3.3, 15.3.4: the declarations that own Origin sets and clauses, one row per kind; OriginOwner returns the nearest.
    private static bool IsOriginOwner(Koto node) => node switch
    {
        FunctionKoto or PropertyAccessorKoto or AliasKoto or VariableKoto => true,
        DeclarationContainerKoto or IsKoto { IsAssociatedConstraint: true } => true,

        // SPEC 15.3.4: a set named in a Constraint is its declaration's; a conditional conformance owns its `when` list's and its
        // implementation block's, and a Contract-wide clause its own (a Contract has no Origins).
        IsKoto { Parent: ContractKoto } or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto } => true,
        _ => node.Akind is KotoKind.EnumCase or KotoKind.AssociatedType,
    };

    // SPEC 15.3.4, 8.10 (PLAN G972, the owner's O-403 (b)): where a written Origin may name a nested signature's per-call Origin, a
    // Function Type's or an anonymous function's (the omitted Origin of a direct borrowed input): inside its binder only (inner per-call
    // Origins cannot escape their binder), and there not in its inputs, whose every slot and borrow layer is fixed by a complete Type or
    // an outer Origin expression (nested Origins stay fixed), so in its result and body.
    private static bool OriginVisible(BoundOrigin origin, Koto use)
    {
        if (origin.Binder is FunctionTypeKoto || origin is { Kind: OriginKind.Input, Binder: FunctionKoto { IsAnonymous: true } })
        {
            var binder = origin.Binder;
            if (!IsWithin(use, binder))
            {
                return false;
            }

            for (var i = 0; i < InputCount(binder); i++)
            {
                if (InputType(binder, i) is { } input && IsWithin(use, input))
                {
                    return false;
                }
            }
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!OriginVisible(origin.Operands[i], use))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 15.3.5: the variance of a position nested at `inner` within a position of variance `outer`; an unused position is compared
    // as an invariant one (RelationWalk).
    private static OriginVariance ComposeVariance(OriginVariance outer, OriginVariance inner)
        => outer == OriginVariance.Invariant || inner is OriginVariance.Invariant or OriginVariance.Unused ? OriginVariance.Invariant
            : inner == OriginVariance.Covariant ? outer
            : outer == OriginVariance.Covariant ? OriginVariance.Contravariant : OriginVariance.Covariant;

    // SPEC 15.3.1, 23.1.2: a binding set that reuses a visible name failed as DuplicateBinding_Kd and declares nothing.
    private static bool RepeatedSet(TypeSemanticsKoto set) => set.BindingFailure == BindingFailure.Duplicate;

    // SPEC 15.3.5: the variance of a Type's component within it, which ComposeVariance composes: a layer's target invariant where the
    // layer is (IsInvariantLayer), else covariant; a Function Type's inputs contravariant and its result covariant; a declared Type
    // argument by its slot's inferred variance; any other component covariant. A comparison without a Binding sees only the concrete
    // layers (IsInvariantLayer).
    private static OriginVariance ComponentVariance(BoundType type, int component, Binding? binding) => type.Kind switch
    {
        BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication when IsInvariantLayer(type, binding, null, default) => OriginVariance.Invariant,
        BoundTypeKind.Function when type.Components.Count == 2 && component == 0 => OriginVariance.Contravariant,
        BoundTypeKind.Constructed when type.Symbol?.Schema is { } schema && component < schema.GenericSlots.Count => schema.GenericSlots[component].OriginVariance,
        _ => OriginVariance.Covariant,
    };

    // SPEC 15.3.5: the variance of a Type's Origin argument `index`, the one reading every relation, fit and walk takes: a Closure Type's
    // by its occurrences in its definition's signature (ClosureArgumentVariance, DECISIONS R4-M159); a declared Type's by its schema's
    // inferred variance; an argument without one is invariant.
    private OriginVariance OriginArgumentVariance(BoundType type, int index, Binding? binding)
        => type.Kind == BoundTypeKind.Closure ? this.ClosureArgumentVariance(type, index, binding)
            : type.Symbol?.Schema?.Origins[index].Variance ?? OriginVariance.Invariant;

    // SPEC 15.3.5: the variance of `atom`'s occurrences in `type` (a meet operand included), each position's variance composed from the
    // Type's own as RelationWalk composes it (ComponentVariance, OriginArgumentVariance); equal occurrences keep their variance, different
    // ones are invariant, and none is Unused. One query of `visitedParts` meets each distinct part once per polarity (a state bit per
    // polarity): Join is idempotent, so a part met again at a polarity adds nothing (DECISIONS BU-63). The table is this Binding's, whether
    // or not the caller reads premises (`binding`). Read by a Closure Type's argument variance and a local region's row (LocalRegionSlot).
    private OriginVariance OccurrenceVariance(BoundType type, BoundOrigin atom, Binding? binding)
    {
        return Occurrences(type, atom, OriginVariance.Covariant, binding, OriginVariance.Unused, this, this.visitedParts.Begin(this.storageVersion));

        static OriginVariance Occurrences(BoundType type, BoundOrigin atom, OriginVariance polarity, Binding? binding, OriginVariance seen, Binding owner, uint query)
        {
            if (!type.CarriesAnyOrigin)
            {
                return seen;
            }

            seen = Names(OwnedOrigin(type), atom) ? Join(seen, polarity) : seen;
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                seen = Names(type.OriginArguments[i], atom) ? Join(seen, ComposeVariance(polarity, owner.OriginArgumentVariance(type, i, binding))) : seen;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                var part = type.Components[i];
                var position = ComposeVariance(polarity, ComponentVariance(type, i, binding));
                if (part.CarriesAnyOrigin && owner.FirstVisit(part, query, (byte)(1 << (int)position)))
                {
                    seen = Occurrences(part, atom, position, binding, seen, owner, query);
                }
            }

            return seen;
        }

        static bool Names(BoundOrigin? origin, BoundOrigin atom)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (Names(origin.Operands[i], atom))
                {
                    return true;
                }
            }

            return ReferenceEquals(origin, atom);
        }

        static OriginVariance Join(OriginVariance seen, OriginVariance variance)
            => seen == OriginVariance.Unused || seen == variance ? variance : OriginVariance.Invariant;
    }

    // SPEC 15.3.4: whether a use is a binding set named in a Constraint (not an associated specification) that its declaration holds.
    private bool ConstraintSet(Koto use)
    {
        if (use is not TypeSemanticsKoto { BindingSetName: { } label } || OriginOwner(use) is not { } owner ||
            this.originDeclarations.GetValueOrDefault(owner)?.Set(label) is not { } set || !ReferenceEquals(set, use))
        {
            return false;
        }

        // The Constraint lies between the occurrence and its owner, or is the owner (a Contract-wide clause).
        for (var node = use.Parent; node is not null; node = node.Parent)
        {
            if (node is IsKoto { IsAssociatedConstraint: false })
            {
                return true;
            }

            if (ReferenceEquals(node, owner))
            {
                break;
            }
        }

        return false;
    }

    private OriginDeclaration OriginDeclarationFor(Koto owner)
    {
        if (!this.originDeclarations.TryGetValue(owner, out var declaration))
        {
            this.originDeclarations.Add(owner, declaration = new(owner));
            this.originStateVersion++;
        }

        return declaration;
    }

    private bool PartialOriginContractReady(FunctionKoto function)
    {
        if (!this.originDeclarations.TryGetValue(function, out var declaration))
        {
            return true;
        }

        var clauses = OriginClauses.Get(function);
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            if (Pending(clause.Left) || Pending(clause.Right))
            {
                return false;
            }
        }

        return true;

        bool Pending(Koto syntax) => syntax switch
        {
            IdentifierNameKoto name => declaration.Set(name.IdentifierName) is { BoundType: null },
            ParenthesizedKoto parentheses => Pending(parentheses.Operand),
            MemberAccessKoto member => Pending(member.Left),
            BinaryKoto binary => Pending(binary.Left) || Pending(binary.Right),
            _ => false,
        };
    }

    private void PrepareOriginDeclarations()
    {
        foreach (var declaration in this.originDeclarations.Values.In(this.pass))
        {
            declaration!.Reset();
        }

        foreach (var declaration in this.initializerOrigins.Values.In(this.pass))
        {
            declaration!.Reset();
        }

        foreach (var names in this.discoveredOrigins.Values.In(this.pass))
        {
            names!.Clear();
            ((OriginNameList)names).Spans.Clear();
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (OriginClauses.Get(node).Count != 0)
            {
                this.OriginDeclarationFor(node);
            }

            if (node is not TypeSemanticsKoto annotation || OriginOwner(node) is not { } owner)
            {
                continue;
            }

            if (annotation.BindingSetName is { } label)
            {
                var declaration = this.OriginDeclarationFor(owner);
                if (!declaration.Forming.Sets.TryAdd(label, annotation))
                {
                    this.Fail(annotation, BindingFailure.Duplicate);
                }

                if (owner is VariableKoto and not PropertyKoto && owner.BoundSymbol is { } local)
                {
                    local.Scope.OriginSets ??= new(StringComparer.Ordinal);
                    if (!local.Scope.OriginSets.TryAdd(label, annotation))
                    {
                        this.Fail(annotation, BindingFailure.Duplicate);
                    }
                }

                continue;
            }

            // SPEC 5.7 (design-final §0, bu19): a raw layer's Origin belongs to an input's borrow, a slot or static, so it introduces no name.
            if (annotation.OriginExpression is not { } expression || annotation.SemanticsKind == SemanticsKind.Raw)
            {
                continue;
            }

            // Only a callable signature introduces a name by writing it (SPEC 15.3.4); a Type's own slots are declared in its
            // header alone, so storage names never become slots (SPEC 15.3.2). Nested function Types are a binding boundary.
            var nested = false;
            for (var parent = annotation.Parent; parent is not null && !ReferenceEquals(parent, owner); parent = parent.Parent)
            {
                nested |= parent is FunctionTypeKoto;
            }

            // SPEC 8.13.2: a specialization's written binder names are preliminary; CompleteSpecializationOrigins maps them to the original's.
            if (nested || (owner is FunctionKoto function && (function.IsAnonymous || !IsSignature(annotation, function))) || owner is not (FunctionKoto or PropertyAccessorKoto))
            {
                continue;
            }

            Discover(expression, owner);
        }

        static bool IsSignature(Koto node, FunctionKoto function)
        {
            while (node.Parent is { } parent && !ReferenceEquals(parent, function))
            {
                node = parent;
            }

            if (ReferenceEquals(node, function.ReturnType))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(node, function.Parameters[i].Type))
                {
                    return true;
                }
            }

            return false;
        }

        void Discover(Koto expression, Koto binder)
        {
            if (expression is IdentifierNameKoto name && name.IdentifierName is not ("static" or "_"))
            {
                if (!this.discoveredOrigins.TryGetValue(binder, out var names))
                {
                    this.discoveredOrigins.Add(binder, names = new OriginNameList());
                }

                if (!names.Contains(name.IdentifierName))
                {
                    ((OriginNameList)names).Add(name.IdentifierName, name.Span);
                }
            }
            else if (expression is ParenthesizedKoto grouped)
            {
                Discover(grouped.Operand, binder);
            }
            else if (expression is BinaryKoto { Akind: KotoKind.And } meet)
            {
                Discover(meet.Left, binder);
                Discover(meet.Right, binder);
            }
        }
    }

    private IReadOnlyList<string> DiscoverOriginNames(Koto owner, IReadOnlyList<string> written, BindingScope scope)
    {
        if (!this.discoveredOrigins.TryGetValue(owner, out var names))
        {
            return written;
        }

        // SPEC 15.3.4: a name already bound in an enclosing scope (an Origin or a set of that scope's declaration, a parameter, a capture,
        // or a local declared before the owner) introduces no Origin; only the remaining names are introduced by the signature. Bound is
        // not visible: a later parameter of a function whose default holds the owner is bound here though no lookup may select it (SPEC
        // 7.2.3, ParameterVisible), the reading the owner's D-1236 (A) requires: its name introduces nothing and selects nothing, so it
        // fails where it is written.
        for (var i = names.Count - 1; i >= 0; i--)
        {
            var name = names[i];
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.ContainsKey(name) == true ||
                    (current.Values.TryGetValue(name, out var value) && value.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                        (value.Kind != BindingSymbolKind.Local || value.Declaration.Span.Start <= owner.Span.Start)) ||
                    this.originDeclarations.GetValueOrDefault(current.Owner)?.Set(name) is not null)
                {
                    names.RemoveAt(i);
                    ((OriginNameList)names).Spans.RemoveAt(i);
                    break;
                }
            }
        }

        if (owner is FunctionKoto function)
        {
            function.SetOrigins(names);
        }
        else if (owner is PropertyAccessorKoto accessor)
        {
            accessor.Origins = names;
        }

        return names;
    }

    private OriginDeclaration? BeginOriginDeclaration(Koto owner, BindingScope scope)
    {
        if (!this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.Forming.State != 0)
        {
            return null;
        }

        (declaration.Forming.Scope, declaration.Forming.Body, declaration.Forming.State) = (scope, this.formingBody, 1);
        this.originStateVersion++;
        foreach (var entry in declaration.Forming.Sets)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                var scalar = current.Origins?.ContainsKey(entry.Key) == true;
                var value = current.Values.TryGetValue(entry.Key, out var symbol) && symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                    (symbol.Kind != BindingSymbolKind.Local || symbol.Declaration.Span.Start <= owner.Span.Start);
                // Like a value, a local's set is visible only to declarations that follow it (SPEC 15.3.1, 15.4.4).
                var local = current.OriginSets?.GetValueOrDefault(entry.Key);
                var set = this.originDeclarations.GetValueOrDefault(current.Owner)?.Set(entry.Key) ??
                    (local is not null && OriginOwner(local) is { } localOwner && localOwner.Span.Start <= owner.Span.Start ? local : null);
                if (scalar || value || (set is not null && !ReferenceEquals(set, entry.Value)))
                {
                    this.Fail(entry.Value, BindingFailure.Duplicate);
                    break;
                }
            }
        }

        return declaration;
    }

    // SPEC 15.3.1, 23.1.2: a name that resolves to a binding set that failed as a repeat names neither that set nor the outer
    // declaration it repeats, so the Origin it would denote, as in a clause attached to the same declaration or `during a` beside
    // it, rests on that DuplicateBinding_Kd and reports nothing of its own; it never resolves to the outer name.
    private void RestOnRepeatedSet(Koto use, TypeSemanticsKoto set)
    {
        if (use.BindingFailure == BindingFailure.None)
        {
            this.RecordRead(use, set);
            this.Fail(use, BindingFailure.MissingName, true);
        }
    }

    private BoundType? BindOriginSetType(TypeSemanticsKoto syntax, BindingScope scope)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        // SPEC 9.2, 9.7.2: `(Outer<T>.G{o}).f` puts the set's Type at a qualifier position; elsewhere it is a Type occurrence (15.3.1).
        var path = this.NamePath(syntax, scope);
        return path.Symbol?.Declaration is GroupKoto
            ? this.BindContainerQualifier(syntax, path.Symbol, scope, this.TypeContext(syntax, scope))
            : this.BindType(syntax, scope);
    }

    // A local's binding sets bind their Types before its declaration completes (BindVariableCore).
    private void BindOriginSetTypes(OriginDeclaration declaration, BindingScope scope)
    {
        foreach (var set in declaration.Forming.Sets.Values)
        {
            this.BindOriginSetType(set, scope);
        }
    }

    private BoundOrigin? PendingOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement, int slot, BindingSymbol? borrowCondition)
    {
        var owner = OriginOwner(use);
        if (owner is null || !this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.Forming.State != 1 ||
            (context.Position == TypePosition.Parameter && slot < 0 && context.Direct))
        {
            return null;
        }

        for (var i = 0; i < declaration.Forming.Pending.Count; i++)
        {
            if (ReferenceEquals(declaration.Forming.Pending[i].Use, use) && declaration.Forming.Pending[i].Slot == slot)
            {
                return declaration.Forming.Pending[i].Origin;
            }
        }

        var origin = this.OriginAtom(use, OriginKind.Unbound, slot);
        declaration.Forming.Pending.Add(new(use, scope, context, requirement, slot, origin, borrowCondition));
        return origin;
    }

    private BoundOrigin ResolveOrigin(BoundOrigin origin, OriginDeclaration declaration, int depth = 0)
    {
        if (depth > declaration.Substitutions)
        {
            return origin;
        }

        if (declaration.Substitution(origin) is { } replacement)
        {
            return this.ResolveOrigin(replacement, declaration, depth + 1);
        }

        if (origin.Kind == OriginKind.Intersection)
        {
            var result = BoundOrigin.Static;
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.ResolveOrigin(origin.Operands[i], declaration, depth));
            }

            return result;
        }

        return origin;
    }

    // SPEC 15.3.3, 15.4.1: a declaration's completion binds its clauses, a failed one resting the slots it names
    // (Binding.OriginDeclarations.Failures.cs); its equalities substitute, its pending slots complete, and its Types and Origins are
    // rewritten to the completed Origins.
    private void CompleteOriginDeclaration(OriginDeclaration? declaration)
    {
        if (declaration is null || declaration.Forming.State != 1)
        {
            return;
        }

        var clauses = OriginClauses.Get(declaration.Owner);
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var a = this.BindOrigin(clause.Left, declaration.Forming.Scope!);
            var b = this.BindOrigin(clause.Right, declaration.Forming.Scope!);
            if (IsRecovery(clause, out _) || a is null || b is null)
            {
                this.FailOriginClause(declaration, clause, a, b);
            }
            else
            {
                declaration.Forming.Relations.Add(new(a, b, clause.IsEquality, clause));
                clause.BindingState = BindingState.Resolved;
            }
        }

        this.SubstituteOriginEqualities(declaration);
        this.CompletePendingOrigins(declaration);
        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(declaration.Owner);
        visitor.Declaration = null;
        for (var i = 0; i < declaration.Forming.Relations.Count; i++)
        {
            var relation = declaration.Forming.Relations[i];
            declaration.Forming.Relations[i] = relation with
            {
                Longer = this.ResolveOrigin(relation.Longer, declaration),
                Shorter = this.ResolveOrigin(relation.Shorter, declaration),
            };
        }

        declaration.Forming.State = 3;
        this.originStateVersion++;
    }

    // Equalities are substitutions before result defaulting. Revisit equations after
    // each substitution so chains are independent of clause order.
    private void SubstituteOriginEqualities(OriginDeclaration declaration)
    {
        for (var pass = 0; pass <= declaration.Forming.Relations.Count; pass++)
        {
            var changed = false;
            foreach (var relation in declaration.Forming.Relations)
            {
                if (!relation.Equality)
                {
                    continue;
                }

                var a = this.ResolveOrigin(relation.Longer, declaration);
                var b = this.ResolveOrigin(relation.Shorter, declaration);
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                var leftRank = Flexibility(a);
                var rightRank = Flexibility(b);
                if (rightRank > leftRank || (rightRank == leftRank && rightRank != 0 && CompareOrigins(a, b) < 0))
                {
                    (a, b) = (b, a);
                }

                if (Flexibility(a) != 0 && !ContainsOrigin(b, a))
                {
                    declaration.Forming.Replacements[a] = b;
                    this.originStateVersion++;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        int Flexibility(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Unbound)
            {
                // An enclosing input can complete an equality class containing a nested
                // signature or result. Such dependent occurrences must not elide first.
                foreach (var pending in declaration.Forming.Pending)
                {
                    if (ReferenceEquals(pending.Origin, origin))
                    {
                        return !ReferenceEquals(pending.Context.Owner, declaration.Owner) ? 4 :
                            pending.Context.Position == TypePosition.Parameter ? 2 : 3;
                    }
                }

                return 3;
            }

            return ReferenceEquals(origin.Binder, declaration.Owner) && declaration.Owner is FunctionKoto or PropertyAccessorKoto &&
                origin.Kind is OriginKind.Parameter or OriginKind.Input ? 1 : 0;
        }
    }

    // The pending slots no equality substituted and no failed clause rests (RestsOnFailedClause) complete: a result slot a nontrivial
    // outlives clause names as an aggregate input, any other by the position rule (OmittedOrigin).
    private void CompletePendingOrigins(OriginDeclaration declaration)
    {
        declaration.Forming.State = 2; // Further omission performs the established position rule.
        this.originStateVersion++;
        foreach (var pending in declaration.Forming.Pending)
        {
            var origin = this.ResolveOrigin(pending.Origin, declaration);
            if (origin.Kind != OriginKind.Unbound || !ReferenceEquals(origin, pending.Origin) || this.RestsOnFailedClause(declaration, origin))
            {
                continue;
            }

            // Only a nontrivial explicit outlives clause universally quantifies a result
            // slot. Intrinsic well-formedness obligations do not change result elision.
            var quantified = false;
            if (pending.Context.Position == TypePosition.Result && ReferenceEquals(pending.Context.Owner, declaration.Owner) && pending.Slot >= 0)
            {
                foreach (var relation in declaration.Forming.Relations)
                {
                    var a = this.ResolveOrigin(relation.Longer, declaration);
                    var b = this.ResolveOrigin(relation.Shorter, declaration);
                    if (!relation.Equality && !OriginOutlives(a, b) && (ContainsOrigin(a, origin) || ContainsOrigin(b, origin)))
                    {
                        quantified = true;
                        break;
                    }
                }
            }

            BoundOrigin? completed;
            if (quantified && declaration.Owner.BoundSymbol is { } symbol)
            {
                var slots = symbol.AggregateInputOrigins ??= new();
                completed = null;
                for (var i = 0; i < slots.Count; i++)
                {
                    if (ReferenceEquals(slots[i].Occurrence, pending.Use) && slots[i].TargetSlot == pending.Slot)
                    {
                        completed = slots[i];
                        break;
                    }
                }

                if (completed is null)
                {
                    completed = new(OriginKind.Input, declaration.Owner, InputCount(declaration.Owner) + slots.Count)
                    {
                        InputIndex = -1,
                        Occurrence = pending.Use,
                        TargetSlot = pending.Slot,
                    };
                    slots.Add(completed);
                }
            }
            else
            {
                // SPEC 15.4.1: the slot completes as it would have without the clauses, including its Semantics condition.
                completed = this.OmittedOrigin(pending.Use, pending.Scope, pending.Context, pending.Requirement, pending.Slot, pending.BorrowCondition);
            }

            if (completed is not null && !ReferenceEquals(completed, origin))
            {
                declaration.Forming.Replacements[origin] = completed;
                this.originStateVersion++;
            }
        }
    }

    private BoundType RewriteOrigins(BoundType type, OriginDeclaration declaration)
    {
        if (!type.CarriesAnyOrigin)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        var arguments = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.RewriteOrigins(type.Components[i], declaration);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                arguments[i] = this.ResolveOrigin(type.OriginArguments[i], declaration);
            }

            var origin = type.Origin is { } outer ? this.ResolveOrigin(outer, declaration) : null;
            if (type.RawOrigin is { } raw)
            {
                origin = this.ResolveOrigin(raw, declaration);
            }

            return this.types.Intern(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.InstanceContext, type.LengthArguments, type.Binder);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }
    }

    private sealed class OriginRewriteVisitor(Binding binding) : KotoVisitor
    {
        internal OriginDeclaration? Declaration { get; set; }

        public override void Visit(Koto node)
        {
            if (node.Parent is FunctionKoto function && (ReferenceEquals(node, function.Body) || ReferenceEquals(node, function.ExpressionBody)))
            {
                return;
            }

            var declaration = this.Declaration!;
            if (node.BoundType is { } type)
            {
                node.BoundType = binding.RewriteOrigins(type, declaration);
            }

            if (node.BoundOrigin is { } origin)
            {
                node.BoundOrigin = binding.ResolveOrigin(origin, declaration);
                if (!OriginVisible(node.BoundOrigin, node))
                {
                    binding.Record(node, BindingFailure.InvalidOrigin, []);
                }
            }

            if (node.BoundSymbol is { Type: { } symbolType } symbol && ReferenceEquals(symbol.Declaration, node))
            {
                symbol.Type = binding.RewriteOrigins(symbolType, declaration);
            }

            node.VisitChildren(this);
        }
    }

    /// <summary>The `originDeclarations` family's one accessor (DECISIONS BU-220): a kept family, an owner's declared Origins (SPEC 15.3),
    /// made once and reset in place by each pass (PrepareOriginDeclarations), kept in its key node's module's NodeColumns at its SyntaxId.
    /// Its members keep the names its readers use.</summary>
    private readonly struct OriginDeclarationRows(Binding binding)
    {
        internal FamilyRows<OriginDeclaration?> Values => new(binding, static (table, _) => table.Hir.Families.OriginDeclarations.Enumerate());

        internal void Add(Koto node, OriginDeclaration value)
        {
            ref var row = ref HirTables.Of(node).Families.OriginDeclarations.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`originDeclarations` already holds a row at `{node}`.", nameof(node));
        }

        internal bool ContainsKey(Koto node) => this.TryGetValue(node, out _);

        internal OriginDeclaration? GetValueOrDefault(Koto node)
            => node.SyntaxOwner is { } table ? table.Hir.Families.OriginDeclarations.Get(node.SyntaxId) : null;

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out OriginDeclaration? value)
            => (value = this.GetValueOrDefault(node)) is not null;
    }

    /// <summary>The `discoveredOrigins` family's one accessor (DECISIONS BU-220): a kept family, a binder's discovered Origin names, an
    /// OriginNameList made once and emptied in place by each pass (PrepareOriginDeclarations), kept in its key node's module's NodeColumns
    /// at its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct DiscoveredOriginRows(Binding binding)
    {
        internal FamilyRows<List<string>?> Values => new(binding, static (table, _) => table.Hir.Families.DiscoveredOrigins.Enumerate());

        internal void Add(Koto node, List<string> value)
        {
            ref var row = ref HirTables.Of(node).Families.DiscoveredOrigins.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`discoveredOrigins` already holds a row at `{node}`.", nameof(node));
        }

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out List<string>? value)
            => (value = node.SyntaxOwner is { } table ? table.Hir.Families.DiscoveredOrigins.Get(node.SyntaxId) : null) is not null;
    }
}

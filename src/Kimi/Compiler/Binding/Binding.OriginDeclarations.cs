// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Persist declaration storage across provisional/final binding passes. No environment is
    // allocated for declarations with neither written relations nor binding-set names.
    private readonly Dictionary<Koto, OriginDeclaration> originDeclarations = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<Koto, List<string>> discoveredOrigins = new(ReferenceEqualityComparer.Instance);
    private OriginRewriteVisitor? originRewriteVisitor;

    private static Koto? OriginOwner(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto or AliasKoto or IsKoto { IsAssociatedConstraint: true } ||
                current is VariableKoto || current.Akind is KotoKind.EnumCase or KotoKind.AssociatedType)
            {
                // SPEC 15.3.3, 15.3.4: a Case's payload list is an inner form of the same kind; the Case, which the clauses attach to,
                // owns the sets of all its payloads.
                return current is { Akind: KotoKind.EnumCase, Parent: { Akind: KotoKind.EnumCase } outer } ? outer : current;
            }
        }

        return null;
    }

    // SPEC 23.1.2 (DECISIONS BU-62): the one writer of PendingFailure. A declaration that ends without completing its pending Origins leaves them
    // unbound, resting on the part that failed: its failed clause (CompleteOriginDeclaration), or, when completion is skipped, its Type,
    // alias target or payload operand (path c). No State change: a later bind in State 1 keeps the same pending atom.
    private static void RestPendingOn(OriginDeclaration? declaration, Koto part)
    {
        if (declaration is { State: 1 })
        {
            declaration.PendingFailure ??= part;
        }
    }

    private static bool OriginVisible(BoundOrigin origin, Koto use)
    {
        if (origin.Binder is FunctionTypeKoto binder)
        {
            var inside = false;
            for (var node = use; node is not null; node = node.Parent)
            {
                inside |= ReferenceEquals(node, binder);
            }

            if (!inside)
            {
                return false;
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
    // as an invariant one (FitsTypeCore).
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
        BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication when IsInvariantLayer(type, binding) => OriginVariance.Invariant,
        BoundTypeKind.Function when type.Components.Count == 2 && component == 0 => OriginVariance.Contravariant,
        BoundTypeKind.Constructed when type.Symbol?.Schema is { } schema && component < schema.GenericSlots.Count => schema.GenericSlots[component].OriginVariance,
        _ => OriginVariance.Covariant,
    };

    // SPEC 15.3.5: the variance of a Type's Origin argument `index`, the one reading every relation, fit and walk takes: a declared
    // Type's by its schema's inferred variance; an argument without one is invariant.
    private static OriginVariance OriginArgumentVariance(BoundType type, int index, Binding? binding)
        => type.Symbol?.Schema?.Origins[index].Variance ?? OriginVariance.Invariant;

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
            IdentifierNameKoto name => declaration.Sets.TryGetValue(name.IdentifierName, out var set) && set.BoundType is null,
            ParenthesizedKoto parentheses => Pending(parentheses.Operand),
            MemberAccessKoto member => Pending(member.Left),
            BinaryKoto binary => Pending(binary.Left) || Pending(binary.Right),
            _ => false,
        };
    }

    private void PrepareOriginDeclarations()
    {
        foreach (var declaration in this.originDeclarations.Values)
        {
            declaration.Reset();
        }

        foreach (var declaration in this.initializerOrigins.Values)
        {
            declaration.Reset();
        }

        foreach (var names in this.discoveredOrigins.Values)
        {
            names.Clear();
            ((OriginNameList)names).Spans.Clear();
        }

        this.absentSlotProjections?.Clear();

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
                if (!declaration.Sets.TryAdd(label, annotation))
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

        // A name already visible names that Origin; only the remaining names are introduced by the signature (SPEC 15.3.4).
        for (var i = names.Count - 1; i >= 0; i--)
        {
            var name = names[i];
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.ContainsKey(name) == true ||
                    (current.Values.TryGetValue(name, out var value) && value.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                        (value.Kind != BindingSymbolKind.Local || value.Declaration.Span.Start <= owner.Span.Start)) ||
                    this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.ContainsKey(name) == true)
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
        if (!this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.State != 0)
        {
            return null;
        }

        (declaration.Scope, declaration.Body, declaration.State) = (scope, this.formingBody, 1);
        this.originStateVersion++;
        foreach (var entry in declaration.Sets)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                var scalar = current.Origins?.ContainsKey(entry.Key) == true;
                var value = current.Values.TryGetValue(entry.Key, out var symbol) && symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                    (symbol.Kind != BindingSymbolKind.Local || symbol.Declaration.Span.Start <= owner.Span.Start);
                // Like a value, a local's set is visible only to declarations that follow it (SPEC 15.3.1, 15.4.4).
                var local = current.OriginSets?.GetValueOrDefault(entry.Key);
                var set = this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.GetValueOrDefault(entry.Key) ??
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
            this.prerequisites[use] = (this.prerequisiteStore.Count, 1);
            this.prerequisiteStore.Add(set);
            this.Fail(use, BindingFailure.MissingName, true);
        }
    }

    private BoundType? BindOriginSetType(TypeSemanticsKoto syntax, BindingScope scope)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        var symbol = this.TypeName(syntax, scope, false);
        return symbol?.Declaration is GroupKoto
            ? this.BindContainerQualifier(syntax, symbol, scope, this.TypeContext(syntax, scope))
            : this.BindType(syntax, scope);
    }

    private BoundOrigin? PendingOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement, int slot, BindingSymbol? borrowCondition)
    {
        var owner = OriginOwner(use);
        if (owner is null || !this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.State != 1 ||
            (context.Position == TypePosition.Parameter && slot < 0 && context.Direct))
        {
            return null;
        }

        for (var i = 0; i < declaration.Pending.Count; i++)
        {
            if (ReferenceEquals(declaration.Pending[i].Use, use) && declaration.Pending[i].Slot == slot)
            {
                return declaration.Pending[i].Origin;
            }
        }

        var origin = this.OriginAtom(use, OriginKind.Unbound, slot);
        declaration.Pending.Add(new(use, scope, context, requirement, slot, origin, borrowCondition));
        return origin;
    }

    private BoundOrigin ResolveOrigin(BoundOrigin origin, OriginDeclaration declaration, int depth = 0)
    {
        if (depth > declaration.Replacements.Count)
        {
            return origin;
        }

        if (declaration.Replacements.TryGetValue(origin, out var replacement))
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

    // The part an Unbound atom rests on: its declaration's PendingFailure (paths a and c), else its binder, the slot whose own omission
    // failed (OmittedOrigin, path b). The declaration is the atom's binder's owner (PendingOrigin made it there).
    private Koto UnboundCause(BoundOrigin atom)
        => OriginOwner(atom.Binder!) is { } owner && this.originDeclarations.TryGetValue(owner, out var declaration) &&
            declaration.PendingFailure is { } failure ? failure : atom.Binder!;

    private void CompleteOriginDeclaration(OriginDeclaration? declaration)
    {
        if (declaration is null || declaration.State != 1)
        {
            return;
        }

        var clauses = OriginClauses.Get(declaration.Owner);
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var a = this.BindOrigin(clause.Left, declaration.Scope!);
            var b = this.BindOrigin(clause.Right, declaration.Scope!);
            var recovered = IsRecovery(clause, out _);
            if (recovered || a is null || b is null)
            {
                // A guessed operator or unavailable operand supplies no equality, outlives edge or elision evidence.
                if (recovered)
                {
                    this.Fail(clause, BindingFailure.InvalidOrigin);
                }
                else
                {
                    this.CompleteDependent(clause, a is null ? clause.Left : clause.Right);
                }

                if (declaration.Owner.BindingFailure == BindingFailure.None)
                {
                    this.Fail(declaration.Owner, BindingFailure.InvalidOrigin);
                    this.AddPrerequisite(declaration.Owner, clause);
                }

                declaration.Failure ??= clause;
            }
            else
            {
                declaration.Relations.Add(new(a, b, clause.IsEquality, clause));
                clause.BindingState = BindingState.Resolved;
            }
        }

        if (declaration.Failure is { } failure)
        {
            RestPendingOn(declaration, failure);

            // The contract is unavailable. In particular, its missing relations cannot justify defaulting a result to
            // static, or supply premises through the valid-looking subset of the same rejected declaration; only the
            // declaration judge reads that subset (RejectedRelations).
            (declaration.RejectedRelations ??= new(declaration.Relations.Count)).AddRange(declaration.Relations);
            declaration.Relations.Clear();
            declaration.State = 3;
            this.originStateVersion++;
            foreach (var pending in declaration.Pending)
            {
                this.CompleteDependent(pending.Use, failure);
            }

            return;
        }

        // Equalities are substitutions before result defaulting. Revisit equations after
        // each substitution so chains are independent of clause order.
        for (var pass = 0; pass <= declaration.Relations.Count; pass++)
        {
            var changed = false;
            foreach (var relation in declaration.Relations)
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

                if (Flexibility(a) != 0 && !Contains(b, a))
                {
                    declaration.Replacements[a] = b;
                    this.originStateVersion++;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        declaration.State = 2; // Further omission performs the established position rule.
        this.originStateVersion++;
        foreach (var pending in declaration.Pending)
        {
            var origin = this.ResolveOrigin(pending.Origin, declaration);
            if (origin.Kind != OriginKind.Unbound || !ReferenceEquals(origin, pending.Origin))
            {
                continue;
            }

            // Only a nontrivial explicit outlives clause universally quantifies a result
            // slot. Intrinsic well-formedness obligations do not change result elision.
            var quantified = false;
            if (pending.Context.Position == TypePosition.Result && ReferenceEquals(pending.Context.Owner, declaration.Owner) && pending.Slot >= 0)
            {
                foreach (var relation in declaration.Relations)
                {
                    var a = this.ResolveOrigin(relation.Longer, declaration);
                    var b = this.ResolveOrigin(relation.Shorter, declaration);
                    if (!relation.Equality && !OriginOutlives(a, b) && (Contains(a, origin) || Contains(b, origin)))
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
                declaration.Replacements[origin] = completed;
                this.originStateVersion++;
            }
        }

        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(declaration.Owner);
        visitor.Declaration = null;
        for (var i = 0; i < declaration.Relations.Count; i++)
        {
            var relation = declaration.Relations[i];
            declaration.Relations[i] = relation with
            {
                Longer = this.ResolveOrigin(relation.Longer, declaration),
                Shorter = this.ResolveOrigin(relation.Shorter, declaration),
            };
        }

        declaration.State = 3;
        this.originStateVersion++;

        int Flexibility(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Unbound)
            {
                // An enclosing input can complete an equality class containing a nested
                // signature or result. Such dependent occurrences must not elide first.
                foreach (var pending in declaration.Pending)
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

        static bool Contains(BoundOrigin expression, BoundOrigin atom)
        {
            if (ReferenceEquals(expression, atom))
            {
                return true;
            }

            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Contains(expression.Operands[i], atom))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Queried only after an Origin fit failed: structural Type failures and refuted lifetime relations stay independent.
    private Koto? FailedOriginEnvironment(Koto use)
    {
        for (var owner = use; owner is not null; owner = owner.Parent)
        {
            if (this.originDeclarations.TryGetValue(owner, out var declaration) && declaration.Failure is { } failure)
            {
                return failure;
            }
        }

        return null;
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

            return this.types.Intern(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments);
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
                    binding.Fail(node, BindingFailure.InvalidOrigin);
                }
            }

            if (node.BoundSymbol is { Type: { } symbolType } symbol && ReferenceEquals(symbol.Declaration, node))
            {
                symbol.Type = binding.RewriteOrigins(symbolType, declaration);
            }

            node.VisitChildren(this);
        }
    }
}

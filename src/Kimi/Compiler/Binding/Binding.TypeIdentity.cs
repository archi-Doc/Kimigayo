// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding.TypeIdentity: SPEC 8.2.3's Type identity of a scope's view, the one identity judge (R7-C3a).
// - Responsibility: a view's identity classes (Binding.TypeIdentity.Classes): every available identity premise applied, closed under
//   decomposition and congruence (a node keyed by its head, compared attributes and components' class ids); a class's state One,
//   Unsupported, Unproven, Cyclic or Conflict from its constructed members' meets, each compared attribute decided by its domain's own
//   rule (Origins: OriginDeclaration's `==`, then the outlives judgment both ways, SPEC 15.6.5; lengths: the normalized expression;
//   Function Types: quantified Origins paired by position within the pair; pair heads: the admitted set, SPEC 8.3.4, an application
//   admitting exactly `owner` being its target from its addition); cycles: the class graph's strongly connected components; the
//   premise error an error class is (SPEC 8.2.3's table), recorded once per pass at the declaration whose premises first form it;
//   the identity proposition's Proven (IdentityProven); ContractTypeCore's guard (ProjectionClassFailed); the class key the premise
//   supply compares by (ClassKey, R7-C2c2 c2-1). Until R7-C2c2 c2-4 replaces it, dev's member substitution (SubstituteIdentity: a
//   Type parameter's first premise, in scope order) and BU-72's HIR premise stay here.
// - Inputs: the view's chain of valid Constraint environments: positive identity premises and their availability (R7-C2e1's
//   Completion); the Origin declarations' relations and completion; the pairs' admitted sets.
// - Outputs: per view and pass, the classes (ClassesOf) and their Completion (DECISIONS R7-M20's W7); Proven; the premise error; a
//   Type's key in a table (ClassKey: a parameter's or projection's class's least parameter or projection by a total structural order,
//   Representative, or the Type over its components' keys; a constructed member keys to itself, its conformance being
//   ResolveConformance's alone), which relates only what Proven relates (a class in state One with complete meets, whatever the
//   table's finality) and which only ContractSolver's supply reads: no reader receives a key as a Type; such a class's complete
//   constructed member (CompleteMember, the same scan), which IdentityRefuted (a parameter bound by a premise) and ProveProposition's
//   contradiction check (N9) read; whether two Types are known to differ at a corresponding position (IdentityRefuted, SPEC 8.2.3's
//   Refuted read positionally over the abstract Types, in the conforming Type's terms; HeadsDiffer, the head rule it shares with the
//   collision check), which the Contract judge's paths and the Callable judge's signature read before they refute (PLAN G866).
// - Invariants: the classes are a function of the premise set (union by the smaller index, states from the round that joins nothing);
//   one finality rule per class (final unless a meet rests on an Origin "no" while a declaration it read is not complete or the
//   premise environment cannot be rented); only a Complete table is memoized, by SerialOf, for one pass, and a table that is not is
//   rebuilt per read: the provisional path costs reads times the view's premise nodes (the reads include the premise clauses' own, so
//   it is quadratic in the premises and linear in the uses); one table per view and pass, rebuilt in place; warm 0 B (pooled tables).
// - Prohibitions: no premise is withheld, ranked or chosen by order, and no mode parameter; no judgment inside the closure but the
//   attribute domains', which read no identity; an erroneous class proves nothing and makes its declaration's context no failed one.
public sealed partial class Binding
{
    private readonly HashSet<BoundType> identityExpansions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingScope, (int Serial, IdentityClasses Classes)> identityClasses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingScope> identityBuilding = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingScope, bool> identityRecorded = new(ReferenceEqualityComparer.Instance); // whether it is the owner's failure
    private readonly Stack<IdentityClasses> identityPool = new();
    private readonly List<IdentityClasses> identityLent = new();
    private IdentityClasses? identityExtended; // R7-C2c2 c2-2: ExtendedClassesOf's one request-local table

    // The scaling rows' measures: the nodes the identity classes added in this compilation, and ClassKey's steps and the class members
    // its Representative scanned.
    internal int IdentityClassWork { get; private set; }

    internal long IdentityKeyWork { get; private set; }

    private static bool HasParameterIdentity(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true })
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 8.2.3: whether a Type-identity premise of any subject is available in `scope` or an enclosing scope (HasTypeIdentity).
    private static bool HasTypeIdentity(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasTypeIdentity: true })
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 8.2.3 (Refuted): two Types whose heads or compared attributes are known and differ: kinds, declarations, Semantics,
    // primitives, component and length-argument counts, constant lengths. Origins are not compared (unproven agreement is no
    // disagreement). The identity judge's known difference (IdentityRefuted) and the collision check's (UnifyTypes) read this one rule.
    private static bool HeadsDiffer(BoundType a, BoundType b)
    {
        if (a.Kind != b.Kind || a.Symbol != b.Symbol || a.Semantics != b.Semantics || a.Components.Count != b.Components.Count || a.LengthArguments.Length != b.LengthArguments.Length ||
            (a.Kind == BoundTypeKind.Primitive && a.Name != b.Name) ||
            (a.Kind == BoundTypeKind.FixedArray && a.LengthExpression is null && b.LengthExpression is null && a.Length != b.Length))
        {
            return true;
        }

        for (var i = 0; i < a.LengthArguments.Length; i++)
        {
            if (a.LengthArguments[i] is { IsConstant: true } x && b.LengthArguments[i] is { IsConstant: true } y && !ReferenceEquals(x, y))
            {
                return true;
            }
        }

        return false;
    }

    // IdentityRefuted's walk, a complete member standing for its class's parameter or projection. An abstract position (IsAbstract: a
    // parameter, a Contract's Self among them, a projection, a pair application) differs only from a Type it is a proper part of;
    // elsewhere the heads (HeadsDiffer) and then each pair of components decide.
    private static bool Differs(BoundType a, BoundType b, IdentityClasses? classes)
    {
        a = classes?.CompleteMember(a) ?? a;
        b = classes?.CompleteMember(b) ?? b;
        if (ReferenceEquals(a, b))
        {
            return false;
        }

        if (a.IsAbstract || b.IsAbstract)
        {
            return IsProperPart(a, b) || IsProperPart(b, a);
        }

        if (HeadsDiffer(a, b))
        {
            return true;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (Differs(a.Components[i], b.Components[i], classes))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 8.2.3: `part` is a proper part of `whole` through constructed heads alone; a projection or pair application between them
    // decides nothing (a pair admitting `owner` may be its target).
    private static bool IsProperPart(BoundType part, BoundType whole)
    {
        if (whole.IsAbstract)
        {
            return false;
        }

        for (var i = 0; i < whole.Components.Count; i++)
        {
            if (ReferenceEquals(whole.Components[i], part) || IsProperPart(part, whole.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The index pass's reset of a member or element access's identity premise, which each pass publishes again (DECISIONS BU-72).
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private static void ClearIdentityPremise(Koto node)
    {
        if (node is MemberAccessKoto or IndexKoto)
        {
            HirTables.PlanFor(node, written: false).Second = null;
        }
    }

    // The pass's tables go back to the pool (ResetCapabilities); each keeps its capacity.
    private void ResetIdentityClasses()
    {
        foreach (var classes in this.identityLent)
        {
            this.identityPool.Push(classes);
        }

        this.identityLent.Clear();
        this.identityClasses.Clear();
        this.identityRecorded.Clear();
    }

    // The view's classes and their Completion; a re-entered build answers nothing (Provisional).
    private IdentityClasses? ClassesOf(BindingScope scope, out Completion completion)
    {
        var serial = SerialOf(scope);
        if (this.identityClasses.TryGetValue(scope, out var kept) && kept.Serial == serial)
        {
            completion = kept.Classes.Completion;
            return kept.Classes;
        }

        completion = Completion.Provisional;
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            // The view's one table of the pass is rebuilt in place when its premises moved or it was not Complete: no reader holds a
            // view's table across a build of the same view (the attribute domains read no identity), and a build nested in another one
            // takes its own table.
            if (kept.Classes is not { } classes || this.identityBuilding.Count != 1)
            {
                classes = this.identityPool.Count != 0 ? this.identityPool.Pop() : new IdentityClasses(this);
                this.identityLent.Add(classes);
            }

            classes.Open(scope);
            this.ApplyIdentityPremises(classes, scope);
            classes.Close();
            this.IdentityClassWork += classes.Work;
            completion = classes.Completion;
            this.identityClasses[scope] = (classes.Final && SerialOf(scope) == serial ? serial : -1, classes); // -1: kept, not memoized
            return classes;
        }
        finally
        {
            this.identityBuilding.Remove(scope);
        }
    }

    // R7-C2c2 c2-2: the view's classes with the identity rows a request's walk recorded (SPEC 8.2.3's grounds include the
    // Constraints `T is C` supplies at every depth). Request-local: one reused table, never memoized as the view's; its rows are
    // Provisional (a derived availability), so nothing keyed by it is kept, and at R7-C2e2 e2-1's merge what it keys stays the
    // request's. Its keys follow the table's rule: a constructed member keys to itself (Representative).
    private IdentityClasses? ExtendedClassesOf(BindingScope scope, List<(BoundType A, BoundType B)> rows)
    {
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            var classes = this.identityExtended ??= new IdentityClasses(this);
            classes.Open(scope);
            this.ApplyIdentityPremises(classes, scope);
            for (var i = 0; i < rows.Count; i++)
            {
                classes.Premise(rows[i].A, rows[i].B, Completion.Provisional);
            }

            classes.Close();
            this.IdentityClassWork += classes.Work;
            return classes;
        }
        finally
        {
            this.identityBuilding.Remove(scope);
        }
    }

    private void ApplyIdentityPremises(IdentityClasses classes, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject: { } subject, RequiredType: { } required })
                {
                    continue;
                }

                if (this.AvailableConstraintFact(environment, fact, out var availability))
                {
                    classes.Premise(subject, required, availability);
                }
                else
                {
                    classes.Read(availability); // An unavailable premise that may become available: the classes may grow.
                }
            }
        }
    }

    // SPEC 8.2.3: `a is b` holds in the view when the two are one complete Type, or its classes make them one (Verdict's Proven); an
    // erroneous or provisional class proves nothing.
    // SPEC 8.2.3 (Refuted), 8.3.2 ("absence of proof is not enough"; "Unbound Types ... are never negative results"): whether two
    // Types are known to differ at a corresponding position, in the view and in the terms of the conforming Type `self` (a Contract's
    // Self is `self`, SPEC 8.4; completed projections first, ContractType). A judge refutes two Types' agreement only so; a pair with no
    // known difference that it cannot prove is Unknown (the Contract judge's paths, the Callable judge's signature). Sound, not
    // complete: an admitted set or a proper part through a class is not read, so such a pair stays Unknown.
    private bool IdentityRefuted(BoundType a, BoundType b, BindingScope scope, BoundType? self)
        => Differs(this.ContractType(a, scope, self), this.ContractType(b, scope, self), HasTypeIdentity(scope) ? this.ClassesOf(scope, out _) : null);

    private bool IdentityProven(BoundType a, BoundType b, BindingScope scope)
        => this.SameCompleteType(a, b) || (HasTypeIdentity(scope) && this.ClassesOf(scope, out _) is { } classes && classes.Same(a, b));

    // SPEC 8.2.3 ("an abstract Type ... takes the form its binding, a completed projection or its admitted set fixes"): the members of a
    // Type's identity class in the view, appended to `into`; none without a premise or where the class is erroneous or provisional. The
    // key walk's reader (Binding.Retention.PlaceKeys' View, PLAN G823), which chooses no premise: a class's constructed members are one.
    private void IdentityMembers(BoundType type, BindingScope scope, List<BoundType> into)
    {
        if (HasTypeIdentity(scope) && this.ClassesOf(scope, out _) is { } classes)
        {
            classes.Members(type, into);
        }
    }

    // ContractTypeCore's guard: a Type in a final erroneous class is completed by no premise; the reading meets the table's Completion (W4).
    private bool ProjectionClassFailed(BoundType projection, BindingScope scope, out Completion completion)
        => this.ClassesOf(scope, out completion) is { } classes && classes.Failed(projection);

    // SPEC 8.2.3's premise errors at the validation step: a scope's own final error class is recorded once per pass at its owner, from
    // the evidence available there, as that step's own proofs are; the environment stays valid (its facts stay evidence, and an
    // erroneous class publishes no member). In the Final pass an own error class still waiting for its Origin declarations is the
    // located limit (an Origin attribute the judge could not decide).
    private bool RecordIdentityPremiseError(BindingScope scope, BindingMode mode)
    {
        if (scope.Constraints is not { Invalid: false, HasTypeIdentity: true } || this.identityRecorded.ContainsKey(scope) ||
            this.ClassesOf(scope, out _) is not { Worst: not IdentityClasses.One } own)
        {
            return false;
        }

        var (worst, waiting) = own.OwnError(scope.Parent is { } parent ? this.ClassesOf(parent, out _) : null);
        var failure = worst switch
        {
            IdentityClasses.One when waiting && mode == BindingMode.Final => BindingFailure.Unsupported,
            IdentityClasses.One => BindingFailure.None,
            IdentityClasses.Unproven => BindingFailure.UnprovenConstraint,
            IdentityClasses.Unsupported => BindingFailure.Unsupported,
            _ => BindingFailure.InvalidConstraint,
        };

        if (failure == BindingFailure.None)
        {
            return false;
        }

        this.identityRecorded.Add(scope, scope.Owner.BindingFailure == BindingFailure.None);
        this.Fail(scope.Owner, failure);
        return true;
    }

    // SPEC 8.2.3, 23.1.2: a declaration whose own failure is its premise error keeps its context, as a shape violation keeps its signature:
    // the error is reported once, at it, and its other checks are judged as in a valid view.
    private bool KeepsPremiseErrorContext(Koto declaration)
        => this.scopes.TryGetValue(declaration, out var scope) && this.identityRecorded.TryGetValue(scope, out var own) && own &&
            scope.Constraints is { Invalid: false };

    // SPEC 8.2.3, 23.1.2 (the producer channel): the declaration whose recorded premise error a check at `node` read, when one of the
    // Types it compared has a part in a final erroneous class of the node's view; the outermost scope whose own classes hold it is the
    // producer. A failure path only.
    private Koto? IdentityPremiseErrorRead(Koto node, BoundType? actual, BoundType? expected)
    {
        Koto? producer = null;
        for (var current = this.ConstraintScope(node); current is not null && this.identityRecorded.Count != 0; current = current.Parent)
        {
            if (this.identityRecorded.TryGetValue(current, out var own) && own && this.ClassesOf(current, out _) is { } classes &&
                (classes.Holds(actual) || classes.Holds(expected)))
            {
                producer = current.Owner;
            }
        }

        return producer;
    }

    // SPEC 8.2.3's Origin attribute, decided by the Origins' owner: OriginDeclaration's `==` (one Origin substituted by the other) and the
    // outlives judgment both ways (EntailsOrigin, SPEC 15.6.5's reachability over the declaration's premises). A "no" is final once the
    // declarations it read are complete and the premise environment can be rented; a "yes" only grows with the premises.
    private bool OriginsAgree(BoundOrigin? a, BoundOrigin? b, Koto owner, ref bool provisional)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        var incomplete = false;
        for (Koto? node = owner; node is not null; node = node.Parent)
        {
            if (!this.originDeclarations.TryGetValue(node, out var declaration))
            {
                continue;
            }

            incomplete |= !declaration.Completed;
            if (declaration.Resolves)
            {
                (a, b) = (declaration.Substitution(a) ?? a, declaration.Substitution(b) ?? b);
            }
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (!this.OriginPremisesAvailable)
        {
            provisional = true; // The premise environment would depend on itself (PLAN G74): no answer yet.
            return false;
        }

        var agree = this.EntailsOrigin(a, b, owner, default, 0) && this.EntailsOrigin(b, a, owner, default, 0);
        provisional |= !agree && incomplete;
        return agree;
    }

    /// <summary>Substitutes the Type-identity premises available in <paramref name="scope"/> into <paramref name="type"/>.</summary>
    /// <param name="type">A bound expression Type.</param>
    /// <param name="scope">The scope whose Constraint environments supply the premises.</param>
    /// <returns><paramref name="type"/> itself when no premise applies; otherwise the substituted interned Type.</returns>
    private BoundType SubstituteIdentityPremises(BoundType type, BindingScope scope)
    {
        if (!type.ContainsParameter || !HasParameterIdentity(scope))
        {
            return type;
        }

        return this.SubstituteIdentity(type, scope, parametersOnly: false, out _);
    }

    // DECISIONS BU-72: a member access or an element access whose receiver's Type, through its Semantics layers, is a Type parameter P
    // that the premises make the complete Type U was resolved on U; it publishes the interned `P is U` (HirTables.IdentityPremise), since
    // a MIR Place keeps P.
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private void PublishIdentityPremise(Koto receiver, BoundType actual, BoundType substituted)
    {
        var access = receiver.Parent;
        while (access is ParenthesizedKoto)
        {
            access = access.Parent;
        }

        if (access is not (MemberAccessKoto or IndexKoto) || !ReferenceEquals(KotoHelper.UnwrapParentheses(((BinaryKoto)access).Left), receiver))
        {
            return;
        }

        while (actual is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && substituted is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            (actual, substituted) = (actual.Components[0], substituted.Components[0]);
        }

        if (actual.Kind == BoundTypeKind.Parameter && substituted.Kind != BoundTypeKind.Parameter)
        {
            HirTables.PlanFor(access, written: true).Second = this.InternConstraint(new(ConstraintKind.TypeIdentity, actual, substituted));
        }
    }

    // W2 (ContractSolver's Completion): Complete without a parameter or at the formation's own recursion (identityExpansions), else
    // the meet of IdentityPremise's, the components' and the held Type's.
    private BoundType SubstituteIdentity(BoundType type, BindingScope scope, bool parametersOnly, out Completion completion)
    {
        completion = Completion.Complete;
        if (!type.ContainsParameter)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (!this.identityExpansions.Add(type))
            {
                return type; // A recursive structural premise must not expand without bound.
            }

            try
            {
                var required = this.IdentityPremise(type, scope, parametersOnly, out completion);
                if (required.Kind == BoundTypeKind.Parameter)
                {
                    return required;
                }

                var image = this.SubstituteIdentity(required, scope, parametersOnly, out var part);
                completion = Meet(completion, part);
                return image;
            }
            finally
            {
                this.identityExpansions.Remove(type);
            }
        }

        var count = type.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.SubstituteIdentity(type.Components[i], scope, parametersOnly, out var part);
                completion = Meet(completion, part);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            // DECISIONS BU-60: the Type instance a closure's context holds is substituted with the Closure Type.
            var context = type.ClosureContext;
            if (TyCtxt.HeldType(type) is { } held)
            {
                var image = this.SubstituteIdentity(held, scope, parametersOnly, out var heldPart);
                completion = Meet(completion, heldPart);
                context = ReferenceEquals(image, held) ? context : this.types.HeldContext(type.Symbol!, image);
            }

            if (!changed && ReferenceEquals(context, type.ClosureContext))
            {
                return type;
            }

            // A substituted struct or enum is another Type: its stored fields and cases follow the substitution, as for the
            // declared Types whose storage is prepared, so a field of it keeps the substituted Type (SPEC 8.2).
            var substituted = this.WithContext(this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments), context);
            this.PrepareTypeStorage(substituted);
            return substituted;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // Parameter identities form equivalence classes, not directed rewrite chains. Close only over the finite
    // parameter premises in scope, then choose the same representative for every member. No structural unification
    // or inference of missing premises is performed. Scratch storage is reused across queries and rebinding.
    // W3 (ContractSolver's Completion): Complete when every fact whose availability it asks is direct (W6).
    private BoundType IdentityPremise(BoundType parameter, BindingScope scope, bool parametersOnly, out Completion completion)
    {
        completion = Completion.Complete;
        var capacity = 1;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true } environment)
            {
                capacity += environment.Facts.Count * 2;
            }
        }

        var members = this.RentTypes(capacity);
        var count = 1;
        members[0] = parameter;
        try
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType.Kind: BoundTypeKind.Parameter })
                        {
                            continue;
                        }

                        var available = this.AvailableConstraintFact(environment, fact, out var availability);
                        completion = Meet(completion, availability);
                        if (!available)
                        {
                            continue;
                        }

                        var hasSubject = members.AsSpan(0, count).Contains(fact.Subject);
                        var hasRequired = members.AsSpan(0, count).Contains(fact.RequiredType);
                        if (hasSubject != hasRequired)
                        {
                            members[count++] = hasSubject ? fact.RequiredType : fact.Subject;
                            changed = true;
                        }
                    }
                }
            }

            BoundType? representative = null;
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType: { } required } ||
                        !members.AsSpan(0, count).Contains(fact.Subject))
                    {
                        continue;
                    }

                    var available = this.AvailableConstraintFact(environment, fact, out var availability);
                    completion = Meet(completion, availability);
                    if (available)
                    {
                        if (required.Kind != BoundTypeKind.Parameter)
                        {
                            if (!parametersOnly)
                            {
                                return required;
                            }

                            continue;
                        }

                        representative ??= required;
                    }
                }
            }

            return representative ?? parameter;
        }
        finally
        {
            this.typeScratch.Return(members, clearArray: true);
        }
    }
}

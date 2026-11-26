// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The compilation's level of the interned constraints (PLAN G949 Stage 2 U4): those interned after the library's seal; the library's
    // are LibraryLayer.Constraints, found first (InternConstraint).
    private readonly Dictionary<ConstraintKey, BoundConstraint> constraints = new();

    private readonly DeferredConstraintRows deferredConstraints;

    // The images of NormalizeProofType's queries (DECISIONS BU-19, BU-64): one outermost call is a query, its scope fixed, so each
    // distinct part is normalized once per query, a projection leaf's ContractType answer included; a call begun inside it (a proof that
    // a leaf's answer runs) is a nested query, which can only evict this query's entries, recomputed then (TypeMemo's contract).
    private readonly TypeMemo<BoundType, (BoundType Image, Completion Completion)> proofParts = new();

    // The runs of AddConjuncts' readers (AddConstraintFact, AddSupplyRows), each removed by its reader.
    private readonly List<BoundConstraint> conjuncts = new();
    private ConstraintSyntaxVisitor? constraintSyntaxVisitor;

    // The scaling rows' measure: the parts NormalizeProofType normalized in this compilation (its walk's visits that missed the memo).
    internal int ProofPartWork { get; private set; }

    /// <summary>Queries a bound proposition using the lexical input constraints at a syntax node.</summary>
    /// <param name="constraint">The proposition produced by this compilation's Binding.</param>
    /// <param name="context">The use site supplying lexical assumptions.</param>
    /// <returns>The four-valued proof result; Unknown is never permission to proceed.</returns>
    public ConstraintProof Prove(BoundConstraint constraint, Koto context)
        => this.ProveConstraint(constraint, this.ConstraintScope(context), context);

    /// <summary>Substitutes declaration slots and queries the result in a caller's lexical environment.</summary>
    /// <param name="constraint">The bound declaration constraint.</param>
    /// <param name="binder">The declaration owning the substituted slots.</param>
    /// <param name="arguments">Complete type arguments in slot order.</param>
    /// <param name="context">The caller's syntax node.</param>
    /// <returns>The proof result after identity-preserving substitution.</returns>
    public ConstraintProof Prove(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments, Koto context)
        => this.ProveConstraint(this.SubstituteConstraint(constraint, new CallSubstitution(binder, arguments)), this.ConstraintScope(context), context);

    // The scope a node's names and premises are judged in: its nearest enclosing scope, where a match arm's body is in its pattern's
    // scope (SPEC 14.8.2) and a standard witness bridge has its conformance path's premises (SPEC 11.9.2).
    internal BindingScope ConstraintScope(Koto node)
    {
        Koto? child = null;
        for (Koto? current = node; current is not null; child = current, current = current.Parent)
        {
            if (current is FunctionKoto { IsPropertyWitness: true, BoundSymbol.Scope: { } bridge })
            {
                return bridge; // SPEC 11.9.2: a standard witness bridge's premises are its conformance path's (PropertyWitnessFunction).
            }

            if (this.ScopeStop(current, child) is { } scope)
            {
                return this.NodeScope(node, scope);
            }
        }

        return this.ModuleScope(node);
    }

    private static ConstraintProof NegateProof(ConstraintProof value) => value switch
    {
        ConstraintProof.Proven => ConstraintProof.Refuted,
        ConstraintProof.Refuted => ConstraintProof.Proven,
        _ => value,
    };

    // R7-C2c2 c2-4 (SPEC 8.3.2): the one join of a premise with a judge's closed world, by polarity: a premise is positive evidence,
    // the closed world's Refuted negative, both contradictory evidence (Error). ProveConformance and the capability judge read it.
    private static ConstraintProof PremiseJoin(bool premise, ConstraintProof proof)
        => !premise || proof == ConstraintProof.Error ? proof : proof == ConstraintProof.Refuted ? ConstraintProof.Error : CombineProof(ConstraintProof.Proven, proof, false);

    private static ConstraintProof CombineProof(ConstraintProof left, ConstraintProof right, bool conjunction)
    {
        if (left == ConstraintProof.Error || right == ConstraintProof.Error)
        {
            return ConstraintProof.Error;
        }

        var decisive = conjunction ? ConstraintProof.Refuted : ConstraintProof.Proven;
        var both = conjunction ? ConstraintProof.Proven : ConstraintProof.Refuted;
        return left == decisive || right == decisive ? decisive : left == both && right == both ? both : ConstraintProof.Unknown;
    }

    // SPEC 8.3.1, 8.3.2 Conjunction (8.2.3 grounds: conjunction elimination): a supplied constraint is itself and, a conjunction, each
    // conjunct's supply, appended to `into` in that order. The one split of a premise's supply; its readers are the environment's intake
    // (AddConstraintFact) and the walk's item intake (ContractSolver's AddSupplyRows), each over its own run of `conjuncts`, so a
    // reader nested in another appends above it and removes its run before returning.
    private static void AddConjuncts(List<BoundConstraint> into, BoundConstraint constraint)
    {
        into.Add(constraint);
        if (constraint.Kind == ConstraintKind.And)
        {
            AddConjuncts(into, constraint.Left!);
            AddConjuncts(into, constraint.Right!);
        }
    }

    private static bool IsSelfConstraint(IsKoto clause)
        => clause.Left is IdentifierNameKoto { IdentifierName: "Self" };

    // SPEC 7.5, 8.5.2: the base of a subject's projection chain (`T` of `T.(C).X.(D).Y`), below transparent wrappers; the subject itself
    // when it is no projection. Its published symbol is the root of a subject that formed no Type.
    private static Koto SubjectBase(Koto subject)
    {
        var node = UnwrapTransparentType(subject)!;
        while (node is MemberAccessKoto projection)
        {
            node = UnwrapTransparentType(projection.Left)!;
        }

        return node;
    }

    private static bool IsSelfRequirement(BoundConstraint constraint)
        => (constraint.Contract is { } contract && IntrinsicContract.Of(contract.Intrinsic).SelfRequirement) ||
        (constraint.Kind == ConstraintKind.Not && constraint.Left?.Contract?.Intrinsic == IntrinsicKind.ObjectPayload);

    // SPEC 6.1.4, 8.4.3: whether a condition of a Contract's clause is an implementation requirement, a condition on the conforming Self:
    // one of its Types (subject, required Type, Contract reference, an `or` or `not` operand's) names the Contract's Self. An obligation of
    // every conformer, never an input condition of a reference to the Contract, which is a condition only on the Contract's parameters.
    // Its conditions are ReferenceInputCondition's; an associated requirement (`associate X is R`: the subject `Self.(C).X`) is one whole.
    private static bool IsImplementationRequirement(BoundConstraint condition)
        => (condition.Subject is { } subject && NamesContractSelf(subject)) || (condition.RequiredType is { } required && NamesContractSelf(required)) ||
        (condition.Contract?.Type is { } reference && NamesContractSelf(reference)) ||
        (condition.Left is { } left && IsImplementationRequirement(left)) || (condition.Right is { } right && IsImplementationRequirement(right));

    private static bool NamesContractSelf(BoundType type)
    {
        if (IsContractSelf(type))
        {
            return true;
        }

        for (var i = 0; type.HasAbstractPart && i < type.Components.Count; i++)
        {
            if (NamesContractSelf(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // J1's one test (R7-C2c2 c2-5c, CC-36 C4): a reading can differ from what is written only through an associated projection in it
    // (`projection`: a constraint's HasAssociatedProjection, or an environment's, any of its facts') or an identity premise in the view
    // (`typeIdentity`: HasTypeIdentity). Its readers: NormalizeProofConstraint's early return, Supply's consult (per environment) and
    // IndexNormalized (per environment, then per fact).
    private static bool ReadingCanDiffer(bool projection, bool typeIdentity) => projection || typeIdentity;

    // SPEC 14.8.2: the scope of the arm of `match` whose body is `body`, its pattern's; null when `body` is no arm's body.
    private BindingScope? ArmScope(MatchKoto match, Koto body)
        => KotoHelper.IsSelectionBody(match, body) ? this.scopes.GetValueOrDefault(match.Arms[body.SelectionArm].Pattern) : null;

    // Whether a Type depends on a generic input; a Contract's Self counts only with `contractSelf`, since a Contract clause on
    // Self is an obligation of every conformer rather than a closed proposition. An existence walk: each distinct part visited once
    // (DECISIONS BU-19, BU-55).
    private bool DependentType(BoundType type, bool unresolvedProjection = true, bool contractSelf = false, bool includeOrigins = true)
    {
        var query = this.visitedParts.Begin(this.storageVersion);
        return Dependent(type);

        bool Dependent(BoundType part)
        {
            if ((part.Kind == BoundTypeKind.Parameter && (contractSelf || !IsContractSelf(part))) ||
                part.Kind is BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
                (unresolvedProjection && part.Kind == BoundTypeKind.AssociatedProjection) || part.LengthExpression is not null ||
                (includeOrigins && part.Origin is not null && part.Origin.Kind != OriginKind.Static))
            {
                return true;
            }

            foreach (var length in part.LengthArguments)
            {
                if (length is { IsConstant: false })
                {
                    return true;
                }
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (includeOrigins && part.OriginArguments[i].Kind != OriginKind.Static)
                {
                    return true;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (this.FirstVisit(part.Components[i], query) && Dependent(part.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool IsDependentConstraint(IsKoto clause)
        => !IsSelfConstraint(clause) && clause.BoundConstraint is { } constraint && this.DependentConstraint(constraint);

    private bool DependentConstraint(BoundConstraint constraint, bool contractSelf = false)
        => (constraint.Subject is { } subject && this.DependentType(subject, unresolvedProjection: false, contractSelf)) ||
        (constraint.RequiredType is { } required && this.DependentType(required, unresolvedProjection: false, contractSelf)) ||
        (constraint.Left is { } left && this.DependentConstraint(left, contractSelf)) ||
        (constraint.Right is { } right && this.DependentConstraint(right, contractSelf));

    // SPEC 6.1.4, 8.3.1: what forming a reference to a Contract judges of its clause `clause`: the reference input condition, the clause's
    // conditions (requirement precedence splits `and`; `or` and `not` are one condition each) that are not implementation requirements,
    // conjoined in their order; null when every condition is one. Read by a reference's formation (CheckConstraints); the premise supply's
    // gate is another contract, a clause that is not closed (DependentConstraint with the Contract's Self, both kinds).
    private BoundConstraint? ReferenceInputCondition(BoundConstraint clause)
    {
        if (clause.Kind != ConstraintKind.And)
        {
            return IsImplementationRequirement(clause) ? null : clause;
        }

        var left = this.ReferenceInputCondition(clause.Left!);
        var right = this.ReferenceInputCondition(clause.Right!);
        return left is null || right is null ? left ?? right
            : ReferenceEquals(left, clause.Left) && ReferenceEquals(right, clause.Right) ? clause : this.InternConstraint(new(ConstraintKind.And, left: left, right: right));
    }

    private ConstraintProof JudgeConstraintAtom(BoundConstraint proposition, BindingScope scope)
    {
        var contexts = this.OperandContexts(proposition);
        if (contexts.Invalid)
        {
            return ConstraintProof.Error;
        }

        if (contexts.Unresolved)
        {
            return ConstraintProof.Unknown;
        }

        // PLAN G924 AF-3b v3-A (Binding.OpenSlots; SPEC 10.8 Unbound slots, 8.3.2): an operand holding an open atom is an unbound slot's,
        // so its Constraint is not judged: the prover's one intake guard (UnsolvedSlots keeps a candidate's own Constraints from it).
        if (proposition.Subject is { ContainsOpenSlot: true } || proposition.RequiredType is { ContainsOpenSlot: true })
        {
            return ConstraintProof.Unknown;
        }

        // R7-C2c2 c2-4 (SPEC 8.2.3, 8.3.2; design R1.1): an identity, Callable or Semantics atom is judged on its subject's Member
        // (MemberOf); the Contract and capability judges read it at their own entries (ProveConformance, RequestCapability).
        if (proposition.Kind is ConstraintKind.TypeIdentity or ConstraintKind.Callable or ConstraintKind.Semantics &&
            proposition.Subject is { } held && this.MemberOf(held, scope, out _) is var member && !ReferenceEquals(member, held))
        {
            return this.JudgeConstraintAtom(this.InternConstraint(new(proposition.Kind, member, proposition.RequiredType, proposition.Contract, proposition.Mask)), scope);
        }

        // SPEC 8.2.3 (F's C2/C3 rulings 333, 334): an identity is decided once, by the identity judge. Over a Type with a part in a
        // recorded erroneous class (IdentityPremiseErrorRead) the atom reads the judge's verdict as it is: Refuted where the judge
        // refutes (IdentityRefuted: a known difference, as heads, whatever the class; one erroneous class is no difference), else
        // Unknown, the read being the proof's limit (IdentityProven's ReadIdentityPremiseError), so an answer the valid facts leave open
        // rests on the judge's record (EndProof). The other atom kinds (Callable, Semantics, a Contract or capability) are judged by
        // their own judges, as before.
        if (proposition.Kind == ConstraintKind.TypeIdentity)
        {
            var (subject, required) = (proposition.Subject!, proposition.RequiredType!);
            if (this.IdentityProven(subject, required, scope, out var read))
            {
                return ConstraintProof.Proven;
            }

            if (this.DependentType(subject) || this.DependentType(required))
            {
                // G1406-11 C-a (SPEC 8.3.3): an erroneous class reads its error (IdentityProven), else a Limited reading is refused.
                if (read is { Limited: true } && this.IdentityPremiseErrorRead(scope, subject, required) is null)
                {
                    this.ReadIdentityLimit(null, scope, read);
                }

                return ConstraintProof.Unknown;
            }

            return this.IdentityPremiseErrorRead(scope, subject, required) is not null && !this.IdentityRefuted(subject, required, scope, null, out _)
                ? ConstraintProof.Unknown : ConstraintProof.Refuted;
        }

        if (proposition.Kind == ConstraintKind.Callable)
        {
            var subject = proposition.Subject!;
            var closure = BoundClosure.Of(subject);
            if (closure is { Signature: null })
            {
                // A Closure value proved Callable has a Signature; a definition without one (a failed slot) is no value (PLAN G1110).
                throw new InvalidOperationException("A Callable subject's Closure definition has no Signature (InternalInvariant; BoundClosure.Of).");
            }

            var signature = this.ClosureSignature(subject) ?? this.FunctionItemSignature(subject) ?? (subject.Kind == BoundTypeKind.Function ? subject : null);
            if (signature is null)
            {
                return this.DependentType(subject) ? ConstraintProof.Unknown : ConstraintProof.Refuted;
            }

            var receiver = closure?.Receiver ?? SemanticsKind.Ref;
            if (!this.permitClosureReceivers && !CallableReceiverFits(receiver, proposition.Mask))
            {
                return ConstraintProof.Refuted;
            }

            // SPEC 15.6.1, 8.3: once the structural part of the whole-contract comparison holds, its Origin part is Proven or Unknown,
            // never Refuted (CallableOriginFailure explains it). A required signature over an open region is decided by the per-call
            // stand-in instead (SPEC 15.3.6, PerCallCallables). A Function Item's Origin conditions, its clauses and result premises, are
            // proven from the required contract and belong to that Origin part (SPEC 15.3.7).
            // The Unknown's comparison failure joins the proof as its U fact (design-r2 Amendment 4).
            var failure = default(ComparisonFailure);
            if (subject.Kind is BoundTypeKind.FunctionItem or BoundTypeKind.Closure ? this.DefinitionContractFits(subject, proposition.RequiredType!, scope.Owner, null, out failure)
                : this.CallableSignatureFits(signature, proposition.RequiredType!, scope.Owner, out failure))
            {
                return ConstraintProof.Proven;
            }

            if (!TyCtxt.StorageMatches(proposition.RequiredType, signature) || HasOpenOrigin(proposition.RequiredType!))
            {
                // SPEC 8.3.2, 10.7: only a known difference from the required signature, as compatibility compares it (a Never result
                // fits any; Origins by subtyping, 15.6.1, never a difference, so both sides with every Origin static), refutes; a
                // stand-in decides an open region's (PerCallCallables).
                if (TyCtxt.StorageMatches(proposition.RequiredType, signature) ||
                    this.IdentityRefuted(this.types.Erase(this.CompatibleSignature(signature, proposition.RequiredType!)), this.types.Erase(signature), scope, null, out var read))
                {
                    return ConstraintProof.Refuted;
                }

                this.ReadIdentityLimit(null, scope, read); // G1406-11 C-a: an Unknown over a Limited reading is refused
                return ConstraintProof.Unknown;
            }

            this.ScopeReads.Unknown ??= new(proposition, failure) { Epoch = this.frameEpoch };
            return ConstraintProof.Unknown;
        }

        if (proposition.Kind == ConstraintKind.Semantics)
        {
            var subject = proposition.Subject!;
            if (subject.IsAbstract)
            {
                // SPEC 8.3: a requirement on a Semantics binding is decided by containment of its admitted set.
                var admitted = this.ResultSemantics(subject, scope, QuerySite.Nested);
                return admitted == SemanticsMask.None ? ConstraintProof.Error :
                    (admitted & ~proposition.Mask) == 0 ? ConstraintProof.Proven :
                    (admitted & proposition.Mask) == 0 ? ConstraintProof.Refuted : ConstraintProof.Unknown;
            }

            return proposition.Mask.Contains(subject.Semantics) ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (proposition.Contract is { } intrinsic && IntrinsicContract.Of(intrinsic.Intrinsic).Judged)
        {
            return this.RequestCapability(proposition.Subject!, intrinsic, scope, QuerySite.Nested);
        }

        // Registration is not verified conformance. In particular, absence is not refutation.
        return proposition.Contract is { } contract ? this.ProveConformance(proposition.Subject!, contract, scope, QuerySite.Nested) : ConstraintProof.Unknown;
    }

    // The one find over the two levels (PassPart.Levels; PLAN G949 Stage 2 U4; C-380 F3 (a)): content in no level is inserted at the
    // open one, so a program's constraint equal to a frozen one is that object.
    private BoundConstraint InternConstraint(ConstraintKey key)
    {
        var levels = PassPart.Interned(this.Library, this.Library.Layer.Constraints, this.constraints);
        if (PassPart.Find(levels, key, out var result) is null)
        {
            result = new(key, this.ContainsAssociatedProjection(key.Subject) || this.ContainsAssociatedProjection(key.RequiredType) || this.ContainsAssociatedProjection(key.Contract?.Type));
            levels.Open.Add(key, result);
        }

        return result!;
    }

    // A frozen constraint's negation is set before the seal (D-4, U2b's eager step), so `??` forms only a compilation-level one: a frozen
    // constraint without it would hold a compilation-level object (U4's invariant), the internal-invariant form.
    private BoundConstraint NegateConstraint(BoundConstraint value)
        => value.Kind == ConstraintKind.Not ? value.Left! : value.Negation ?? (value.Negation = this.FormNegation(value));

    private BoundConstraint FormNegation(BoundConstraint value)
        => this.Library.Frozen && this.Library.Layer.Constraints.ContainsKey(ConstraintKey.Of(value))
            ? throw new InvalidOperationException("A frozen constraint has no negation after the library's seal (InternalInvariant; PLAN G949 Stage 2 U4).")
            : this.InternConstraint(new(ConstraintKind.Not, left: value));

    // D-4 (PLAN G949 Stage 2 U2b; C-380 F3 (b) as O-U2-4 narrows it): the library pass forms the negation of every constraint it interned,
    // so no later pass writes a library constraint's Negation (a Not constraint's negation is its operand, never stored). The library's
    // constraints are the layer's level (PLAN G949 Stage 2 U4), the open level during the library pass, so each negation is inserted
    // there before the seal, which the FormNegation refusal rests on.
    private void CompleteNegations()
    {
        foreach (var constraint in this.Library.Layer.Constraints.Values.ToArray())
        {
            _ = this.NegateConstraint(constraint);
        }
    }

    private void BindConstraints()
    {
        this.ResetSourceStates();
        this.bindingConstraintTypes = true;
        // Collect ordinary facts, then complete outer Type/Contract constraints, then P.
        // Dependent function constraints may use P only through their declaration scope.
        for (var pass = 0; pass < 3; pass++)
        {
            if (pass == 2)
            {
                this.BindConditionalDeclarations(true);
            }

            for (var i = 0; i < this.nodes.Count; i++)
            {
                var node = this.nodes[i];
                if (node is FunctionKoto function && pass != 1)
                {
                    for (var j = 0; j < function.TypeConstraints.Count; j++)
                    {
                        var clause = (IsKoto)function.TypeConstraints[j];
                        if (IsRecovery(clause, out _) || this.DeferredConstraint(clause, this.scopes[function]) == (pass != 0))
                        {
                            this.BindConstraint(clause, this.scopes[function]);
                        }
                    }
                }
                else if (node is DeclarationContainerKoto container && pass != 2)
                {
                    // SPEC 8.4, Appendix D: a Contract declares Type parameters; Contract-owned Origin parameters are not introduced.
                    if (container is ContractKoto && container.OriginNames.Count != 0)
                    {
                        this.Record(container, BindingFailure.InvalidConstraint, []);
                    }

                    for (var j = 0; j < container.ConstraintNodes.Count; j++)
                    {
                        var clause = container.ConstraintNodes[j];
                        if (IsRecovery(clause, out _) || this.DeferredConstraint(clause, this.scopes[container]) == (pass != 0))
                        {
                            this.BindConstraint(clause, this.scopes[container]);
                        }
                    }
                }
            }

            if (pass == 0)
            {
                this.RegisterConformances();
                this.BindCopyDeclarations();
                this.BindConditionalDeclarations(false);
                this.impls.Advance(ImplTable.AdmissionPoint.ConditionalClauses); // Point 2 (rule (a)).
                this.RegisterInheritedConformances();
            }
        }

        this.BindConditionalAssociatedSpecifications();

        // SPEC 8.5.1, 8.5.3: every owner's specifications are its members (Parser.ParseAssociatedTypeItem), a Contract's included.
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto or EnumKoto or ContractKoto)
            {
                var owner = (DeclarationContainerKoto)this.nodes[i];
                for (var j = 0; j < owner.Members.Count; j++)
                {
                    if (owner.Members[j] is IsKoto { IsAssociatedConstraint: true } clause)
                    {
                        this.BindAssociatedSpecification(clause, this.scopes[owner]);
                    }
                }
            }
        }

        this.PublishIdentityClauses();
        this.ExpandContractPremises();
        this.PublishSourceStates();
        this.PrepareAssociatedBindings();
        this.impls.Advance(ImplTable.AdmissionPoint.AssociatedBindings); // Point 3 (rule (a)).
        // SPEC 8.3.3: after point 3, where the arguments' normalization exists ("normalized under K's premises": a binding is admitted
        // from here). Uses read the failure through the failed-context read, the states through ValidateConformances' publication.
        this.CheckExpandingRecursion();
        this.bindingConstraintTypes = false;
        this.ValidateConstraintProjectionAccess();
    }

    // A clause waits for the conformances when it names an associated projection (SPEC 8.5, 15.3.1), except a conformance declaration
    // (DeclaresConformance): RegisterConformances registers it after pass 0, so it binds in pass 0 (point 1, ImplTable.Reached). Each clause is
    // decided once per Bind run, before any Constraint binds: the query's TypeName lookups publish import and qualifier facts as every
    // lookup does, and a lookup through a base clause not ready yet is an early read (TypeName's note) that VerifyEarlyReads evaluates
    // again, running the declarations once more on a different answer, so the decision cannot disagree with the final names
    // (Binding.BaseLayers).
    private bool DeferredConstraint(IsKoto clause, BindingScope scope)
    {
        if (!this.deferredConstraints.TryGetValue(clause, out var deferred))
        {
            deferred = !DeclaresConformance(clause) && (clause.IsAssociatedConstraint || this.ContainsProjection(clause.Left, scope) || this.ContainsProjection(clause.Right, scope));
            this.deferredConstraints.Add(clause, deferred);
        }

        return deferred;
    }

    // SPEC 8.5, 15.3.1: whether a requirement names an associated projection anywhere: a PathSuffix that names no declared Type, under
    // the requirement's operators (a group, not, and, or: BindRequirement's) or in any Type component a Type node publishes. It decides by
    // name lookup over the written Type's components, never by the top node's shape.
    private bool ContainsProjection(Koto node, BindingScope scope)
    {
        switch (node)
        {
            // Each PathSuffix is asked in its position's row (UseRow), the row its Type former reads (R7-N1e2-1R).
            case MemberAccessKoto when this.TypeName(node, scope) is null:
                return true;
            case ParenthesizedKoto group:
                return this.ContainsProjection(group.Operand, scope);
            case UnaryKoto { Akind: KotoKind.Not } not:
                return this.ContainsProjection(not.Operand, scope);
            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } binary:
                return this.ContainsProjection(binary.Left, scope) || this.ContainsProjection(binary.Right, scope);
        }

        for (var i = 0; i < node.TypeComponentCount; i++)
        {
            if (this.ContainsProjection(node.TypeComponent(i), scope))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 8.2.1 (PLAN G567-a; the owner's O-249 Q2 (a)): whether the path of a subject that bound to no projection writes Type arguments
    // (`W<Z>.Inner`, `A.B<i32>.C`, `::W<Z>.Inner`, `(Outer<Z>{o}).Cell`), read from its syntax; no fact is published for it. Such a path
    // is F.5's ContainerPath in either tree: Names, Type arguments, `::`, `. Name` and `.(C). Name` suffixes and a BoundContainerQualifier's
    // group, each below its transparent wrappers (UnwrapTransparentType, that rule's owner, as SubjectBase reads them). A Core head (F.5)
    // is the base of a projection only, so it or any other node here is a broken contract: the clause's InternalInvariant_Kd, its one
    // record, and no subject, never "writes none".
    private bool WritesTypeArguments(Koto? syntax, IsKoto clause)
    {
        while (true)
        {
            switch (UnwrapTransparentType(syntax))
            {
                case GenericsKoto:
                    return true;
                case IdentifierNameKoto or TypeSemanticsKoto { Type: null }:
                    return false;
                case SyntaxFormKoto { Akind: KotoKind.RootName } rooted:
                    syntax = rooted.Operands[0];
                    break;
                case ParenthesizedTypeKoto group:
                    syntax = group.Type;
                    break;
                case MemberAccessKoto path:
                    if (this.WritesTypeArguments(path.Right, clause))
                    {
                        return true;
                    }

                    syntax = path.Left;
                    break;
                default:
                    this.Fail(clause, BindingFailure.Internal);
                    return true;
            }
        }
    }

    // PLAN G651 a2 (F2, BU-161; IMPL 21.3.2): a clause binds in a site frame at itself where none is open (OpenQuerySite), so a Type past
    // the limit its subject or requirement forms (a Contract reference's instance) is refused at the clause.
    private void BindConstraint(IsKoto clause, BindingScope scope)
    {
        var site = this.OpenQuerySite(clause);
        this.BindConstraintClause(clause, scope);
        this.CloseQuerySite(site);
    }

    private void BindConstraintClause(IsKoto clause, BindingScope scope)
    {
        if (IsRecovery(clause, out _))
        {
            // Every entry path, including conditional premises, depends on the clause's complete syntax.
            clause.BoundConstraint = this.InternConstraint(new(ConstraintKind.Error));
            this.Record(clause, BindingFailure.InvalidConstraint, []);
            if (IsRecovery(clause.Left, out _))
            {
                // A rejected subject (SPEC F.6) supplies none, and the requirement's own records stay (SPEC 23.1.2), as a failed selector's below.
                this.BindRequirement(clause.Right, BoundType.Unit, false, this.NodeScope(clause, scope));
            }

            return;
        }

        if (clause.FormationType is not null && !IsAssociatedRequirement(clause))
        {
            this.Record(clause, BindingFailure.InvalidConstraint, []); // SPEC 8.5: only a requirement may end with a formation Type.
            return;
        }

        var bindingScope = this.NodeScope(clause, scope);
        var subjectProjections = this.projectionUses.Count; // The projection uses the subject's binding registers: from here to subjectEnd.
        var symbol = this.TypeName(clause.Left, bindingScope); // The subject's one lookup (Requirement, SPEC 8.2.1).
        var semantics = symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        var subject = semantics ? symbol!.Pair!.WholeType : symbol?.Kind == BindingSymbolKind.SemanticsTarget ? symbol.Type : this.BindType(clause.Left, bindingScope);
        clause.Left.BoundSymbol = symbol ?? clause.Left.BoundSymbol;
        this.PublishType(clause.Left, subject);
        var subjectEnd = this.projectionUses.Count; // The requirement's own projection uses come after it and stay its own (SPEC 23.1.2).
        // SPEC 23.1.2: a subject that formed no Type because of a failure in it, its own or a part's (the published HasFailureCauses),
        // supplies none: the requirement binds against no subject, and a rejecting verdict rests on the subject.
        var failedSubject = subject is null &&
            (this.RecordedFailure(clause) != BindingFailure.None || this.RecordedState(clause.Left) == BindingState.Invalid || this.HasFailureCauses(clause.Left));
        // Closed subjects are valid in a struct's, an enum's and, Self apart, a Contract's region.
        var closedScope = scope.Owner is StructKoto or EnumKoto || (scope.Owner is ContractKoto && !IsSelfConstraint(clause));
        // A subject that formed nothing for a missing name is pending when a later source can complete it into a subject valid here,
        // as a formed one is below: its root (RootedInput) is the base its projection chain published (SPEC 7.5, 8.8.1: `T.(D).Item`,
        // D missing), and whether it is closed is not known yet, so any subject is pending where closed subjects are valid. A root no
        // source can make an input (`U is Copy` in a function) is decided now.
        var pendingRoot = subject is null ? SubjectBase(clause.Left).BoundSymbol : null;
        var unresolvedSubject = subject is null && this.HasUnresolvedConstraintSyntax(clause.Left) &&
            (RootedInput(pendingRoot is { IsGenericInput: true }, pendingRoot) || closedScope);
        // A failed selector supplies no subject. Check the independent requirement syntax, then discard the provisional
        // proposition; its valid Type or Contract must not become another failure merely because the selector failed.
        var requirement = this.BindRequirement(clause.Right, unresolvedSubject || failedSubject ? BoundType.Unit : subject, semantics, bindingScope);
        if (AssociatedHead(clause) is OriginApplicationKoto && this.HasUnsupportedAssociatedIdentity(requirement))
        {
            this.Fail(clause, BindingFailure.Unsupported, true);
            return;
        }

        if (AssociatedHead(clause) is OriginApplicationKoto && subject?.Symbol is { Kind: BindingSymbolKind.AssociatedType } associated)
        {
            requirement = this.CanonicalAssociatedOrigins(requirement, clause, this.AssociatedParameters(associated.Declaration));
        }

        if (requirement is { Kind: ConstraintKind.Error, Subject: null } && subject is not null && !unresolvedSubject && !failedSubject)
        {
            // R7-C3 (F's C2 rulings 272-273): an unbound requirement keeps its bound subject, so the clause's failed fact is a premise of
            // that subject, which every judgment of it consults (the rows, RowKinds).
            requirement = this.InternConstraint(new(ConstraintKind.Error, subject));
        }

        if (unresolvedSubject && requirement.Kind != ConstraintKind.Error)
        {
            // Validate the available requirement syntax without inventing a subject identity.
            requirement = this.InternConstraint(new(ConstraintKind.Unresolved));
        }

        var root = subject;
        while (root?.Kind == BoundTypeKind.AssociatedProjection)
        {
            root = root.Components[0];
        }

        // Only propositions dependent on generic inputs or the conforming Self are premises.
        // Closed propositions are independently checked declaration obligations.
        // SPEC 8.7.3, 8.7: inside a Contract, `Self is K` for the table's Self requirements and `Self is not ObjectPayload` are
        // Self-dependent implementation requirements: premises for the Contract's own signatures, derived for every Type
        // that the Contract is available for, and obligations of every conformer.
        var contractSelfClause = scope.Owner is ContractKoto && IsSelfConstraint(clause) && IsSelfRequirement(requirement);
        var input = symbol is { IsGenericInput: true } || (subject?.Kind == BoundTypeKind.AssociatedProjection && root?.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection);
        input |= scope.Owner is StructKoto or EnumKoto && !IsSelfConstraint(clause) && this.DependentConstraint(requirement);
        input |= contractSelfClause;
        var closedSubject = closedScope && subject is not null && !this.DependentType(subject, unresolvedProjection: false);
        // SPEC 8.2.1 (PLAN G567-a; the owner's O-249 Q2 (a)): a subject whose path writes Type arguments is one only as an associated-Type
        // projection (`D<Z>.Item`), never as a nested Type reached through them (`W<Z>.Inner`). It reads the bound subject's kind, a
        // projection whose value is known included (`D<i32>.Item`): a later normalization of subject projections keeps reading "is a
        // projection", not the normalized Type.
        var validSubject = subject is not null && (RootedInput(input, root?.Symbol ?? symbol) || closedSubject ||
            (IsSelfConstraint(clause) && scope.Owner is DeclarationContainerKoto and not (GroupKoto or ContractKoto))) &&
            (subject.Kind == BoundTypeKind.AssociatedProjection || !this.WritesTypeArguments(clause.Left, clause));
        if (!validSubject && !unresolvedSubject)
        {
            requirement = this.InternConstraint(new(ConstraintKind.Error));
            if (failedSubject)
            {
                // SPEC 23.1.2: the clause's verdict reads its failed subject directly; the subject's record is the one.
                this.AddPrerequisite(clause, clause.Left);
            }
            else
            {
                // A projection of the formed subject is proved in the environment this verdict makes invalid: it rests on the clause.
                for (var i = subjectProjections; i < subjectEnd; i++)
                {
                    this.AddPendingPrerequisite(this.projectionUses[i].Use, clause);
                }
            }

            this.Record(clause, BindingFailure.InvalidConstraint, []);
        }

        clause.BoundConstraint = requirement;
        Complete(clause, BoundType.Boolean);
        if (input || requirement.Kind == ConstraintKind.Error)
        {
            // R7-C3 gate 2: a clause that failed, or holds a recovery, adds its Error fact; the environment writer fails that premise
            // alone (ValidateConstraintEnvironments, FailPremise), so nothing else in the environment reads it.
            var environment = scope.Constraints ??= new();
            this.AddConstraintFact(environment, requirement, origin: requirement.Kind == ConstraintKind.Error ? clause : null); // its origin (R7-E5 c1)

            if (requirement.Kind == ConstraintKind.Error && this.RecordedFailure(clause) != BindingFailure.None)
            {
                this.AddPrerequisite(scope.Owner, clause);
            }
        }

        // A subject's root valid here, the one statement for a formed subject and a pending one: an input, and (SPEC 7.5) in a function
        // its own parameter or one of its declaring Type, the Container whose scope the member is declared in (Indexing; a conditional
        // block's member included), never a local function's enclosing function. A closed subject is the other valid subject, where
        // closed subjects are valid (closedScope: never a function).
        bool RootedInput(bool input, BindingSymbol? rootSymbol)
            => input && (scope.Owner is not FunctionKoto member || ReferenceEquals(rootSymbol?.Scope, scope) ||
                (rootSymbol?.Scope is { Owner: DeclarationContainerKoto } declaring && ReferenceEquals(declaring, member.BoundSymbol?.Scope)));

        // Self clauses remain implementation obligations, never assumptions.
    }

    // Adds a fact and its conjuncts; true when one of them is an Error. BindConstraints and BindAssociatedSpecification name their clause
    // as the environment's failed node. The witness scope (ContractMatching), an inherited path's scope (InheritedConformances) and a Contract's premises
    // (ContractSolver) can add one too, a failed clause's Error passed through or a null substitution, and name no node, so such an
    // environment stays valid (PLAN G651's open item).
    private bool AddConstraintFact(ConstraintEnvironment environment, BoundConstraint fact, BindingSymbol? source = null, IsKoto? origin = null)
    {
        if (fact.HasUnresolved)
        {
            return false;
        }

        var start = this.conjuncts.Count;
        AddConjuncts(this.conjuncts, fact);
        var error = false;
        for (var i = start; i < this.conjuncts.Count; i++)
        {
            environment.Add(this.conjuncts[i], source, origin);
            error |= this.conjuncts[i].Kind == ConstraintKind.Error;
        }

        this.conjuncts.RemoveRange(start, this.conjuncts.Count - start);
        return error;
    }

    private ConstraintProof ProveProposition(BoundConstraint proposition, BindingScope scope)
    {
        var normalized = this.NormalizeProofConstraint(proposition, scope, out _);
        var evidence = this.Supply(normalized, this.ViewOf(scope));
        var positive = evidence.Positive;
        var negative = evidence.Negative;

        // Always validate both operands, even when an exact compound assumption is available.
        var structural = proposition.Kind switch
        {
            ConstraintKind.Error => ConstraintProof.Error,
            ConstraintKind.Unresolved => ConstraintProof.Unknown,
            ConstraintKind.Not => NegateProof(this.ProveConstraint(proposition.Left!, scope, QuerySite.Nested)),
            ConstraintKind.And => CombineProof(this.ProveConstraint(proposition.Left!, scope, QuerySite.Nested), this.ProveConstraint(proposition.Right!, scope, QuerySite.Nested), true),
            ConstraintKind.Or => CombineProof(this.ProveConstraint(proposition.Left!, scope, QuerySite.Nested), this.ProveConstraint(proposition.Right!, scope, QuerySite.Nested), false),
            // R7-C3 (C7; J1): the written atom is checked for its operands' contexts only; the judge forms at its own intake, so judging it
            // again would be the normalized judgment twice (two outermost requests per projection-input proof).
            _ => !ReferenceEquals(proposition, normalized) && this.OperandContexts(proposition).Invalid ? ConstraintProof.Error : this.JudgeConstraintAtom(normalized, scope),
        };
        if (structural != ConstraintProof.Error && normalized.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not &&
            ClauseSubject(normalized) is { } whole && SemanticsPremise(normalized, whole))
        {
            // Universal case evidence must judge the whole set: neither disjunct alone need cover every case.
            structural = this.JudgeConstraintAtom(this.InternConstraint(new(ConstraintKind.Semantics, whole, mask: SemanticsSet(normalized))), scope);
        }

        positive |= structural == ConstraintProof.Proven;
        negative |= structural == ConstraintProof.Refuted;
        if (structural != ConstraintProof.Error && (positive || negative) && this.OperandContexts(proposition).Unresolved)
        {
            return ConstraintProof.Unknown;
        }

        var verdict = structural == ConstraintProof.Error || (positive && negative) ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
        return this.SpokenVerdict(normalized, scope, verdict); // R7-C3 (F's C2 ruling 278): the Failed premises that speak to it
    }

    // W1 (ContractSolver's Completion): Complete with nothing to normalize, else the meet of the parts'; a Callable fact's required
    // Type (CallableContractType) is Provisional.
    private BoundConstraint NormalizeProofConstraint(BoundConstraint constraint, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        if (!ReadingCanDiffer(constraint.HasAssociatedProjection, HasTypeIdentity(scope)))
        {
            return constraint;
        }

        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.NormalizeProofConstraint(constraint.Left!, scope, out completion));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            var left = this.NormalizeProofConstraint(constraint.Left!, scope, out var a);
            var right = this.NormalizeProofConstraint(constraint.Right!, scope, out var b);
            completion = Meet(a, b);
            return ReferenceEquals(left, constraint.Left) && ReferenceEquals(right, constraint.Right) ? constraint : this.InternConstraint(new(constraint.Kind, left: left, right: right));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        // R7-C2c2 c2-4b (design R4.12b, J1): a proposition is identified by its normalized complete Types (SPEC 8.3.1), its Contract
        // reference's arguments included (R7-C2c2 c2-5a: a completed projection there is interpreted as in the subject, 8.2.3), each in
        // its class's form in the proposition's view, so request and facts meet here.
        var subject = this.NormalizedForm(constraint.Subject, scope, out completion);
        var required = constraint.RequiredType;
        if (required is not null)
        {
            var part = Completion.Provisional;
            required = constraint.Kind == ConstraintKind.Callable ? this.CallableContractType(required, scope) : this.NormalizedForm(required, scope, out part);
            completion = Meet(completion, part);
        }

        var contract = constraint.Contract;
        if (contract is not null && IsBoundContractReference(contract))
        {
            contract = this.NormalizedContract(contract, scope, out var part);
            completion = Meet(completion, part);
        }

        return ReferenceEquals(subject, constraint.Subject) && ReferenceEquals(required, constraint.RequiredType) && ReferenceEquals(contract, constraint.Contract) ? constraint : this.InternConstraint(new(constraint.Kind, subject, required, contract, constraint.Mask));
    }

    // J1's reading of a bound Contract reference (NormalizeProofConstraint's Contract arm): its arguments read as a proposition's Types
    // (NormalizedForm), so a completed projection there is its binding (SPEC 8.2.3; R7-C2c2 c2-5a). Both J1 entries read a reference by
    // it: NormalizeProofConstraint and ProveConformance, whose direct callers bypass that intake (R7-C2c2 c2-5c).
    private BindingSymbol NormalizedContract(BindingSymbol reference, BindingScope scope, out Completion completion)
    {
        var form = this.NormalizedForm(reference.Type!, scope, out completion);
        return ReferenceEquals(form, reference.Type) ? reference : this.BoundContractReference(form);
    }

    // J1's one reading of a proposition's Type (SPEC 8.3.1's normalized complete Types): normalized in the scope, a completed projection
    // interpreted (8.2.3, NormalizeProofType), then its class's form in the scope's view (ClassForm); the meet of both Completions (W1).
    // Read by NormalizeProofConstraint and by SPEC 8.3.3's pass walk (Binding.Contracts.Expansion: "normalized under K's premises").
    private BoundType NormalizedForm(BoundType type, BindingScope scope, out Completion completion)
    {
        var form = this.ClassForm(this.NormalizeProofType(type, scope, out completion), scope, out var formed);
        completion = Meet(completion, formed);
        return form;
    }

    // A proof's Type with its parameter identities substituted and its projections' images formed in the scope (proofParts' query).
    private BoundType NormalizeProofType(BoundType type, BindingScope scope, out Completion completion)
        => this.NormalizeProofTypeCore(type, scope, this.proofParts.Begin(this.storageVersion), out completion);

    private BoundType NormalizeProofPart(BoundType type, BindingScope scope, uint query, out Completion completion)
    {
        if (!this.proofParts.TryGet(type, query, out var answer))
        {
            answer.Image = this.NormalizeProofTypeCore(type, scope, query, out answer.Completion);
            this.proofParts.Set(type, query, answer);
        }

        completion = answer.Completion;
        return answer.Image;
    }

    private BoundType NormalizeProofTypeCore(BoundType type, BindingScope scope, uint query, out Completion completion)
    {
        this.ProofPartWork++;
        completion = Completion.Complete;
        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            var projected = this.ContractTypeAnswer(type, scope);
            completion = Meet(completion, projected.Completion);
            return projected.Image;
        }

        // Keep symbolic subjects symbolic: share facts within a parameter equivalence class, but do not turn a
        // concrete-Type assumption into an extra proof/refutation rule. Associated identities are reduced as before.
        var count = type.Components.Count;
        if (count == 0)
        {
            return type;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.NormalizeProofPart(type.Components[i], scope, query, out var part);
                completion = Meet(completion, part);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private bool HasUnresolvedConstraintSyntax(Koto node)
    {
        if (this.capabilityMode != BindingMode.Provisional)
        {
            return false;
        }

        this.constraintSyntaxVisitor ??= new();
        this.constraintSyntaxVisitor.Missing = false;
        this.constraintSyntaxVisitor.Invalid = false;
        this.constraintSyntaxVisitor.Visit(node);
        return this.constraintSyntaxVisitor.Missing && !this.constraintSyntaxVisitor.Invalid;
    }

    private sealed class ConstraintSyntaxVisitor : KotoVisitor
    {
        internal bool Missing { get; set; }

        internal bool Invalid { get; set; }

        public override void Visit(Koto node)
        {
            // A missing name is pending whatever the role excluded (SPEC 9.2, 9.5): a declaration that is no candidate decides nothing.
            this.Missing |= node.BindingState == BindingState.Unresolved && node.BindingFailure is BindingFailure.MissingType or BindingFailure.MissingName;
            this.Invalid |= node.BindingState == BindingState.Invalid;
            node.VisitChildren(this);
        }
    }

    /// <summary>The `deferredConstraints` family's one accessor (DECISIONS BU-220): a pass family, whether a clause waits for the
    /// conformances (DeferredConstraint), kept in its module's NodeColumns at the clause's SyntaxId as a `bool?` (null, all-zero, is no
    /// decision). It is decided once per Bind run: every read and write is BindConstraints', which each run's ResetPass precedes, so the
    /// pass counter empties it where BindConstraints cleared it. Its members keep the names its readers use.</summary>
    private readonly struct DeferredConstraintRows(Binding binding)
    {
        internal void Add(IsKoto clause, bool deferred) => HirTables.Of(clause).Families.DeferredConstraints.Ref(clause.SyntaxId, binding) = deferred;

        internal bool TryGetValue(IsKoto clause, out bool deferred)
        {
            var row = clause.SyntaxOwner is { } table ? table.Hir.Families.DeferredConstraints.Get(clause.SyntaxId, binding) : null;
            deferred = row ?? false;
            return row.HasValue;
        }
    }
}

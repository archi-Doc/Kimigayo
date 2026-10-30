// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private EffectSummary? effectSummary;

    // The pass after ownership analysis keeps its own call pool: its contexts differ from the Binding pass (SPEC 8.6).
    private EffectSummary? destructionSummary;

    // Cleanup extraction may run while a transitive effect check requests a library body; keep its scratch independent.
    private EffectSummary? cleanupCollector;

    /// <summary>SPEC 4.9.6: summarizes the body of a Kimi operation that publishes its summary, under its own premises with abstract
    /// Type parameters, as its published row permits: the parameter-dependent requirement the row names and the calls through the
    /// Callable input it names (`PublishedRow.Calls`) are covered.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="published">The requirement whose effects the row publishes, such as `K` equality, or null.</param>
    /// <returns>Whether the body stays within confined, and the first effect that does not.</returns>
    internal (bool Valid, EffectViolation Violation, Koto? Node) SummarizePublishedOperation(BindingSymbol operation, BindingSymbol? published)
    {
        var summary = this.effectSummary ??= new(this);
        var callable = KimiLibraryCatalog.Row(operation) is { Calls: >= 0 } row ? TyCtxt.Signature(operation, null, row.Calls, out _) : null;
        var valid = summary.Check(operation, this.ConstraintScope(operation.Declaration), false, implementationBody: true, published: published, publishedCallable: callable);
        return (valid, summary.Violation, summary.ViolationNode);
    }

    internal void CollectCaseDestructions(OwnershipBody source, OwnershipBody target, ulong cases)
        => (this.cleanupCollector ??= new(this)).CollectDestructions(source, target, cases);

    /// <summary>
    /// SPEC 8.6, 22.2.3: checks every effect bound again after ownership analysis, now counting the destructions each
    /// reached body performs, as its planned cleanups show. An implementation whose body ownership analysis did not reach,
    /// such as an unused library Iterator, runs nowhere and is not checked.
    /// </summary>
    /// <param name="rejected">Receives the use of each conformance whose destruction effects exceed its bound.</param>
    internal void ValidateDestructionEffects(List<Koto> rejected)
    {
        this.destructionSummary?.BeginPass();
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            if (this.EffectBoundViolation(this.activeConformancePaths[i], true) is { } path)
            {
                rejected.Add(path.Use);
            }
        }

        this.InvalidateAssociatedEvidence();
        if (rejected.Count != 0)
        {
            this.InvalidateProjectionCertificates(BindingMode.Final);
        }
    }

    /// <summary>Gets whether a failed bound check stopped at an effect this implementation cannot classify yet, which is reported as
    /// the located Unsupported_Kd, never as a violation of the bound (plan rule 5).</summary>
    /// <param name="use">The conformance, the Callable use or the virtual function of the failed check.</param>
    /// <returns>Whether the recorded violation is an implementation limit.</returns>
    internal bool IsEffectLimit(Koto use)
        => (this.effectViolations?.TryGetValue(use, out var conformance) == true && conformance.Kind == EffectViolation.Limit) ||
            (this.callableEffectViolations.TryGetValue(use, out var callable) && callable.Kind == EffectViolation.Limit) ||
            (use is FunctionKoto function && this.virtualEffectViolations?.TryGetValue(function, out var slot) == true && slot.Kind == EffectViolation.Limit);

    // SPEC 8.11.3: a conformance checks the complete transitive effect summary of every witness against the bounds that
    // its Contract and the ancestors declare for the requirement; an effect it cannot classify is a conflict (SPEC 8.6).
    // Binding checks the environment the implementation reaches and the calls it makes. Which values it destroys is known only
    // from the cleanups ownership analysis plans, so destruction effects are checked afterwards (ValidateDestructionEffects),
    // as are the selected uses of bounded Callable clauses, which this pass enumerates (SPEC 8.11.4).
    private void ValidateEffectBounds()
    {
        this.effectViolations?.Clear();
        this.effectSummary?.BeginPass();
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            if (this.EffectBoundViolation(this.activeConformancePaths[i], false) is { } path)
            {
                var limit = this.IsEffectLimit(path.Use);
                this.Fail(path.Use, limit ? BindingFailure.Unsupported : BindingFailure.IncompatibleImplementation, limit);
            }
        }

        this.InvalidateAssociatedEvidence();
        this.CollectCallableEffectUses();
    }

    // The path, marked invalid, when one of its bounded implementations exceeds its bound; null otherwise.
    private BoundConformancePath? EffectBoundViolation(BoundConformancePath path, bool destructions)
    {
        if (!path.IsVerified || path.Contract.Contract is not { } shape)
        {
            return null;
        }

        this.EnsureEffectBounds(shape);
        if (!shape.HasEffectBounds)
        {
            return null;
        }

        for (var w = 0; w < path.WitnessStorage.Count; w++)
        {
            var witness = path.WitnessStorage[w];
            if (witness.Requirement.Declaration is not FunctionKoto requirement)
            {
                continue;
            }

            if (this.DeclaredEffectBound(shape, witness.Identity) is null ||
                (destructions && witness.Implementation.Declaration is FunctionKoto implementation && this.compilation.Ownership.TemplateBody(implementation, false) is null))
            {
                continue;
            }

            var summary = destructions ? this.destructionSummary ??= new(this) : this.effectSummary ??= new(this);
            if (!summary.Check(witness.Implementation, path.Scope, destructions))
            {
                (this.effectViolations ??= new(ReferenceEqualityComparer.Instance))[path.Use] =
                    new(summary.Violation, summary.ViolationSite, summary.ViolationNode, requirement, shape, witness.Identity);
                path.Invalid = true;
                path.IsVerified = false;
                path.Identity.Invalid = true;
                path.Identity.IsVerified = false;
                return path;
            }
        }

        return null;
    }

    // One reusable transitive summary: bodies, callees, specializations, synthesized calls (accessors, indexers, ranges,
    // iteration, formatting, comparisons), lazy initialization and destruction are visited once per instantiation context.
    // Anything it cannot classify, such as an indirect call or a Type it cannot instantiate, fails the bound. Destruction
    // is counted only in the pass after ownership analysis, from the cleanups each body actually performs.
    private sealed class EffectSummary(Binding binding) : KotoVisitor
    {
        private readonly HashSet<(Koto Node, int Context)> seen = new();
        private readonly HashSet<BoundType> destroyed = new(ReferenceEqualityComparer.Instance);
        private readonly List<(Koto Node, int Context, Koto? Site)> pending = new();
        private readonly List<CallPlan?> contexts = new();
        private readonly Dictionary<CallPlan, int> contextIndex = new(CallInstanceComparer.Instance);
        private readonly List<CallPlan> calls = new();
        private readonly List<int> selectedArms = new();
        private BindingScope? scope;
        private int context;
        private int callCount;
        private Koto? stepUse;
        private Koto? site;
        private Koto? last;
        private bool valid;
        private bool destructions;
        private BindingSymbol? published;
        private BoundType? publishedCallable; // SPEC 4.9.6: the Callable input whose calls the checked operation's row publishes.
        private OwnershipBody? cleanupSource;
        private OwnershipBody? subjectBody;
        private InterpretationContext subjectContext;
        private OwnershipBody? cleanupTarget;
        private ulong cleanupCases;

        /// <summary>Gets the kind of the first effect that violated a bound in the last check.</summary>
        internal EffectViolation Violation { get; private set; }

        /// <summary>Gets the node of that effect, which may lie in a callee.</summary>
        internal Koto? ViolationNode { get; private set; }

        /// <summary>Gets the syntax of the implementation's own body through which that effect is reached.</summary>
        internal Koto? ViolationSite { get; private set; }

        public override void Visit(Koto node)
        {
            if (!this.valid || node is FunctionKoto or DeclarationContainerKoto)
            {
                return; // A declaration is not an evaluation.
            }

            this.last = node;
            if (node is FieldKoto local)
            {
                // The initializer is evaluated in place, in the same body. Its destruction, if any, is one of the body's cleanups.
                if (local.InitializerKoto is { } initializer)
                {
                    this.Visit(initializer);
                }

                return;
            }

            if (node is not InvocationKoto && node.BoundSymbol?.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter)
            {
                return; // A Type reference evaluates nothing.
            }

            if (node.Formatting is { } formatting && this.Formatting(formatting, node))
            {
                return;
            }

            this.Synthesized(node);
            this.Accesses(node);

            if (node is IdentifierNameKoto or MemberAccessKoto && node.BoundSymbol?.Declaration is PropertyKoto { BoundSymbol: { } propertySymbol } property && IsStatic(propertySymbol))
            {
                // SPEC 8.11.2, 7.7.2: the Property and its accessor's storage name the same static declaration, also in nested bodies.
                // Lazy initialization runs at the first read; confined reaches no mutable static.
                // Ownership analysis plans no cleanups for an initializer other than a constant scalar, so its destructions
                // are unknown.
                this.Queue(property.InitializerKoto);
                if (property.VariableKind == VariableKind.Var)
                {
                    this.Violate(EffectViolation.MutableStatic, node);
                    return;
                }

                // SPEC 8.11.2: immutability does not protect a pointee, so a raw pointer read from an immutable static
                // obtains authority from the environment.
                if (propertySymbol.Type is { } stored && this.ContainsRawPointer(this.Type(stored), 0))
                {
                    this.Violate(EffectViolation.StaticPointer, node);
                    return;
                }

                if (this.destructions && property.InitializerKoto is not null && !StaticScalar.TryGet(propertySymbol.Property, out _))
                {
                    this.DestructionLimit(node);
                    return;
                }
            }

            // SPEC 8.11.2: a pointer made from an integer has no provenance; it obtains authority from the environment.
            if (node is ConversionKoto { ConversionBinding: ConversionBinding.Pointer, Left.BoundType.Kind: BoundTypeKind.Primitive })
            {
                this.Violate(EffectViolation.IntegerPointer, node);
                return;
            }

            if (node is InvocationKoto invocation && !binding.TryGetEnumConstruction(node, out _))
            {
                if (invocation.BoundValueCall is { } valueCall)
                {
                    this.Callable(valueCall, invocation);
                    node.VisitChildren(this);
                    return;
                }

                if (invocation.BoundCall is not { } call)
                {
                    this.Violate(EffectViolation.UnclassifiedCall, node); // An indirect or unbound call has no published effect bound.
                    return;
                }

                this.Call(call, node);
            }

            if (node is IndexKoto or DictionaryLiteralKoto && Dictionary(node is IndexKoto index ? index.Left.BoundType : node.BoundType) is { } dictionary)
            {
                // Dictionary selection and literal insertion compare keys with their Equatable witness.
                this.Comparison(this.Type(dictionary) is { } key ? binding.DictionaryComparison(key) : null);
            }

            node.VisitChildren(this);
        }

        // Keep one pool slot per call across the whole validation pass. Resetting for each implementation makes
        // unrelated signatures repeatedly resize the same call's argument and substitution arrays on every rebind.
        internal void BeginPass() => this.callCount = 0;

        // Capture the same remaining parts that ordinary effect checking visits, before this pooled case body is reused.
        internal void CollectDestructions(OwnershipBody source, OwnershipBody target, ulong cases)
        {
            if (source.Issues.Count != 0)
            {
                return;
            }

            this.valid = true;
            this.destructions = true;
            this.contexts.Clear();
            this.contexts.Add(null);
            this.context = 0;
            this.cleanupSource = source;
            this.cleanupTarget = target;
            this.cleanupCases = cases;
            try
            {
                this.Cleanups(source);
            }
            finally
            {
                this.cleanupSource = null;
                this.cleanupTarget = null;
                this.cleanupCases = 0;
            }
        }

        // SPEC 8.3.3: confined is judged in the conformance scope (D and the conditions P). With `destructions`, the values each
        // reached body destroys are summarized too.
        internal bool Check(BindingSymbol implementation, BindingScope scope, bool destructions, BoundType? callable = null, SemanticsKind receiver = SemanticsKind.Ref, bool implementationBody = false, BindingSymbol? published = null, BoundType? publishedCallable = null)
        {
            this.published = published;
            this.publishedCallable = publishedCallable;
            this.scope = scope;
            this.valid = true;
            this.destructions = destructions;
            this.seen.Clear();
            this.destroyed.Clear();
            this.pending.Clear();
            this.contexts.Clear();
            this.contextIndex.Clear();
            this.contexts.Add(null);
            this.context = 0;
            this.site = null;
            this.last = null;
            this.Violation = EffectViolation.None;
            this.ViolationNode = null;
            this.ViolationSite = null;
            this.stepUse = null;
            if (implementationBody && implementation.Declaration is FunctionKoto body)
            {
                // The obligation checks this body; all calls inside it still use their own dispatch kind.
                this.Body(body, null);
            }
            else
            {
                this.Function(implementation, callable is null ? null : binding.FunctionItemContext(callable, requireClosed: false));
            }

            if (receiver == SemanticsKind.Owner && BoundClosure.Of(callable) is { Receiver: not SemanticsKind.Owner })
            {
                this.Destruction(callable, implementation.Declaration);
            }

            for (var i = 0; this.valid && i < this.pending.Count; i++)
            {
                this.context = this.pending[i].Context;
                this.site = this.pending[i].Site;
                this.Visit(this.pending[i].Node);
            }

            return this.valid;
        }

        private static BoundType? Dictionary(BoundType? type)
        {
            while (type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
            {
                type = type.Components[0];
            }

            return type?.Kind == BoundTypeKind.Dictionary ? type : null;
        }

        // A receiver through which a selected Place is reached rather than owned: a reference, a pair layer or a Slice.
        private static bool IsReached(BoundType? receiver)
            => receiver is { Kind: BoundTypeKind.Slice or BoundTypeKind.SemanticsApplication } or
                { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };

        private static bool IsAssignment(KotoKind kind) => kind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;

        private static LoanRequirement Mode(BoundType? type)
            => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref;

        private SemanticsMask AdaptationSemantics(BoundType type, Koto use)
        {
            if (this.Type(type) is not { } selected)
            {
                this.Violate(EffectViolation.Limit, use);
                return SemanticsMask.None;
            }

            var scope = !ReferenceEquals(selected, type) && TryAdaptationSelector(selected, out var whole) && whole.Symbol is { } selector
                ? selector.Scope : binding.ConstraintScope(use);
            return binding.ResultSemantics(selected, scope);
        }

        // Whether a value of `type` holds a raw pointer at any depth: in a Type argument, a component, a stored Field or a
        // Case payload. A recursive struct is cut off at a fixed depth, which counts as holding one.
        private bool ContainsRawPointer(BoundType? type, int depth)
        {
            if (type is null || depth > 32)
            {
                return depth > 32;
            }

            if (type.Semantics == SemanticsKind.Raw)
            {
                return true;
            }

            // A referent or payload reached through the value is read from the same static too.
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.ContainsRawPointer(type.Components[i], depth + 1))
                {
                    return true;
                }
            }

            if (AdtDef.IsStruct(type))
            {
                for (var i = 0; i < AdtDef.Count(type); i++)
                {
                    if (this.ContainsRawPointer(AdtDef.FieldType(type, i), depth + 1))
                    {
                        return true;
                    }
                }
            }

            for (var i = 0; AdtDef.CaseTypes(type) is { } cases && i < cases.Length; i++)
            {
                if (this.ContainsRawPointer(cases[i], depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        // A formatting root evaluates its synthesized calls. $tryWrite's acquisition only borrows its Writer operand, and
        // its written operand is replaced by the writes; an interpolation also evaluates its own segments.
        private bool Formatting(BoundFormatting formatting, Koto node)
        {
            if (formatting.Acquisition is { } acquisition)
            {
                if (acquisition.BoundCall is { ArgumentOperations.Length: > 0 } acquired)
                {
                    this.Argument(acquired.ArgumentOperations[0]);
                }

                this.Queue(acquisition.ArgumentNodes[0]);
                this.Queue(formatting.Outcome);
            }
            else
            {
                this.Queue(formatting.Heap);
                this.Queue(formatting.Adapter);
            }

            for (var i = 0; i < formatting.Writes.Count; i++)
            {
                this.Queue(formatting.Writes[i]);
            }

            return formatting.Acquisition is not null;
        }

        // The calls Binding synthesizes for a node are evaluated where the node is. Accessor and indexer calls are
        // selected by the use of their Place instead (PlaceAccess), as Ownership selects them.
        private void Synthesized(Koto node)
        {
            this.Queue(binding.StorageProjection(node));
            switch (node)
            {
                case ConversionKoto { CreationCall: { } creation }:
                    this.Queue(creation);
                    break;
                case ConversionKoto { Adaptation: { Creates: true } plan, CreationStorage.BoundCall: { } factory } conversion:
                    if ((this.AdaptationSemantics(plan.Source, conversion) & SemanticsMask.Owner) != 0 &&
                        (this.AdaptationSemantics(plan.Target, conversion) & SemanticsMask.Object) != 0)
                    {
                        // Every creation case has the same confined allocation effect. Its ordinary input acquisition
                        // and written operand are inspected below once, without revisiting a synthetic argument tree.
                        this.Target(factory, conversion);
                    }

                    break;
                case IndexKoto:
                    this.Queue(binding.ResolvedKeyCall(node));
                    break;
                case RangeKoto or FromEndIndexKoto:
                    this.Queue(binding.RangeValueCall(node));
                    break;
                case BinaryKoto { ComparisonCall: { } comparison }:
                    this.Queue(comparison);
                    break;
                case BinaryKoto { ArithmeticCall: { } arithmetic } arithmeticSource:
                    this.Queue(arithmetic);
                    if (arithmetic.RightFirstArguments)
                    {
                        if (binding.PropertyCall(arithmeticSource.Left, PropertyAccessorKind.Set) is { } arithmeticSetter)
                        {
                            this.Queue(arithmeticSetter);
                        }
                        else
                        {
                            this.PlaceAccess(arithmeticSource.Left, LoanRequirement.Uniq);
                        }
                    }

                    break;
                case UnaryKoto { ArithmeticCall: { } unary }:
                    this.Queue(unary);
                    break;
                case ForKoto { Iteration: { Decomposition.IsCurrent: true } iteration } loop:
                    // SPEC 14.6.2: the entry call, the internal Iterator local with its destruction, and each step.
                    this.Queue(loop.EntryCall);
                    this.Queue(iteration.Iterator);
                    this.Queue(iteration.Next);
                    break;
            }
        }

        // The reads, writes and borrows of one node, each through the Place path it uses, which selects the accessor and
        // indexer calls it makes. A Place is accessed once, by its outermost use: an operand of a projection, a borrow, a Move
        // or a write is accessed there.
        private void Accesses(Koto node)
        {
            var adapted = binding.TryGetAdaptation(node, out var adaptation);
            if (adapted && adaptation.Kind is ExpectedAdaptationKind.Reborrow or ExpectedAdaptationKind.ReferenceRead or ExpectedAdaptationKind.ReferentRead or ExpectedAdaptationKind.SharedBorrow)
            {
                this.PlaceAccess(node, LoanRequirement.Ref);
            }

            switch (node)
            {
                case BinaryKoto assignment when IsAssignment(assignment.Akind):
                    if (assignment.Akind != KotoKind.Equals)
                    {
                        this.Queue(binding.PropertyCall(assignment.Left, PropertyAccessorKind.Get)); // A compound assignment reads first.
                    }

                    this.PlaceAccess(assignment.Left, LoanRequirement.Uniq);
                    break;
                case UnaryKoto update when ElementAccess.UpdateOperator(update.Akind) != KotoKind.Invalid:
                    this.Queue(binding.PropertyCall(update.Operand, PropertyAccessorKind.Get));
                    this.PlaceAccess(update.Operand, LoanRequirement.Uniq);
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address } borrow when ElementAccess.IsRawPlace(borrow.Left):
                    break; // SPEC 5.2.2, 5.4: a fresh anchor, compared with no Place it does not derive from.
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow:
                    this.PlaceAccess(borrow.Left, Mode(borrow.BoundType));
                    break;
                case ConversionKoto { Adaptation: { } explicitPlan } explicitAdaptation:
                    var admitted = this.AdaptationSemantics(explicitPlan.Target, explicitAdaptation);
                    var borrows = (explicitPlan.Operations & ((1U << (int)ConversionBinding.Borrow) | (1U << (int)ConversionBinding.ObjectUpcast))) != 0 &&
                        (admitted & SemanticsMask.Borrow) != 0;
                    this.PlaceAccess(explicitAdaptation.Left, borrows && (admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0 ? LoanRequirement.Uniq : LoanRequirement.Ref);
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Address } address:
                    this.PlaceAccess(address.Left, LoanRequirement.Ref); // SPEC 5.4: an immediately ending borrow.
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                    this.PlaceAccess(transfer.Left, LoanRequirement.Uniq); // A Move invalidates its Place.
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } follow when !adapted && !this.IsOperand(node):
                    this.PlaceAccess(follow, LoanRequirement.Ref);
                    break;
                case MatchKoto match when binding.TryGetMatch(match, out var plan) && plan is not null:
                    // SPEC 15.1.6: the Subject Place is accessed in the mode it grants.
                    this.PlaceAccess(match.Expression, plan.Mode == SubjectMode.Exclusive ? LoanRequirement.Uniq : LoanRequirement.Ref);
                    break;
                case ForKoto loop when loop.Iteration is not { Decomposition.IsCurrent: true }:
                    this.PlaceAccess(loop.Iterable, loop.Mode == SubjectMode.Exclusive ? LoanRequirement.Uniq : LoanRequirement.Ref);
                    break;
                case IdentifierNameKoto or MemberAccessKoto or IndexKoto when !adapted && !this.IsOperand(node):
                    this.PlaceAccess(node, LoanRequirement.Ref);
                    break;
            }
        }

        // Whether an enclosing use accesses the node's Place: a projection, borrow, follow, Move or write of it, a Subject,
        // an iterated value, or a borrowing argument.
        private bool IsOperand(Koto node)
        {
            var parent = node.Parent;
            while (parent is ParenthesizedKoto)
            {
                node = parent;
                parent = parent.Parent;
            }

            switch (parent)
            {
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address or ConversionBinding.Transfer or ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow or ConversionBinding.CaseAdaptation } conversion:
                    return ReferenceEquals(conversion.Left, node);
                case BinaryKoto assignment when IsAssignment(assignment.Akind):
                    return ReferenceEquals(assignment.Left, node);
                case MemberAccessKoto or IndexKoto:
                    return ReferenceEquals(((BinaryKoto)parent).Left, node);
                case UnaryKoto update when ElementAccess.UpdateOperator(update.Akind) != KotoKind.Invalid:
                case MatchKoto:
                case ForKoto:
                    return true;
                case InvocationKoto { BoundCall: { } call }:
                    for (var i = 0; i < call.ArgumentOperations.Length; i++)
                    {
                        if (ReferenceEquals(call.ArgumentOperations[i].Source, node))
                        {
                            return call.ArgumentOperations[i].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead);
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        // The access of a Place path in a mode: through each reference, pair layer or Slice that reaches it (the outermost
        // one in the mode, the inner ones read). An accessor or user indexer Place is its synthesized call, selected by the
        // mode; the static storage of a group variable is reached at its read (Visit), and a local, parameter or raw pointer
        // root makes no call.
        private void PlaceAccess(Koto? node, LoanRequirement mode)
        {
            for (var depth = 0; node is not null && this.valid && depth < 256; depth++)
            {
                node = KotoHelper.UnwrapParentheses(node);
                if (node is IdentifierNameKoto or MemberAccessKoto)
                {
                    if (node.BoundSymbol?.Declaration is PropertyKoto { VariableKind: VariableKind.Var } property && IsStatic(property.BoundSymbol))
                    {
                        return;
                    }

                    if (binding.PropertyCall(node, mode == LoanRequirement.Uniq ? PropertyAccessorKind.Set : PropertyAccessorKind.Get) is { } accessor)
                    {
                        this.Queue(accessor); // Its receiver argument continues the path.
                        return;
                    }
                }

                switch (node)
                {
                    case ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } follow:
                        mode = LoanRequirement.Ref;
                        node = follow.Left;
                        break;
                    case IdentifierNameKoto when binding.StorageProjection(node) is { } projection:
                        node = projection;
                        break;
                    case IndexKoto when binding.IndexerCall(node, false) is { } shared:
                        // SPEC 4.7.1: indexUniq serves updates and exclusive borrows; its receiver argument continues the path.
                        this.Queue(mode == LoanRequirement.Uniq ? binding.IndexerCall(node, true) ?? shared : shared);
                        return;
                    case IndexKoto:
                    case MemberAccessKoto { Right: NumberLiteralKoto }:
                    case MemberAccessKoto { BoundSymbol.Property.IsStored: true }:
                        var part = (BinaryKoto)node;
                        if (IsReached(ElementAccess.AccessType(part.Left)))
                        {
                            mode = LoanRequirement.Ref; // Reaching the reference reads its container.
                        }

                        node = part.Left;
                        break;
                    default:
                        return; // A local, a temporary, a getter result or a raw pointer root.
                }
            }
        }

        // An argument accesses its source Place as its operation does; the Place path selects synthesized calls.
        private void Argument(in BoundArgumentOperation argument)
        {
            var borrows = argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.BaseBorrow or ArgumentOperationKind.StorageProjection or ArgumentOperationKind.PayloadProjection;
            this.PlaceAccess(argument.Source, borrows ? Mode(argument.ParameterType) : LoanRequirement.Ref);
        }

        private void Call(CallPlan call, Koto use)
        {
            this.Argument(call.ReceiverOperation);
            for (var i = 0; i < call.ArgumentOperations.Length; i++)
            {
                this.Argument(call.ArgumentOperations[i]);
            }

            if (this.contexts[this.context] is { } outer)
            {
                if (binding.InstantiateForwardedCall(call, outer, this.NextCall()) is not { } instantiated)
                {
                    this.Violate(EffectViolation.Limit, use);
                    return;
                }

                call = instantiated;
            }

            var caller = this.context;
            this.context = this.Context(call);
            for (var i = 0; i < call.DefaultArguments.Length; i++)
            {
                this.Queue(call.DefaultArguments[i].Expression);
            }

            this.context = caller;
            this.Target(call, use);
        }

        private void Callable(CallPlan call, Koto use)
        {
            for (var i = 0; i < call.ArgumentOperations.Length; i++)
            {
                this.Argument(call.ArgumentOperations[i]);
            }

            var type = CallableCore(this.Type(call.CalleeType));
            if (type is null)
            {
                this.Violate(EffectViolation.Limit, use);
                return;
            }

            if (ReferenceEquals(type, this.publishedCallable))
            {
                return; // SPEC 4.9.6: the calls of the Callable input the operation's row publishes.
            }

            if (type.Kind is BoundTypeKind.FunctionItem or BoundTypeKind.Closure && type.Symbol is { } symbol)
            {
                this.stepUse = use;
                this.Function(symbol, binding.FunctionItemContext(type, requireClosed: false));
                this.stepUse = null;
                if (call.ReceiverKind == SemanticsKind.Owner && BoundClosure.Of(type) is { Receiver: not SemanticsKind.Owner })
                {
                    this.Destruction(type, use);
                }

                return;
            }

            // A forwarded abstract argument belongs to the caller's premises; an unsubstituted parameter belongs to the visited
            // declaration. Neither substitutes a favorable concrete body for its universal proof.
            var signature = this.Type(call.DeclaredSignature);
            if (signature is null || (!binding.AvailableCallableEffects(type, signature, call.ReceiverKind, use) &&
                !binding.AvailableCallableEffects(type, signature, call.ReceiverKind, this.scope!)))
            {
                this.Violate(EffectViolation.UnclassifiedCall, use);
            }
        }

        private void Target(CallPlan call, Koto use)
        {
            var kind = call.Target.CompilerFunction;
            if (kind is CompilerFunctionKind.ArrayClear or CompilerFunctionKind.Replace)
            {
                // The receiver's replaced or cleared elements are destroyed.
                var receiver = call.ReceiverOperation.ParameterType;
                for (var i = 0; receiver is null && i < call.ArgumentOperations.Length; i++)
                {
                    if (call.ArgumentOperations[i].ParameterIndex == 0)
                    {
                        receiver = call.ArgumentOperations[i].ParameterType;
                    }
                }

                var storage = receiver is null ? null : this.Type(receiver);
                if (storage is { Semantics: SemanticsKind.Uniq, Components.Count: 1 })
                {
                    storage = storage.Components[0];
                }

                this.Destruction(storage, use);
            }

            if (kind is CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat)
            {
                // Formatting dispatch runs its selected witness; built-in formatting only writes its inputs.
                if (!binding.TryResolveFormattingCallback(call, out var implementation))
                {
                    this.Violate(EffectViolation.Limit, use);
                }
                else if (implementation is not null)
                {
                    this.Function(implementation.Target, implementation);
                }

                return;
            }

            if (kind is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare && ComparisonTypes.IsComposite(call.ConformingType))
            {
                this.Comparison(binding.ComparisonPlan(call));
                return;
            }

            this.stepUse = use;
            this.Function(call.Target, call);
            this.stepUse = null;
        }

        private void Comparison(BoundComparison? plan)
        {
            if (plan is null)
            {
                this.Violate(EffectViolation.Limit, null);
                return;
            }

            if (plan.Implementation is { } implementation)
            {
                this.Function(implementation.Target, implementation);
            }

            for (var i = 0; i < plan.Parts.Length; i++)
            {
                this.Comparison(plan.Parts[i]);
            }
        }

        private void Function(BindingSymbol symbol, CallPlan? call)
        {
            // A closed requirement Item has already retained its verified witness mapping.
            symbol = call?.Target ?? symbol;
            if (symbol.CompilerFunction != CompilerFunctionKind.None)
            {
                var effect = IntrinsicDisposition.Effect(symbol.CompilerFunction);
                if (effect != CompilerFunctionEffect.Inputs)
                {
                    this.Violate(effect == CompilerFunctionEffect.Environment ? EffectViolation.ExternalOperation : EffectViolation.Limit, null);
                }

                return;
            }

            if (symbol.Declaration is not FunctionKoto function)
            {
                this.Violate(EffectViolation.Limit, null);
                return;
            }

            if (function.IsRequirement)
            {
                this.Requirement(symbol, call);
                return;
            }

            if (function.IsVirtual)
            {
                if (call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true })
                {
                    if (call.VirtualImplementation is not { BoundSymbol: { } implementation } body || call.VirtualImplementingType is null)
                    {
                        this.Violate(EffectViolation.Limit, null);
                        return;
                    }

                    var context = call;
                    if (!ReferenceEquals(body, function))
                    {
                        context = this.NextCall();
                        context.Set(implementation, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, declaringType: call.VirtualImplementingType, origins: call.Origins, inputOrigins: call.InputOrigins, operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, lengthArguments: call.LengthArguments);
                    }

                    this.Body(body, context);
                }
                else if (call is not null)
                {
                    this.Requirement(symbol, call);
                }
                else
                {
                    // A Contract witness must satisfy its bound from the slot contract, not an accidental body effect.
                    if (!binding.AvailableEffectBounds(function, null, this.scope!))
                    {
                        this.Violate(EffectViolation.UnboundedVirtual, function);
                    }
                }

                return;
            }

            if (function.Body is null && function.ExpressionBody is null && !(function.IsConstructor && function.IsGenerated))
            {
                // SPEC 22.6, 8.11.2: a foreign function accesses only what its arguments permit, which the arguments
                // already accessed, and the environment, which confined excludes. Any other bodyless call is unknown.
                this.Violate(IsLibraryImport(function) ? EffectViolation.ForeignCall : EffectViolation.Limit, null);
                return;
            }

            if (call is not null && binding.SelectSpecialization(call) is { } selected)
            {
                this.Specialization(selected, call);
                return;
            }

            this.Body(function, call);
            if (binding.specializationsByOriginal.TryGetValue(symbol, out var candidates))
            {
                // Every specialization the instance may still select is an implementation of the same call.
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (call is null)
                    {
                        this.Body(candidates[i], null);
                    }
                    else if (MaySelect(binding.specializations[candidates[i]], call.TypeArguments, call.LengthArguments))
                    {
                        this.Specialization(candidates[i], call);
                    }
                }
            }
        }

        // SPEC 8.11.3: a requirement call has the effects that the bounds available under its premises leave. Without confined,
        // its environment effects are unknown, which a bound check counts as a conflict; its inputs are reached by path, which
        // the arguments already accessed.
        private void Requirement(BindingSymbol symbol, CallPlan? call)
        {
            if (call is null || symbol.Declaration is not FunctionKoto requirement)
            {
                this.Violate(EffectViolation.UnboundedRequirement, null);
                return;
            }

            // The premises are those of the call's own scope and, in the implementation's own body, the conditions of the
            // conformance (SPEC 8.3.3), such as I is Iterator for Self is Iterator when I is Iterator.
            var conforming = call.ConformingType is { } type ? this.Type(type) : null;
            var reference = call.RequirementContract is { } declared && this.Type(declared.Type!) is { } applied
                ? ReferenceEquals(applied, declared.Type) ? declared : binding.BoundContractReference(applied) : null;
            var confined = binding.AvailableEffectBounds(requirement, conforming, this.scope!, reference: reference);
            if (ReferenceEquals(requirement.BoundSymbol, this.published))
            {
                return; // SPEC 4.9.6: the parameter-dependent effect the operation's row publishes.
            }

            if (this.stepUse is { } use && !confined)
            {
                confined = binding.AvailableEffectBounds(requirement, conforming, binding.ConstraintScope(use), reference: call.RequirementContract);
            }

            if (!confined)
            {
                this.Violate(requirement.IsVirtual ? EffectViolation.UnboundedVirtual : EffectViolation.UnboundedRequirement, requirement.IsVirtual && this.stepUse is null ? requirement : null);
            }
        }

        private void Specialization(FunctionKoto function, CallPlan call)
        {
            // The specialization has its own input/Origin binders but inherits their slots from the original.
            var specialized = this.NextCall();
            specialized.Set(function.BoundSymbol!, call.ReturnType, call.Receiver, call.ArgumentToParameter, [], conformingType: call.ConformingType, declaringType: call.DeclaringType, origins: call.Origins, inputOrigins: call.InputOrigins);
            this.Body(function, specialized);
        }

        private void Body(FunctionKoto function, CallPlan? call)
        {
            var previous = this.context;
            if (call is not null)
            {
                this.context = this.Context(call);
            }

            this.Queue(function.Body);
            this.Queue(function.ExpressionBody);
            this.Queue(function.BaseInitializer);
            if (function.IsConstructor && (call?.DeclaringType ?? AdtDef.ReceiverType(function)) is { } owner)
            {
                for (var i = 0; i < AdtDef.Count(owner); i++)
                {
                    this.Queue(AdtDef.Field(owner, i).InitializerKoto);
                }
            }

            if (this.destructions)
            {
                this.Cleanups(function);
            }

            this.context = previous;
        }

        // SPEC 22.2.3: the values a body destroys are exactly the cleanups ownership analysis planned for it: a local,
        // parameter or temporary destroyed at its scope end, a replaced value, element or pointee, each reduced to the parts
        // still initialized there. Their Types are read in the current instantiation context. A body without a plan has
        // unknown effects; a body with its own ownership diagnostic is rejected by that diagnostic.
        private void Cleanups(FunctionKoto function)
        {
            if (function is { IsConstructor: true, IsGenerated: true, Body: null, ExpressionBody: null })
            {
                return; // A generated constructor moves its parameters into the new value's fields.
            }

            if (binding.compilation.Ownership.TemplateBody(function, true) is not { } body)
            {
                this.DestructionLimit(null);
                return;
            }

            if (body.Issues.Count != 0)
            {
                return;
            }

            if (body.HasCaseDestructions)
            {
                var cases = body.ApplicableCleanupCases(binding, this.contexts[this.context]);
                for (var i = 0; this.valid && i < body.CaseDestructions.Count; i++)
                {
                    var destroyed = body.CaseDestructions[i];
                    if ((destroyed.Cases & cases) != 0)
                    {
                        this.Destruction(destroyed.Type is { } type ? this.Type(type) : null, destroyed.Source);
                    }
                }

                return;
            }

            this.Cleanups(body);
        }

        private void Cleanups(OwnershipBody body)
        {
            for (var id = 0; this.valid && id < body.Operations.Count; id++)
            {
                if (!body.IsReachable(id))
                {
                    continue;
                }

                var operation = body.Operations[id];
                switch (operation.Kind)
                {
                    // A write to the result initializes it and has no cleanup step of its own.
                    case OwnershipOperationKind.Cleanup or OwnershipOperationKind.Write when operation.Place >= 0 && (uint)body.OperationSteps[id] < (uint)body.CleanupSteps.Count &&
                        body.CleanupSteps[body.OperationSteps[id]].Operation == id:
                        if (body.CleanupSteps[body.OperationSteps[id]].Action != CleanupAction.Skip && (body.GetStorageState(id, operation.Place) & PlaceState.MayOwn) != 0)
                        {
                            var place = body.Places[operation.Place];
                            this.subjectBody = body;
                            this.subjectContext = body.ContextOf(operation.Place); // The Subject's own context, recorded at its construction.
                            if (operation.Kind != OwnershipOperationKind.Cleanup || place.Kind != OwnershipPlaceKind.Subject || this.Type(place.Type) is not { } subject ||
                                !this.SubjectDestroyed(place.Source, operation.Source, subject))
                            {
                                this.Destroyed(body, id, body.MoveRoot(operation.Place), place.Type, operation.Source);
                            }
                        }

                        break;
                    case OwnershipOperationKind.WriteElement when operation.Input >= 0:
                        var projection = operation.Projection;
                        var path = projection >= 0 && body.Projections[projection].Path == projection ? body.ProjectionPath(projection) : -1;
                        this.Destroyed(body, id, path, body.Places[operation.Input].Type, operation.Source);
                        break;
                    case OwnershipOperationKind.StorePointer when operation.Place >= 0:
                        this.Destruction(this.Type(body.Places[operation.Place].Type), operation.Source); // The replaced pointee.
                        break;
                }
            }
        }

        // SPEC 14.8: a by-value Subject holds a Case its arms selected, less what their Patterns acquired. In an arm's guard
        // it holds that arm's whole Case, whose payload a guard never Moves (SPEC 14.8.3); in the arm's body, and where the
        // arms that can complete normally join after the match, it holds only the payload parts those Patterns left.
        // Destroying it destroys just those (nothing for `.None`). False, for the whole Subject, when the use is elsewhere
        // or an arm concerned does not select a Case of the Subject's enum.
        private bool SubjectDestroyed(Koto subject, Koto use, BoundType type)
        {
            // The Subject Place is sourced by the Subject expression (or by the match itself).
            var match = subject as MatchKoto;
            for (var source = subject; match is null && source.Parent is { } parent; source = parent)
            {
                match = parent is MatchKoto candidate && ReferenceEquals(candidate.Expression, source) ? candidate : null;
                if (match is null && parent is not ParenthesizedKoto)
                {
                    break;
                }
            }

            if (match is null || !binding.TryGetMatch(match, out var plan) || plan is not { Mode: SubjectMode.ByValue } || type.Symbol?.Declaration is not EnumKoto ||
                binding.compilation.Ownership.ControlFlow is not { } flow)
            {
                return false;
            }

            var node = ReferenceEquals(use, match) ? null : use;
            while (node is not null && !ReferenceEquals(node.Parent, match))
            {
                node = node.Parent;
            }

            if (node is null && !ReferenceEquals(use, match))
            {
                return false;
            }

            this.selectedArms.Clear();
            for (var a = 0; a < plan.Arms.Count; a++)
            {
                var arm = plan.Arms[a];
                if (node is null ? flow.Nodes.TryGetValue(arm.Syntax.Body, out var info) && info.CanCompleteNormally
                    : ReferenceEquals(arm.Syntax.Body, node) || ReferenceEquals(arm.Syntax.Guard, node))
                {
                    if ((uint)arm.Pattern >= (uint)plan.Positions.Count ||
                        plan.Positions[arm.Pattern] is not { Kind: BoundPatternKind.Case, ImplicitFollows: 0, AccessMode: PatternAccessMode.Owned, Case: { } selected } ||
                        !ReferenceEquals(selected.Owner, type.Symbol))
                    {
                        return false;
                    }

                    this.selectedArms.Add(a);
                }
            }

            for (var i = 0; i < this.selectedArms.Count; i++)
            {
                var arm = plan.Arms[this.selectedArms[i]];
                if (node is not null && ReferenceEquals(arm.Syntax.Guard, node))
                {
                    foreach (var syntax in plan.Positions[arm.Pattern].Case!.Payload)
                    {
                        this.Destruction(binding.StoredType(syntax, type), use);
                    }
                }
                else
                {
                    this.LeftParts(plan, arm.Pattern, use);
                }
            }

            return node is null || this.selectedArms.Count != 0;
        }

        // The parts of a Case or Tuple position that its Pattern left in the Subject: a part bound by a Move or Copy-or-Move
        // acquisition leaves nothing (that binding's own cleanup counts it), a part reached through a reference in every case
        // owns nothing (OwnedPart), and any other leaf (a wildcard, a literal, a Copy binding, a borrow past a pair layer that
        // admits owner, SPEC 13.5.5) stays and is destroyed with the Subject.
        private void LeftParts(BoundMatch plan, int position, Koto use)
        {
            for (var p = position + 1; this.valid && p < plan.Positions[position].End; p = plan.Positions[p].End)
            {
                var part = plan.Positions[p];
                if (!part.OwnedPart || (part.Kind == BoundPatternKind.Binding && part.Acquisition is PatternAcquisition.Move or PatternAcquisition.CopyOrMove))
                {
                    continue;
                }

                if (part.Kind is BoundPatternKind.Case or BoundPatternKind.Tuple)
                {
                    this.LeftParts(plan, p, use);
                }
                else
                {
                    this.Destruction(this.DeclaredType(part.MatchedType), use);
                }
            }
        }

        // A destroyed value, or, when parts of its move path were moved out, the parts still possibly initialized.
        private void Destroyed(OwnershipBody body, int operation, int path, BoundType type, Koto use)
        {
            if (path < 0)
            {
                this.Destruction(this.Type(type), use);
                return;
            }

            body.LoadPathInput(operation);
            this.DestroyedParts(body, path, use);
        }

        // As part destruction lowers them: a complete path is destroyed whole; otherwise every part that has no path of its
        // own (and a stored base) is destroyed while the remainder may own a value, and each part path recursively. A
        // Copy remnant of a Copy-or-Move acquisition owns nothing whose destruction has an effect (PlaceState.MayOwn).
        private void DestroyedParts(OwnershipBody body, int path, Koto use)
        {
            var node = body.GetMovePath(path);
            var remainder = (body.CurrentRemainderState(path) & PlaceState.MayOwn) != 0;
            if (node.Child < 0 || (body.CurrentPathState(path) & PlaceState.MustInit) != 0)
            {
                if (remainder)
                {
                    this.Destruction(this.Type(node.Type), use);
                }

                return;
            }

            this.DestroyedLayer(body, path, node.Type, use, remainder);
        }

        private void DestroyedLayer(OwnershipBody body, int path, BoundType type, Koto use, bool remainder)
        {
            var node = body.GetMovePath(path);
            var first = AdtDef.Base(type) is { } parentType ? AdtDef.StorageCount(parentType) : 0;
            var count = type.Kind == BoundTypeKind.FixedArray ? (int)type.Length : type.Kind == BoundTypeKind.Tuple ? type.Components.Count : AdtDef.StorageCount(type);
            for (var part = first; remainder && this.valid && part < count; part++)
            {
                if (!HasPath(body, node, part))
                {
                    this.Destruction(SelectedType(type, part - first) is { } stored ? this.Type(stored) : null, use);
                }
            }

            for (var child = node.Child; this.valid && child >= 0; child = body.GetMovePath(child).Next)
            {
                if (body.GetMovePath(child).Selector >= first && body.GetMovePath(child).Selector < count)
                {
                    this.DestroyedParts(body, child, use);
                }
            }

            if (AdtDef.Base(type) is { } parent)
            {
                var complete = body.CurrentBaseComplete(path, first);
                if (complete && remainder)
                {
                    this.Destruction(this.Type(parent), use);
                }
                else if (!complete)
                {
                    this.DestroyedLayer(body, path, parent, use, remainder);
                }
            }

            static bool HasPath(OwnershipBody body, MovePath node, int selector)
            {
                for (var child = node.Child; child >= 0; child = body.GetMovePath(child).Next)
                {
                    if (body.GetMovePath(child).Selector == selector)
                    {
                        return true;
                    }
                }

                return false;
            }

            // The Type of the part `selector` selects in a layer: a fixed array's element, a Tuple's component or a Field.
            static BoundType? SelectedType(BoundType type, int selector) => type.Kind switch
            {
                BoundTypeKind.FixedArray => type.Components[0],
                BoundTypeKind.Tuple => type.Components[selector],
                _ => AdtDef.FieldType(type, selector),
            };
        }

        private void Destruction(BoundType? type, Koto use)
        {
            if (this.cleanupTarget is { } target)
            {
                target.AddCaseDestruction(type, use, this.cleanupCases);
                return;
            }

            if (!this.destructions)
            {
                return; // Binding checks accesses and calls; destruction is counted after ownership analysis.
            }

            if (type is null)
            {
                this.DestructionLimit(use);
                return;
            }

            if (!this.destroyed.Add(type))
            {
                return;
            }

            if (type.HandleMode is not null)
            {
                // An open view can hide any more-derived destructor, which has no complete effect bound.
                if (binding.ProveSealed(type.Components[0], use) == ConstraintProof.Proven)
                {
                    this.Destruction(type.Components[0], use);
                }
                else
                {
                    this.Violate(EffectViolation.UnknownDestruction, use);
                }

                return;
            }

            // Destroying a Copy value, such as a Slice handle or a shared reference, has no effect.
            if (type.Semantics != SemanticsKind.Owner || binding.ProveCopy(type, use) == ConstraintProof.Proven)
            {
                return;
            }

            if (type.Kind == BoundTypeKind.Parameter)
            {
                this.Violate(EffectViolation.UnknownDestruction, use); // A Type parameter that is not proven Copy may have any destructor.
            }
            else if (AdtDef.IsStruct(type))
            {
                if (binding.DestructionCall(type) is { } destructor)
                {
                    this.Function(destructor.Target, destructor);
                }

                for (var i = 0; i < AdtDef.Count(type); i++)
                {
                    this.Destruction(AdtDef.FieldType(type, i), use);
                }

                if (AdtDef.Base(type) is { } parent)
                {
                    this.Destruction(parent, use);
                }
            }
            else
            {
                for (var i = 0; i < type.Components.Count; i++)
                {
                    this.Destruction(type.Components[i], use);
                }

                if (AdtDef.CaseTypes(type) is { } cases)
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        this.Destruction(cases[i], use);
                    }
                }
            }
        }

        private CallPlan NextCall()
        {
            if (this.callCount == this.calls.Count)
            {
                this.calls.Add(new());
            }

            return this.calls[this.callCount++];
        }

        // A queued node keeps the syntax of the implementation's own body through which it is reached: none for the body
        // itself, the node being visited for what that node evaluates or calls.
        private void Queue(Koto? node)
        {
            if (node is not null && this.seen.Add((node, this.context)))
            {
                this.pending.Add((node, this.context, this.site ?? this.last));
            }
        }

        // Records the first effect that violates a bound, at its node and at the own-body syntax that reaches it.
        // A destruction this implementation cannot classify yet: a limit of the check, or an unknown destruction of the collected
        // cleanups, which their check judges.
        private void DestructionLimit(Koto? at) => this.Violate(this.cleanupTarget is null ? EffectViolation.Limit : EffectViolation.UnknownDestruction, at);

        private void Violate(EffectViolation kind, Koto? at)
        {
            if (this.cleanupTarget is { } target && kind == EffectViolation.UnknownDestruction)
            {
                target.AddCaseDestruction(null, at ?? this.cleanupSource!.Function, this.cleanupCases);
                return;
            }

            if (!this.valid)
            {
                return;
            }

            this.valid = false;
            this.Violation = kind;
            this.ViolationNode = at ?? this.stepUse ?? this.last;
            this.ViolationSite = this.site ?? this.ViolationNode;
        }

        // SPEC 8.15 (PLAN G82): a declared Pattern Type is interpreted in its Subject's context, then composed with any outer call
        // context like an analyzed Type.
        private BoundType? DeclaredType(BoundType type) => this.subjectBody!.SubstituteDefaults(type, this.subjectContext) is { } declared ? this.Type(declared) : null;

        private BoundType? Type(BoundType type)
            => this.cleanupSource is { } body ? body.Resolve(type, InterpretationContext.Root)
            : this.contexts[this.context] is { } call ? binding.InstantiateStorageType(type, call) : type;

        // A call instantiates the callee's parameters, lengths and Origins; a call that substitutes nothing reads
        // the callee's Types as declared.
        private int Context(CallPlan call)
        {
            if (call.TypeArguments.Length == 0 && call.Origins.Length == 0 && call.InputOrigins.Length == 0 &&
                call.DeclaringType is null or { Components.Count: 0, OriginArguments.Count: 0 })
            {
                return 0;
            }

            if (this.contextIndex.TryGetValue(call, out var index))
            {
                return index;
            }

            if (this.contexts.Count >= 1024)
            {
                this.Violate(EffectViolation.Limit, null); // An effect expansion beyond 1024 instantiation contexts is not summarized.
                return 0;
            }

            this.contexts.Add(call);
            this.contextIndex.Add(call, this.contexts.Count - 1);
            return this.contexts.Count - 1;
        }
    }

    // Two calls read the callee's Types identically when they agree on target, declaring Type and every substitution.
    private sealed class CallInstanceComparer : IEqualityComparer<CallPlan>
    {
        internal static readonly CallInstanceComparer Instance = new();

        public bool Equals(CallPlan? x, CallPlan? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null && ReferenceEquals(x.Target, y.Target) && ReferenceEquals(x.DeclaringType, y.DeclaringType) &&
                x.TypeArguments.SequenceEqual(y.TypeArguments) && x.LengthArguments.SequenceEqual(y.LengthArguments) &&
                x.Origins.SequenceEqual(y.Origins) && x.InputOrigins.SequenceEqual(y.InputOrigins));

        public int GetHashCode(CallPlan call)
        {
            var hash = HashCode.Combine(call.Target, call.DeclaringType, call.TypeArguments.Length, call.Origins.Length, call.InputOrigins.Length);
            for (var i = 0; i < call.TypeArguments.Length; i++)
            {
                hash = HashCode.Combine(hash, call.TypeArguments[i]);
            }

            return hash;
        }
    }
}

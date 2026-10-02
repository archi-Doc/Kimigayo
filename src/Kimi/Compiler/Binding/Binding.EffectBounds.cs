// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private EffectSummary? effectSummary;

    // The pass after ownership analysis keeps its own call pool: its contexts differ from the Binding pass (SPEC 8.4.5).
    private EffectSummary? destructionSummary;

    /// <summary>
    /// SPEC 8.4.5, 22.1.2.4: checks every effect bound again after ownership analysis, now counting the destructions each
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
    }

    // SPEC 8.4.10.4: a conformance checks the complete transitive effect summary of every witness against the bounds that
    // its Contract and the ancestors declare for the requirement; an effect it cannot classify is a conflict (SPEC 8.4.5).
    // Binding checks the Loans the implementation accesses and the calls it makes. Which values it destroys is known only
    // from the cleanups ownership analysis plans, so destruction effects are checked afterwards (ValidateDestructionEffects).
    private void ValidateEffectBounds()
    {
        this.effectViolations?.Clear();
        this.effectSummary?.BeginPass();
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            if (this.EffectBoundViolation(this.activeConformancePaths[i], false) is { } path)
            {
                this.Fail(path.Use, BindingFailure.IncompatibleImplementation);
            }
        }
    }

    // The path, marked invalid, when one of its bounded implementations exceeds a bound; null otherwise. One traversal of a
    // witness's summary serves both bounds.
    private BoundConformancePath? EffectBoundViolation(BoundConformancePath path, bool destructions)
    {
        if (!path.IsVerified || path.Contract.Declaration.BoundSymbol?.Contract is not { HasEffectBounds: true } shape)
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

            var confined = this.DeclaredEffectBound(shape, requirement, EffectBoundKind.Confined) is not null;
            var preserves = this.DeclaredEffectBound(shape, requirement, EffectBoundKind.PreservesResults) is not null;
            if ((!confined && !preserves) ||
                (destructions && witness.Implementation.Declaration is FunctionKoto implementation && this.compilation.Ownership.TemplateBody(implementation, false) is null))
            {
                continue;
            }

            var summary = destructions ? this.destructionSummary ??= new(this) : this.effectSummary ??= new(this);
            if (!summary.Check(confined, preserves, witness.Implementation, path.Scope, destructions))
            {
                (this.effectViolations ??= new(ReferenceEqualityComparer.Instance))[path.Use] =
                    new(summary.Violation, summary.ViolationSite, summary.ViolationNode, summary.Delegation, summary.DelegationNode, requirement, shape, confined, preserves);
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
        private readonly List<BoundCall?> contexts = new();
        private readonly Dictionary<BoundCall, int> contextIndex = new(CallInstanceComparer.Instance);
        private readonly List<BoundCall> calls = new();
        private readonly List<(BoundType Value, BoundOrigin Storage)> storedValues = new();
        private readonly List<int> selectedArms = new();

        // SPEC 8.4.10.5: the abstract parts of the result, the requirement calls producing values of them, the own-body calls
        // tentatively compared with no earlier result, and the replacements of values on self paths in the own body.
        private readonly List<BoundType> itemParts = new();
        private readonly List<(FunctionKoto Requirement, Koto Call, Koto? Receiver)> producers = new();
        private readonly List<(FunctionKoto Requirement, Koto Call, Koto Receiver, bool Confined)> candidates = new();
        private readonly List<(Koto Path, Koto Node)> replacements = new();
        private BodyScan? scan;
        private bool delegable;
        private IReadOnlyList<BoundOrigin> selfOrigins = [];
        private BindingScope? scope;
        private BoundType item = BoundType.Unit;
        private bool confined;
        private bool preserves;
        private int context;
        private int callCount;
        private FunctionKoto? implementation;
        private BoundType? receiverType;
        private Koto? stepUse;
        private Koto? site;
        private Koto? last;
        private bool valid;
        private bool destructions;

        /// <summary>Gets the kind of the first effect that violated a bound in the last check.</summary>
        internal EffectViolation Violation { get; private set; }

        /// <summary>Gets the node of that effect, which may lie in a callee.</summary>
        internal Koto? ViolationNode { get; private set; }

        /// <summary>Gets the syntax of the implementation's own body through which that effect is reached.</summary>
        internal Koto? ViolationSite { get; private set; }

        /// <summary>Gets why the Loans of earlier results were not excluded for a requirement call, when exclusion was tried.</summary>
        internal DelegationFailure Delegation { get; private set; }

        /// <summary>Gets the source or replacement that prevented the exclusion.</summary>
        internal Koto? DelegationNode { get; private set; }

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

            if (node is IdentifierNameKoto or MemberAccessKoto && node.BoundSymbol is { Declaration: PropertyKoto property, Scope.Owner: GroupKoto })
            {
                // Lazy initialization runs at the first read. SPEC 8.4.10.2: confined reaches no mutable static.
                // Ownership analysis plans no cleanups for an initializer other than a constant scalar, so its destructions
                // are unknown.
                this.Queue(property.InitializerKoto);
                if (property.VariableKind == VariableKind.Var && this.confined)
                {
                    this.Violate(EffectViolation.MutableStatic, node);
                    return;
                }

                // SPEC 8.4.10.2: immutability does not protect a pointee, so a raw pointer read from an immutable static
                // obtains authority from the environment.
                if (this.confined && node.BoundSymbol.Type is { } stored && this.ContainsRawPointer(this.Type(stored), 0))
                {
                    this.Violate(EffectViolation.StaticPointer, node);
                    return;
                }

                if (this.destructions && property.InitializerKoto is not null && !StaticScalar.TryGet(node.BoundSymbol.Property, out _))
                {
                    this.Violate(EffectViolation.UnknownDestruction, node);
                    return;
                }
            }

            // SPEC 8.4.10.2: a pointer made from an integer has no provenance; it obtains authority from the environment.
            if (this.confined && node is ConversionKoto { ConversionBinding: ConversionBinding.Pointer, Left.BoundType.Kind: BoundTypeKind.Primitive })
            {
                this.Violate(EffectViolation.IntegerPointer, node);
                return;
            }

            if (node is InvocationKoto invocation && !binding.TryGetEnumConstruction(node, out _))
            {
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

        // SPEC 8.4.8.2: the bounds are judged in the conformance scope (D and the conditions P); the implementation's result
        // is normalized there, so a forwarded `I.(LendingIterator).LentItem(step)` is the step-independent `I.Item` under
        // `I is Iterator`. With `destructions`, the values each reached body destroys are summarized too.
        internal bool Check(bool confined, bool preserves, BindingSymbol implementation, BindingScope scope, bool destructions)
        {
            this.confined = confined;
            this.preserves = preserves;
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
            this.Delegation = DelegationFailure.None;
            this.DelegationNode = null;
            this.implementation = implementation.Declaration as FunctionKoto;
            this.receiverType = null;
            this.stepUse = null;
            this.delegable = false;
            this.itemParts.Clear();
            this.producers.Clear();
            this.candidates.Clear();
            if (preserves)
            {
                if (implementation.Type is not { } declared)
                {
                    return false;
                }

                var item = binding.ContractType(declared, scope);
                if (!item.CarriesOrigin && !AbstractTypes.HasAbstractPart(item))
                {
                    // A result that keeps no Loan conflicts with no effect.
                    this.preserves = false;
                    if (!confined)
                    {
                        return true;
                    }
                }
                else
                {
                    this.item = item;
                    this.selfOrigins = SelfOrigins(implementation, out var receiver);
                    this.receiverType = receiver;
                    this.StoredValues(receiver);
                    this.delegable = this.CollectItemParts(item);
                }
            }

            this.Function(implementation, null);
            for (var i = 0; this.valid && i < this.pending.Count; i++)
            {
                this.context = this.pending[i].Context;
                this.site = this.pending[i].Site;
                this.Visit(this.pending[i].Node);
            }

            if (this.valid && this.candidates.Count != 0)
            {
                this.ValidateDelegations();
            }

            return this.valid;
        }

        // The Origins of the conforming Type as its receiver names them; an abstract item part may keep a Loan of any.
        private static IReadOnlyList<BoundOrigin> SelfOrigins(BindingSymbol implementation, out BoundType? receiver)
        {
            receiver = implementation.Declaration is FunctionKoto function && implementation.ReceiverIndex >= 0 && implementation.ReceiverIndex < function.Parameters.Count
                ? function.Parameters[implementation.ReceiverIndex].Type.BoundType : null;
            while (receiver is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
            {
                receiver = receiver.Components[0];
            }

            return receiver?.OriginArguments ?? [];
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

        private static bool MaySelect(Specialization candidate, BoundCall call)
        {
            for (var i = 0; i < call.TypeArguments.Length; i++)
            {
                if (call.TypeArguments[i] is { } argument && candidate.Arguments[i] is { } closed && !MayMatch(argument, closed))
                {
                    return false;
                }
            }

            for (var i = 0; i < call.LengthArguments.Length; i++)
            {
                if (call.LengthArguments[i] is { IsConstant: true } length && !SameLengthSignature(length, candidate.Lengths[i], null!, null!))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MayMatch(BoundType pattern, BoundType closed)
        {
            if (ReferenceEquals(pattern, closed) || AbstractTypes.IsAbstract(pattern))
            {
                return true;
            }

            if (pattern.Kind == BoundTypeKind.Primitive || pattern.Kind != closed.Kind || pattern.Semantics != closed.Semantics ||
                !ReferenceEquals(pattern.Symbol, closed.Symbol) || pattern.Components.Count != closed.Components.Count ||
                (pattern.LengthExpression is null && pattern.Length != closed.Length))
            {
                return false;
            }

            for (var i = 0; i < pattern.Components.Count; i++)
            {
                if (!MayMatch(pattern.Components[i], closed.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // The receiver parameter of a call, whether bound as its receiver or as its first argument.
        private static BoundType? Receiver(BoundCall call)
        {
            var receiver = call.ReceiverOperation.ParameterType;
            for (var i = 0; receiver is null && i < call.ArgumentOperations.Length; i++)
            {
                if (call.ArgumentOperations[i].ParameterIndex == 0)
                {
                    receiver = call.ArgumentOperations[i].ParameterType;
                }
            }

            return receiver;
        }

        // An associated projection, whose root Type is not a part of its values.
        private static bool IsProjection(BoundType type) => type is { Kind: BoundTypeKind.AssociatedProjection, Components.Count: 2 };

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

            if (StructStorage.IsStruct(type))
            {
                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    if (this.ContainsRawPointer(StructStorage.FieldType(type, i), depth + 1))
                    {
                        return true;
                    }
                }
            }

            for (var i = 0; type.StoredCases is { } cases && i < cases.Length; i++)
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
                    this.Argument(acquired.ArgumentOperations[0], node);
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
                case IndexKoto:
                    this.Queue(binding.ResolvedKeyCall(node));
                    break;
                case RangeKoto or FromEndIndexKoto:
                    this.Queue(binding.RangeValueCall(node));
                    break;
                case BinaryKoto { ComparisonCall: { } comparison }:
                    this.Queue(comparison);
                    break;
                case ForKoto { Iteration: { Decomposition.IsCurrent: true } iteration } loop:
                    // SPEC 14.6.2: the entry call, the internal Iterator local with its destruction, and each step.
                    this.Queue(loop.EntryCall);
                    this.Queue(iteration.Iterator);
                    this.Queue(iteration.Next);
                    break;
            }
        }

        // The reads, writes and borrows of one node, each through the Place path it uses (SPEC 22.1.2.4). A Place is
        // accessed once, by its outermost use: an operand of a projection, a borrow, a Move or a write is accessed there.
        private void Accesses(Koto node)
        {
            var adapted = binding.TryGetAdaptation(node, out var adaptation);
            if (adapted)
            {
                switch (adaptation.Kind)
                {
                    case ExpectedAdaptationKind.Reborrow or ExpectedAdaptationKind.ReferenceRead or ExpectedAdaptationKind.ReferentRead:
                        // SPEC 10.2, 15.6.4: shortening a Reborrow's lifetime does not remove its original access effect.
                        this.Access(node.BoundType, Mode(adaptation.Type), node);
                        this.PlaceAccess(node, LoanRequirement.Ref, node);
                        break;
                    case ExpectedAdaptationKind.SharedBorrow:
                        this.PlaceAccess(node, LoanRequirement.Ref, node);
                        break;
                    case ExpectedAdaptationKind.ExclusiveBorrow:
                        this.PlaceAccess(node, LoanRequirement.Uniq, node);
                        break;
                }
            }

            switch (node)
            {
                case BinaryKoto assignment when IsAssignment(assignment.Akind):
                    if (assignment.Akind != KotoKind.Equals)
                    {
                        this.Queue(binding.PropertyCall(assignment.Left, PropertyAccessorKind.Get)); // A compound assignment reads first.
                    }

                    this.PlaceAccess(assignment.Left, LoanRequirement.Uniq, node);
                    break;
                case UnaryKoto update when ElementAccess.UpdateOperator(update.Akind) != KotoKind.Invalid:
                    this.Queue(binding.PropertyCall(update.Operand, PropertyAccessorKind.Get));
                    this.PlaceAccess(update.Operand, LoanRequirement.Uniq, node);
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow when ElementAccess.IsRawPlace(borrow.Left):
                    break; // SPEC 5.2.2: a fresh anchor, compared with no Place it does not derive from.
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow:
                    this.Access(borrow.BoundType, Mode(borrow.BoundType), node);
                    this.PlaceAccess(borrow.Left, Mode(borrow.BoundType), node);
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Address } address:
                    this.PlaceAccess(address.Left, LoanRequirement.Ref, node); // SPEC 5.4: an immediately ending borrow.
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                    this.PlaceAccess(transfer.Left, LoanRequirement.Uniq, node); // A Move invalidates its Place.
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } follow when !adapted && !this.IsOperand(node):
                    this.PlaceAccess(follow, LoanRequirement.Ref, node);
                    break;
                case MatchKoto match when binding.TryGetMatch(match, out var plan) && plan is not null:
                    // SPEC 15.1.6: the Subject Place is accessed in the mode it grants.
                    var mode = plan.Mode == SubjectMode.Exclusive ? LoanRequirement.Uniq : LoanRequirement.Ref;
                    this.Access(plan.SubjectBorrow, mode, node);
                    this.PlaceAccess(match.Expression, mode, node);
                    break;
                case ForKoto loop when loop.Iteration is not { Decomposition.IsCurrent: true }:
                    var access = loop.Mode == SubjectMode.Exclusive ? LoanRequirement.Uniq : LoanRequirement.Ref;
                    this.Access(loop.Iterable.BoundType, access, node);
                    this.PlaceAccess(loop.Iterable, access, node);
                    break;
                case IdentifierNameKoto or MemberAccessKoto or IndexKoto when !adapted && !this.IsOperand(node):
                    this.PlaceAccess(node, LoanRequirement.Ref, node);
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
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address or ConversionBinding.Transfer or ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } conversion:
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
        // one in the mode, the inner ones read), through the reference a Place call publishes, or to the static storage
        // of a group variable. An accessor or user indexer Place is its synthesized call, selected by the mode; an owned
        // local or parameter root needs no Loan, and a raw pointer path is unsafe and outside the checked summary.
        private void PlaceAccess(Koto? node, LoanRequirement mode, Koto use)
        {
            for (var depth = 0; node is not null && this.valid && depth < 256; depth++)
            {
                node = KotoHelper.UnwrapParentheses(node);
                if (node is IdentifierNameKoto or MemberAccessKoto)
                {
                    if (node.BoundSymbol is { Declaration: PropertyKoto { VariableKind: VariableKind.Var }, Scope.Owner: GroupKoto })
                    {
                        this.AccessOrigin(BoundOrigin.Static, mode, use);
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
                        this.Access(follow.Left.BoundType, mode, use);
                        mode = LoanRequirement.Ref;
                        node = follow.Left;
                        break;
                    case InvocationKoto when ElementAccess.PlaceCallReference(node) is { } reference:
                        this.Access(reference, mode, use);
                        return;
                    case IdentifierNameKoto when binding.StorageProjection(node) is { } projection:
                        node = projection;
                        break;
                    case IndexKoto when binding.IndexerCall(node, false) is { } shared:
                        // SPEC 4.6.9: indexUniq serves updates and exclusive borrows; its receiver argument continues the path.
                        this.Queue(mode == LoanRequirement.Uniq ? binding.IndexerCall(node, true) ?? shared : shared);
                        return;
                    case IndexKoto:
                    case MemberAccessKoto { Right: NumberLiteralKoto }:
                    case MemberAccessKoto { BoundSymbol.Property.IsStored: true }:
                        var part = (BinaryKoto)node;
                        var receiver = ElementAccess.AccessType(part.Left);
                        if (IsReached(receiver))
                        {
                            this.Access(receiver, mode, use);
                            mode = LoanRequirement.Ref; // Reaching the reference reads its container.
                        }

                        node = part.Left;
                        break;
                    default:
                        return; // A local, a temporary, a getter result or a raw pointer root.
                }
            }
        }

        // SPEC 8.4.10.3: only preserves results classifies Loans; confined concerns the environment the summary reaches.
        private void Access(BoundType? type, LoanRequirement mode, Koto use)
        {
            if (type is null || !this.preserves)
            {
                return;
            }

            if (this.Type(type) is not { } layer)
            {
                this.Violate(EffectViolation.UnclassifiedAccess, use); // An access whose Type cannot be instantiated has no classified Loan.
                return;
            }

            // Reading through ref/uniq/T also reads through each inner reference.
            while (this.valid)
            {
                if (layer.Origin is { } origin)
                {
                    this.AccessOrigin(origin, mode, use);
                }
                else if (layer.Kind == BoundTypeKind.SemanticsApplication)
                {
                    this.Violate(EffectViolation.UnclassifiedAccess, use); // A pair layer without an Origin may be a borrow of any Loan.
                }

                if (layer is not { Kind: BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication, Components.Count: 1 })
                {
                    return;
                }

                layer = layer.Components[0];
            }
        }

        private void AccessOrigin(BoundOrigin origin, LoanRequirement mode, Koto use)
        {
            if (this.preserves && this.Conflicts(this.item, origin, mode, false, use))
            {
                this.Violate(EffectViolation.ResultLoan, use);
            }
        }

        // Every Loan reachable from a Type: its Origin, its Origin arguments and those of its parts. An abstract part may
        // reach any Loan, which conflicts with any item Loan.
        private void Reachable(BoundType? type, LoanRequirement mode, Koto use)
        {
            if (type is null || AbstractTypes.IsAbstract(type))
            {
                this.Violate(EffectViolation.UnclassifiedAccess, use);
                return;
            }

            if (type.Origin is { } origin)
            {
                this.AccessOrigin(origin, mode, use);
            }

            for (var i = 0; this.valid && i < type.OriginArguments.Count; i++)
            {
                this.AccessOrigin(type.OriginArguments[i], mode, use);
            }

            for (var i = 0; this.valid && i < type.Components.Count; i++)
            {
                this.Reachable(type.Components[i], mode, use);
            }
        }

        // An argument accesses its source Place as its operation does; the Place path also selects synthesized calls.
        private void Argument(in BoundArgumentOperation argument, Koto use)
        {
            var mode = Mode(argument.ParameterType);
            switch (argument.Kind)
            {
                case ArgumentOperationKind.Borrow or ArgumentOperationKind.BaseBorrow or ArgumentOperationKind.StorageProjection or ArgumentOperationKind.PayloadProjection:
                    this.Access(argument.ParameterType, mode, use);
                    this.PlaceAccess(argument.Source, mode, use);
                    break;
                case ArgumentOperationKind.Reborrow or ArgumentOperationKind.ReferenceRead:
                    this.Access(argument.ParameterType, mode, use);
                    this.Access(argument.SourceType, mode, use);
                    this.PlaceAccess(argument.Source, LoanRequirement.Ref, use);
                    break;
                default:
                    this.Access(argument.Kind == ArgumentOperationKind.CopyRead ? argument.SourceType : null, LoanRequirement.Ref, use);
                    this.PlaceAccess(argument.Source, LoanRequirement.Ref, use);
                    break;
            }
        }

        private void Call(BoundCall call, Koto use)
        {
            this.Argument(call.ReceiverOperation, use);
            for (var i = 0; i < call.ArgumentOperations.Length; i++)
            {
                this.Argument(call.ArgumentOperations[i], use);
            }

            if (this.contexts[this.context] is { } outer)
            {
                if (binding.InstantiateForwardedCall(call, outer, this.NextCall()) is not { } instantiated)
                {
                    this.Violate(EffectViolation.UnclassifiedCall, use);
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

        private void Target(BoundCall call, Koto use)
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
                    this.Violate(EffectViolation.UnclassifiedCall, use);
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
                this.Violate(EffectViolation.UnclassifiedCall, null);
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

        private void Function(BindingSymbol symbol, BoundCall? call)
        {
            if (symbol.CompilerFunction != CompilerFunctionKind.None)
            {
                if (!this.Allows(symbol.CompilerFunction))
                {
                    this.Violate(symbol.CompilerFunction is CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 ? EffectViolation.ExternalOperation : EffectViolation.UnclassifiedCall, null);
                }

                return;
            }

            if (symbol.Declaration is not FunctionKoto function)
            {
                this.Violate(EffectViolation.UnclassifiedCall, null);
                return;
            }

            if (function.IsRequirement)
            {
                this.Requirement(symbol, call);
                return;
            }

            if (function.Body is null && function.ExpressionBody is null && !(function.IsConstructor && function.IsGenerated))
            {
                // SPEC 22.3.1, 8.4.10.2: a foreign function accesses only what its arguments permit, which the arguments
                // already accessed, and the environment, which confined excludes. Any other bodyless call is unknown.
                if (!IsLibraryImport(function))
                {
                    this.Violate(EffectViolation.UnclassifiedCall, null);
                }
                else if (this.confined)
                {
                    this.Violate(EffectViolation.ForeignCall, null);
                }

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
                    else if (MaySelect(binding.specializations[candidates[i]], call))
                    {
                        this.Specialization(candidates[i], call);
                    }
                }
            }
        }

        // These operations act only on their acquired inputs and the allocator; comparison, formatting and destruction
        // callbacks are summarized separately. Console output is an environment effect, outside confined (SPEC 8.4.10.2).
        private bool Allows(CompilerFunctionKind kind) => kind switch
        {
            CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 => !this.confined,
            CompilerFunctionKind.Abort or CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap or CompilerFunctionKind.MakeObj or
                >= CompilerFunctionKind.ArrayReserve and <= CompilerFunctionKind.TextHeap or
                CompilerFunctionKind.TextWriter or CompilerFunctionKind.TextUtf8 or CompilerFunctionKind.TextValidateUtf8 or
                >= CompilerFunctionKind.TextRelease and <= CompilerFunctionKind.WindowCommit or CompilerFunctionKind.WriterStatus or CompilerFunctionKind.BuiltinFormat or
                CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare or
                CompilerFunctionKind.StorageBorrowShared or CompilerFunctionKind.StorageBorrowExclusive or
                CompilerFunctionKind.StorageOwn or
                >= CompilerFunctionKind.StorageBorrowDictionary and <= CompilerFunctionKind.StorageShrinkDictionary or
                >= CompilerFunctionKind.RawAllocate and <= CompilerFunctionKind.RawInitialize => true, // SPEC 5.6: an allocation and raw accesses.
            _ => false,
        };

        // SPEC 8.4.10.4: a requirement call has the effects that the bounds available under its premises leave. With preserves
        // results, a call of the one delegated requirement on the one stored value is covered (SPEC 8.4.10.5). Without
        // confined, its environment effects are unknown, which a bound check counts as a conflict. Its inputs are affected
        // in their parameters' modes, which only preserves results classifies.
        private void Requirement(BindingSymbol symbol, BoundCall? call)
        {
            if (call is null || symbol.Declaration is not FunctionKoto requirement)
            {
                this.Violate(EffectViolation.UnboundedRequirement, null);
                return;
            }

            // The premises are those of the call's own scope and, in the implementation's own body, the conditions of the
            // conformance (SPEC 8.4.8.2), such as I is Iterator for Self is Iterator when I is Iterator.
            var conforming = call.ConformingType is { } type ? this.Type(type) : null;
            var (confined, preserves) = binding.AvailableEffectBounds(requirement, conforming, this.scope!);
            if (this.stepUse is { } use && (!confined || !preserves))
            {
                var (local, held) = binding.AvailableEffectBounds(requirement, conforming, binding.ConstraintScope(use));
                confined |= local;
                preserves |= held;
            }

            if (this.preserves)
            {
                // SPEC 8.4.10.5: every requirement call producing a value of an abstract part of the result is recorded.
                var own = this.OwnFieldPathReceiver();
                if (this.Type(call.ReturnType) is { } produced && this.MentionsItemPart(binding.ContractType(produced, this.scope!)))
                {
                    this.producers.Add((requirement, this.stepUse ?? symbol.Declaration, own));
                }

                if (preserves && this.delegable && own is not null)
                {
                    this.candidates.Add((requirement, this.stepUse!, own, confined));
                    return;
                }

                if (preserves)
                {
                    this.Delegation = this.delegable ? DelegationFailure.CallSite : DelegationFailure.Untraced;
                }
            }

            if (!confined)
            {
                this.Violate(EffectViolation.UnboundedRequirement, null);
                return;
            }

            if (this.preserves)
            {
                this.Reachable(Receiver(call) is { } receiver ? this.Type(receiver) : null, LoanRequirement.Uniq, symbol.Declaration);
                for (var i = 0; this.valid && i < call.ArgumentOperations.Length; i++)
                {
                    if (call.ArgumentOperations[i] is { ParameterIndex: > 0, ParameterType: { } parameter } && (ReferenceTypes.IsBorrow(parameter) || ObjectTypes.IsBorrow(parameter)))
                    {
                        this.Reachable(this.Type(parameter), Mode(parameter), symbol.Declaration);
                    }
                }
            }
        }

        // SPEC 8.4.10.5: a result keeps Loans only through its abstract parts when its other parts carry no Origin. A value of
        // such a part is produced only by a requirement call, an argument, or a Field that keeps one across calls, so a call of
        // d on a value w reached from self through a Field path in the implementation's own body is compared with no Loan of
        // an earlier result when every producer is d on w and the body replaces no value on the path to w. Candidates are
        // judged after the whole summary is visited, when every producer is known.
        private void ValidateDelegations()
        {
            this.scan ??= new(this);
            this.replacements.Clear();
            if (this.implementation!.Body is { } body)
            {
                this.scan.Visit(body);
            }

            if (this.implementation.ExpressionBody is { } expression)
            {
                this.scan.Visit(expression);
            }

            for (var c = 0; this.valid && c < this.candidates.Count; c++)
            {
                var candidate = this.candidates[c];
                var failure = this.DelegationFailureOf(candidate.Requirement, candidate.Receiver, out var blocking);
                if (failure != DelegationFailure.None)
                {
                    // The call's effects are then those of SPEC 8.4.10.4, which reach every Loan of the abstract value.
                    this.Delegation = failure;
                    this.DelegationNode = blocking;
                    this.site = null;
                    this.stepUse = candidate.Call;
                    this.Violate(candidate.Confined ? EffectViolation.UnclassifiedAccess : EffectViolation.UnboundedRequirement, candidate.Call);
                }
            }
        }

        private DelegationFailure DelegationFailureOf(FunctionKoto requirement, Koto receiver, out Koto? blocking)
        {
            for (var i = 0; i < this.replacements.Count; i++)
            {
                if (this.IsPathPrefix(this.replacements[i].Path, receiver))
                {
                    blocking = this.replacements[i].Node;
                    return DelegationFailure.Replaced;
                }
            }

            var function = this.implementation!;
            var receiverIndex = function.BoundSymbol?.ReceiverIndex ?? -1;
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != receiverIndex && function.Parameters[i].Type.BoundType is { } parameter && this.MentionsItemPart(binding.ContractType(parameter, this.scope!)))
                {
                    blocking = function.Parameters[i].Type;
                    return DelegationFailure.Argument;
                }
            }

            for (var i = 0; StructStorage.IsStruct(this.receiverType) && i < StructStorage.Count(this.receiverType!); i++)
            {
                if (StructStorage.FieldType(this.receiverType!, i) is not { } stored || this.MentionsItemPart(binding.ContractType(stored, this.scope!)))
                {
                    blocking = StructStorage.Field(this.receiverType!, i);
                    return DelegationFailure.Untraced;
                }
            }

            for (var i = 0; i < this.producers.Count; i++)
            {
                var producer = this.producers[i];
                var failure = !ReferenceEquals(producer.Requirement, requirement) ? DelegationFailure.OtherRequirement
                    : producer.Receiver is not { } path || this.SelfPathDepth(path) != this.SelfPathDepth(receiver) || !this.IsPathPrefix(path, receiver) ? DelegationFailure.OtherValue
                    : DelegationFailure.None;
                if (failure != DelegationFailure.None)
                {
                    blocking = producer.Call;
                    return failure;
                }
            }

            blocking = null;
            return DelegationFailure.None;
        }

        // The receiver path of a requirement call made on a value reached from self through a Field path, in the implementation's
        // own body; null otherwise.
        private Koto? OwnFieldPathReceiver()
            => this.context == 0 && this.stepUse is InvocationKoto { Method: MemberAccessKoto { Left: var receiver } } call && this.IsOwnBody(call) &&
                this.SelfPathDepth(receiver) >= 1 ? receiver : null;

        // Whether a node is in the implementation's own body rather than in a function literal inside it.
        private bool IsOwnBody(Koto node)
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is FunctionKoto function)
                {
                    return ReferenceEquals(function, this.implementation);
                }
            }

            return false;
        }

        // The number of Fields on a path from the implementation's receiver `self`, as in `self.inner`; -1 when the node is not
        // such a path. Parentheses are transparent.
        private int SelfPathDepth(Koto? node)
        {
            var depth = 0;
            node = node is null ? null : KotoHelper.UnwrapParentheses(node);
            while (node is MemberAccessKoto { BoundSymbol.Property.IsStored: true } access)
            {
                depth++;
                node = KotoHelper.UnwrapParentheses(access.Left);
            }

            return node is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter, Slot: var slot, Declaration: var owner } } &&
                ReferenceEquals(owner, this.implementation) && this.implementation?.BoundSymbol is { ReceiverIndex: >= 0 and var index } && slot == index ? depth : -1;
        }

        // Whether the self path `prefix` is `path` or one of the paths it passes through.
        private bool IsPathPrefix(Koto prefix, Koto path)
        {
            var shorter = this.SelfPathDepth(prefix);
            var longer = this.SelfPathDepth(path);
            if (shorter < 0 || longer < shorter)
            {
                return false;
            }

            path = KotoHelper.UnwrapParentheses(path);
            for (; longer > shorter; longer--)
            {
                path = KotoHelper.UnwrapParentheses(((MemberAccessKoto)path).Left);
            }

            prefix = KotoHelper.UnwrapParentheses(prefix);
            for (; shorter > 0; shorter--)
            {
                if (!ReferenceEquals(prefix.BoundSymbol, path.BoundSymbol))
                {
                    return false;
                }

                prefix = KotoHelper.UnwrapParentheses(((MemberAccessKoto)prefix).Left);
                path = KotoHelper.UnwrapParentheses(((MemberAccessKoto)path).Left);
            }

            return true;
        }

        // Collects the abstract parts of the result Type; false when another part carries an Origin, whose Loans may come from
        // anywhere.
        private bool CollectItemParts(BoundType type)
        {
            if (AbstractTypes.IsAbstract(type))
            {
                if (!this.itemParts.Contains(type))
                {
                    this.itemParts.Add(type);
                }

                return true;
            }

            if (type.Origin is not null || type.OriginArguments.Count != 0)
            {
                return false;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!this.CollectItemParts(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Whether a value of `type` may hold a value of an abstract part of the result. The root of a projection is not a part
        // of its values.
        private bool MentionsItemPart(BoundType type)
        {
            if (this.itemParts.Contains(type))
            {
                return true;
            }

            if (IsProjection(type))
            {
                return false;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.MentionsItemPart(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void Specialization(FunctionKoto function, BoundCall call)
        {
            // The specialization has its own input/Origin binders but inherits their slots from the original.
            var specialized = this.NextCall();
            specialized.Set(function.BoundSymbol!, call.ReturnType, call.Receiver, call.ArgumentToParameter, [], conformingType: call.ConformingType, declaringType: call.DeclaringType, origins: call.Origins, inputOrigins: call.InputOrigins);
            this.Body(function, specialized);
        }

        private void Body(FunctionKoto function, BoundCall? call)
        {
            var previous = this.context;
            if (call is not null)
            {
                this.context = this.Context(call);
            }

            this.Queue(function.Body);
            this.Queue(function.ExpressionBody);
            this.Queue(function.BaseInitializer);
            if (function.IsConstructor && (call?.DeclaringType ?? StructStorage.ReceiverType(function)) is { } owner)
            {
                for (var i = 0; i < StructStorage.Count(owner); i++)
                {
                    this.Queue(StructStorage.Field(owner, i).InitializerKoto);
                }
            }

            if (this.destructions)
            {
                this.Cleanups(function);
            }

            this.context = previous;
        }

        // SPEC 22.1.2.4: the values a body destroys are exactly the cleanups ownership analysis planned for it: a local,
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
                this.Violate(EffectViolation.UnknownDestruction, null);
                return;
            }

            if (body.Issues.Count != 0)
            {
                return;
            }

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
                        var action = body.CleanupSteps[body.OperationSteps[id]].Action;
                        if (action == CleanupAction.Unsupported)
                        {
                            this.Violate(EffectViolation.UnknownDestruction, operation.Source);
                        }
                        else if (action != CleanupAction.Skip && (body.GetStorageState(id, operation.Place) & PlaceState.MayOwn) != 0)
                        {
                            var place = body.Places[operation.Place];
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
        // acquisition leaves nothing (that binding's own cleanup counts it), a part reached through a reference owns
        // nothing, and any other leaf (a wildcard, a literal, a Copy binding) stays and is destroyed with the Subject.
        private void LeftParts(BoundMatch plan, int position, Koto use)
        {
            for (var p = position + 1; this.valid && p < plan.Positions[position].End; p = plan.Positions[p].End)
            {
                var part = plan.Positions[p];
                if (part.ImplicitFollows != 0 || part.AccessMode != PatternAccessMode.Owned ||
                    (part.Kind == BoundPatternKind.Binding && part.Acquisition is PatternAcquisition.Move or PatternAcquisition.CopyOrMove))
                {
                    continue;
                }

                if (part.Kind is BoundPatternKind.Case or BoundPatternKind.Tuple)
                {
                    this.LeftParts(plan, p, use);
                }
                else
                {
                    this.Destruction(this.Type(part.MatchedType), use);
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

            for (var part = 0; remainder && this.valid && part < node.Count; part++)
            {
                if (!HasPath(body, node, part))
                {
                    this.Destruction(PartType(node.Type, part) is { } stored ? this.Type(stored) : null, use);
                }
            }

            for (var child = node.Child; this.valid && child >= 0; child = body.GetMovePath(child).Next)
            {
                this.DestroyedParts(body, child, use);
            }

            if (remainder && node.Type.StoredBase is { } parent)
            {
                this.Destruction(this.Type(parent), use);
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

            static BoundType? PartType(BoundType type, int selector) => type.Kind switch
            {
                BoundTypeKind.FixedArray => type.Components[0],
                BoundTypeKind.Tuple => type.Components[selector],
                _ => StructStorage.FieldType(type, selector),
            };
        }

        private void Destruction(BoundType? type, Koto use)
        {
            if (!this.destructions)
            {
                return; // Binding checks accesses and calls; destruction is counted after ownership analysis.
            }

            if (type is null)
            {
                this.Violate(EffectViolation.UnknownDestruction, use);
                return;
            }

            if (!this.destroyed.Add(type))
            {
                return;
            }

            if (ObjectTypes.IsOwner(type))
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
            else if (StructStorage.IsStruct(type))
            {
                if (binding.DestructionCall(type) is { } destructor)
                {
                    this.Function(destructor.Target, destructor);
                }

                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    this.Destruction(StructStorage.FieldType(type, i), use);
                }

                if (binding.StoredBase(type) is { } parent)
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

                if (type.StoredCases is { } cases)
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        this.Destruction(cases[i], use);
                    }
                }
            }
        }

        // SPEC 8.4.10.5: a Field `s/J during o` borrows the Storage of J, and an associated Type of J without Origin parameters
        // cannot name that Storage, so it keeps no Loan of o even though o is an Origin of the conforming Type.
        private void StoredValues(BoundType? receiver)
        {
            this.storedValues.Clear();
            if (!StructStorage.IsStruct(receiver))
            {
                return;
            }

            for (var i = 0; i < StructStorage.Count(receiver!); i++)
            {
                if (StructStorage.FieldType(receiver!, i) is { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner, Components: [var value], Origin: { } storage })
                {
                    this.storedValues.Add((value, storage));
                }
            }
        }

        // Whether `origin` is the Loan of the Storage of the stored value whose associated Type `type` is.
        private bool IsStoredValueStorage(BoundType type, BoundOrigin origin)
        {
            if (!IsProjection(type) || type.OriginArguments.Count != 0)
            {
                return false;
            }

            var root = type.Components[0];
            for (var i = 0; i < this.storedValues.Count; i++)
            {
                if (ReferenceEquals(this.storedValues[i].Value, root) && ReferenceEquals(this.storedValues[i].Storage, origin))
                {
                    return true;
                }
            }

            return false;
        }

        // Whether an access conflicts with a Loan the item may keep. Below a shared layer an item keeps shared Loans only,
        // so only an exclusive access conflicts there; an abstract part may keep any Loan of the conforming Type's Origins.
        private bool Conflicts(BoundType type, BoundOrigin accessed, LoanRequirement mode, bool shared, Koto use)
        {
            var exclusive = mode == LoanRequirement.Uniq;
            if (type.Origin is { } retained && (exclusive || (!shared && type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq)) &&
                this.SharesDependency(accessed, retained, use))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if ((exclusive || (!shared && type.Symbol?.Schema?.Origins[i].LoanRequirement == LoanRequirement.Uniq)) &&
                    this.SharesDependency(accessed, type.OriginArguments[i], use))
                {
                    return true;
                }
            }

            if (AbstractTypes.IsAbstract(type) && (exclusive || !shared))
            {
                if (type.Origin is { } outer && this.SharesDependency(accessed, outer, use))
                {
                    return true;
                }

                for (var i = 0; i < this.selfOrigins.Count; i++)
                {
                    if (!this.IsStoredValueStorage(type, this.selfOrigins[i]) && this.SharesDependency(accessed, this.selfOrigins[i], use))
                    {
                        return true;
                    }
                }

                if (accessed.Kind == OriginKind.Static)
                {
                    return true;
                }
            }

            if (IsProjection(type))
            {
                return false; // The root of a projection is not a part of its values; the checks above cover it.
            }

            shared |= type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef;
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.Conflicts(type.Components[i], accessed, mode, shared, use))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SharesDependency(BoundOrigin accessed, BoundOrigin retained, Koto use)
        {
            if (binding.ProvesOriginOutlives(accessed, retained, use) && binding.ProvesOriginOutlives(retained, accessed, use))
            {
                return true;
            }

            // SPEC 15.6.4: an intersection retains every possible source, not only the common lifetime.
            if (accessed.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < accessed.Operands.Count; i++)
                {
                    if (this.SharesDependency(accessed.Operands[i], retained, use))
                    {
                        return true;
                    }
                }
            }

            if (retained.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < retained.Operands.Count; i++)
                {
                    if (this.SharesDependency(accessed, retained.Operands[i], use))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private BoundCall NextCall()
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
        private void Violate(EffectViolation kind, Koto? at)
        {
            if (!this.valid)
            {
                return;
            }

            this.valid = false;
            this.Violation = kind;
            this.ViolationNode = at ?? this.stepUse ?? this.last;
            this.ViolationSite = this.site ?? this.ViolationNode;
        }

        private BoundType? Type(BoundType type)
            => this.contexts[this.context] is { } call ? binding.InstantiateStorageType(type, call) : type;

        // A call instantiates the callee's parameters, lengths and Origins; a call that substitutes nothing reads
        // the callee's Types as declared.
        private int Context(BoundCall call)
        {
            if (call.TypeArguments.Length == 0 && call.LengthArguments.Length == 0 && call.Origins.Length == 0 && call.InputOrigins.Length == 0 &&
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
                this.Violate(EffectViolation.UnclassifiedCall, null); // An unbounded effect expansion cannot prove conformance.
                return 0;
            }

            this.contexts.Add(call);
            this.contextIndex.Add(call, this.contexts.Count - 1);
            return this.contexts.Count - 1;
        }

        // Collects the replacements of values on self paths in the own body: a write, a Move, an exclusive borrow, or an
        // exclusive acquisition by a call other than a requirement call's receiver, any of which may replace, swap or move the
        // value (SPEC 8.4.10.5). Nested function literals are separate bodies.
        private sealed class BodyScan(EffectSummary summary) : KotoVisitor
        {
            public override void Visit(Koto node)
            {
                if (node is FunctionKoto or DeclarationContainerKoto)
                {
                    return;
                }

                switch (node)
                {
                    case BinaryKoto assignment when IsAssignment(assignment.Akind):
                        this.Replace(assignment.Left, node);
                        break;
                    case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                        this.Replace(transfer.Left, node);
                        break;
                    case ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } borrow:
                        this.Replace(borrow.Left, node);
                        break;
                    case InvocationKoto { BoundCall: { } call }:
                        // A requirement call's receiver is the called value itself, whose own bound covers its effects.
                        var requirement = call.Target.Declaration is FunctionKoto { IsRequirement: true };
                        if (!requirement && IsExclusive(call.ReceiverOperation.ParameterType))
                        {
                            this.Replace(call.Receiver, node);
                        }

                        for (var i = 0; i < call.ArgumentOperations.Length; i++)
                        {
                            if (IsExclusive(call.ArgumentOperations[i].ParameterType) && !(requirement && call.ArgumentOperations[i].ParameterIndex == call.Target.ReceiverIndex))
                            {
                                this.Replace(call.ArgumentOperations[i].Source, node);
                            }
                        }

                        break;
                }

                node.VisitChildren(this);
            }

            private static bool IsExclusive(BoundType? type) => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

            private void Replace(Koto? path, Koto node)
            {
                if (path is not null && summary.SelfPathDepth(path) >= 0)
                {
                    summary.replacements.Add((path, node));
                }
            }
        }
    }

    // Two calls read the callee's Types identically when they agree on target, declaring Type and every substitution.
    private sealed class CallInstanceComparer : IEqualityComparer<BoundCall>
    {
        internal static readonly CallInstanceComparer Instance = new();

        public bool Equals(BoundCall? x, BoundCall? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null && ReferenceEquals(x.Target, y.Target) && ReferenceEquals(x.DeclaringType, y.DeclaringType) &&
                x.TypeArguments.SequenceEqual(y.TypeArguments) && x.LengthArguments.SequenceEqual(y.LengthArguments) &&
                x.Origins.SequenceEqual(y.Origins) && x.InputOrigins.SequenceEqual(y.InputOrigins));

        public int GetHashCode(BoundCall call)
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

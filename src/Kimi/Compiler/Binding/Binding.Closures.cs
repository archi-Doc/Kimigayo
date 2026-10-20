// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Retained closure conversion and capture vocabulary.

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    // Header probes supply omitted inputs without committing parameter bindings or visiting the body. A written
    // result's elision depends on those inputs, so it is rebound for each fixed context and at commitment.
    private FunctionKoto? closureHeader;
    private BoundType? closureHeaderInputs;

    private BoundType? ClosureResultType(FunctionKoto function, BoundType inputs, BindingScope scope)
    {
        var previous = this.closureHeader;
        var previousInputs = this.closureHeaderInputs;
        this.closureHeader = function;
        this.closureHeaderInputs = inputs;
        try
        {
            return function.ReturnType is { } result ? this.BindType(result, scope) : null;
        }
        finally
        {
            this.closureHeader = previous;
            this.closureHeaderInputs = previousInputs;
        }
    }

    // Set only while TryCandidate asks whether a rejected candidate applies once its closure argument's minimum receiver is permitted.
    private bool permitClosureReceivers;

    // SPEC 15.8.2: the first Origin of an inferred closure result that names the call's own storage: a parameter slot or a body local
    // of the closure, or, in a Consuming call, an environment binding. Origins of the enclosing body and receiver-dependent bindings
    // of a borrowed environment are no such storage.
    private static BoundOrigin? CallLocalOrigin(BoundType type, FunctionKoto function, bool consuming)
    {
        if (CallLocal(type.Origin) is { } found)
        {
            return found;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (CallLocal(type.OriginArguments[i]) is { } argument)
            {
                return argument;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (type.Kind != BoundTypeKind.Function && CallLocalOrigin(type.Components[i], function, consuming) is { } part)
            {
                return part;
            }
        }

        return null;

        BoundOrigin? CallLocal(BoundOrigin? origin)
        {
            if (origin is null)
            {
                return null;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (CallLocal(origin.Operands[i]) is { } operand)
                    {
                        return operand;
                    }
                }

                return null;
            }

            return origin.Kind is OriginKind.Projection or OriginKind.Anchor && origin.Binder is { } binder &&
                (ReferenceEquals(binder, function) ? origin.Slot >= 0 || (consuming && origin.Slot <= EnvironmentSlot(0)) : IsWithin(binder, function)) ? origin : null;
        }
    }

    // The Borrow in a closure's result expression whose Origin is the call-local one, which the record names and relates.
    private static Koto? LocalBorrow(Koto value, BoundOrigin origin)
    {
        var finder = new LocalBorrowFinder(origin);
        finder.Visit(value);
        return finder.Found;
    }

    // SPEC 3, 10.7, 10.8: a written input that the anonymous function binds per call fits an expected input over any other Origin
    // with the same referent, fixed or an open region, by instantiating that call-time Origin; the header is compatible, not equal.
    private static bool InstantiatesPerCallInput(BoundType written, BoundType expected, FunctionKoto function, int position)
        => written is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, OriginArguments.Count: 0, Origin: { Kind: OriginKind.Input, Occurrence: null } origin } &&
            ReferenceEquals(origin.Binder, function) && origin.Slot == position &&
            expected is { Kind: BoundTypeKind.Semantics, Components.Count: 1, OriginArguments.Count: 0, Origin: not null } && expected.Semantics == written.Semantics &&
            ReferenceEquals(expected.Components[0], written.Components[0]);

    // SPEC 7.7.3, 8.10: the one closure argument whose minimum call receiver the Callable Constraint of its parameter does not permit;
    // none when no argument or several do. A common Function parameter has no Callable Constraint and is never named here.
    private ClosureReceiverRefutation? ClosureReceiverMismatch(InvocationKoto call, FunctionKoto function, int[] mapping)
    {
        ClosureReceiverRefutation? found = null;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var actual = call.ArgumentNodes[i].BoundType;
            var owner = actual is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? actual.Components[0] : actual;
            var pattern = function.Parameters[mapping[i]].Type.BoundType;
            if (owner is not { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { BoundClosure: { Receiver: not SemanticsKind.Ref } closure } } ||
                (pattern is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? pattern.Components[0] : pattern)?.Kind != BoundTypeKind.Parameter ||
                !this.TryCallable(pattern!, this.ConstraintScope(function), out _, out var required) || CallableReceiverFits(closure.Receiver, CallableReceiverMask(required)))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = new(mapping[i], closure.Receiver, required);
        }

        return found;
    }

    // With `openResult`, the signature's result is open (SPEC 10.8): only its parameter Types guide the body, whose result is inferred.
    private BoundType? BindClosureArgument(Koto argument, FunctionKoto closure, BindingScope scope, BoundType? signature, bool openResult = false)
    {
        // A Callable expectation supplies a body context, not an erasure target: F keeps the concrete Closure Type.
        var parent = this.BeginConsultation(closure);
        this.BindClosure(closure, this.NodeScope(closure, scope), signature, openResult);
        this.EndConsultation(closure, parent);
        return this.BindNode(argument, scope);
    }

    // SPEC 10.5, 15.6.4: a fixed expected signature names its fresh per-call inputs through its own Function Type's binder; an
    // anonymous function takes them as its own inputs, so the expectation is restated over the anonymous function's binder. Inputs
    // and results written over a fixed Origin of the enclosing body stay as written.
    private BoundType ClosureExpectation(FunctionKoto function, BoundType expected)
    {
        if (expected.Kind != BoundTypeKind.Function || !expected.CarriesOrigin || FunctionTypeBinder(expected) is not { } binder ||
            ReferenceEquals(binder, function))
        {
            return expected;
        }

        var count = expected.Components[0].Components.Count;
        var inputs = this.originScratch.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                inputs[i] = this.OriginAtom(function, OriginKind.Input, i);
            }

            return this.SubstituteStoredOrigins(expected, binder, default, inputs.AsSpan(0, count));
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
        }
    }

    // The anonymous function's header as written, with omitted parts taken from the fixed signature, is the found Type; a
    // different parameter count is stated by its count.
    private BoundType? FailClosureHeader(FunctionKoto function, BoundType expected)
    {
        if (expected.Kind != BoundTypeKind.Function)
        {
            // An expectation that is no Function Type, such as a Type parameter F under Callable (SPEC 7.2.3), names the
            // anonymous function as found.
            return this.RecordMismatch(function, function, "an anonymous function", expected);
        }

        var restated = this.ClosureExpectation(function, expected);
        var inputs = restated.Components[0];
        var count = ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count;
        if (function.Parameters.Count != count)
        {
            return this.RecordMismatch(function, function, function.Parameters.Count == 1 ? "an anonymous function with 1 parameter" : $"an anonymous function with {function.Parameters.Count} parameters", expected);
        }

        var scope = this.scopes[function];
        var types = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var parameter = function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType } ? inputs.Components[i] : this.BindType(function.Parameters[i].Type, scope);
                if (parameter is null)
                {
                    return this.Fail(function, BindingFailure.TypeMismatch);
                }

                types[i] = parameter;
            }

            var result = function.ReturnType is not null ? this.ClosureResultType(function, inputs, scope) : restated.Components[1];
            if (result is null)
            {
                return this.Fail(function, BindingFailure.TypeMismatch);
            }

            var parameters = count == 0 ? BoundType.Unit : this.types.Tuple(types.AsSpan(0, count));
            return this.FailMismatch(function, function, this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]), expected);
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
        }
    }

    private bool ClosureSignatureFits(FunctionKoto function, BoundType expected, bool openResult = false)
    {
        if (expected.Kind != BoundTypeKind.Function)
        {
            return false;
        }

        expected = this.ClosureExpectation(function, expected);

        var inputs = expected.Components[0];
        var count = ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count;
        if (function.Parameters.Count != count)
        {
            return false;
        }

        var scope = this.scopes[function];
        for (var i = 0; i < count; i++)
        {
            if (function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType })
            {
                continue; // SPEC 10.5: an omitted parameter Type takes the fixed expected input.
            }

            var type = this.BindType(function.Parameters[i].Type, scope);
            if (type is null || !(ReferenceEquals(type, inputs.Components[i]) || InstantiatesPerCallInput(type, inputs.Components[i], function, i)))
            {
                return false;
            }
        }

        return openResult || function.ReturnType is null || ReferenceEquals(this.ClosureResultType(function, inputs, scope), expected.Components[1]);
    }

    private BoundType? BindClosure(FunctionKoto function, BindingScope scope, BoundType? expected, bool openResult = false)
    {
        if (function.ClosureStorage is { } previous)
        {
            previous.Signature = null!; // BoundClosure.Of is this binding's definition, set once it is bound.
        }

        if (expected is not null && !this.ClosureSignatureFits(function, expected, openResult))
        {
            // SPEC 10.5, 23.1.2: the written header disagrees with the fixed signature; the omitted parameter Types that
            // signature would have supplied rest on this failure.
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred)
                {
                    this.CompleteDependent(inferred, function);
                }
            }

            return this.FailClosureHeader(function, expected);
        }

        // A fixed signature supplies header inference, never a different capture or ownership model. The ordinary
        // expected-Type adaptation erases the resulting concrete value only after its body and receiver are known.
        return this.BindConcreteClosure(function, scope, expected, openResult);
    }

    // SPEC 23.1: the `closure` display `call result`, the shorter end of a result that outlives its call.
    private BoundOrigin CallResultOrigin(FunctionKoto function) => this.OriginAtom(function, OriginKind.Projection, CallResultSlot);

    private BoundType? BindConcreteClosure(FunctionKoto function, BindingScope scope, BoundType? expected, bool openResult = false)
    {
        expected = expected is null ? null : this.ClosureExpectation(function, expected);
        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.Receiver = SemanticsKind.Ref;
        plan.ExclusiveUse = null;
        plan.ExclusiveReborrow = false;
        var symbol = this.symbols[function];
        // The declaration identity distinguishes environments with identical storage.
        plan.EnvironmentType = this.types.Intern(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, []);
        symbol.HeaderBound = true;
        var originDeclaration = this.BeginOriginDeclaration(function, scope);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = expected is not null && function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred
                ? Complete(inferred, expected.Components[0].Components[i]) : this.BindType(function.Parameters[i].Type, scope);
        }

        symbol.Type = function.ReturnType is { } annotation
            ? expected is null ? this.BindType(annotation, scope) : this.ClosureResultType(function, expected.Components[0], scope)
            : openResult ? null : expected?.Components[1];
        this.CompleteOriginDeclaration(originDeclaration);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = function.Parameters[i].Type.BoundType;
        }

        if (function.Captures is { } captures)
        {
            // SPEC 7.7.2: a repeated capture name and a collision with a parameter are judged from the written list, so a closure bound
            // again keeps the environment bindings of its earlier binding. The other entries are still bound, and the first that
            // fails on its own is reported beside the repeats (SPEC 23.1.2).
            var repeats = RepeatedCaptures(function, captures);
            foreach (var capture in captures)
            {
                if (repeats is not null && RepeatsEarlierEntry(repeats, capture))
                {
                    continue;
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source is not null && Receiver(source) is not null)
                {
                    // SPEC 7.7.2, 6.3, 16.3: an explicit capture obeys the construction and destruction restrictions, under
                    // which self is reached only through its Fields (Receiver).
                    return this.FailCaptureEntry(function, repeats, capture, BindingFailure.Capture, source.Type ?? BoundType.Unit, source);
                }

                if (source is not null && this.HelperCarrier(function, source) is { } helper)
                {
                    return this.FailHelperEntry(function, repeats, capture, source, helper); // SPEC 16.3.1: nor its helper borrow.
                }

                if (source is null || this.Capture(function, source, scope, capture) is not { } environment)
                {
                    // An entry whose binding failed derives from that failure and adds nothing to the repeats.
                    return source is { Type: null } ? repeats is null ? this.CompleteDependent(function, source.Declaration) : this.FailCaptureEntry(function, repeats, capture, BindingFailure.None, null, null)
                        : this.FailCaptureEntry(function, repeats, capture, BindingFailure.Capture, null, null);
                }

                environment.MutableCapture = capture.IsMutable;
                if (this.CaptureEntry(function, capture, source, environment) is { } failure)
                {
                    return this.FailCaptureEntry(function, repeats, capture, failure, source.Type!, source);
                }
            }

            if (repeats is not null)
            {
                return this.FailCaptureEntry(function, repeats, default, BindingFailure.None, null, null);
            }
        }

        if ((function.Body ?? function.ExpressionBody) is { } body)
        {
            if (symbol.Type is null)
            {
                // SPEC 14.9: the function owns its result context; nested functions and defer bodies keep their own boundaries.
                // Survey independent evidence before binding, then join the return operands with the single-item body's value,
                // which takes the surveyed Type as an if branch does, or with the indented body's structural end Unit.
                var context = this.BeginResult(function, scope, null);
                this.BindNode(body, scope, function.Body is null ? context.Expected : null);
                symbol.Type = this.FinishResult(function, context);
            }
            else
            {
                this.RequireType(body, scope, function.Body is null ? symbol.Type : null); // A single-item body has the written result.
            }
        }

        if (symbol.Type is null)
        {
            return Complete(function, null);
        }

        if (function.ReturnType is null && (expected is null || openResult))
        {
            symbol.Type = this.ResolveLocalResultOrigins(symbol.Type);
        }

        var buffer = this.typeScratch.Rent(Math.Max(function.Parameters.Count, plan.Storage.Count));
        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } input)
                {
                    return Complete(function, null);
                }

                buffer[i] = input;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.types.Tuple(((BoundType[])(object)buffer).AsSpan(0, function.Parameters.Count));
            plan.Signature = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, symbol.Type]);
            for (var i = 0; i < plan.Storage.Count; i++)
            {
                buffer[i] = plan.Storage[i].Environment.Type!;
            }

            plan.EnvironmentType = this.types.Intern(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, ((BoundType[])(object)buffer).AsSpan(0, plan.Storage.Count));
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }

        (this.closureEffects ??= new(this)).Classify(function, plan);
        if (function.ReturnType is null && (expected is null || openResult) && CallLocalOrigin(symbol.Type, function, plan.Receiver == SemanticsKind.Owner) is { } local)
        {
            // SPEC 15.8.2, 15.6.1: a result inferred from the body that borrows the call's own storage (a parameter, a body local,
            // or an environment binding the call consumes) cannot outlive the call; the relation is Refuted at that Borrow.
            return this.FailExplained(ref this.originRelations, function, BindingFailure.OriginRelation, new(function.ExpressionBody ?? function.Body ?? (Koto)function, local, this.CallResultOrigin(function), false, null, true));
        }

        return Complete(function, plan.EnvironmentType);
    }

    // One reusable visitor classifies each closure's receiver from the uses of its captures (SPEC 7.7.3); it never visits a
    // nested function's body, so one closure is classified at a time.
    private sealed class ClosureEffects(Binding binding) : KotoVisitor
    {
        private FunctionKoto function = null!;
        private BoundClosure plan = null!;

        public void Classify(FunctionKoto function, BoundClosure plan)
        {
            this.function = function;
            this.plan = plan;
            (function.Body as Koto ?? function.ExpressionBody)?.VisitChildren(this);
            if (function.ExpressionBody is { } bodyExpression)
            {
                this.Visit(bodyExpression);
            }
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto nested)
            {
                if (nested.BoundClosure is { } child)
                {
                    // Indexed over the storage list: enumerating the read-only interface boxes its enumerator on every pass.
                    for (var i = 0; i < child.Storage.Count; i++)
                    {
                        var capture = child.Storage[i];
                        if (!ReferenceEquals(capture.Source.Declaration, this.function))
                        {
                            continue;
                        }

                        // SPEC 7.7.2, 7.7.3: moving an outer environment value is Consuming; Reborrowing it or borrowing its slot
                        // exclusively needs exclusive access to the outer environment.
                        if (capture.Environment.CaptureAcquisition == CaptureAcquisition.Move)
                        {
                            this.plan.Receiver = SemanticsKind.Owner;
                        }
                        else if ((capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.ExclusiveSlotBorrow ||
                            (capture.Environment.CaptureAcquisition == CaptureAcquisition.Bare && capture.Source.Type is { } captured && binding.BareReborrows(captured, nested))) &&
                            this.plan.Receiver != SemanticsKind.Owner)
                        {
                            this.plan.Receiver = SemanticsKind.Uniq;
                            this.plan.ExclusiveUse ??= nested;
                        }
                    }
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && ReferenceEquals(symbol.Declaration, this.function))
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto ||
                    (use.Parent is IndexKoto index && ReferenceEquals(index.Left, use) && use.BoundType?.Kind == BoundTypeKind.FixedArray) ||
                    (use.Parent is MemberAccessKoto part && ReferenceEquals(part.Left, use) &&
                        (use.BoundType?.Kind == BoundTypeKind.Tuple || (AdtDef.IsStruct(use.BoundType) && part.BoundSymbol?.Property?.IsStored == true && !IsGetterResult(part)))))
                {
                    // Reading or updating a captured stored part obtains that part's authority, not a whole-value Move.
                    use = use.Parent;
                }

                var valueCall = use.Parent as InvocationKoto;
                var called = valueCall?.BoundValueCall;
                var receiver = called is not null && ReferenceEquals(called.CalleeValue, use);
                var memberCall = use.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
                    ReferenceEquals(member.Left, use) && ReferenceEquals(selected.Receiver, use) ? selected :
                    use.Parent is MemberAccessKoto property && ReferenceEquals(property.Left, use)
                        ? (binding.PropertyCall(property, PropertyAccessorKind.Set) ?? binding.PropertyCall(property, PropertyAccessorKind.Get))?.BoundCall : null;
                var memberOperation = memberCall?.ReceiverOperation ?? default;
                if (memberCall is not null && memberOperation.Source is null)
                {
                    // Accessor calls pass their receiver as an explicit argument; ordinary methods retain it separately.
                    foreach (var argument in memberCall.ArgumentOperations)
                    {
                        if (ReferenceEquals(argument.Source, use))
                        {
                            memberOperation = argument;
                            break;
                        }
                    }
                }

                var memberBorrow = memberOperation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection;
                // A Copy payload Field read lends the captured handle; it does not consume that handle (SPEC 3.4.3).
                if ((use.Parent is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, use)) ||
                    use.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Uniq) || (memberBorrow && memberOperation.ParameterType?.Semantics == SemanticsKind.Uniq) ||
                    this.UsesReferentExclusively(use) ||
                    (ReferenceEquals(use, node) && IsBareAcquisitionPosition(node) && node.BoundType is { } bare && binding.BareReborrows(bare, node)))
                {
                    if (this.plan.Receiver != SemanticsKind.Owner)
                    {
                        this.plan.Receiver = SemanticsKind.Uniq;
                        if (this.plan.ExclusiveUse is null)
                        {
                            this.plan.ExclusiveUse = node;
                            this.plan.ExclusiveReborrow = binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow;
                        }
                    }
                }
                else if (use.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Transfer } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Owner) || (memberCall is not null && memberOperation.Kind == ArgumentOperationKind.Value && !memberBorrow &&
                        use.BoundType is { } receiverType && binding.ProveCopy(receiverType, this.function) == ConstraintProof.Refuted))
                {
                    // SPEC 7.7.3, 13.5.3: transferring a capture out of the environment makes the call Consuming. A bare Non-Copy
                    // Place is never moved, so every other use, such as `items.length`, a shared element read, a bare `for` or
                    // `match` Subject or a borrowed argument, only borrows the capture; ownership rejects a Move out of a capture
                    // in a Shared or Exclusive call.
                    this.plan.Receiver = SemanticsKind.Owner;
                }
            }

            node.VisitChildren(this);
        }

        // SPEC 7.7.3: a capture used in a way that needs it, or the referent of a captured exclusive reference, exclusively:
        // Reborrowed exclusively (bare, at an expected uniq Type or as a uniq argument), or reached through Fields, elements or a
        // follow to a Place that is written, incremented, borrowed exclusively or used as an exclusive receiver. Such a body
        // mutates the environment or a captured referent, so the call is Exclusive.
        private bool UsesReferentExclusively(Koto use)
        {
            if (binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow && adaptation.Type.Semantics == SemanticsKind.Uniq)
            {
                return true;
            }

            if (use.Parent is InvocationKoto { BoundCall: { } call })
            {
                foreach (var argument in call.ArgumentOperations)
                {
                    if (ReferenceEquals(argument.Source, use) && argument.Kind is ArgumentOperationKind.Reborrow or ArgumentOperationKind.Borrow &&
                        argument.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq)
                    {
                        return true;
                    }
                }
            }

            var target = use;
            while (target.Parent is ParenthesizedKoto ||
                (target.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } selected && ReferenceEquals(selected.Left, target)) ||
                (target.Parent is MemberAccessKoto member && ReferenceEquals(member.Left, target)) ||
                (target.Parent is IndexKoto index && ReferenceEquals(index.Left, target)))
            {
                target = target.Parent;
            }

            if (ReferenceEquals(target, use))
            {
                return false;
            }

            return (target.Parent is BinaryKoto write && write.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(write.Left, target)) ||
                target.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                target.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ||
                (target.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selectedCall } } receiverAccess && ReferenceEquals(receiverAccess.Left, target) &&
                    ReferenceEquals(selectedCall.Receiver, target) && selectedCall.ReceiverOperation.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) ||
                (target.Parent is InvocationKoto { BoundValueCall: { ReceiverKind: SemanticsKind.Uniq } valueCall } && ReferenceEquals(valueCall.CalleeValue, target));
        }
    }

    // The first Borrow in a result expression whose Origin contains the call-local atom.
    private sealed class LocalBorrowFinder(BoundOrigin atom) : KotoVisitor
    {
        internal Koto? Found { get; private set; }

        public override void Visit(Koto node)
        {
            if (this.Found is not null)
            {
                return;
            }

            if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Origin: { } borrowed } && this.Contains(borrowed))
            {
                this.Found = node;
                return;
            }

            node.VisitChildren(this);
        }

        private bool Contains(BoundOrigin origin)
        {
            if (ReferenceEquals(origin, atom))
            {
                return true;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (this.Contains(origin.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

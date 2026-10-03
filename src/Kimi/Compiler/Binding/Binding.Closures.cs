// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Retained closure conversion and capture vocabulary.

public readonly record struct BoundCapture(BindingSymbol Source, BindingSymbol Environment);

/// <summary>A retained capture environment, signature and minimum call receiver.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType Signature { get; internal set; } = null!;

    public BoundType? EnvironmentType { get; internal set; }

    public SemanticsKind Receiver { get; internal set; } = SemanticsKind.Ref;

    internal List<BoundCapture> Storage { get; } = new();

    internal List<BindingSymbol> SymbolPool { get; } = new();
}

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    private bool ClosureSignatureFits(FunctionKoto function, BoundType expected)
    {
        if (expected.Kind != BoundTypeKind.Function || expected.CarriesOrigin)
        {
            return false;
        }

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
            if (type is null || !ReferenceEquals(type, inputs.Components[i]))
            {
                return false;
            }
        }

        return function.ReturnType is null || ReferenceEquals(this.BindType(function.ReturnType, scope), expected.Components[1]);
    }

    // SPEC 10.5: an anonymous function whose parameter and result Types are all written offers its header as call evidence
    // before its body is checked, so a generic parameter it fixes is inferred before selection.
    private BoundType? ClosureHeaderType(FunctionKoto function)
    {
        if (function.ReturnType is null || !this.scopes.TryGetValue(function, out var scope))
        {
            return null;
        }

        var scratch = this.RentTypes(function.Parameters.Count);
        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (this.BindType(function.Parameters[i].Type, scope) is not { } input)
                {
                    return null;
                }

                scratch[i] = input;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, scratch.AsSpan(0, function.Parameters.Count));
            return this.BindType(function.ReturnType, scope) is { } result ? this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, result]) : null;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    private BoundType? BindClosure(FunctionKoto function, BindingScope scope, BoundType? expected)
    {
        if (expected is null)
        {
            return this.BindConcreteClosure(function, scope);
        }

        if (!this.ClosureSignatureFits(function, expected))
        {
            return this.Fail(function, expected is null ? BindingFailure.Unsupported : BindingFailure.TypeMismatch);
        }

        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.EnvironmentType = null;
        plan.Receiver = SemanticsKind.Ref;
        plan.Signature = expected;
        var symbol = this.symbols[function];
        symbol.Type = expected.Components[1];
        symbol.HeaderBound = true;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = expected.Components[0].Components[i];
            if (function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred)
            {
                Complete(inferred, expected.Components[0].Components[i]);
            }
        }

        if (function.Captures is { } captures)
        {
            for (var i = 0; i < captures.Length; i++)
            {
                var capture = captures[i];
                var transfer = capture.Operation == Constants.MoveOperation;
                if (capture.IsMutable || (capture.Operation is not null && !transfer))
                {
                    return this.Fail(function, BindingFailure.Unsupported);
                }

                if (scope.Values.ContainsKey(capture.Name))
                {
                    return this.Fail(function, BindingFailure.Duplicate);
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source?.Type is { } captureType && !(ScalarTypes.Supports(captureType) || ReferenceEquals(captureType, BoundType.Unit)))
                {
                    return this.FailExplained(ref this.captureFailures, function, BindingFailure.Unsupported, (capture, captureType));
                }

                if (source is null || this.Capture(function, source, scope, capture) is not { } environment)
                {
                    return source is { Type: null } ? this.CompleteDependent(function, source.Declaration) : this.Fail(function, BindingFailure.Capture);
                }

                // SPEC 7.6.2: a bare capture Copies; a Non-Copy binding is transferred only by x@move.
                environment.CaptureAcquisition = transfer ? CaptureAcquisition.Move : CaptureAcquisition.Copy;
                if (!transfer && this.ProveCopy(source.Type!, function) != ConstraintProof.Proven)
                {
                    return this.Fail(function, BindingFailure.TransferRequired);
                }
            }
        }

        if (function.Body is { } block)
        {
            this.BindNode(block, scope);
        }
        else if (function.ExpressionBody is { } expression)
        {
            this.RequireType(expression, scope, symbol.Type);
        }

        return Complete(function, expected);
    }

    // An unsupported captured Type is reported at the written capture entry when there is one.
    private BindingSymbol? Capture(FunctionKoto function, BindingSymbol source, BindingScope scope, CaptureKoto? entry = null)
    {
        if (scope.Parent?.Function is { } outer && !ReferenceEquals(source.Scope.Function, outer))
        {
            // A nested closure can capture only what its immediately enclosing activation owns.
            // An inferred outer environment may forward the capture; an explicit list may not.
            if (!outer.IsAnonymous || outer.Captures is not null || this.Capture(outer, source, this.scopes[outer]) is not { } forwarded)
            {
                return null;
            }

            source = forwarded;
        }

        if (source.Type is null && source.Declaration is VariableKoto variable)
        {
            this.BindVariable(variable, source.Scope);
        }

        // Concrete environments preserve complete captured Types and dependencies.
        // Common-function erasure retains its independent Owned requirement.
        if (source.Kind is not (BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture) || source.Type is not { } type)
        {
            return null;
        }

        if (!(ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
                (function.ClosureStorage?.EnvironmentType is not null && (ReferenceEquals(type, BoundType.String) || type.Kind == BoundTypeKind.Closure ||
                    ReferenceTypes.IsStorage(type) || ObjectTypes.IsOwner(type)))))
        {
            if (entry is { } written)
            {
                this.FailExplained(ref this.captureFailures, function, BindingFailure.Unsupported, (written, type));
            }
            else
            {
                this.Fail(function, BindingFailure.Unsupported);
            }

            return null;
        }

        var plan = function.ClosureStorage!;
        for (var i = 0; i < plan.Storage.Count; i++)
        {
            if (ReferenceEquals(plan.Storage[i].Source, source))
            {
                return plan.Storage[i].Environment;
            }
        }

        var index = plan.Storage.Count;
        if (index == plan.SymbolPool.Count)
        {
            plan.SymbolPool.Add(new(source.Name, BindingSymbolKind.Capture, function, scope));
        }

        var environment = plan.SymbolPool[index];
        if (environment.Name != source.Name)
        {
            environment = new(source.Name, BindingSymbolKind.Capture, function, scope);
            plan.SymbolPool[index] = environment;
        }

        environment.Type = type;
        environment.Scope = scope;
        environment.Slot = index;
        environment.MutableCapture = false;
        plan.Storage.Add(new(source, environment));
        scope.Values[source.Name] = environment;
        return environment;
    }

    // SPEC 7.6.2: each explicit entry initializes one environment binding exactly as `let x = x` or `let x = x@op` would.
    // A bare entry Copies a Copy binding and Reborrows a binding storing an exclusive reference; `x@ref` and `x@uniq`
    // borrow the outer binding's slot, adding a reference layer, and an exclusive slot borrow needs a writable slot.
    private BindingFailure? CaptureEntry(FunctionKoto function, CaptureKoto capture, BindingSymbol source, BindingSymbol environment)
    {
        var type = source.Type!;
        switch (capture.Operation)
        {
            case Constants.MoveOperation:
                environment.CaptureAcquisition = CaptureAcquisition.Move;
                return null;
            case null when this.ProveCopy(type, function) == ConstraintProof.Proven:
                environment.CaptureAcquisition = CaptureAcquisition.Copy;
                return null;
            case null when type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 }:
                environment.CaptureAcquisition = CaptureAcquisition.Reborrow;
                return null;
            case null:
                return BindingFailure.TransferRequired;
        }

        var exclusive = capture.Operation == Constants.UniqKeyword;
        if (exclusive && !(source.MutableCapture || IsMutableDeclaration(source.Declaration)))
        {
            return BindingFailure.InvalidAssignment; // A let binding's slot grants no Write (SPEC 15.1.5).
        }

        var binder = source.Declaration ?? function;
        environment.Type = this.InternType(BoundTypeKind.Semantics, null, exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, [type], origin: this.OriginAtom(binder, OriginKind.Projection, source.Slot));
        environment.CaptureAcquisition = exclusive ? CaptureAcquisition.ExclusiveSlotBorrow : CaptureAcquisition.SharedSlotBorrow;
        return null;
    }

    private BoundType? BindConcreteClosure(FunctionKoto function, BindingScope scope)
    {
        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.Receiver = SemanticsKind.Ref;
        var symbol = this.symbols[function];
        // The declaration identity distinguishes environments with identical storage.
        plan.EnvironmentType = this.InternType(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, []);
        symbol.Type = function.ReturnType is { } annotation ? this.BindType(annotation, scope) : null;
        symbol.HeaderBound = true;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = this.BindType(function.Parameters[i].Type, scope);
        }

        if (function.Captures is { } captures)
        {
            foreach (var capture in captures)
            {
                if (scope.Values.ContainsKey(capture.Name))
                {
                    return this.Fail(function, BindingFailure.Duplicate);
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source is null || this.Capture(function, source, scope, capture) is not { } environment)
                {
                    return source is { Type: null } ? this.CompleteDependent(function, source.Declaration) : this.Fail(function, BindingFailure.Capture);
                }

                environment.MutableCapture = capture.IsMutable;
                if (this.CaptureEntry(function, capture, source, environment) is { } failure)
                {
                    return this.FailExplained(ref this.captureFailures, function, failure, (capture, source.Type!));
                }
            }
        }

        if (function.Body is { } block)
        {
            // Unannotated block results still need the general result-inference pass.
            if (symbol.Type is null)
            {
                return this.Fail(function, BindingFailure.Unsupported);
            }

            this.BindNode(block, scope);
        }
        else if (function.ExpressionBody is { } expression)
        {
            var result = this.RequireType(expression, scope, symbol.Type);
            symbol.Type ??= result;
        }

        if (symbol.Type is null)
        {
            return Complete(function, null);
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

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, ((BoundType[])(object)buffer).AsSpan(0, function.Parameters.Count));
            plan.Signature = this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, symbol.Type]);
            for (var i = 0; i < plan.Storage.Count; i++)
            {
                buffer[i] = plan.Storage[i].Environment.Type!;
            }

            plan.EnvironmentType = this.InternType(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, ((BoundType[])(object)buffer).AsSpan(0, plan.Storage.Count));
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }

        (this.closureEffects ??= new(this)).Classify(function, plan);
        return Complete(function, plan.EnvironmentType);
    }

    // One reusable visitor classifies each closure's receiver from the uses of its captures (SPEC 7.6.3); it never visits a
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

                        // SPEC 7.6.2, 7.6.3: moving an outer environment value is Consuming; Reborrowing it or borrowing its slot
                        // exclusively needs exclusive access to the outer environment.
                        if (capture.Environment.CaptureAcquisition == CaptureAcquisition.Move && binding.ProveCopy(capture.Source.Type!, this.function) == ConstraintProof.Refuted)
                        {
                            this.plan.Receiver = SemanticsKind.Owner;
                        }
                        else if (capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.ExclusiveSlotBorrow && this.plan.Receiver != SemanticsKind.Owner)
                        {
                            this.plan.Receiver = SemanticsKind.Uniq;
                        }
                    }
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && ReferenceEquals(symbol.Declaration, this.function))
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto parentheses)
                {
                    use = parentheses;
                }

                var valueCall = use.Parent as InvocationKoto;
                var called = valueCall?.BoundValueCall;
                var receiver = called is not null && ReferenceEquals(called.Receiver, use);
                var memberCall = use.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
                    ReferenceEquals(member.Left, use) && ReferenceEquals(selected.Receiver, use) ? selected : null;
                var memberBorrow = memberCall?.ReceiverOperation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection;
                var exclusiveReference = symbol.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 };
                if ((use.Parent is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, use)) ||
                    use.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Uniq) || (memberBorrow && memberCall!.ReceiverOperation.ParameterType?.Semantics == SemanticsKind.Uniq) ||
                    (exclusiveReference && this.UsesReferentExclusively(use)))
                {
                    if (this.plan.Receiver != SemanticsKind.Owner)
                    {
                        this.plan.Receiver = SemanticsKind.Uniq;
                    }
                }
                else if (use.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Transfer } ||
                    (!exclusiveReference && binding.ProveCopy(symbol.Type!, this.function) == ConstraintProof.Refuted && use.Parent is not ConversionKoto &&
                    !(receiver && called!.ReceiverKind == SemanticsKind.Ref) && !memberBorrow && !InspectedString(use)))
                {
                    // SPEC 7.6.3: transferring a capture out of the environment makes the call Consuming.
                    this.plan.Receiver = SemanticsKind.Owner;
                }
            }

            node.VisitChildren(this);
        }

        private static bool InspectedString(Koto use)
        {
            if (!ReferenceEquals(use.BoundType, BoundType.String))
            {
                return false;
            }

            if (use.Parent is BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals })
            {
                return true;
            }

            if (use.Parent is InvocationKoto { BoundCall: { } call })
            {
                foreach (var argument in call.ArgumentOperations)
                {
                    if (ReferenceEquals(argument.Source, use) && argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // SPEC 7.6.3: a captured exclusive reference used in a way that needs its referent exclusively: Reborrowed exclusively
        // (bare, at an expected uniq Type or as a uniq argument), or followed to a Place that is written, incremented, borrowed
        // exclusively or used as an exclusive receiver. Such a body mutates a captured referent, so the call is Exclusive.
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
                    ReferenceEquals(selectedCall.Receiver, target) && selectedCall.ReceiverOperation.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq);
        }
    }
}

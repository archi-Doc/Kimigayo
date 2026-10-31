// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Closure literals (SPEC 7.7, 10.5, 10.8, 15.3.4): an anonymous function bound as a Closure value.
// - Responsibility: the literal's header against a fixed expected signature (ClosureExpectation restates it over the literal's
//   binder; ClosureSignatureFits, InstantiatesPerCallInput, FailClosureHeader; a written result rebound per fixed context,
//   ClosureResultType); its capture entries, body and result, and the published Signature and EnvironmentType
//   (BindConcreteClosure); a Closure argument bound under a consultation (BindClosureArgument); for TryCandidate, the one Closure
//   argument whose receiver a candidate's Callable Constraint does not permit (ClosureReceiverMismatch, permitClosureReceivers).
// - Inputs: the literal, its scope, an expected Type and whether its result is open, the capture sources it looks up; capture
//   formation (Binding.Captures.cs), the receiver classification (Binding.ClosureReceivers.cs) and the call-local result test
//   (Binding.ClosureResults.cs).
// - Outputs: the literal's Type (its EnvironmentType) or its failure; BoundClosure's Storage, Signature, EnvironmentType and
//   EvaluatingBody; a ClosureReceiverRefutation; the located Unsupported_Kd of each use the classification leaves unlisted.
// - Invariants: BindClosure clears the published Signature and Classified before a binding, and the pending relations an earlier
//   binding formed in its body; BindConcreteClosure starts from reset receiver facts (empty Storage, Shared, no exclusive cause) and
//   has them classified right after the body is bound, before a failed result or parameter Type returns, its pending relations judged
//   then (Binding.OriginProof.cs); a header probe binds no parameter, visits no body and restores the enclosing probe on exit.
// - Prohibitions: no receiver classification, capture formation or capture record here (Binding.ClosureReceivers.cs,
//   Binding.Captures.cs, Binding.CaptureDiagnostics.cs).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Retained closure conversion and capture vocabulary.

public sealed partial class Binding
{
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
            if (BoundClosure.Of(owner) is not { Receiver: not SemanticsKind.Ref } closure ||
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
        if (expected.Kind != BoundTypeKind.Function || !expected.CarriesAnyOrigin || FunctionTypeBinder(expected) is not { } binder ||
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
        // SPEC 8.11.1: a literal's effect items are judged as every function's are (BindEffectBound), on every path; a literal
        // declares no bound.
        for (var i = 0; i < function.EffectBounds.Count; i++)
        {
            this.BindNode(function.EffectBounds[i], scope);
        }

        if (function.ClosureStorage is { } previous)
        {
            previous.Signature = null!; // BoundClosure.Of is this binding's definition, set once it is bound.
            previous.Classified = false; // And its receiver facts this binding's, once it has classified them.
            this.LeavePendingRelations(function); // The pending relations of an earlier binding leave with it (R4-M126 §2.5 (d)).
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

    private BoundType? BindConcreteClosure(FunctionKoto function, BindingScope scope, BoundType? expected, bool openResult = false)
    {
        expected = expected is null ? null : this.ClosureExpectation(function, expected);
        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.Receiver = plan.CaseFreeReceiver = SemanticsKind.Ref;
        plan.ExclusiveUse = null;
        plan.ExclusiveReborrow = false;
        var symbol = this.symbols[function];
        plan.EvaluatingBody = this.formingBody; // SPEC 15.3.4: the header and captures are the evaluating body's (DECISIONS R4-M101 E1).
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

                if (source is null || this.Capture(function, source, scope, capture) is not { } environment || ReferenceEquals(environment, source))
                {
                    // An entry whose binding failed derives from that failure (its TypeSource) and adds nothing to the repeats.
                    return source is { Type: null } ? repeats is null ? this.CompleteDependent(function, TypeSource(source) ?? source.Declaration) : this.FailCaptureEntry(function, repeats, capture, BindingFailure.None, null, null)
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
            var saved = this.EnterBody(new(symbol)); // The body and its result are the Closure's own.
            try
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
            finally
            {
                this.formingBody = saved;
            }
        }

        // SPEC 7.7.3: the receiver is classified from the bound body on every path that bound it, a failed result or parameter
        // included; classification reads neither the signature nor the environment Type. A use of a pair-layer capture that no consumer
        // takes, which could leave a case Shared, is a located Unsupported_Kd (the owner's net, DECISIONS R4-M145). The
        // relations that waited for the classification are judged then (SPEC 15.8.2, 23.1.2; DECISIONS R4-M126 §2.5).
        var effects = this.closureEffects ??= new(this);
        effects.Classify(function, plan);
        foreach (var (use, _) in effects.Unlisted)
        {
            this.Fail(use, BindingFailure.Unsupported);
        }

        this.JudgePendingRelations(function);
        if (symbol.Type is null)
        {
            return Complete(function, null);
        }

        // SPEC 15.8.2 (DECISIONS R4-M126 P8, R4-M152): an inferred result is published once the receiver is classified.
        var publishes = function.ReturnType is null && (expected is null || openResult);
        var closureBody = new ExecutableBody(symbol);
        if (publishes)
        {
            symbol.Type = this.PublishedResult(function, plan.Receiver, closureBody, this.ResolveLocalResultOrigins(symbol.Type));
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

            plan.EnvironmentType = this.ClosureType(symbol, function, plan.Signature, ((BoundType[])(object)buffer).AsSpan(0, plan.Storage.Count));
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }

        if (publishes)
        {
            this.FitResultSources(function, closureBody, symbol.Type); // A returned value over the call's own storage is Refuted where it returns.
        }

        return Complete(function, plan.EnvironmentType);
    }
}

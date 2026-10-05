// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

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

    // The first capture use that made the call Exclusive, and whether it Reborrows a captured exclusive reference (SPEC 7.6.3).
    internal Koto? ExclusiveUse { get; set; }

    internal bool ExclusiveReborrow { get; set; }

    internal List<BoundCapture> Storage { get; } = new();

    internal List<BindingSymbol> SymbolPool { get; } = new();
}

// A capture name repeated by a later entry or by a parameter (Parameter), at Later, and the entry it repeats, at Earlier (SPEC 7.6.2).
internal readonly record struct CaptureRepeat(SourceSpan Later, SourceSpan Earlier, string Name, bool Parameter);

// The repeats of one capture list, and the first other entry that failed independently of them (SPEC 23.3.6.4): its failure, none
// when every other entry is valid, and the Type its facts name, null for an entry without facts, such as an unresolved name.
internal readonly record struct CaptureListFailure(CaptureRepeat[] Repeats, CaptureKoto Entry, BindingFailure Failure, BoundType? Type, BindingSymbol? Source);

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    // Set only while TryCandidate asks whether a rejected candidate applies once its closure argument's minimum receiver is permitted.
    private bool permitClosureReceivers;

    // Only closures with a repeated capture name need this storage: each later entry or parameter, the entry it repeats, and an
    // independent failure of another entry.
    private Dictionary<Koto, CaptureListFailure>? captureRepeats;

    // SPEC 7.6.2: each capture name that an earlier entry repeats, then each that a parameter repeats; null for a valid list, which
    // allocates nothing.
    private static CaptureRepeat[]? RepeatedCaptures(FunctionKoto function, CaptureKoto[] captures)
    {
        List<CaptureRepeat>? repeats = null;
        for (var i = 0; i < captures.Length; i++)
        {
            var name = captures[i].Name;
            var repeated = false;
            for (var j = 0; j < i && !repeated; j++)
            {
                if (captures[j].Name == name)
                {
                    (repeats ??= new()).Add(new(captures[i].Span, captures[j].Span, name, false));
                    repeated = true;
                }
            }

            for (var p = 0; p < function.Parameters.Count && !repeated; p++)
            {
                if (function.Parameters[p].InternalName == name)
                {
                    (repeats ??= new()).Add(new(function.Parameters[p].ExternalNameSpan, captures[i].Span, name, true));
                    repeated = true;
                }
            }
        }

        return repeats?.ToArray();
    }

    // The later entry of a repeated name declares nothing, so the list binds only the entry it repeats.
    private static bool RepeatsEarlierEntry(CaptureRepeat[] repeats, CaptureKoto capture)
    {
        for (var i = 0; i < repeats.Length; i++)
        {
            if (!repeats[i].Parameter && repeats[i].Later == capture.Span)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 7.6.2: the receiver `self` of a named function, accessor, constructor or destructor, and a setter's `value`.
    private static bool IsContextualBinding(BindingSymbol symbol)
        => symbol.Kind == BindingSymbolKind.Parameter && symbol.Declaration switch
        {
            FunctionKoto { IsAnonymous: false } declaring => symbol.Name == "self" || (symbol.Name == "value" && declaring.Accessor?.Kind == PropertyAccessorKind.Set),
            PropertyAccessorKoto accessor => symbol.Name == "self" || (symbol.Name == "value" && accessor.AccessorKind == PropertyAccessorKind.Set),
            _ => false,
        };

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

    // SPEC 7.6.2, 23.3.6.4: a repeated capture name is one problem at the later entry or parameter, with the entry it repeats related.
    // Another entry that failed on its own is reported beside the repeats, exactly as it is in a list that repeats no name.
    private void ReportCaptureRepeats(Koto function, CaptureListFailure list, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var repeats = list.Repeats;
        if (list.Failure != BindingFailure.None)
        {
            var entryRequirement = DiagnosticRequirement.Binding(list.Failure);
            var entryCode = list.Failure switch
            {
                BindingFailure.TransferRequired => DiagnosticCode.TransferRequired_Kd,
                BindingFailure.InvalidAssignment => DiagnosticCode.InvalidAssignment_Kd,
                _ => DiagnosticCode.InvalidCaptureBinding_Kd,
            };
            if (list.Type is { } type)
            {
                this.ReportCaptureEntry(function, list.Entry, type, list.Source, entryRequirement, entryCode);
            }
            else
            {
                function.Report(entryRequirement, entryCode);
            }
        }

        for (var i = 0; i < repeats.Length; i++)
        {
            var repeat = repeats[i];
            var name = repeat.Name;
            var note = repeat.Parameter
                ? $"The capture entry {name} and the parameter {name} would both declare {name} in the anonymous function's body; a parameter cannot repeat a capture name (SPEC 7.6.2)"
                : $"The capture list names {name} twice; each entry declares its own environment binding, so a name is captured once (SPEC 7.6.2)";
            function.Report(
                requirement,
                code,
                note: note,
                evidence: [name],
                advice: repeat.Parameter ? $"Rename the parameter, or remove the capture entry {name} if the body needs only the argument" : $"Remove the repeated entry {name}",
                span: repeat.Later,
                relatedSpans: [("declaration", function, repeat.Earlier, "capture entry")],
                condition: (ushort)i);
        }
    }

    // A capture entry that failed (its facts name the Type, or none for an unresolved name), or, in a list that repeats a name, the
    // repeats with that entry's failure beside them.
    private BoundType? FailCaptureEntry(FunctionKoto function, CaptureRepeat[]? repeats, CaptureKoto capture, BindingFailure failure, BoundType? type, BindingSymbol? source)
        => repeats is not null ? this.FailExplained(ref this.captureRepeats, function, BindingFailure.Duplicate, new CaptureListFailure(repeats, capture, failure, type, source))
            : type is not null ? this.FailExplained(ref this.captureFailures, function, failure, (capture, type, source)) : this.Fail(function, failure);

    // SPEC 7.6.3, 8.6: the one closure argument whose minimum call receiver the Callable Constraint of its parameter does not permit;
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

            var result = function.ReturnType is { } written ? this.BindType(written, scope) : restated.Components[1];
            if (result is null)
            {
                return this.Fail(function, BindingFailure.TypeMismatch);
            }

            var parameters = count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, types.AsSpan(0, count));
            return this.FailMismatch(function, function, this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]), expected);
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

        return openResult || function.ReturnType is null || ReferenceEquals(this.BindType(function.ReturnType, scope), expected.Components[1]);
    }

    private BoundType? BindClosure(FunctionKoto function, BindingScope scope, BoundType? expected, bool openResult = false)
    {
        if (expected is not null && !this.ClosureSignatureFits(function, expected, openResult))
        {
            // SPEC 10.5, 23.3.6.4: the written header disagrees with the fixed signature; the omitted parameter Types that
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

    // An unsupported captured Type is reported at the written capture entry when there is one.
    private BindingSymbol? Capture(FunctionKoto function, BindingSymbol source, BindingScope scope, CaptureKoto? entry = null)
    {
        if (entry is null && IsContextualBinding(source))
        {
            return null; // SPEC 7.6.2: contextual self and a setter's value are never captured implicitly.
        }

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

        // SPEC 7.6.2: an environment holds any Type with slot storage, including enums (Option, Result), Arrays, Dictionaries,
        // Slices and raw pointers.
        if (!(ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
                (function.ClosureStorage?.EnvironmentType is not null && (type.Kind == BoundTypeKind.Parameter || SlotTypes.IsResult(type) ||
                    ReferenceTypes.IsStorage(type) || ReferenceTypes.IsPointer(type)))))
        {
            if (entry is { } written)
            {
                this.FailExplained(ref this.captureFailures, function, BindingFailure.Unsupported, (written, type, null));
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

    // SPEC 23.3.6.5: the `closure` display `call result`, the shorter end of a result that outlives its call.
    private BoundOrigin CallResultOrigin(FunctionKoto function) => this.OriginAtom(function, OriginKind.Projection, CallResultSlot);

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
        environment.Type = this.InternType(BoundTypeKind.Semantics, null, exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, [type], origin: this.OriginAtom(binder, OriginKind.Projection, SymbolOriginSlot(source)));
        environment.CaptureAcquisition = exclusive ? CaptureAcquisition.ExclusiveSlotBorrow : CaptureAcquisition.SharedSlotBorrow;
        return null;
    }

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
        plan.EnvironmentType = this.InternType(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, []);
        symbol.Type = function.ReturnType is { } annotation ? this.BindType(annotation, scope) : openResult ? null : expected?.Components[1];
        symbol.HeaderBound = true;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = expected is not null && function.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred
                ? Complete(inferred, expected.Components[0].Components[i]) : this.BindType(function.Parameters[i].Type, scope);
        }

        if (function.Captures is { } captures)
        {
            // SPEC 7.6.2: a repeated capture name and a collision with a parameter are judged from the written list, so a closure bound
            // again keeps the environment bindings of its earlier binding. The other entries are still bound, and the first that
            // fails on its own is reported beside the repeats (SPEC 23.3.6.4).
            var repeats = RepeatedCaptures(function, captures);
            foreach (var capture in captures)
            {
                if (repeats is not null && RepeatsEarlierEntry(repeats, capture))
                {
                    continue;
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source is { Kind: BindingSymbolKind.Parameter, Name: "self", Declaration: FunctionKoto { IsConstructor: true } or FunctionKoto { IsDestructor: true } })
                {
                    // SPEC 7.6.2, 6.2.3, 16.3: an explicit capture obeys the construction and destruction restrictions, under
                    // which self is reached only through its Fields.
                    return this.FailCaptureEntry(function, repeats, capture, BindingFailure.Capture, source.Type ?? BoundType.Unit, source);
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
        if (function.ReturnType is null && (expected is null || openResult) && CallLocalOrigin(symbol.Type, function, plan.Receiver == SemanticsKind.Owner) is { } local)
        {
            // SPEC 15.8.2, 15.6.1: a result inferred from the body that borrows the call's own storage (a parameter, a body local,
            // or an environment binding the call consumes) cannot outlive the call; the relation is Refuted at that Borrow.
            return this.FailExplained(ref this.originRelations, function, BindingFailure.OriginRelation, new(function.ExpressionBody ?? function, local, this.CallResultOrigin(function), false, null, true));
        }

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
                        if (capture.Environment.CaptureAcquisition == CaptureAcquisition.Move)
                        {
                            this.plan.Receiver = SemanticsKind.Owner;
                        }
                        else if (capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.ExclusiveSlotBorrow && this.plan.Receiver != SemanticsKind.Owner)
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
                        (use.BoundType?.Kind == BoundTypeKind.Tuple || (StructStorage.IsStruct(use.BoundType) && part.BoundSymbol?.Property?.IsStored == true && !IsGetterResult(part)))))
                {
                    // Reading or updating a captured stored part obtains that part's authority, not a whole-value Move.
                    use = use.Parent;
                }

                var valueCall = use.Parent as InvocationKoto;
                var called = valueCall?.BoundValueCall;
                var receiver = called is not null && ReferenceEquals(called.Receiver, use);
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
                // A Copy payload Field read lends the captured handle; it does not consume that handle (SPEC 3.4.1).
                var fieldRead = ObjectTypes.HandleMode(use.BoundType) is not null && use.Parent is MemberAccessKoto field &&
                    ReferenceEquals(field.Left, use) && field.BoundSymbol?.Property?.IsStored == true &&
                    field.BoundType is { } fieldType && binding.ProveCopy(fieldType, this.function) == ConstraintProof.Proven && !this.UsesReferentExclusively(field);
                var exclusiveReference = use.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 };
                if ((use.Parent is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, use)) ||
                    use.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Uniq) || (memberBorrow && memberOperation.ParameterType?.Semantics == SemanticsKind.Uniq) ||
                    this.UsesReferentExclusively(use))
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
                    // SPEC 7.6.3, 13.5.3: transferring a capture out of the environment makes the call Consuming. A bare Non-Copy
                    // Place is never moved, so every other use, such as `items.length`, a shared element read, a bare `for` or
                    // `match` Subject or a borrowed argument, only borrows the capture; ownership rejects a Move out of a capture
                    // in a Shared or Exclusive call.
                    this.plan.Receiver = SemanticsKind.Owner;
                }
            }

            node.VisitChildren(this);
        }

        private static bool InspectedValue(Koto use)
        {
            if (ReferenceEquals(use.BoundType, BoundType.String) && use.Parent is BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals })
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

        // SPEC 7.6.3: a capture used in a way that needs it, or the referent of a captured exclusive reference, exclusively:
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
                    ReferenceEquals(selectedCall.Receiver, target) && selectedCall.ReceiverOperation.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq);
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

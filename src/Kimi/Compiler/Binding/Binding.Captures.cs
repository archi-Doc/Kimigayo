// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture formation (SPEC 7.7.2): which binding a capture entry or a use in an omitted-list Closure captures, the environment binding
// it initializes and how (CaptureAcquisition), and the repeated names of a written list. A failed capture's facts and record belong to
// Binding.CaptureDiagnostics; a Closure's conversion and signature to Binding.Closures, its call receiver to Binding.ClosureReceivers.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The repeat fact beside the capture formation that produces it.

// A capture name repeated by a later entry or by a parameter (Parameter), at Later, and the entry it repeats, at Earlier (SPEC 7.7.2).
internal readonly record struct CaptureRepeat(SourceSpan Later, SourceSpan Earlier, string Name, bool Parameter);

public sealed partial class Binding
{
    // SPEC 7.7.2: each capture name that an earlier entry repeats, then each that a parameter repeats; null for a valid list, which
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

    // SPEC 7.7.2: the receiver `self` of a named function, accessor, constructor or destructor, and a setter's `value`.
    private static bool IsContextualBinding(BindingSymbol symbol)
        => symbol.Kind == BindingSymbolKind.Parameter && symbol.Declaration switch
        {
            FunctionKoto { IsAnonymous: false } declaring => symbol.Name == "self" || (symbol.Name == "value" && declaring.Accessor?.Kind == PropertyAccessorKind.Set),
            PropertyAccessorKoto accessor => symbol.Name == "self" || (symbol.Name == "value" && accessor.AccessorKind == PropertyAccessorKind.Set),
            _ => false,
        };

    // The environment binding of `source` in `function`, null when it cannot hold it, or `source` itself when its Type failed. An
    // unsupported captured Type is reported at the written capture entry when there is one, resting on the projection use that formed
    // it (BU-61).
    private BindingSymbol? Capture(FunctionKoto function, BindingSymbol source, BindingScope scope, CaptureKoto? entry = null)
    {
        if (entry is null && IsContextualBinding(source))
        {
            return null; // SPEC 7.7.2: contextual self and a setter's value are never captured implicitly.
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

        if (source.Kind is not (BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture))
        {
            return null;
        }

        // SPEC 23.1.2: no environment holds a binding whose Type failed; a use of the source itself rests on its declaration.
        if (source.Type is not { } type)
        {
            return source;
        }

        // Concrete environments preserve complete captured Types and dependencies.
        // Common-function erasure retains its independent Owned requirement.
        // SPEC 7.7.2: an environment holds any Type with slot storage, including enums (Option, Result), Arrays, Dictionaries,
        // Slices, object Views and raw pointers. All safe borrows use the same representation boundary.
        if (!(ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
                (function.ClosureStorage?.EnvironmentType is not null && (type.Kind == BoundTypeKind.Parameter || SlotTypes.IsResult(type) ||
                    ReferenceTypes.IsBorrow(type) || ReferenceTypes.IsPointer(type)))))
        {
            // DECISIONS BU-61 (SPEC 23.1.2): a projection whose use's conformance check rejects it names no Type, so the failure rests
            // on the use judged on the closure's own scope chain. Only the closure's first failure records it, so an earlier independent
            // failure stays direct.
            if (function.BindingFailure == BindingFailure.None && this.ProjectionUseOf(type, this.ConstraintScope(function)) is { } use)
            {
                this.AddPendingPrerequisite(function, use);
            }

            if (entry is { } written)
            {
                this.FailExplained(ref this.unsupportedSpans, function, BindingFailure.Unsupported, written.Span);
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

    // SPEC 7.7.2: each explicit entry initializes one environment binding exactly as `let x = x` or `let x = x@op` would.
    // A bare entry Copies a Copy binding and Reborrows a binding storing an exclusive reference; `x@ref` and `x@uniq`
    // borrow the outer binding's slot, adding a reference layer, and an exclusive slot borrow needs a writable slot. A bare entry that
    // Reborrows, in every or some admitted case, an enclosing Closure's environment binding `u: uniq/T during o` is contained in that
    // Closure's call receiver too, so its binding has `uniq/T during meet(o, ρ)`, a pair layer that meet on its outer-Origin slot
    // (DECISIONS R4-M123's 7.7.2, R4-M126 P4); an enclosing local's or parameter's keeps its Type (BU-28).
    private BindingFailure? CaptureEntry(FunctionKoto function, CaptureKoto capture, BindingSymbol source, BindingSymbol environment)
    {
        var type = source.Type!;
        switch (capture.Operation)
        {
            case Constants.MoveOperation:
                environment.CaptureAcquisition = CaptureAcquisition.Move;
                return null;
            case null:
                // SPEC 8.14: by case, each case run and instance acquires as its case Type does.
                var bare = this.BareAcquire(type, function, out _);
                if (bare == HirAcquire.Move)
                {
                    return BindingFailure.TransferRequired;
                }

                environment.CaptureAcquisition = bare == HirAcquire.Copy ? CaptureAcquisition.Copy : bare == HirAcquire.ByCase ? CaptureAcquisition.Bare : CaptureAcquisition.Reborrow;
                if ((bare == HirAcquire.Reborrow || (bare == HirAcquire.ByCase && this.BareReborrows(type, function))) &&
                    this.EnvironmentContainment(source) is { } receiver && this.OuterOrigin(type) is { } outer)
                {
                    environment.Type = this.types.Rebuild(type, (BoundType[])type.Components, this.Meet(outer, receiver), (BoundOrigin[])type.OriginArguments);
                }

                return null;
        }

        var exclusive = capture.Operation == Constants.UniqKeyword;
        if (exclusive && !Reassignable(source))
        {
            return BindingFailure.InvalidAssignment; // A let binding's slot grants no Write (SPEC 15.1.5).
        }

        var binder = source.Declaration ?? function;
        environment.Type = this.types.Intern(BoundTypeKind.Semantics, null, exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, [type], origin: this.OriginAtom(binder, OriginKind.Projection, SymbolOriginSlot(source)));
        environment.CaptureAcquisition = exclusive ? CaptureAcquisition.ExclusiveSlotBorrow : CaptureAcquisition.SharedSlotBorrow;
        return null;
    }

    // SPEC 7.7.2: a use, in an omitted-list Closure, of a binding of an enclosing activation captures it as the Closure's environment
    // binding; null when no environment holds it (a named function, an explicit list, a binding Capture refuses, or a value that carries
    // the helper borrow, whose use fails as a contextual binding's does, SPEC 16.3.1); the binding itself when its Type failed.
    private BindingSymbol? CaptureOuterUse(Koto use, BindingSymbol symbol, BindingScope scope)
        => scope.Function is { IsAnonymous: true, Captures: null } closure && !this.FailHelperUse(closure, symbol, use)
            ? this.Capture(closure, symbol, this.scopes[closure]) : null;

    // SPEC 16.3.1: an omitted list's use of a value that carries the helper borrow fails before the Closure's environment holds it.
    private bool FailHelperUse(FunctionKoto closure, BindingSymbol source, Koto use)
    {
        if (this.HelperCarrier(closure, source) is not { } helper)
        {
            return false;
        }

        this.FailExplained(ref this.captureFailures, use, BindingFailure.Capture, this.HelperCaptureFact(use.Span, source, helper));
        return true;
    }
}

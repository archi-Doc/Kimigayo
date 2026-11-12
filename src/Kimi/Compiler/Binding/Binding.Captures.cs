// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture formation (SPEC 7.7.2): which binding a capture entry or a use in an omitted-list Closure captures, the environment binding
// it initializes and how (CaptureAcquisition), and the repeated names of a written list. A failed capture's facts and record belong to
// Binding.CaptureDiagnostics; a Closure's conversion and signature to Binding.Closures, its call receiver to Binding.ClosureReceivers.
// Interim (PLAN G721; Capture): a captured pair layer other than the original `s/T` whose plan Reborrows in some case (until PLAN G254's
// unit), and a captured pair target or projection under an identity premise (until PLAN G170's query), are a located Unsupported_Kd at
// a written entry; an omitted list's use of one reports InvalidCaptureBinding_Kd instead.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The repeat fact beside the capture formation that produces it.

// A capture name repeated by a later entry or by a parameter (Parameter), at Later, and the entry it repeats, at Earlier (SPEC 7.7.2).
internal readonly record struct CaptureRepeat(SourceSpan Later, SourceSpan Earlier, string Name, bool Parameter);

public sealed partial class Binding
{
    // SPEC 7.7.2: the receiver `self` of a named function, accessor, constructor or destructor, and a setter's `value`.
    private static bool IsContextualBinding(BindingSymbol symbol)
        => symbol.Kind == BindingSymbolKind.Parameter && symbol.Declaration switch
        {
            FunctionKoto { IsAnonymous: false } declaring => symbol.Name == "self" || (symbol.Name == "value" && declaring.Accessor?.Kind == PropertyAccessorKind.Set),
            PropertyAccessorKoto accessor => symbol.Name == "self" || (symbol.Name == "value" && accessor.AccessorKind == PropertyAccessorKind.Set),
            _ => false,
        };

    // SPEC 7.7.2: each capture name that an earlier entry repeats, then each that a parameter repeats, as a range of captureRepeatRows;
    // empty for a valid list.
    private (int Start, int Count) RepeatedCaptures(FunctionKoto function, CaptureKoto[] captures)
    {
        var start = this.captureRepeatRows.Count;
        for (var i = 0; i < captures.Length; i++)
        {
            var name = captures[i].Name;
            var repeated = false;
            for (var j = 0; j < i && !repeated; j++)
            {
                if (captures[j].Name == name)
                {
                    this.captureRepeatRows.Add(new(captures[i].Span, captures[j].Span, name, false));
                    repeated = true;
                }
            }

            for (var p = 0; p < function.Parameters.Count && !repeated; p++)
            {
                if (function.Parameters[p].InternalName == name)
                {
                    this.captureRepeatRows.Add(new(function.Parameters[p].ExternalNameSpan, captures[i].Span, name, true));
                    repeated = true;
                }
            }
        }

        return (start, this.captureRepeatRows.Count - start);
    }

    // The later entry of a repeated name declares nothing, so the list binds only the entry it repeats.
    private bool RepeatsEarlierEntry((int Start, int Count) repeats, CaptureKoto capture)
    {
        for (var i = repeats.Start; i < repeats.Start + repeats.Count; i++)
        {
            if (!this.captureRepeatRows[i].Parameter && this.captureRepeatRows[i].Later == capture.Span)
            {
                return true;
            }
        }

        return false;
    }

    // The environment binding of `source` in `function`, null when it cannot hold it, or `source` itself when its Type failed. Any
    // Type is held but those of the interim rows below (PLAN G254, G170).
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

        // Interim rows, as before G721: a located Unsupported_Kd at a written entry, and for an omitted list a function-level Unsupported
        // that surfaces no record, so the use reports InvalidCaptureBinding_Kd (a pre-existing Language error on a valid program).
        // (1) Removal condition PLAN G254's unit: a pair layer (SPEC 13.5.5) other than the original `s/T` whose bare acquisition the
        // one plan (SPEC 8.14) makes a Reborrow in some admitted case, the owner case's own included (BareReborrows): a body's bare use
        // of it would Reborrow there, and no rule contains that Reborrow in the call receiver (G254). The original (a Parameter) was
        // captured before G721 and stays: its forms are G254's own.
        // (2) Removal condition PLAN G170's identity query: an abstract Type dev did not capture (a pair target, a projection) where an
        // identity premise is available in scope (HasTypeIdentity, SPEC 8.2.3), which may make it a pair layer that no plan reads.
        if ((type is { IsPairLayer: true, Kind: not BoundTypeKind.Parameter } && this.BareReborrows(type, function)) ||
            (type is { IsAbstract: true, IsPairLayer: false, Kind: not BoundTypeKind.Parameter } && HasTypeIdentity(this.ConstraintScope(function))))
        {
            if (entry is { } written)
            {
                this.FailUnsupportedAt(function, written.Span);
            }
            else
            {
                this.Fail(function, BindingFailure.Unsupported);
            }

            return null;
        }

        // SPEC 7.7.2, 8.15: an environment binding holds the source's complete Type, whatever it is, as a local would; each instance
        // lowers it at its case Type (a pair target, a projection, a Type parameter alike).
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
    // Reborrows an enclosing Closure's environment binding `u: uniq/T during o` is contained in that Closure's call receiver too, so its
    // binding has `uniq/T during meet(o, ρ)`, a pair layer that meet on its outer-Origin slot where its plan Reborrows the layer: the bare
    // Reborrow's one rule, BareReborrowLayer and ReborrowedType (DECISIONS R4-M123's 7.7.2, R4-M126 P4, PLAN G254); an enclosing local's
    // or parameter's keeps its Type (BU-28).
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
                if (bare is HirAcquire.Reborrow or HirAcquire.ByCase && this.EnvironmentContainment(source) is { } receiver &&
                    this.BareReborrowLayer(type, function) is { } layer)
                {
                    environment.Type = this.ReborrowedType(type, layer, receiver);
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

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Capture formation (SPEC 7.7.2): which binding a capture entry or a use in an omitted-list Closure captures, the environment binding
// it initializes and how (CaptureAcquisition), the enclosing regions its entries name (BoundClosure.Carried, its own entries' regions;
// CarryRegions, SPEC 15.6.5, R7-O2 A1; read by Binding.CarriedRegion, which admits them and reads what is carried in turn), and the
// repeated names of a written list. A literal's written header is bound only in the scope ClosureHeaderScope gives (R7-O2 O2-H), which
// records first the literal's evaluating body and the regions its written entries name, in its Carried and in each enclosing omitted
// list's that forwards the entry (ForwardsThrough, the one statement of forwarding); it forms no capture. An omitted list's own entries
// are its body's uses, recorded as each captures.
// A failed capture's facts and record belong to Binding.CaptureDiagnostics; a Closure's conversion and signature to Binding.Closures, its
// call receiver to Binding.ClosureReceivers.
// Interim (DECISIONS BU-184; Capture): a captured pair layer other than the original `s/T` whose plan Reborrows in some case (until
// PLAN G254's unit), and a captured pair target or projection under an identity premise (until PLAN G170's query), are a located
// Unsupported_Kd at a written entry; an omitted list's use of one reports InvalidCaptureBinding_Kd instead.

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

    // SPEC 7.7.2 (Nested Closures): a Closure whose scope is `scope` captures only what its immediately enclosing activation owns; an
    // enclosing omitted list forwards the capture of `source` (`outer`, that Closure), an explicit list or a named function does not
    // (false). `outer` is null when the immediately enclosing activation owns `source`. The one statement of forwarding: Capture forms the
    // forwarded capture, ClosureHeaderScope records its regions before a header binds.
    private static bool ForwardsThrough(BindingSymbol source, BindingScope scope, out FunctionKoto? outer)
    {
        outer = scope.Parent?.Function is { } enclosing && !ReferenceEquals(source.Scope.Function, enclosing) ? enclosing : null;
        return outer is null || (outer.IsAnonymous && outer.Captures is null);
    }

    // SPEC 7.7.2: a binding a Closure body can capture: an outer runtime local or parameter (a pattern's, a loop's, an environment's, an
    // accessor's storage among them); Types, Containers, static storage and named functions are never captures. The one kind rule of an
    // outer use (BindReference) and of an environment no body forms (CanCapture).
    private static bool IsRuntimeBinding(BindingSymbol symbol)
        => symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture;

    // SPEC 7.7.2 with 9.3 (G812 ruling 182): whether a body at `literal` can capture a binding of `scope`: a name whose first binding
    // visible at the literal (VisibleAt, the lookup's rule) is a runtime binding. It answers for every use in the body: a binding visible
    // at the literal's start is visible at each of them, and one not visible there at none. Read by TypedByHeader.
    private bool CanCapture(BindingScope scope, FunctionKoto literal)
    {
        foreach (var head in scope.Values.Values)
        {
            for (var candidate = head; candidate is not null; candidate = candidate.Next)
            {
                if (!this.VisibleAt(candidate, literal))
                {
                    continue;
                }

                if (IsRuntimeBinding(candidate))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

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

    // SPEC 15.6.5, 7.7.2, 15.4.4 (R7-O2 O2-H; the owner's O-487 (a)): the scope in which every binding of a literal's written header is
    // bound (BindClosureHeader, the header probe ClosureSignatureFits, FailClosureHeader and the call-signature evidence InferClosureHeader
    // each take it here, the one way to it), with what the header's Owned proofs read recorded first, so CarriedRegion reads a complete
    // set at any of them: the literal's plan (created when absent) and its evaluating body, the body the literal is in (this is its one
    // writer); and the regions its written list's entries name, each entry's binding's Type fixed by its declaration, in the literal's
    // Carried and in that of each enclosing omitted list that forwards the entry (ForwardsThrough). It forms no environment binding: the
    // entries' and the forwarded captures' bindings, their slots and their order are Capture's, at the literal's binding
    // (CaptureEntriesFail), which records the same regions again for nothing (CarryOrigin keeps a region once), so a probe records exactly
    // the regions of the captures the binding forms. Two exceptions reach only rejected programs (rule W): an entry CaptureEntriesFail
    // refuses before Capture (a receiver, a helper carrier) has its regions recorded, and a literal a probe reads that is never bound (its
    // call failed) leaves an enclosing omitted list's Carried with regions it does not capture. An omitted list's own entries are its
    // body's uses, recorded as each captures (Capture).
    private BindingScope ClosureHeaderScope(FunctionKoto literal)
    {
        var scope = this.scopes[literal];
        var plan = literal.ClosureStorage ??= new();
        plan.EvaluatingBody = this.formingBody; // SPEC 15.3.4: the header and captures are the evaluating body's (DECISIONS R4-M101 E1).
        foreach (var capture in literal.Captures ?? [])
        {
            if (this.Lookup(capture.Name, scope.Parent!, literal) is not { Type: { } type } source)
            {
                continue;
            }

            this.CarryRegions(plan, type);
            for (var at = scope; ForwardsThrough(source, at, out var outer) && outer is { ClosureStorage: { } enclosing }; at = this.scopes[outer])
            {
                this.CarryRegions(enclosing, type);
            }
        }

        return scope;
    }

    // The environment binding of `source` in `function`, null when it cannot hold it, or `source` itself when its Type failed. Any
    // Type is held but those of the interim rows below (PLAN G254, G170).
    private BindingSymbol? Capture(FunctionKoto function, BindingSymbol source, BindingScope scope, CaptureKoto? entry = null)
    {
        if (entry is null && IsContextualBinding(source))
        {
            return null; // SPEC 7.7.2: contextual self and a setter's value are never captured implicitly.
        }

        if (!ForwardsThrough(source, scope, out var outer))
        {
            return null;
        }

        if (outer is not null)
        {
            if (this.Capture(outer, source, this.scopes[outer]) is not { } forwarded)
            {
                return null;
            }

            source = forwarded;
        }

        if (source.Type is null && source.Declaration is VariableKoto variable && this.RecordedState(variable) == BindingState.Unvisited)
        {
            this.BindVariable(variable, source.Scope); // A declaration this pass has not bound yet; a bound one without a Type failed.
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
        // (1) Removal condition: the bare Reborrow rule names an adaptation family (PLAN, G254b's family row). A pair layer (SPEC 13.5.5)
        // other than the original `s/T` whose complete plan (SPEC 8.14, BareAcquisition) Reborrows in some admitted case where that rule
        // names no layer (ReborrowedType null: a family, OuterOrigin having no family row): a body's bare use of it would Reborrow there,
        // uncontained in the call receiver. Every other such layer is captured, its uses named by the rule or its located limit; an
        // incomplete plan's use has no plan (SPEC 8.14). The original (a Parameter) was captured before G721 and stays.
        // (2) Removal condition PLAN G170's identity query: an abstract Type dev did not capture (a pair target, a projection) where an
        // identity premise is available in scope (HasTypeIdentity, SPEC 8.2.3), which may make it a pair layer that no plan reads.
        if ((type is { IsPairLayer: true, Kind: not BoundTypeKind.Parameter } && this.BareAcquisition(type, function, out var reborrow) == SemanticsMask.None &&
                reborrow != SemanticsMask.None && this.ReborrowedType(type, function, BoundOrigin.Static, [], out _) is null) ||
            (type is { IsAbstract: true, IsPairLayer: false, Kind: not BoundTypeKind.Parameter } && HasTypeIdentity(this.ConstraintScope(function))))
        {
            if (entry is { } written)
            {
                this.RecordLocated(function, written.Span);
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
        this.CarryRegions(plan, type);
        scope.Values[source.Name] = environment;
        return environment;
    }

    // SPEC 15.6.5 (R7-O2 A1): the Closure's fact of the regions it carries (BoundClosure.Carried): each local region an entry's Type names
    // (the source's: an entry's binding holds it, by value or under a borrow of its slot) that the evaluating body reads as its value, a
    // region of that body or one it carries, kept once; each distinct part of the Type once (DECISIONS BU-19).
    private void CarryRegions(BoundClosure plan, BoundType type) => this.CarryPart(plan, type, this.visitedParts.Begin(this.storageVersion));

    private void CarryPart(BoundClosure plan, BoundType type, uint query)
    {
        if (!type.CarriesAnyOrigin)
        {
            return;
        }

        this.CarryOrigin(plan, type.Origin);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            this.CarryOrigin(plan, type.OriginArguments[i]);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query))
            {
                this.CarryPart(plan, type.Components[i], query);
            }
        }
    }

    // Each region slot `origin` names, kept once: the Closure's own entries' regions alone, with no test of whether one is carried; the
    // walk admits them, and reads what is carried in turn through their values, once no body is being bound (CarriedRegions; R7-O2
    // A1+A2: the capture formation reads no candidate).
    private void CarryOrigin(BoundClosure plan, BoundOrigin? origin)
    {
        if (origin is null)
        {
            return;
        }

        if (SlotRegion(origin) && !plan.Carried.Contains(origin))
        {
            plan.Carried.Add(origin);
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            this.CarryOrigin(plan, origin.Operands[i]);
        }
    }

    // SPEC 7.7.2: each explicit entry initializes one environment binding exactly as `let x = x` or `let x = x@op` would.
    // A bare entry Copies a Copy binding and Reborrows a binding storing an exclusive reference; `x@ref` and `x@uniq`
    // borrow the outer binding's slot, adding a reference layer, and an exclusive slot borrow needs a writable slot. A bare entry that
    // Reborrows an enclosing Closure's environment binding `u: uniq/T during o` is contained in that Closure's call receiver too, so its
    // binding has `uniq/T during meet(o, ρ)`, a pair layer that meet on its outer-Origin slot where its plan Reborrows the layer: the bare
    // Reborrow's one rule, ReborrowedType (DECISIONS R4-M123's 7.7.2, R4-M126 P4, PLAN G254); an enclosing local's
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
                    this.ReborrowedType(type, function, receiver, [], out var limit) is { } reborrowed)
                {
                    environment.Type = reborrowed;
                    return limit ? BindingFailure.Unsupported : null; // No one Type carries the Reborrow (ReborrowedType's limit).
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

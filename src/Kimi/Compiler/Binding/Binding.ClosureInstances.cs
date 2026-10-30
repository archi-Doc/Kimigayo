// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The old generator's CallPlan of each closure an instance closed, by closure and context, kept until an edit (DECISIONS R5-M36).
    [Transitional("R6-gen", Reason = "G7 deletes the old generator, the bridge's one reader.")]
    private readonly Dictionary<(BindingSymbol Closure, Subst Context), CallPlan> closureCalls = new();

    // SPEC 15.8.2, 6.1.3: a Closure Type's call signature, its definition's under the Type's closure context; the definition is the one
    // the Type names (BoundClosure.Of), whatever its literal's creation outcome (DECISIONS R5-M63, R4-M152).
    internal BoundType? ClosureSignature(BoundType type)
        => BoundClosure.Of(type) is { } closure
            ? type.ClosureContext is { } context ? this.types.Apply(closure.Signature, context) : closure.Signature : null;

    // The bridge from a closure's context to the CallPlan the old generator keys the closure's instance by (GenericStoragePlan,
    // BodyLowering): one per closure and context, so two closures of one instance keep two entries (S2-S4 X10), and the same one at
    // every request until an edit, so warm generation allocates nothing; null for a closure no instance closed.
    [Transitional("R6-gen", Reason = "G7 deletes the old generator, the bridge's one reader.")]
    internal CallPlan? ClosureCall(BoundType closure)
    {
        if (closure is not { ClosureContext: { } context, Symbol: { } symbol } || BoundClosure.Of(closure) is not { } definition)
        {
            return null;
        }

        if (!this.closureCalls.TryGetValue((symbol, context), out var call))
        {
            var result = this.types.Apply(definition.Signature.Components[1], context)!; // Checked when the instance made the context.
            call = new();
            call.Set(
                context.Target,
                result,
                null,
                [],
                context.Types,
                declaringType: context.Declaring,
                origins: context.Origins,
                inputOrigins: context.Inputs,
                lengthArguments: context.Lengths);
            this.closureCalls.Add((symbol, context), call);
        }

        return call;
    }

    // SPEC 6.1.3, 8.1.3: the closures of the instance a substitution makes closed in it: each closure whose enclosing function is its binder
    // (TyCtxt.EnclosingFunction) gets the substitution as its context, its conforming Self included (DECISIONS R5-M36). One call is one
    // query of the closing table, its instance fixed, so a part shared within the Type is closed once (DECISIONS BU-19); a closure
    // signature's own instantiation begins its own query.
    private BoundType? CloseClosureTypes(BoundType type, Subst instance) => this.ClosePart(type, instance, this.closedParts.Begin(this.storageVersion));

    private BoundType? ClosePart(BoundType type, Subst instance, uint query)
    {
        if (type.ClosureContext is not null || (type.Components.Count == 0 && type.Kind != BoundTypeKind.Closure))
        {
            return type;
        }

        if (!this.closedParts.TryGet(type, query, out var closed))
        {
            closed = this.ClosePartCore(type, instance, query);
            this.closedParts.Set(type, query, closed);
        }

        return closed;
    }

    private BoundType? ClosePartCore(BoundType type, Subst instance, uint query)
    {
        var scratch = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.ClosePart(type.Components[i], instance, query) is not { } part)
                {
                    return null;
                }

                scratch[i] = part;
                changed |= !ReferenceEquals(part, type.Components[i]);
            }

            Subst? context = null;
            if (BoundClosure.Of(type) is { } closure && type.Symbol is { } symbol && this.types.Instantiates(symbol) &&
                ReferenceEquals(this.types.EnclosingFunction(symbol), instance.Target))
            {
                // The closure's result in the instance is the failure check.
                if (this.InstantiateStorageType(closure.Signature.Components[1], instance) is null)
                {
                    return null;
                }

                context = instance;
            }

            return changed || context is not null
                ? this.types.Intern(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin ?? type.RawOrigin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, context, type.LengthArguments)
                : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    // The call contexts of function items (read by TyCtxt.SubstOfItem and the old generator) and of closures for the old generator, kept
    // across unchanged passes and revoked by an edit; G7 inlines this with ClosureCall's removal.
    private void ResetCallContexts()
    {
        this.functionItemContexts.Clear();
        this.closureCalls.Clear();
    }
}

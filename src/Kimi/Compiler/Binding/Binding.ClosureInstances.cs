// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure Type's call signature, its Origin arguments and its instances (SPEC 3.2.1, 6.1.3, 7.7.3, 15.3.4, 15.3.5, 15.6.4, 15.8.2;
// DECISIONS R5-M63, R4-M152, R4-M159).
// - Responsibility: ClosureType, a definition's Type with its Origin arguments: the atoms of enclosing Closures its signature names (a receiver,
//   an environment binding, a call result or an input, in any Origin position, raw layers included), which its own Type carries, so
//   every walk that images a Type's Origins (a maker's call, P8's publication, an instantiation, erasure) images them with its
//   environment; ClosureSignature, a Closure Type's call signature as its value reads it: its definition's under its closure context,
//   each definition argument replaced by the Type's; ClosureDefinitionSignature, the definition's under the context, which a call
//   declares (CallPlan.DeclaredSignature) and retention reads; ClosureArgumentVariance, an argument's variance by its occurrences in the
//   definition's signature; CloseClosureTypes, the closures an instance makes closed.
// - Inputs: the definition (BoundClosure.Of), its published signature and its Type (EnvironmentType).
// - Outputs: interned Types and Origin arguments.
// - Invariants: a definition naming no enclosing Closure's atom has no argument, so a flat Closure's Type is unchanged; a Type's
//   arguments are positionally its definition's images; no atom of a Closure that does not enclose a use reaches the use's Types through
//   a Type that passed its maker's call (SPEC 15.3.4).
// - Prohibitions: no second substitution (SubstituteStoredOrigins' PerCallImages); no judgment of a relation.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.8.2, 6.1.3: a Closure Type's call signature as its value reads it: its definition's under the Type's closure context, each of
    // the definition's Origin arguments replaced by the Type's (DECISIONS R4-M159), so a Closure returned from its maker's call reads the
    // Origins that call bound; the definition is the one the Type names (BoundClosure.Of), whatever its literal's creation outcome
    // (DECISIONS R5-M63, R4-M152).
    internal BoundType? ClosureSignature(BoundType type)
    {
        var signature = this.ClosureDefinitionSignature(type);
        if (signature is null || type.OriginArguments.Count == 0)
        {
            return signature;
        }

        // A Type's arguments are positionally its definition's images (R4-M159); a Type with arguments its definition lacks is a broken contract.
        if (BoundClosure.Of(type)!.EnvironmentType is not { } definition || definition.OriginArguments.Count != type.OriginArguments.Count)
        {
            throw new InvalidOperationException($"Closure Type `{type}` has Origin arguments its definition does not declare (InternalInvariant; R4-M159).");
        }

        var from = (BoundOrigin[])definition.OriginArguments;
        var to = (BoundOrigin[])type.OriginArguments;
        for (var i = 0; i < from.Length; i++)
        {
            if (!ReferenceEquals(from[i], to[i]))
            {
                return this.SubstituteStoredOrigins(signature, definition.Symbol!.Declaration!, [], new PerCallImages([], null, null, from, to));
            }
        }

        return signature;
    }

    // The call signature a Closure Type's definition declares, under the Type's closure context (CallPlan.DeclaredSignature; retention
    // reads a call in the definition's world).
    internal BoundType? ClosureDefinitionSignature(BoundType type)
        => BoundClosure.Of(type) is { } closure
            ? TyCtxt.ClosureInstance(type) is { } context ? this.types.Apply(closure.Signature, context) : closure.Signature : null;

    // SPEC 15.3.5: the variance of a Closure Type's Origin argument `index`: the composition of its atom's occurrences in its definition's
    // signature (a parameter contravariant, the result covariant, each layer's target as ComponentVariance composes it); equal
    // occurrences keep their variance, different ones are invariant.
    private static OriginVariance ClosureArgumentVariance(BoundType type, int index, Binding? binding)
    {
        if (BoundClosure.Of(type) is not { Signature: { } signature, EnvironmentType: { } definition } || index >= definition.OriginArguments.Count)
        {
            return OriginVariance.Invariant;
        }

        var variance = Occurrences(signature, definition.OriginArguments[index], OriginVariance.Covariant, binding, OriginVariance.Unused);
        return variance == OriginVariance.Unused ? OriginVariance.Invariant : variance;

        static OriginVariance Occurrences(BoundType type, BoundOrigin atom, OriginVariance polarity, Binding? binding, OriginVariance seen)
        {
            if (!type.CarriesAnyOrigin)
            {
                return seen;
            }

            seen = Names(OwnedOrigin(type), atom) ? Join(seen, polarity) : seen;
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                seen = Names(type.OriginArguments[i], atom) ? Join(seen, ComposeVariance(polarity, OriginArgumentVariance(type, i, binding))) : seen;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                seen = Occurrences(type.Components[i], atom, ComposeVariance(polarity, ComponentVariance(type, i, binding)), binding, seen);
            }

            return seen;
        }

        static bool Names(BoundOrigin? origin, BoundOrigin atom)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (Names(origin.Operands[i], atom))
                {
                    return true;
                }
            }

            return ReferenceEquals(origin, atom);
        }

        static OriginVariance Join(OriginVariance seen, OriginVariance variance)
            => seen == OriginVariance.Unused || seen == variance ? variance : OriginVariance.Invariant;
    }

    // SPEC 3.2.1, 15.3.4, 15.6.4 (DECISIONS R4-M159): the Type of the Closure `function` with its environment and call signature: its Origin
    // arguments are each atom of another Closure (an enclosing one) the signature names, in first-occurrence order, raw layers and nested
    // arguments included.
    private BoundType ClosureType(BindingSymbol symbol, FunctionKoto function, BoundType signature, ReadOnlySpan<BoundType> environment)
    {
        var scratch = this.originScratch.Rent(Collect(signature, function, null, 0));
        try
        {
            var count = Collect(signature, function, scratch, 0);
            return this.types.Intern(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, environment, originArguments: scratch.AsSpan(0, count));
        }
        finally
        {
            this.originScratch.Return(scratch, clearArray: true);
        }

        // With no scratch, an upper bound: the candidate occurrences.
        static int Collect(BoundType type, FunctionKoto function, BoundOrigin[]? scratch, int count)
        {
            if (!type.CarriesAnyOrigin)
            {
                return count;
            }

            count = Add(OwnedOrigin(type), function, scratch, count);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                count = Add(type.OriginArguments[i], function, scratch, count);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                count = Collect(type.Components[i], function, scratch, count);
            }

            return count;
        }

        static int Add(BoundOrigin? origin, FunctionKoto function, BoundOrigin[]? scratch, int count)
        {
            if (origin is null)
            {
                return count;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                count = Add(origin.Operands[i], function, scratch, count);
            }

            if (origin.Kind is not (OriginKind.Projection or OriginKind.Input or OriginKind.Parameter) ||
                origin.Binder is not FunctionKoto { IsAnonymous: true } binder || ReferenceEquals(binder, function))
            {
                return count;
            }

            for (var i = 0; scratch is not null && i < count; i++)
            {
                if (ReferenceEquals(scratch[i], origin))
                {
                    return count;
                }
            }

            if (scratch is not null)
            {
                scratch[count] = origin;
            }

            return count + 1;
        }
    }

    // SPEC 6.1.3, 8.1.3: the closures of the instance a substitution makes closed in it (Close). One call is one query of the closing table,
    // its inputs fixed, so a part shared within the Type is closed once (DECISIONS BU-19); a closure signature's own instantiation begins
    // its own query.
    private BoundType? CloseClosureTypes(BoundType type, Subst instance) => this.Close(type, instance, instance.Declaring);

    // DECISIONS BU-60: the closures a kept part of an owner's instance holds, closed by substitution alone (KeptType runs inside storage
    // walks, so this never queries storage).
    private BoundType? CloseHeldClosures(BoundType type, BoundType owner) => this.Close(type, null, owner);

    // SPEC 6.1.3, 6.3.3: each closure without a context gets the instance of the declaration whose instance it holds
    // (TyCtxt.EnclosingDeclaration), when the inputs carry it: a function's is the instance when that is its binder, its conforming Self
    // included (DECISIONS R5-M36), its result there the failure check; a Type's is its identity's image in the environment Type, an instance
    // of it or of a declaration it encloses (HeldContext; BU-60); a group has none (its statics are R5-M82's gap).
    private BoundType? Close(BoundType type, Subst? instance, BoundType? environment)
        => this.ClosePart(type, instance, environment, this.closedParts.Begin(this.storageVersion));

    private BoundType? ClosePart(BoundType type, Subst? instance, BoundType? environment, uint query)
    {
        if (type.ClosureContext is not null || (type.Components.Count == 0 && type.Kind != BoundTypeKind.Closure))
        {
            return type;
        }

        if (!this.closedParts.TryGet(type, query, out var closed))
        {
            closed = this.ClosePartCore(type, instance, environment, query);
            this.closedParts.Set(type, query, closed);
        }

        return closed;
    }

    private BoundType? ClosePartCore(BoundType type, Subst? instance, BoundType? environment, uint query)
    {
        var scratch = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.ClosePart(type.Components[i], instance, environment, query) is not { } part)
                {
                    return null;
                }

                scratch[i] = part;
                changed |= !ReferenceEquals(part, type.Components[i]);
            }

            Subst? context = null;
            if (BoundClosure.Of(type) is { } closure && type.Symbol is { } symbol && this.types.Instantiates(symbol))
            {
                switch (this.types.EnclosingDeclaration(symbol))
                {
                    case { Declaration: FunctionKoto } function when instance is not null && ReferenceEquals(function, instance.Target):
                        if (this.InstantiateStorageType(closure.Signature.Components[1], instance) is null)
                        {
                            return null;
                        }

                        context = instance;
                        break;
                    case { Kind: BindingSymbolKind.Type } declaration when environment is not null:
                        context = this.HeldContext(symbol, declaration, environment);
                        break;
                }
            }

            return changed || context is not null ? this.WithContext(type, scratch.AsSpan(0, type.Components.Count), context) : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    // DECISIONS BU-60: the instance of a Type `declaration` an environment Type is or encloses, the declaration's identity substituted by
    // the environment's arguments (StoredType), as a held closure's context (HeldContext); none where that leaves the identity (the
    // environment is the identity, or no instance of the declaration).
    private Subst? HeldContext(BindingSymbol closure, BindingSymbol declaration, BoundType environment)
    {
        var identity = this.SelfType(declaration);
        return environment.Symbol is not null && this.StoredType(identity, environment) is { } held && !ReferenceEquals(held, identity)
            ? this.HeldContext(closure, held) : null;
    }

    // DECISIONS BU-60: the one producer of a held closure's context: the Type instance it belongs to (TyCtxt.HeldContext). An instance
    // whose erased form has an Origin position (a borrow, Slice, handle or raw layer, or an Origin argument, static included), a fact every
    // owner of that instance shares, is staged: the context is the closure's own identity, closed in no instance, which the instance
    // collector reports as the EnclosingInstance gap at each site requesting its body, as before this rule, until a use opens the
    // instance's Origin positions (PLAN G300).
    private Subst HeldContext(BindingSymbol closure, BoundType held)
        => TyCtxt.HasOriginPosition(held) ? this.types.Identity(closure.Declaration) : this.types.HeldContext(closure, held);

    // A Closure Type with another context (DECISIONS BU-60: the Type instance a held context holds, which substitution rewrites), every
    // other part kept.
    private BoundType WithContext(BoundType type, Subst? context)
        => ReferenceEquals(context, type.ClosureContext) ? type : this.WithContext(type, (BoundType[])type.Components, context);

    // The Type with these components and this closure context, every other part kept.
    private BoundType WithContext(BoundType type, ReadOnlySpan<BoundType> components, Subst? context)
        => this.types.Intern(type.Kind, type.Symbol, type.Semantics, components, type.Length, type.Origin ?? type.RawOrigin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, context, type.LengthArguments);
}

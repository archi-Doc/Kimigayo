// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.8.2, 6.1.3: a Closure Type's call signature, its definition's under the Type's closure context; the definition is the one
    // the Type names (BoundClosure.Of), whatever its literal's creation outcome (DECISIONS R5-M63, R4-M152).
    internal BoundType? ClosureSignature(BoundType type)
        => BoundClosure.Of(type) is { } closure
            ? type.ClosureContext is { } context ? this.types.Apply(closure.Signature, context) : closure.Signature : null;

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

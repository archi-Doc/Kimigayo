// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The atoms EnclosingAtoms collects, reused by each literal's binding.
    private readonly List<BoundOrigin> enclosingAtoms = new();

    // SPEC 15.8.2, 6.1.3, 3.2.1: a Closure Type's call signature, its definition's under the Type's Origin arguments (each enclosing
    // anonymous function's atom the definition names stands for the image the Type carries) and its closure context; the definition is
    // the one the Type names (BoundClosure.Of), whatever its literal's creation outcome (DECISIONS R5-M63, R4-M152, R4-M159).
    internal BoundType? ClosureSignature(BoundType type)
    {
        if (this.ClosureContract(type) is not { } signature)
        {
            return null;
        }

        var images = (BoundOrigin[])type.OriginArguments;
        if (images.Length != 0 && BoundClosure.Of(type)!.EnvironmentType is { OriginArguments: BoundOrigin[] atoms } && atoms.Length == images.Length &&
            !images.AsSpan().SequenceEqual(atoms))
        {
            signature = this.SubstituteStoredOrigins(signature, type.Symbol!.Declaration, [], new([], null, null, atoms, images));
        }

        return signature;
    }

    // SPEC 15.8.2 (DECISIONS R4-M148, R4-M159): a Closure Type's call contract as its body sees it, its definition's signature under the
    // Type's closure context, the terms of its environment (TyCtxt.ClosureEnvironment): a value call's declared signature, which A5's
    // retention reads with the receiver input.
    internal BoundType? ClosureContract(BoundType type)
        => BoundClosure.Of(type) is { } closure
            ? type.ClosureContext is { } context ? this.types.Apply(closure.Signature, context) : closure.Signature : null;

    // SPEC 3.2.1, 15.3.4, 5.7 (DECISIONS R4-M159): the Origin arguments of a Closure literal's Type: each atom of an anonymous function
    // enclosing the literal that its signature names, in every Origin position, a raw layer's included, once, in the order a walk first
    // meets it. Every Type walk substitutes them as it substitutes the Type's parts, so a value keeps the images its creation's calls gave
    // them, and ClosureSignature reads the definition under them.
    private ReadOnlySpan<BoundOrigin> EnclosingAtoms(BoundType signature, FunctionKoto closure)
    {
        this.enclosingAtoms.Clear();
        Collect(signature, closure, this.enclosingAtoms);
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.enclosingAtoms);

        static void Collect(BoundType type, FunctionKoto closure, List<BoundOrigin> atoms)
        {
            if (!type.CarriesAnyOrigin)
            {
                return;
            }

            Add(type.Origin ?? type.RawOrigin, closure, atoms);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                Add(type.OriginArguments[i], closure, atoms);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                Collect(type.Components[i], closure, atoms);
            }
        }

        static void Add(BoundOrigin? origin, FunctionKoto closure, List<BoundOrigin> atoms)
        {
            if (origin is null)
            {
                return;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                Add(origin.Operands[i], closure, atoms);
            }

            if ((origin.Kind == OriginKind.Input || (origin.Kind == OriginKind.Projection && ReceiverAtom(origin))) &&
                origin.Binder is FunctionKoto { IsAnonymous: true } binder && !ReferenceEquals(binder, closure) && !atoms.Contains(origin))
            {
                atoms.Add(origin);
            }
        }
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
}

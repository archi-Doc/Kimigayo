// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Inline layouts: the declarations whose storage contains themselves by value (AdtDef.InlineCycle, InvalidInlineLayout_Kd).
public sealed partial class Binding
{
    private readonly List<DeclarationContainerKoto> inlineLayoutStack = new();
    private readonly List<bool> inlineLayoutOwnDeclaration = new();
    private readonly List<DeclarationContainerKoto> cyclicInlineLayouts = new();
    private readonly HashSet<BoundType> finiteInlineLayouts = new(ReferenceEqualityComparer.Instance);
    private readonly TypeMemo<bool> layoutParts = new(); // The Tuples and fixed arrays a container frame met that added no cycle.

    // SPEC 21.3.2: a struct or enum that contains itself by value (through stored Fields and inline
    // bases, payloads, Tuple/array components and other by-value containers, under the substitution
    // each use supplies) has no finite inline layout; a reference or object Semantics layer is an
    // indirection. Every declaration on such a cycle is diagnosed once and published as AdtDef.InlineCycle.
    private void ValidateInlineLayouts()
    {
        this.cyclicInlineLayouts.Clear();
        this.finiteInlineLayouts.Clear();
        foreach (var container in this.storageDeclarations)
        {
            container.BoundSymbol!.Adt!.InlineCycle = false;
            if (container.BoundSymbol.Type is { } type)
            {
                this.inlineLayoutStack.Clear();
                this.inlineLayoutOwnDeclaration.Clear();
                this.VisitInlineLayout(type, this.layoutParts.Begin(this.storageVersion));
            }
        }

        for (var i = 0; i < this.cyclicInlineLayouts.Count; i++)
        {
            this.cyclicInlineLayouts[i].BoundSymbol!.Adt!.InlineCycle = true;
            this.Fail(this.cyclicInlineLayouts[i], BindingFailure.InvalidInlineLayout);
        }
    }

    // A visit adds the cycles it closes against the containers on the stack and marks a struct or enum instance whose visit added none
    // finite (finiteInlineLayouts): what it adds depends on the Type and the stack. Each container frame (the root and each pushed
    // instance) is one query of layoutParts, whose stack is fixed, and a Tuple or fixed array whose visit added no cycle is not visited
    // again in its frame (DECISIONS BU-55): every struct or enum instance below it is then finite (skipped) or met a container on the
    // same stack, which adds nothing again, so the revisit is a no-op, and the walk is linear in a frame's distinct parts (V16).
    private void VisitInlineLayout(BoundType? type, uint frame)
    {
        if (type is null || this.finiteInlineLayouts.Contains(type))
        {
            return;
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray)
        {
            if (this.layoutParts.TryGet(type, frame, out _))
            {
                return;
            }

            var added = this.cyclicInlineLayouts.Count;
            for (var i = 0; i < type.Components.Count; i++)
            {
                this.VisitInlineLayout(type.Components[i], frame);
            }

            if (this.cyclicInlineLayouts.Count == added)
            {
                this.layoutParts.Set(type, frame, true);
            }

            return;
        }

        if ((!AdtDef.IsStruct(type) && !AdtDef.IsEnum(type)) || type.Symbol!.Adt is not { } shape)
        {
            return; // Parameters, primitives, references, objects, Slices and pointers store no inline copy of this Type.
        }

        var container = (DeclarationContainerKoto)type.Symbol.Declaration;
        var index = this.inlineLayoutStack.IndexOf(container);
        if (index >= 0)
        {
            // The cycle's members are the declarations reached as themselves; a generic container reached
            // under an instantiation (Box<E> inside E) merely stores the offending Type argument.
            for (var i = index; i < this.inlineLayoutStack.Count; i++)
            {
                if (this.inlineLayoutOwnDeclaration[i] && !this.cyclicInlineLayouts.Contains(this.inlineLayoutStack[i]))
                {
                    this.cyclicInlineLayouts.Add(this.inlineLayoutStack[i]);
                }
            }

            return;
        }

        // The declaration itself is visited with its declared stored Types (its own parameters stay open);
        // an instantiation substitutes them.
        var own = ReferenceEquals(type, container.BoundSymbol?.Type);
        this.inlineLayoutStack.Add(container);
        this.inlineLayoutOwnDeclaration.Add(own);
        var cycles = this.cyclicInlineLayouts.Count;
        var inner = this.layoutParts.Begin(this.storageVersion);
        for (var i = 0; i < shape.Types.Count; i++)
        {
            this.VisitInlineLayout(own ? shape.Types[i].BoundType : this.StoredType(shape.Types[i], type), inner);
        }

        this.inlineLayoutStack.RemoveAt(this.inlineLayoutStack.Count - 1);
        this.inlineLayoutOwnDeclaration.RemoveAt(this.inlineLayoutOwnDeclaration.Count - 1);
        if (this.cyclicInlineLayouts.Count == cycles)
        {
            this.finiteInlineLayouts.Add(type);
        }
    }
}

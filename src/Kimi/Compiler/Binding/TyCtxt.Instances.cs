// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns instance identity (DECISIONS R5-M21, R5-M34, R5-M39, R5-M40, R5-M43; R5 package F 4.11, K 4.1, X8, X26, X40): whether a definition's
// body is checked once and generated per closed substitution (Instantiates), the null rule of instance keys, the function whose body holds
// a scope, whose instance a nested definition's instance holds and whose self a direct base call uses (EnclosingFunction), whether a
// function's calls dispatch (Dispatches), and the Origin-erased form of a Type and a substitution (Erase), the one identity of instance
// keys, descriptors, glue and layouts (SPEC 3.3.1, 8.13.3).
// Input: a definition's BindingSymbol (a named or anonymous function, an accessor's executing function, a property) once the pass has bound
// its schemas, or for EnclosingFunction a scope; for Erase, an interned Type or substitution. Output: whether a definition instantiates,
// kept across passes with no edit and discarded at an edit (Binding.Invalidate); an interned erased value, memoized like the interning.
// Erase: every Origin becomes static (a reference's or a Slice's, written or inferred, every Origin argument and every Origin binding of a
// substitution or closure context) and a raw layer states none (DECISIONS BU-24), while Kind, Semantics, symbols, lengths, arities, Self and
// the declaring Type are kept; idempotent; Types whose instances differ in Origins only erase to one value; it only interns, never
// preparing storage or rewriting a value.
// Invariants: true exactly when the definition or a declaration enclosing it on its scope chain has a Type, Semantics or length slot, even an
// unused one (SPEC 6.1.3). A specialization declares no slot (SPEC 8.13.1), so one in a generic container, and every declaration nested in
// it, is generic in the container's slots, its instance key holding the container's arguments (SPEC 8.13.3). Origin slots never count.
// Must not: decide by syntax what a declaration's schema publishes, write any state other than its memos and the interning, or prove
// anything on an erased value (proofs read Origins; an instance's proofs run on its representative).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    // Per definition, whether it instantiates; its scope chain and the schemas on it change only at an edit.
    private readonly Dictionary<BindingSymbol, bool> instances = new();

    // Per Type and per substitution, the erased form, its own erasure included; erasure is structural, so like the interning they stay.
    private readonly Dictionary<BoundType, BoundType> erasedTypes = new();
    private readonly Dictionary<Subst, Subst> erasedSubsts = new();
    private BoundOrigin[] statics = []; // Every entry is BoundOrigin.Static.

    // SPEC 6.1.3, 8.15.1: whether a body keyed by the definition is generated per closed substitution of its own and its enclosing
    // declarations' slots rather than once.
    internal bool Instantiates(BindingSymbol definition)
    {
        if (!this.instances.TryGetValue(definition, out var result))
        {
            result = Walk(definition);
            this.instances.Add(definition, result);
        }

        return result;
    }

    // R4-M51: discarded with the source analysis at an edit.
    internal void ResetInstances() => this.instances.Clear();

    // SPEC 3.3.1, 8.13.3: the Type with every Origin static, interned.
    internal BoundType Erase(BoundType type)
    {
        if (!this.erasedTypes.TryGetValue(type, out var erased))
        {
            erased = this.EraseParts(type);
            this.erasedTypes.Add(type, erased);
            this.erasedTypes.TryAdd(erased, erased);
        }

        return erased;
    }

    // The named function whose body holds a scope, through anonymous functions and blocks: the first named function on its scope chain, or
    // in a custom accessor's body the accessor's executing function (SPEC 11.3), the definition its body is keyed by; null for a scope in a
    // container outside every body. Lexical self and the lexical declaring struct of a direct base call are this function's (SPEC 6.4.4;
    // an instance accessor's body is a derived instance body, DECISIONS BU-53).
    internal BindingSymbol? EnclosingFunction(BindingScope? scope)
    {
        for (; scope is not null; scope = scope.Parent)
        {
            switch (scope.Owner)
            {
                case FunctionKoto { IsAnonymous: false } function:
                    return function.BoundSymbol;
                case PropertyAccessorKoto accessor:
                    return CallPlan.Accessor(accessor);
                case DeclarationContainerKoto:
                    return null;
            }
        }

        return null;
    }

    // The function whose body declares a definition, the scope declaring it: null for a member or a top-level declaration. An instance of a
    // nested definition holds that function's instance (SPEC 6.1.3, 9.3): the instance collector requires its substitution to carry it, and
    // a Closure takes it as its context (SubstOfClosure, Binding.ClosePart).
    internal BindingSymbol? EnclosingFunction(BindingSymbol definition) => this.EnclosingFunction(definition.Scope);

    // SPEC 6.4.6, 8.4: whether a call of a function dispatches (CallPlan.KindOf), a requirement to the witness of its conforming Type and a
    // virtual slot to the override its receiver's Type selects, so that no one body is the target of the function's Item (K6 and K8 resolve
    // them).
    internal bool Dispatches(BindingSymbol function) => CallPlan.KindOf(function) != CalleeKind.Function;

    // K package KD1, KD2: the erased substitution of a binder that instantiates; null for one that does not, whose bodies are keyed without
    // a substitution.
    internal Subst? Erase(Subst subst) => this.Instantiates(subst.Target) ? this.EraseBindings(subst) : null;

    // Whether a Type's own Origin erases: a reference's or a Slice's, written or inferred, or any written one.
    private static bool HasOwnOrigin(BoundType type) => type.Origin is not null || type.Kind == BoundTypeKind.Slice ||
        (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq);

    // Whether the definition, or a declaration its scope chain passes, has a slot.
    private static bool Walk(BindingSymbol definition)
    {
        BindingSymbol? declaration = definition;
        for (var scope = definition.Scope; ; scope = scope.Parent)
        {
            if (declaration?.Schema is { GenericSlots.Count: > 0 })
            {
                return true;
            }

            if (scope is null)
            {
                return false;
            }

            // A scope's owner contributes the slots of the declaration it is, never those of a symbol it names (a Pattern's Type).
            declaration = scope.Owner.BoundSymbol is { } owner && ReferenceEquals(owner.Declaration, scope.Owner) ? owner : null;
        }
    }

    // A Type with its parts erased; a closure context's binder instantiates, so the context erases to its erased bindings.
    private BoundType EraseParts(BoundType type)
    {
        if (type.Components.Count + type.OriginArguments.Count == 0 && type.ClosureContext is null && !HasOwnOrigin(type))
        {
            return type;
        }

        var components = type.Components.Count == 0 ? [] : new BoundType[type.Components.Count];
        for (var i = 0; i < components.Length; i++)
        {
            components[i] = this.Erase(type.Components[i]);
        }

        var context = type.ClosureContext is { } closing ? this.EraseBindings(closing) : null;
        return this.Intern(
            type.Kind,
            type.Symbol,
            type.Semantics,
            components,
            type.Length,
            HasOwnOrigin(type) ? BoundOrigin.Static : null,
            this.Statics(type.OriginArguments.Count),
            type.LengthExpression,
            context,
            type.LengthArguments);
    }

    // A substitution with its bindings erased: its Types, Self and declaring Type erased, every Origin static, lengths kept.
    private Subst EraseBindings(Subst subst)
    {
        if (!this.erasedSubsts.TryGetValue(subst, out var erased))
        {
            var types = subst.Types.Length == 0 ? [] : new BoundType?[subst.Types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                types[i] = subst.Types[i] is { } type ? this.Erase(type) : null;
            }

            var self = subst.Self is { } conforming ? this.Erase(conforming) : null;
            var declaring = subst.Declaring is { } owner ? this.Erase(owner) : null;
            var origins = this.Statics(subst.Origins.Length + subst.Inputs.Length);
            erased = this.Intern(subst.Target, self, declaring, types, subst.Lengths, origins[..subst.Origins.Length], origins[subst.Origins.Length..]);
            this.erasedSubsts.Add(subst, erased);
            this.erasedSubsts.TryAdd(erased, erased);
        }

        return erased;
    }

    // Count static Origins, from one array that only grows.
    private ReadOnlySpan<BoundOrigin> Statics(int count)
    {
        if (this.statics.Length < count)
        {
            this.statics = new BoundOrigin[Math.Max(count, this.statics.Length * 2)];
            Array.Fill(this.statics, BoundOrigin.Static);
        }

        return this.statics.AsSpan(0, count);
    }
}

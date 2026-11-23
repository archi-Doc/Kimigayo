// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Explicit full specializations (SPEC 8.13): each is verified against its original (PrepareSpecializations); a call's closed key selects
// one (Implementation); a verified specialization names its original (SpecializationOriginal; GetSpecializationOriginal by syntax), whose
// call contract and ABI it keeps (SPEC 8.13.2).
public sealed partial class Binding
{
    private readonly SpecializationRows specializations;

    // SPEC 8.13.3: selection is keyed by the original; each original lists its verified specializations.
    private readonly SpecializationListRows specializationsByOriginal;

    internal FunctionKoto? GetSpecializationOriginal(FunctionKoto function)
        => this.specializations.TryGetValue(function, out var specialization) ? (FunctionKoto)specialization.Original.Declaration : null;

    // SPEC 8.13.2, 8.13.3: the original a verified explicit specialization implements, whose call contract it keeps; null for any other
    // symbol, a specialization Binding failed included. GetSpecializationOriginal is the syntax-keyed form of the same entry (SemanticQuery).
    internal BindingSymbol? SpecializationOriginal(BindingSymbol? function)
        => function?.Declaration is FunctionKoto declaration && this.specializations.TryGetValue(declaration, out var specialization)
            ? specialization.Original : null;

    /// <summary>Gets an Origin of a generic body in the instance a call's substitution makes (TyCtxt.Compose too): the call's Origins and
    /// inputs, the pair Origins its Type arguments bind and the stored Origins of the Type declaring the callee, in one pass
    /// (Binding.CallSubstitution).</summary>
    /// <param name="origin">The body's Origin.</param>
    /// <param name="target">The callee.</param>
    /// <param name="declaring">The instantiated Type declaring the callee.</param>
    /// <param name="origins">The substitution of the callee's declared Origins.</param>
    /// <param name="inputs">The input Origins in parameter-slot order.</param>
    /// <param name="types">The Type arguments by slot.</param>
    /// <returns>The Origin in the instance.</returns>
    internal BoundOrigin ForwardOrigin(BoundOrigin origin, BindingSymbol target, BoundType? declaring, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs, ReadOnlySpan<BoundType?> types)
        => this.Substitute(origin, SubstitutionOf(target.Declaration, declaring, types, default, origins, inputs));

    /// <summary>Lists the verified specializations of a generic original (SPEC 8.13).</summary>
    /// <param name="original">The original generic function.</param>
    /// <returns>The specializations, or null or empty when it has none.</returns>
    internal List<FunctionKoto>? Specializations(BindingSymbol original) => SpecializationListRows.GetValueOrDefault(original);

    // SPEC 8.13, 8.13.3: a body a call of a function under its substitution may run, by index, null past the last: the specialization a
    // closed key selects, else the function itself and each specialization an open key may still select (no substitution: every one).
    internal BindingSymbol? Implementation(BindingSymbol function, Subst? subst, int index)
    {
        var types = subst is null ? default : subst.Types;
        var lengths = subst is null ? default : subst.Lengths;
        if (this.SelectSpecialization(function, types, lengths) is { } selected)
        {
            return index == 0 ? selected.BoundSymbol : null;
        }

        var candidates = this.Specializations(function);
        for (var i = 0; index > 0 && candidates is not null && i < candidates.Count; i++)
        {
            if (MaySelect(this.specializations[candidates[i]], types, lengths) && --index == 0)
            {
                return candidates[i].BoundSymbol;
            }
        }

        return index == 0 ? function : null;
    }

    // SPEC 8.13.3: the selection key holds every slot; a length slot is its evaluated value (LengthKey(N)).
    private static bool SameSpecializationLengths(ReadOnlySpan<BoundLength?> left, ReadOnlySpan<BoundLength?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!SameLengthSignature(left[i], right[i], null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 8.13.3: whether a call whose key is still open may select a specialization: each argument it fixes may match the
    // specialization's, and each constant length equals it.
    private static bool MaySelect(Specialization candidate, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths)
    {
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is { } argument && candidate.Arguments[i] is { } closed && !MayMatch(argument, closed))
            {
                return false;
            }
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] is { IsConstant: true } length && !SameLengthSignature(length, candidate.Lengths[i], null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    // Whether an instance of a pattern may be a closed Type: an abstract part matches anything, any other part the same shape.
    private static bool MayMatch(BoundType pattern, BoundType closed)
    {
        if (ReferenceEquals(pattern, closed) || pattern.IsAbstract)
        {
            return true;
        }

        if (pattern.Kind == BoundTypeKind.Primitive || pattern.Kind != closed.Kind || pattern.Semantics != closed.Semantics)
        {
            return false;
        }

        if (!ReferenceEquals(pattern.Symbol, closed.Symbol) || pattern.Components.Count != closed.Components.Count ||
            (pattern.LengthExpression is null && pattern.Length != closed.Length))
        {
            return false;
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!MayMatch(pattern.Components[i], closed.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSpecializationArguments(ReadOnlySpan<BoundType?> left, ReadOnlySpan<BoundType?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] is null && right[i] is null)
            {
                continue; // A length slot; compared by SameSpecializationLengths.
            }

            if (left[i] is not { } a || right[i] is not { } b || !SignatureEquals(a, b, null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    private FunctionKoto? SelectSpecialization(BindingSymbol original, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths)
    {
        if (!SpecializationListRows.TryGetValue(original, out var candidates))
        {
            return null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var specialization = this.specializations[candidates[i]];
            if (SameSpecializationArguments(specialization.Arguments, types) && SameSpecializationLengths(specialization.Lengths, lengths))
            {
                return candidates[i];
            }
        }

        return null;
    }

    private void ResetSpecializations()
    {
        foreach (var siblings in this.specializationsByOriginal.Values.In(this.pass))
        {
            siblings!.Clear();
        }
    }

    private void PrepareSpecializations()
    {
        foreach (var node in this.nodes)
        {
            if (node is not FunctionKoto { IsSpecialization: true, BoundSymbol: { } symbol } function)
            {
                continue;
            }

            // Constraint and attribute specializations require their own inherited contract certificates.
            // Do not accept their syntax by merely erasing the generic header. Written binder names
            // are inherited by CompleteImplementationOrigins (SPEC 8.13.2); a receiver is an ordinary
            // restated parameter matched at the original's position (SPEC 8.13.1).
            if (function.GenericArguments.Count == 0 || HasParameterAttributes(function))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.13.2: a specialization header inherits the original's access, boundary, defaults and
            // Constraints; it redeclares none of them. The parser judges its modifiers, publishing none (ParseFuncDeclaration),
            // and its `!` boundary (ParseParameterList).
            if (function.TypeConstraints.Count != 0 || HasParameterDefaults(function))
            {
                this.Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            // A misplaced specialization (SPEC 8.13.1, F.6) has no declaration scope: it is matched to no target, and its failure
            // rests on its placement Error (a part prerequisite, IsDerived). The checks above read the written header only, so they
            // stay direct. Everything below needs the declaration scope or a target: the selection key's arguments and their
            // closedness (8.13.1 "outer generic parameter"), the target search and the target's checks. The arguments' Names are
            // still resolved with the declaration's other Types, so their own records stay.
            if (RestsOn(function, function.Placement))
            {
                continue;
            }

            ref var kept = ref SpecializationStorageRows.Slot(function);
            if (kept is null || kept.Arguments.Length != function.GenericArguments.Count)
            {
                kept = new(new BoundType?[function.GenericArguments.Count], new BoundLength?[function.GenericArguments.Count]);
            }

            var entry = kept;
            var arguments = entry.Arguments;
            var lengths = entry.Lengths;
            Array.Clear(arguments);
            Array.Clear(lengths);
            var closed = true;
            Koto? failed = null;
            var scope = this.scopes[function];
            for (var i = 0; i < arguments.Length; i++)
            {
                // SPEC 8.13: a length slot takes an evaluated constant (LengthKey(N)); a Type slot takes one closed Type.
                if (this.IsLengthArgument(function.GenericArguments[i], scope))
                {
                    lengths[i] = this.BindLength(function.GenericArguments[i], scope);
                    closed &= lengths[i] is { IsConstant: true };
                }
                else
                {
                    arguments[i] = this.BindType(function.GenericArguments[i], scope);
                    closed &= arguments[i] is { } type && Closed(type);
                    failed ??= FailedPart(function.GenericArguments[i]);
                }
            }

            if (RestsOn(function, failed))
            {
                continue; // SPEC 23.1.2: the closedness verdict over an argument that failed to bind.
            }

            if (!closed)
            {
                this.Fail(function, BindingFailure.InvalidTypeFormation);
                continue;
            }

            BindingSymbol? original = null;
            var matches = 0;
            var candidates = 0; // SPEC 8.13.1: same Name, kind and generic slots; input structure is matched below.
            var restated = new FnSig(function);
            for (var candidate = symbol.Scope.Values.GetValueOrDefault(function.Name); candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsSpecialization: false } ordinary)
                {
                    continue;
                }

                var target = new FnSig(ordinary);
                if (target.GenericCount != arguments.Length || !SameSlotKinds(ordinary, lengths) || target.ReceiverIndex != restated.ReceiverIndex)
                {
                    continue;
                }

                candidates++;
                if (target.InputCount != restated.InputCount)
                {
                    continue; // An input-structure mismatch, not a missing target.
                }

                var equal = true;
                for (var p = 0; p < restated.InputCount; p++)
                {
                    equal &= target.Input(p) is { } input &&
                        this.SubstituteType(input, ordinary, arguments, lengths) is { } substituted && restated.Input(p) is { } actual &&
                        SignatureEquals(substituted, actual, target.Binder, restated.Binder);
                }

                if (equal)
                {
                    original = candidate;
                    matches++;
                }
            }

            if (matches != 1)
            {
                // SPEC 23.1.2: an input-structure mismatch over a restated input that failed to bind rests on the input's record.
                for (var p = 0; failed is null && matches == 0 && candidates != 0 && p < function.Parameters.Count; p++)
                {
                    failed = FailedPart(function.Parameters[p].Type);
                }

                // SPEC 8.13.1: no target, an input-structure mismatch and multiple targets are distinct diagnostics.
                if (!RestsOn(function, failed))
                {
                    this.Record(function, matches > 1 ? BindingFailure.Ambiguous : candidates == 0 ? BindingFailure.MissingSpecializationTarget : BindingFailure.SpecializationInputMismatch, []);
                }

                continue;
            }

            var definition = (FunctionKoto)original!.Declaration;
            // SPEC 8.13.2: the original's Attributes are inherited. An implementation Attribute (IMPL 21.3.4) is a fact of the original
            // declaration, and a specialization is no declaration of its own (SPEC 8.13.3): each reader of such a fact resolves the
            // function to its original first (TyCtxt.Original), so a specialization has it. Any other Attribute is not inherited yet.
            if (HasUninheritedAttributes(definition) || HasParameterAttributes(definition))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.13.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
            var constraints = this.CheckConstraints(definition.TypeConstraints, new CallSubstitution(definition, arguments, lengths), scope, function);
            if (constraints == ConstraintProof.Unknown && NamesHeaderOrigin(arguments, function))
            {
                // SPEC 8.13.2 (PLAN G863): the original admits only the bindings its Constraints allow, so an undecided Constraint over
                // the header's universal Origins is a premise of the body, which nothing states yet: a located limit. The gate reads
                // the arguments, not the proof: an Unknown proof publishes no fact naming the Origin it rests on (only a Callable
                // comparison's CallableUnknown), so an Unknown of another cause at such an argument is the limit too (still rejected,
                // without its Proof evidence) until the proof names its cause or the Constraint becomes a premise (G863's remainder).
                _ = this.TakeUnknownFact();
                this.Fail(function, BindingFailure.Unsupported);
                continue;
            }

            if (constraints != ConstraintProof.Proven)
            {
                _ = constraints == ConstraintProof.Unknown ? this.FailUnprovenConstraint(function, this.TakeUnknownFact(), unresolved: true) : this.RecordConstraint(function, constraints, []);
                continue;
            }

            var valid = this.CompleteImplementationOrigins(function, definition, arguments, lengths, out var originMismatch);
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                valid &= function.Parameters[p].ExternalName == definition.Parameters[p].ExternalName;
            }

            // SPEC 8.13.2: a Type argument's Origins are the ones each call of the original supplies, the header's universal input
            // Origins at that argument (TypeContext), or ones bound inside it; any other Origin the argument's bound Type states adds
            // an Origin restriction (independent of the Origin's own well-formedness, SPEC 15.2.3): a raw layer's too (SPEC 5.7: it
            // states which storage the pointer addresses), in any variance position (a Function Type's input included) and whatever
            // spells it (one reached through a completed projection included).
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is { } argument)
                {
                    var summary = this.SummaryOf(argument);
                    valid &= Unwritten(summary.Nodes, function, function.GenericArguments[i]) && Unwritten(summary.RawNodes, function, function.GenericArguments[i]);
                }
            }

            if (!valid)
            {
                if (originMismatch is { } contract)
                {
                    this.FailExplained(ref this.originContracts, function, BindingFailure.OriginContract, contract);
                }
                else
                {
                    this.Fail(function, BindingFailure.IncompatibleImplementation);
                }

                continue;
            }

            var siblings = SpecializationListRows.Slot(original!) ??= [];

            for (var i = 0; i < siblings.Count; i++)
            {
                var previous = this.specializations[siblings[i]];
                if (SameSpecializationArguments(previous.Arguments, arguments) && SameSpecializationLengths(previous.Lengths, lengths))
                {
                    this.FailDuplicate(siblings[i], function);
                    valid = false;
                }
            }

            if (valid)
            {
                entry.Original = original!;
                this.specializations.Add(function, entry);
                siblings.Add(function);
            }
        }

        // SPEC 23.1.2: the first failed part of a header syntax that formed no Type; a header verdict reading it rests on its record.
        Koto? FailedPart(Koto syntax) => syntax.BoundType is null ? (this.failedSignaturePartVisitor ??= new()).Find(syntax, invalidState: true) : null;

        // The header's failure rests on a part's record (IsDerived): its placement Error, or an argument or input that formed no Type.
        bool RestsOn(FunctionKoto function, Koto? part)
        {
            if (part is null)
            {
                return false;
            }

            this.partPrerequisites[function] = part;
            this.Fail(function, BindingFailure.Unsupported, true);
            return true;
        }

        // SPEC 8.13.1: every slot is supplied with its original kind, in declaration order.
        static bool SameSlotKinds(FunctionKoto ordinary, BoundLength?[] lengths)
        {
            for (var i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] is not null ? ordinary.GenericArguments[i] is not LengthParameterKoto : ordinary.GenericArguments[i] is not GenericParameterKoto)
                {
                    return false;
                }
            }

            return true;
        }

        static bool Closed(BoundType type)
        {
            if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
                type.LengthExpression is not null)
            {
                return false;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!Closed(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Whether an argument's Origins (its Origin summary's nodes) name one of the header's: a universal input or a parameter's Origin.
        // Raw layers' nodes stay out, though a Constraint may be undecided over one (`T is Owned` reads a raw layer's Origin, SPEC
        // 15.2.3): the limit stands for a header that may be valid, and a raw node of a header's argument is never the call's (SPEC
        // 15.4.2 completes no raw Origin), so that header is invalid whichever record is selected and keeps the Constraint's.
        bool NamesHeaderOrigin(BoundType?[] arguments, FunctionKoto function)
        {
            foreach (var argument in arguments)
            {
                if (argument is null)
                {
                    continue;
                }

                foreach (var origin in this.SummaryOf(argument).Nodes)
                {
                    if (ReferenceEquals(origin.Binder, function))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // SPEC 8.13.2: whether every Origin node of a Type argument is the call's: the header's universal input Origin at the
        // argument, one bound inside it (a Function Type's per-call input) or their meet (whose operands are nodes too). Any other
        // (static, a parameter's, a binder's; written in the header or stated by what the argument names) restricts the original.
        static bool Unwritten(BoundOrigin[] nodes, FunctionKoto function, Koto argument)
        {
            foreach (var origin in nodes)
            {
                if (origin.Kind != OriginKind.Intersection && !(origin is { Kind: OriginKind.Input, Binder: { } binder } &&
                    (ReferenceEquals(binder, function) ? origin.Occurrence is { } at && IsWithin(at, argument) : IsWithin(binder, argument))))
                {
                    return false;
                }
            }

            return true;
        }

        // True when the original carries an Attribute whose inheritance is not provided yet: any but an implementation Attribute, which
        // every reader takes from the original (TyCtxt.Original).
        static bool HasUninheritedAttributes(FunctionKoto function)
        {
            for (var attribute = function.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (!attribute.IsImplementation)
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasParameterAttributes(FunctionKoto function)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].AttributeChain is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class Specialization(BoundType?[] arguments, BoundLength?[] lengths)
    {
        internal BindingSymbol Original { get; set; } = null!;

        internal BoundType?[] Arguments { get; } = arguments;

        internal BoundLength?[] Lengths { get; } = lengths;
    }

    /// <summary>The `specializations` family's one accessor (DECISIONS BU-220): a pass family, a verified explicit specialization's entry
    /// (the kept object SpecializationStorageRows holds), in the specialization's module's NodeColumns at its SyntaxId; absent after a pass until
    /// the column's first write. Its members keep the names its readers use.</summary>
    private readonly struct SpecializationRows(Binding binding)
    {
        internal Specialization this[FunctionKoto function]
            => this.TryGetValue(function, out var specialization) ? specialization : throw new KeyNotFoundException($"`{function}` is no verified specialization in this pass.");

        internal void Add(FunctionKoto function, Specialization specialization)
        {
            ref var row = ref HirTables.Of(function).Families.Specializations.Ref(function.SyntaxId, binding);
            row = row is null ? specialization : throw new ArgumentException($"`{function}` was already verified in this pass.", nameof(function));
        }

        internal bool ContainsKey(FunctionKoto function) => this.TryGetValue(function, out _);

        internal bool TryGetValue(FunctionKoto function, [NotNullWhen(true)] out Specialization? specialization)
            => (specialization = function.SyntaxOwner is { } table ? table.Hir.Families.Specializations.Get(function.SyntaxId, binding) : null) is not null;
    }

    /// <summary>The `specializationsByOriginal` family's one accessor (DECISIONS BU-220): an original's verified specializations (SPEC
    /// 8.13.3), kept in its declaration's module's NodeColumns at its SyntaxId as a list made once and emptied in place by each pass
    /// (ResetSpecializations), so a warm rebind reuses it. Its members keep the names its readers use.</summary>
    private readonly struct SpecializationListRows(Binding binding)
    {
        internal FamilyRows<List<FunctionKoto>?> Values => new(binding, static (table, _) => table.Hir.Families.SpecializationsByOriginal.Enumerate());

        internal static ref List<FunctionKoto>? Slot(BindingSymbol original) => ref HirTables.Of(original.Declaration).Families.SpecializationsByOriginal.Ref(original.Declaration.SyntaxId);

        internal static List<FunctionKoto>? GetValueOrDefault(BindingSymbol original)
            => original.Declaration.SyntaxOwner is { } table ? table.Hir.Families.SpecializationsByOriginal.Get(original.Declaration.SyntaxId) : null;

        internal static bool TryGetValue(BindingSymbol original, [NotNullWhen(true)] out List<FunctionKoto>? siblings) => (siblings = GetValueOrDefault(original)) is not null;
    }

    /// <summary>The `specializationStorage` family's one accessor (DECISIONS BU-220): a kept family, a specialization's entry, made at its
    /// first verification and kept in its declaration's module's NodeColumns at its SyntaxId, so a warm rebind reuses its slot arrays;
    /// PrepareSpecializations reads and writes it for the running pass's specializations only.</summary>
    private readonly struct SpecializationStorageRows
    {
        // The kept slot, read and written before the column's next Ref (DeclareKept's form).
        internal static ref Specialization? Slot(FunctionKoto function) => ref HirTables.Of(function).Families.SpecializationStorage.Ref(function.SyntaxId);
    }
}

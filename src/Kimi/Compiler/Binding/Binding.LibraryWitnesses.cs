// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The library witness table (SPEC 4.6.1, 8.7, 22.1, 22.2.1; IMPL 21.3.1; PLAN G32).
// - Responsibility: the one statement of which Contract of a built-in Type an internal Kimi group serves (WitnessGroup), of the witness that
//   group gives a requirement (GroupWitness) and of a witness's generic arguments at the Type (RentWitnessArguments). A built-in Type has
//   no source declaration to state a conformance, so the compiler records the conformance and the library writes its witness; so has
//   Wrapping<T>, whose declaration SPEC 3.1.1 gives no members, in both its forms (a wrapping scalar and the construction over an
//   abstract T).
// - Inputs: a Type and a Contract's identity; the group's member scope; the generic parameters of the group's functions; a Contract's built
//   requirements (ContractWitness), which PrepareContracts fills.
// - Outputs: a row (LibraryWitnessGroup); the witness, the group's one function named as the requirement, explicit specializations aside;
//   the receiver a group's functions state (WitnessReceiver); the requirement call at the witness (InstantiateLibraryWitness); the
//   arguments TyCtxt.LibraryInstance interns.
// - Invariants: a row's Contract declares one function requirement of its own, which the group names. A row proves its conformance
//   (ResolveConformance) and selects its requirements (ResolveRequirementWitness) whatever the group holds; a missing witness is None.
// - Prohibitions: no second statement of which Contract a built-in Type conforms to through a group; a receiver's Type (WitnessReceiver) and
//   the fixed array's member lookup are other facts. A new built-in Type's conformance adds a row, never a witness kind, a proof arm or a
//   name switch: a WitnessMembers value, a WitnessGroup arm, a WitnessReceiver arm, a KimiLibrary.Members arm and the group's property,
//   and a CallerLocation catalog row for the group's function when its witness can Abort on its caller's behalf (SPEC 22.5.1, until G1).
//   No member lookup, diagnostic or written state beyond interning.

/// <summary>The internal Kimi groups of the witness table.</summary>
internal enum WitnessMembers : byte
{
    FixedArray,
    IntegerPosition,
    String,
    BoolFormat,
    CharFormat,
    UnitFormat,
    Utf8SliceFormat,
    F32Format,
    F64Format,
    WrappingFormat,
    IntegerFormat,
}

public sealed partial class Binding
{
    // A row: the group, and the generic arguments its functions take at the Self: TypeArgument for the parameter TypeParameter and, when
    // LengthParameter names a parameter, the Self's length; a row of neither has count 0.
    internal readonly record struct LibraryWitnessGroup(WitnessMembers Members, string? TypeParameter, BoundType? TypeArgument, string? LengthParameter);

    // SPEC 22.1, 4.6.1, 8.7, 13.4.1: the table of the built-in Types' library conformances, one arm per Type, over a Type and a Contract's
    // identity, which every reader holds: a fixed array's entry Contracts (PLAN G32), every integer Type's Position, string's two
    // comparison Contracts and its Utf8Format, and bool's, char's, Unit's, the view's (Text.Utf8Slice), f32's and f64's Utf8Format (SPEC
    // 22.10.2), whose bodies are not generic, so their rows have no parameter (no Contract holds exactly f32 and f64, and SPEC 13.5.4's bit
    // conversion needs a closed Type); Wrapping<T>'s Utf8Format (SPEC 3.1.1) over its argument I, a wrapping scalar's integer or the
    // construction's abstract T, which SPEC 3.1.1's formation proves PrimitiveInteger, so the row proves an abstract Wrapping<T>'s
    // conformance too; and every concrete integer Type's Utf8Format (R8-S2b1, R8-S2b2), one generic body over I whose i128 and u128
    // specializations format through formatInteger128. A bare Type parameter has no shape to key on, so an abstract `I is PrimitiveInteger`
    // keeps SPEC 8.7.4's premise, as for Position.
    internal static LibraryWitnessGroup? WitnessGroup(BoundType? self, KimiDeclarationId? contract) => self switch
    {
        { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner }
            when contract is KimiDeclarationId.Iterable or KimiDeclarationId.UniqIterable or KimiDeclarationId.IntoIterable
            => new(WitnessMembers.FixedArray, "E", self.Components[0], "N"),
        { IsInteger: true, Semantics: SemanticsKind.Owner } when contract == KimiDeclarationId.Position
            => new(WitnessMembers.IntegerPosition, "I", self, null),
        { IsInteger: true, Semantics: SemanticsKind.Owner } when contract == KimiDeclarationId.Utf8Format
            => new(WitnessMembers.IntegerFormat, "I", self, null),
        _ when ReferenceEquals(self, BoundType.String) &&
            contract is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable or KimiDeclarationId.Utf8Format
            => new(WitnessMembers.String, null, null, null),
        { IsWrappingInteger: true, Semantics: SemanticsKind.Owner } when contract == KimiDeclarationId.Utf8Format
            => new(WitnessMembers.WrappingFormat, "I", self.Underlying, null),
        { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Components.Count: 1 }
            when contract == KimiDeclarationId.Utf8Format && self.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping
            => new(WitnessMembers.WrappingFormat, "I", self.Components[0], null),
        _ when contract == KimiDeclarationId.Utf8Format && ReferenceEquals(self, BoundType.Boolean) => new(WitnessMembers.BoolFormat, null, null, null),
        _ when contract == KimiDeclarationId.Utf8Format && ReferenceEquals(self, BoundType.Char) => new(WitnessMembers.CharFormat, null, null, null),
        _ when contract == KimiDeclarationId.Utf8Format && ReferenceEquals(self, BoundType.Unit) => new(WitnessMembers.UnitFormat, null, null, null),
        { Semantics: SemanticsKind.Owner, Symbol.LibraryDeclaration: KimiDeclarationId.Utf8Slice } when contract == KimiDeclarationId.Utf8Format
            => new(WitnessMembers.Utf8SliceFormat, null, null, null),
        _ when contract == KimiDeclarationId.Utf8Format && ReferenceEquals(self, BoundType.F32) => new(WitnessMembers.F32Format, null, null, null),
        _ when contract == KimiDeclarationId.Utf8Format && ReferenceEquals(self, BoundType.F64) => new(WitnessMembers.F64Format, null, null, null),
        _ => null,
    };

    // Rents a group function's generic arguments at the Self, at their slots and null elsewhere (the materializer, TyCtxt.LibraryInstance
    // and FixedArrayIteratorType).
    internal bool RentWitnessArguments(
        in LibraryWitnessGroup group, BindingSymbol witness, BoundType self, out BoundType?[] types, out BoundLength?[] lengths, out int count)
    {
        types = null!;
        lengths = null!;
        count = 0;
        if (!this.WitnessSlots(group, witness, out var type, out var length))
        {
            return false;
        }

        count = Math.Max(type, length) + 1;
        types = this.RentTypes(count);
        lengths = this.lengthScratch.Rent(count);
        if (type >= 0)
        {
            types[type] = group.TypeArgument;
        }

        if (length >= 0)
        {
            lengths[length] = self.LengthExpression ?? this.InternLength(LengthOperation.Constant, self.Length);
        }

        return true;
    }

    internal void ReturnWitnessArguments(BoundType?[] types, BoundLength?[] lengths)
    {
        this.lengthScratch.Return(lengths, clearArray: true);
        this.typeScratch.Return(types, clearArray: true);
    }

    // SPEC 22.1, 4.6.1, 8.7: the receiver a group's functions state: some `[N of E]`, in the ref, uniq or owning form (PLAN G32); an
    // integer or a Type parameter (SPEC 4.6.1); string, which the group's functions write as `ref/string` (SPEC 13.4.1), read through at
    // most one layer, so a bare string passes too, though nothing else reaches this; bool, char, Unit, a view, f32 and f64, which the
    // formatting bodies write as `ref/bool`, `ref/char`, `ref/()`, `ref/Text.Utf8Slice`, `ref/f32` and `ref/f64` (SPEC 22.10.2), likewise;
    // some `Wrapping<I>`, a wrapping scalar or the construction the body writes as `ref/Wrapping<I>` (SPEC 3.1.1); the integers' formatting
    // body's `ref/I`, a Type parameter, and its specializations' `ref/i128` and `ref/u128` (R8-S2b1, R8-S2b2). A new group states its own;
    // none is another's by default.
    private static bool WitnessReceiver(WitnessMembers members, BoundType type) => members switch
    {
        WitnessMembers.FixedArray => EffectiveCore(type) is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner },
        WitnessMembers.IntegerPosition
            => type is { Kind: BoundTypeKind.Parameter, Semantics: SemanticsKind.Owner } or { IsInteger: true, Semantics: SemanticsKind.Owner },
        WitnessMembers.String => ReferenceEquals(EffectiveCore(type), BoundType.String),
        WitnessMembers.BoolFormat => ReferenceEquals(EffectiveCore(type), BoundType.Boolean),
        WitnessMembers.CharFormat => ReferenceEquals(EffectiveCore(type), BoundType.Char),
        WitnessMembers.UnitFormat => ReferenceEquals(EffectiveCore(type), BoundType.Unit),
        WitnessMembers.Utf8SliceFormat => EffectiveCore(type) is { Symbol.LibraryDeclaration: KimiDeclarationId.Utf8Slice },
        WitnessMembers.F32Format => ReferenceEquals(EffectiveCore(type), BoundType.F32),
        WitnessMembers.F64Format => ReferenceEquals(EffectiveCore(type), BoundType.F64),
        WitnessMembers.WrappingFormat => EffectiveCore(type) is { IsWrappingInteger: true } or
            { Kind: BoundTypeKind.Constructed, Symbol.LibraryDeclaration: KimiDeclarationId.Wrapping },
        WitnessMembers.IntegerFormat
            => EffectiveCore(type) is { Kind: BoundTypeKind.Parameter, Semantics: SemanticsKind.Owner } or { IsInteger: true, Semantics: SemanticsKind.Owner },
        _ => throw new InvalidOperationException($"The group {members} states no receiver."),
    };

    // The group's one function of a requirement's Name, explicit specializations aside, which share it; null when it lacks exactly one.
    private BindingSymbol? GroupWitness(in LibraryWitnessGroup group, string name)
    {
        if (this.Library.Members(group.Members) is not { } members || !this.scopes.TryGetValue(members, out var scope) ||
            !scope.Values.TryGetValue(name, out var candidate))
        {
            return null;
        }

        BindingSymbol? witness = null;
        for (var current = candidate; current is not null; current = current.Next)
        {
            if (current.Declaration is FunctionKoto { IsSpecialization: false })
            {
                if (witness is not null)
                {
                    return null;
                }

                witness = current;
            }
        }

        return witness;
    }

    // The witness of a row's Contract, for a reader that holds the Contract and no requirement: GroupWitness of its own requirement.
    private BindingSymbol? ContractWitness(in LibraryWitnessGroup group, BindingSymbol contract)
        => contract.Contract?.OwnRequirement() is { } requirement ? this.GroupWitness(group, requirement.Name) : null;

    // The slots of a group function's row parameters, which share one numbering; -1 for a parameter the row does not name.
    private bool WitnessSlots(in LibraryWitnessGroup group, BindingSymbol witness, out int type, out int length)
    {
        type = length = -1;
        if (!this.scopes.TryGetValue(witness.Declaration, out var scope))
        {
            return false;
        }

        if (group.TypeParameter is { } typeName && scope.Types.TryGetValue(typeName, out var parameter))
        {
            type = parameter.Slot;
        }

        if (group.LengthParameter is { } lengthName && scope.Values.TryGetValue(lengthName, out var extent))
        {
            length = extent.Slot;
        }

        return (group.TypeParameter is null || type >= 0) && (group.LengthParameter is null || (length >= 0 && length != type));
    }

    // SPEC 21.3.1: a requirement call on a Type of the table instantiates the group's witness at the Type's arguments.
    private CallPlan? InstantiateLibraryWitness(CallPlan call, BoundType self, in RequirementWitness witness, CallPlan? destination)
    {
        var target = witness.Target!;
        if (!this.RentWitnessArguments(witness.Group, target, self, out var types, out var lengths, out var count))
        {
            return null;
        }

        try
        {
            var resolved = destination ?? new CallPlan();
            resolved.Set(target, call.ReturnType, call.Receiver, call.ArgumentToParameter, types.AsSpan(0, count), origins: call.Origins, inputOrigins: call.InputOrigins, operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, defaults: call.DefaultArguments, lengthArguments: lengths.AsSpan(0, count));
            return resolved;
        }
        finally
        {
            this.ReturnWitnessArguments(types, lengths);
        }
    }
}

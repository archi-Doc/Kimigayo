// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The library witness table (SPEC 4.6.1, 8.7, 22.1, 22.2.1; IMPL 21.3.1; PLAN G32).
// - Responsibility: the one statement of which Contract of a built-in Type an internal Kimi group serves (WitnessGroup), of the witness that
//   group gives a requirement (GroupWitness) and of a witness's generic arguments at the Type (RentWitnessArguments). A built-in Type has
//   no source declaration to state a conformance, so the compiler records the conformance and the library writes its witness.
// - Inputs: a Type and a Contract's identity; the group's member scope; the generic parameters of the group's functions; a Contract's built
//   requirements (ContractWitness), which PrepareContracts fills.
// - Outputs: a row (LibraryWitnessGroup); the witness, the group's one function named as the requirement, explicit specializations aside;
//   the receiver a group's functions state (WitnessReceiver); the requirement call at the witness (InstantiateLibraryWitness); the
//   arguments TyCtxt.LibraryWitness interns.
// - Invariants: a row's Contract declares one function requirement of its own, which the group names. A row proves its conformance
//   (ResolveConformance) and selects its requirements (ResolveRequirementWitness) whatever the group holds; a missing witness is None.
// - Prohibitions: no second statement of which Contract a built-in Type conforms to through a group; a receiver's Type (WitnessReceiver) and
//   the fixed array's member lookup are other facts. A new built-in Type's conformance adds a row, never a witness kind, a proof arm or a
//   name switch: a WitnessMembers value, a WitnessGroup arm, a WitnessReceiver arm, a KimiLibrary.Members arm and the group's property.
//   No member lookup, diagnostic or written state beyond interning.

/// <summary>The internal Kimi groups of the witness table.</summary>
internal enum WitnessMembers : byte
{
    FixedArray,
    IntegerPosition,
    String,
}

public sealed partial class Binding
{
    // A row: the group, and the generic arguments its functions take at the Self: TypeArgument for the parameter TypeParameter and, when
    // LengthParameter names a parameter, the Self's length; a row of neither has count 0.
    internal readonly record struct LibraryWitnessGroup(WitnessMembers Members, string? TypeParameter, BoundType? TypeArgument, string? LengthParameter)
    {
        // Whether Witnesses answers false for the row, A4's Limit: the group's functions are generic, and that answer, a target and a declaring
        // Type, has no place for their arguments, so A4 would read the body at abstract `E`, `N` or `I` and decide an effect of a valid program.
        // The Limit is a located Unsupported_Kd where a default's copy calls the requirement on a closed Self under a confined obligation
        // (MirEffectsTest). A row of no parameter has its body read.
        [Transitional("R7", Reason = "R7-P5: Witnesses answers a witness's instance with its Types (A-12's read of the view, else R7), and this goes.")]
        internal bool EffectsLimit => this.TypeParameter is not null;
    }

    // SPEC 22.1, 4.6.1, 8.7: the table of the built-in Types' library conformances, one arm per Type, over a Type and a Contract's identity,
    // which every reader holds: a fixed array's entry Contracts (PLAN G32), and every integer Type's Position.
    internal static LibraryWitnessGroup? WitnessGroup(BoundType? self, KimiDeclarationId? contract) => self switch
    {
        { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner }
            when contract is KimiDeclarationId.Iterable or KimiDeclarationId.UniqIterable or KimiDeclarationId.IntoIterable
            => new(WitnessMembers.FixedArray, "E", self.Components[0], "N"),
        { IsInteger: true, Semantics: SemanticsKind.Owner } when contract == KimiDeclarationId.Position
            => new(WitnessMembers.IntegerPosition, "I", self, null),
        _ => null,
    };

    // Rents a group function's generic arguments at the Self, at their slots and null elsewhere (the materializer, TyCtxt.LibraryWitness
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
            lengths[length] = self.LengthExpression ?? this.InternLength(KotoKind.NumberLiteral, self.Length);
        }

        return true;
    }

    internal void ReturnWitnessArguments(BoundType?[] types, BoundLength?[] lengths)
    {
        this.lengthScratch.Return(lengths, clearArray: true);
        this.typeScratch.Return(types, clearArray: true);
    }

    // SPEC 22.1, 4.6.1, 8.7: the receiver a group's functions state: some `[N of E]`, in the ref, uniq or owning form (PLAN G32); an integer
    // or a Type parameter (SPEC 4.6.1); string below any one layer (SPEC 13.4.1). A new group states its own; none is another's by default.
    private static bool WitnessReceiver(WitnessMembers members, BoundType type) => members switch
    {
        WitnessMembers.FixedArray => EffectiveCore(type) is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner },
        WitnessMembers.IntegerPosition
            => type is { Kind: BoundTypeKind.Parameter, Semantics: SemanticsKind.Owner } or { IsInteger: true, Semantics: SemanticsKind.Owner },
        WitnessMembers.String => ReferenceEquals(EffectiveCore(type), BoundType.String),
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

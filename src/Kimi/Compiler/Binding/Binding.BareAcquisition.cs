// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Bare acquisition (SPEC 3.5, 3.5.1, 10.2): a position without a fixed expected Type acquires its operand by bare acquisition.
// - Responsibility: the positions that do (IsBareAcquisitionPosition) and the Reborrow of a Place storing an exclusive reference there
//   (StoredReborrow), recorded as the node's one adaptation, exactly as an annotation of the same complete Type would record it; whether
//   the consumer of a value acquires it by value without an operator at any value position (AcquiredByValue, the list until the
//   Binding-time "consumed by value" fact the HIR publisher's Consume will read too, the owner 2026-10-25; DECISIONS R4-M128), and the
//   operation by which a method or accessor call takes its receiver (ReceiverOperation).
// - Inputs: the node, its bound Type, its Place (IsBarePlace) and the common adaptation (ExpectedAdaptation), which names the Reborrow;
//   the recorded adaptations, Discard Context (KotoHelper.IsValueContext), the call plans of a node's consumer (CallPlan.Of), its
//   enum Case construction and the accessor calls.
// - Outputs: the recorded adaptation and its Type; the bare entries of pair layers per Semantics case are Binding.PairCases'.
// - Invariants: a position with a fixed expected Type adapts by the common adaptation instead; AcquiredByValue answers only for the
//   consumers its list names, and null for any other.
// - Prohibitions: no consumer decides by its own position test whether a bare acquisition Reborrows or whether a value is acquired.
public sealed partial class Binding
{
    // SPEC 3.5, 8.14 (DECISIONS R4-M128): whether the consumer of the value at `node` (its parentheses unwrapped) acquires it by value
    // without an operator, which a pair layer does per admitted case, read from the consumer where one decides: false in Discard Context,
    // where no consumer takes it (a statement, a discarded body), or where an adaptation recorded at it decides; true at a bare position, a
    // Dictionary literal's key or value, or an input its consumer's operation takes by value (a receiver or argument of any callee's plan,
    // an enum Case's payload); else by the consumer: true at an assignment's source, an `and`/`or` operand, a Range's bound, a built-in
    // index key and a conversion's operand under the bare rule, false at a write or update target, an inspected operator operand (SPEC
    // 13.4), a selection through it, a runtime test, a borrow, an address, a follow, a transfer, a view upcast that borrows, a `for` or
    // `match` Subject and a value call's callee; null at any other consumer, which this list, a copy of the HIR publisher's Consume until
    // one Binding-time fact replaces both (the owner, 2026-10-25), does not decide.
    internal bool? AcquiredByValue(Koto node)
    {
        var target = node;
        while (target.Parent is ParenthesizedKoto parenthesized)
        {
            target = parenthesized;
        }

        if (!KotoHelper.IsValueContext(target) || this.adaptations.ContainsKey(target))
        {
            return false;
        }

        if (IsBareAcquisitionPosition(target) || target.Parent is DictionaryLiteralKoto)
        {
            return true;
        }

        if ((this.ReceiverOperation(target) ?? this.ConsumerInput(target)) is { } input)
        {
            return input.Kind == ArgumentOperationKind.Value;
        }

        return target.Parent switch
        {
            IsKoto or UnaryKoto or MemberAccessKoto => false,
            IndexKoto index => !ReferenceEquals(index.Left, target),
            ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address or ConversionBinding.Transfer } => false,
            ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } => false,
            ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true } view } upcast => upcast.Left.BoundType?.Semantics == view.Semantics,
            ConversionKoto => true,
            BinaryKoto { Akind: >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } assignment => ReferenceEquals(assignment.Right, target),
            BinaryKoto { Akind: KotoKind.And or KotoKind.Or } or RangeKoto => true,
            BinaryKoto { Akind: not (KotoKind.OriginRelation or KotoKind.Is) } => false,
            ForKoto loop => ReferenceEquals(loop.Iterable, target) ? false : null,
            MatchKoto match => ReferenceEquals(match.Expression, target) ? false : null,
            InvocationKoto { BoundValueCall: { } call } when ReferenceEquals(call.CalleeValue, target) => false,
            _ => null,
        };
    }

    // The operation by which the method or accessor call whose receiver a Place is takes it (SPEC 7.3.3, 11.3), or null.
    internal BoundArgumentOperation? ReceiverOperation(Koto place)
    {
        var call = place.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
            ReferenceEquals(member.Left, place) && ReferenceEquals(selected.Receiver, place) ? selected :
            place.Parent is MemberAccessKoto property && ReferenceEquals(property.Left, place)
                ? (this.PropertyCall(property, PropertyAccessorKind.Set) ?? this.PropertyCall(property, PropertyAccessorKind.Get))?.BoundCall : null;
        if (call is null || call.ReceiverOperation.Source is not null)
        {
            return call?.ReceiverOperation;
        }

        // Accessor calls pass their receiver as an explicit argument; ordinary methods retain it separately.
        foreach (var argument in call.ArgumentOperations)
        {
            if (ReferenceEquals(argument.Source, place))
            {
                return argument;
            }
        }

        return call.ReceiverOperation;
    }

    // SPEC 3.5, 10.2: the positions without a fixed expected Type that acquire any Place operand by bare acquisition.
    // Arguments, assignment sources and results take their expected Type from the declaration and adapt there instead. A result source
    // (SPEC 14.9.1, 14.9.2) without a fixed Target Result Type is one: an anonymous function's expression body without a written or
    // expected result Type, a jump's operand, and a selection's or `do`'s body when its value is used (ResultTarget); the join's
    // common Type is chosen from the Types its sources take here.
    private static bool IsBareAcquisitionPosition(Koto node)
    {
        var target = node;
        while (target.Parent is ParenthesizedKoto parenthesized)
        {
            target = parenthesized;
        }

        return target.Parent switch
        {
            VariableKoto variable => ReferenceEquals(variable.InitializerKoto, target),
            DiscardKoto discard => ReferenceEquals(discard.Operand, target),
            TupleLiteralKoto or ArrayLiteralKoto => true,
            FunctionKoto { IsAnonymous: true } function => ReferenceEquals(function.ExpressionBody, target),
            JumpKoto jump => ReferenceEquals(jump.Expression, target),
            _ => ResultTarget(target) is IfKoto or MatchKoto or DoKoto && KotoHelper.IsValueContext(target),
        };
    }

    // The operation by which the consumer of `target` takes it: a receiver or argument of one of its calls, or an enum Case's payload.
    private BoundArgumentOperation? ConsumerInput(Koto target)
    {
        if (target.Parent is not { } parent)
        {
            return null;
        }

        for (var i = 0; CallPlan.Of(parent, i) is { } call; i++)
        {
            if (ReferenceEquals(call.ReceiverOperation.Source, target))
            {
                return call.ReceiverOperation;
            }

            if (Input(call.ArgumentOperations, target) is { } argument)
            {
                return argument;
            }
        }

        return this.TryGetEnumConstruction(parent, out var construction) && construction is not null ? Input(construction.PayloadOperations, target) : null;

        static BoundArgumentOperation? Input(ReadOnlySpan<BoundArgumentOperation> operations, Koto target)
        {
            foreach (ref readonly var operation in operations)
            {
                if (ReferenceEquals(operation.Source, target))
                {
                    return operation;
                }
            }

            return null;
        }
    }

    // SPEC 3.5.1: the bare acquisition of a Place storing an exclusive reference, `uniq/T` or `objuniq/T`, Reborrows it in its own
    // Semantics, exactly as an annotation of the same complete Type would; the Reborrow's Type is Binding's name for it (DECISIONS
    // BU-27), which a join's common Type takes from each such source. Returns that Type, null when the node is acquired otherwise.
    private BoundType? StoredReborrow(Koto node, BoundType? actual)
    {
        if (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } &&
            node.ErasedFunctionType is null && IsBareAcquisitionPosition(node) && IsBarePlace(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return null;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the HIR contract between the publisher and the MIR builder: the Binding facts each published Op guarantees (DECISIONS R4-M61).
// Input: a node, the Op its form calls for, Binding's plans and symbols. Output: whether the node has every fact its Op's building rule reads.
// The publisher gives a node an Op only with its facts, so the builder trusts them; a fact the builder starts reading adds its row here.
// Must not decide an Op, require a fact the builder does not read for it (a Type stands for Binding's resolution of the node), or leave
// the builder a fallback for a missing fact.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets whether an Op is an operation (SPEC 3.1, 4.7, 6.2.2, 12.3, 13.2-13.6): a literal's constant, a Field, a Deref, a
    /// built-in selection, a built-in operator, <c>and</c>/<c>or</c>, a borrow, a conversion or a runtime Type test, whose building forms the
    /// node's value or Place from Binding's selection for the node, for which the node's Type stands (<see cref="HasFacts"/>); one Binding
    /// left unresolved is an Error that keeps every operand its form evaluates (DECISIONS R4-M88). A new HirOp member decides here
    /// whether it is one.</summary>
    /// <param name="op">The Op a node's form calls for.</param>
    /// <returns>Whether it is an operation.</returns>
    internal static bool Operation(HirOp op) => op is HirOp.Literal or HirOp.Field or HirOp.Deref or HirOp.Index or HirOp.PrimitiveOp or
        HirOp.Logical or HirOp.Borrow or HirOp.Convert or HirOp.RuntimeTypeTest;

    /// <summary>Gets whether an Op is a construct whose parts deliver its value into its destination, its bodies or its exits (SPEC 14.2,
    /// 14.3, 14.6.3, 14.7, 14.8, 17): a block, an unsafe block, a do, an if, a match, a try or a loop, which, Binding having left it
    /// without a Type, needs a destination of the Type its position fixes; an operation Binding left unresolved has none for its operands,
    /// so it replaces such an operand, also a labeled one (DECISIONS R4-M88, PLAN G121). A new HirOp member decides here whether it is one.</summary>
    /// <param name="op">The Op a node's form calls for.</param>
    /// <returns>Whether it is such a construct.</returns>
    internal static bool Construct(HirOp op) => op is HirOp.Block or HirOp.Unsafe or HirOp.Do or HirOp.If or HirOp.Match or HirOp.Try or
        HirOp.Loop;

    /// <summary>Gets whether a node has the facts the MIR builder reads for its Op: an operation its Type (<see cref="Operation"/>),
    /// every call the builder builds for it complete (each argument with its Source, a direct base call with its implementation's
    /// symbol), and per Op: a call its plan (an index's selected call; a
    /// tuple operator's with its conforming Type and its two arguments, which its expansion reads, DECISIONS R5-M75), a
    /// key resolution its Evaluated argument, an assignment or update of a Property its accessors' plans (an update's setter with its
    /// Evaluated argument) and, for an update with one standard accessor, the Property's symbol; an interpolation its Type, heap,
    /// adapter (or a <c>$tryWrite</c>'s status) and writes, each with its two arguments and a value's Type; a for loop its Type and a
    /// user loop's entry, step, iterator symbol and decomposition (a counted one its bindings' symbols); a match its current plan; a
    /// dictionary literal its lookup; a closure its definition; a conversion its binding (a case adaptation its plan); a Tuple or array
    /// literal its Type and an enum Case construction its plan; a declared local and a name their symbols.</summary>
    /// <param name="node">An evaluated node.</param>
    /// <param name="op">The Op its form calls for.</param>
    /// <returns>Whether the node has its facts.</returns>
    internal bool HasFacts(Koto node, HirOp op)
    {
        if (Operation(op) && HirTables.Meaning(node) is null)
        {
            return false;
        }

        switch (op)
        {
            case HirOp.Call or HirOp.Index when node is IndexKoto index:
                var keyed = Planned(this.ResolvedKeyCall(index), out var key) && (key is null || key.Evaluated >= 0);
                var selected = op == HirOp.Index || (Planned(this.SelectedIndexCall(index, exclusive: false), out var shared) && shared is not null);
                return keyed && selected && Planned(this.IndexerCall(index, exclusive: true), out _); // indexUniq, bound only beside index.
            case HirOp.Call:
                return CallPlan.Of(node) is { } call && Complete(call) &&
                    (!call.TupleOperator || call is { ConformingType: not null, ArgumentOperations.Length: 2 });
            case HirOp.Assign or HirOp.CompoundAssign:
                var target = KotoHelper.UnwrapParentheses(node is BinaryKoto update ? update.Left : ((UnaryKoto)node).Operand);
                if (!Planned(this.PropertyCall(target, PropertyAccessorKind.Get), out var getter) ||
                    !Planned(this.PropertyCall(target, PropertyAccessorKind.Set), out var setter))
                {
                    return false;
                }

                // An update builds its arithmetic call, and its setter takes the computed value; with one standard accessor it reads or
                // writes the Property's storage.
                return op == HirOp.Assign || ((CallPlan.Of(node) is not { } arithmetic || Complete(arithmetic)) &&
                    (setter is null || setter.Evaluated >= 0) &&
                    ((getter is null) == (setter is null) || HirTables.Symbol(target) is not null));
            case HirOp.Interpolation:
                return HirTables.Meaning(node) is not null && node.Formatting is { Writes.Count: > 0 } format && Formats(node, format);
            case HirOp.For:
                return HirTables.Meaning(node) is not null && (BoundMatch.Of(node), CallPlan.Of(node), CallPlan.Of(node, 1)) switch
                {
                    (null, null, null) => Declared(((ForKoto)node).Bindings),
                    (not null, { } entry, { Receiver: { } receiver } step) => Complete(entry) && Complete(step) && HirTables.Symbol(receiver) is not null,
                    _ => false,
                };
            case HirOp.Match or HirOp.Try:
                return BoundMatch.Of(node) is { IsCurrent: true };
            case HirOp.DictionaryLiteral:
                return CallPlan.Of(node) is { } find && Complete(find);
            case HirOp.Closure:
                return ((FunctionKoto)node).ClosureDefinition is not null;
            case HirOp.Convert:
                return ((ConversionKoto)node).ConversionBinding is not ConversionBinding.None and var binding &&
                    (binding != ConversionBinding.CaseAdaptation || HirTables.PlanOf(node).Plan is ExplicitAdaptationPlan);
            case HirOp.Aggregate:
                return node is TupleLiteralKoto or ArrayLiteralKoto ? HirTables.Meaning(node) is not null
                    : BoundEnumConstruction.Of(node) is { } construction && Sourced(construction.PayloadOperations);
            case HirOp.Let:
                return HirTables.Symbol(node) is not null;
            case HirOp.Name:
                return HirTables.Symbol(node) is { } symbol &&
                    (symbol.Kind != BindingSymbolKind.Storage || (symbol.Declaration is { } property && HirTables.Symbol(property) is not null));
            default:
                return true;
        }

        // A call the builder builds: each argument from its Source, a direct base call of the implementation its selection names.
        static bool Complete(CallPlan call)
        {
            var direct = call.Kind == CalleeKind.Virtual && call.VirtualIsDirect;
            return Sourced(call.ArgumentOperations) && (!direct || (call.VirtualImplementation is { } body && HirTables.Symbol(body) is not null));
        }

        static bool Sourced(ReadOnlySpan<BoundArgumentOperation> operations)
        {
            foreach (ref readonly var operation in operations)
            {
                if (operation.Source is null)
                {
                    return false;
                }
            }

            return true;
        }

        // A call Binding selected has its complete plan; no call is no plan.
        static bool Planned(InvocationKoto? call, out CallPlan? plan)
        {
            plan = call?.CallStorage;
            return call is null || (plan is not null && Complete(plan));
        }

        // The calls of a formatting plan in CallPlan.Of's order: an interpolation's heap (whose Type alone the builder reads, FormatBuffer
        // serving it) and adapter (taking the buffer), then its writes; a $tryWrite's writes, then its status. Each write takes the writer
        // and its text or value, of the value's Type.
        static bool Formats(Koto node, BoundFormatting format)
        {
            var first = format.Acquisition is null ? 2 : 0;
            if (first > 0 && (CallPlan.Of(node) is null || CallPlan.Of(node, 1) is not { ArgumentOperations.Length: > 0 } adapter || !Complete(adapter)))
            {
                return false;
            }

            for (var i = 0; i < format.Writes.Count; i++)
            {
                if (CallPlan.Of(node, first + i) is not { ArgumentOperations.Length: 2 } write || !Complete(write) ||
                    (!WritesText(write) && write.TypeArguments is not [not null, ..]))
                {
                    return false;
                }
            }

            return first > 0 || (CallPlan.Of(node, format.Writes.Count) is { } status && Complete(status));
        }

        static bool Declared(IReadOnlyList<Koto> bindings)
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                if (HirTables.Symbol(bindings[i]) is null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

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
    /// left unresolved is an Error, which keeps the operands its form evaluates on every path (DECISIONS R4-M88, Z16). A new HirOp member
    /// decides here whether it is one.</summary>
    /// <param name="op">The Op a node's form calls for.</param>
    /// <returns>Whether it is an operation.</returns>
    internal static bool Operation(HirOp op) => op is HirOp.Literal or HirOp.Field or HirOp.Deref or HirOp.Index or HirOp.PrimitiveOp or
        HirOp.Logical or HirOp.Borrow or HirOp.Convert or HirOp.RuntimeTypeTest;

    /// <summary>Gets whether a published node is a failure fact, from which the MIR builder derives a value or a check (DECISIONS Z16):
    /// an <see cref="HirOp.Error"/>; a consumed Place or a Pattern binding whose acquisition failed (<see cref="HirAcquire.Failed"/>, at
    /// the acquired node, in its column or its plan row); or the declaration of a stored Property whose value the builder reads, not
    /// published <see cref="HirOp.Body"/>: a static one (its uses' Place) or one with a written initializer (each constructor). Each has
    /// its causes (<see cref="RestingCauses"/>), which the publisher checks (<see cref="HirTables.Broken"/>).</summary>
    /// <param name="node">A published node.</param>
    /// <param name="op">Its published Op.</param>
    /// <returns>Whether it is a failure fact.</returns>
    internal static bool FailureFact(Koto node, HirOp op) => op == HirOp.Error || HirTables.AdjustOf(node).Acquire == HirAcquire.Failed ||
        HirTables.PlanOf(node).Adjust.Acquire == HirAcquire.Failed ||
        (op != HirOp.Body && node is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Let or PropertyDeclarationKind.Var } property &&
        (property.InitializerKoto is not null || property.Parent is DeclarationContainerKoto { HasStaticMembersOnly: true }));

    /// <summary>Gets whether a node has the facts the MIR builder reads for its Op: an operation its Type (<see cref="Operation"/>),
    /// every call the builder builds for it complete (each argument with its Source, a direct base call with its implementation's
    /// symbol), and per Op: a sequence's metadata its form (<see cref="BuiltinForm"/>), a call its plan (an index's selected call; one
    /// no expansion supplies inputs to with its operations, omitted defaults and receiver covering its parameters; a
    /// tuple operator's with its conforming Type and its two arguments, which its expansion reads, DECISIONS R5-M75), a
    /// key resolution its Evaluated argument, an assignment or update of a Property its accessors' plans (an update's setter with its
    /// Evaluated argument) and, for an update with one standard accessor, the Property's symbol; an interpolation its Type, heap,
    /// adapter (or a <c>$tryWrite</c>'s status) and writes, each with its two arguments and a value's Type; a for loop its Type and
    /// its entry, step, iterator symbol and decomposition; a match or a try its current plan, valid (no Pattern or guard failed) and not
    /// pending; a dictionary literal its lookup; a closure its definition; a conversion its binding (a case adaptation its plan); a Tuple
    /// literal its Type, an array literal a fixed-array Type or, without a fill, an Array Type, and an enum Case construction its plan; a
    /// declared local its symbol; a name its symbol and, but for a function's name Binding resolved, whose value its consumer forms from the
    /// selection, its Type (DECISIONS Z16: a name of a binding without a Type, or of a function Binding left unresolved, is an Error, which
    /// rests on that binding's failure or on its signature's).</summary>
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
            case HirOp.Call: // A call the builder builds as it is (no expansion supplies its inputs) covers its parameters.
                return CallPlan.Of(this, node) is { } call && Complete(call) && (call.TupleOperator
                    ? call is { ConformingType: not null, ArgumentOperations.Length: 2 } : ExpansionPlan.Of(this, node) is not null || call.Covers());
            case HirOp.Assign or HirOp.CompoundAssign:
                var target = KotoHelper.UnwrapParentheses(node is BinaryKoto update ? update.Left : ((UnaryKoto)node).Operand);
                if (!Planned(this.PropertyCall(target, PropertyAccessorKind.Get), out var getter) ||
                    !Planned(this.PropertyCall(target, PropertyAccessorKind.Set), out var setter))
                {
                    return false;
                }

                // An update builds its arithmetic call, and its setter takes the computed value; with one standard accessor it reads or
                // writes the Property's storage.
                return op == HirOp.Assign || ((CallPlan.Of(this, node) is not { } arithmetic || Complete(arithmetic)) &&
                    (setter is null || setter.Evaluated >= 0) &&
                    ((getter is null) == (setter is null) || HirTables.Symbol(target) is not null));
            case HirOp.Interpolation:
                return HirTables.Meaning(node) is not null && node.Formatting is { Writes.Count: > 0 } format && Formats(node, format);
            case HirOp.For:
                return HirTables.Meaning(node) is not null && BoundMatch.Of(this, node) is not null && CallPlan.Of(this, node) is { } entry &&
                    CallPlan.Of(this, node, 1) is { Receiver: { } receiver } step && Complete(entry) && Complete(step) && HirTables.Symbol(receiver) is not null;
            case HirOp.Match or HirOp.Try:
                return BoundMatch.Of(this, node) is { IsCurrent: true, Invalid: false, Pending: false };
            case HirOp.DictionaryLiteral:
                return CallPlan.Of(this, node) is { } find && Complete(find);
            case HirOp.Closure:
                return ((FunctionKoto)node).ClosureDefinition is not null;
            case HirOp.Convert:
                return ((ConversionKoto)node).ConversionBinding is not ConversionBinding.None and var binding &&
                    (binding != ConversionBinding.CaseAdaptation || HirTables.PlanOf(node).Plan is ExplicitAdaptationPlan);
            case HirOp.Aggregate: // A Tuple, a fixed array, an Array without a fill (SPEC 4.3.3) or an enum Case construction.
                return node switch
                {
                    TupleLiteralKoto => HirTables.Meaning(node) is not null,
                    ArrayLiteralKoto array => HirTables.TypeOf(node)?.Kind is BoundTypeKind.FixedArray ||
                        (array.FillLength is null && HirTables.TypeOf(node)?.Kind is BoundTypeKind.Array),
                    _ => BoundEnumConstruction.Of(this, node) is { } construction && Sourced(construction.PayloadOperations),
                };
            case HirOp.PrimitiveOp when node is MemberAccessKoto member: // A sequence's metadata, by its published form.
                return SequenceMember(member) != BuiltinForm.None;
            case HirOp.Let:
                return HirTables.Symbol(node) is not null;
            case HirOp.Name:
                return HirTables.Symbol(node) is { } symbol &&
                    (HirTables.Meaning(node) is not null || (symbol.Kind == BindingSymbolKind.Function && node.BindingState == BindingState.Resolved)) &&
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

        // The calls of a formatting plan in CallPlan.Of's order: an interpolation's heap (which the builder makes with the capacity 0) and
        // adapter (taking the buffer), then its writes; a $tryWrite's writes, then its status. Each write takes the writer and its text or
        // value, of the value's Type.
        bool Formats(Koto node, BoundFormatting format)
        {
            var first = format.Acquisition is null ? 2 : 0;
            if (first > 0 && (CallPlan.Of(this, node) is null || CallPlan.Of(this, node, 1) is not { ArgumentOperations.Length: > 0 } adapter || !Complete(adapter)))
            {
                return false;
            }

            for (var i = 0; i < format.Writes.Count; i++)
            {
                if (CallPlan.Of(this, node, first + i) is not { ArgumentOperations.Length: 2 } write || !Complete(write) ||
                    (!WritesText(write) && write.TypeArguments is not [not null, ..]))
                {
                    return false;
                }
            }

            return first > 0 || (CallPlan.Of(this, node, format.Writes.Count) is { } status && Complete(status));
        }
    }
}

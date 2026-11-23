// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the HIR contract between the publisher and the MIR builder: the Binding facts each published Op guarantees (DECISIONS R4-M61).
// Input: a node, the Op its form calls for, Binding's plans and symbols. Output: whether the node has every fact its Op's building rule reads,
// each slot it declares with its Type or its failed fact included (SlotsStated, PLAN G1073).
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
    /// the acquired node, in its column or its plan row); or the declaration of a stored Property whose value the builder reads, a static
    /// one (its uses' Place) or one with a written initializer (each constructor), not published <see cref="HirOp.Body"/>: a static with
    /// no initializer, or a declaration whose parts are unverified or whose slots are unstated. A declaration whose own Type failed is Body
    /// with its failed slot, which is that failure's published fact (PLAN G1073, DECISIONS D-834). Each has its causes
    /// (<see cref="RestingCauses"/>), which the publisher checks (<see cref="HirTables.Broken"/>).</summary>
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
    /// pending; a dictionary literal its lookup; a closure's creation its definition with a Signature (a closure's parts, which
    /// HirPublisher.Parts publishes, its definition and its slots stated, whatever the creation, DECISIONS R5-M63 (1) as amended); a
    /// conversion its binding (a case adaptation its plan); a Tuple literal its Type, an array literal a fixed-array Type or, without a
    /// fill, an Array Type, and an enum Case construction its plan; a declared local its symbol; a name its symbol and, but for a
    /// function's name Binding resolved, whose value its consumer forms from the selection, its Type (DECISIONS Z16: a name of a binding
    /// without a Type, or of a function Binding left unresolved, is an Error, which rests on that binding's failure or on its
    /// signature's); and every slot a declared local, a definition's Body, a match, a try or a for loop declares stated
    /// (<see cref="SlotsStated"/>, PLAN G1073).</summary>
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
                return CallPlan.Of(this, node) is { } call && Complete(call) && ByCase(call) && (call.TupleOperator
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
                    CallPlan.Of(this, node, 1) is { Receiver: { } receiver } step && Complete(entry) && Complete(step) && HirTables.Symbol(receiver) is not null &&
                    this.SlotsStated(node);
            case HirOp.Match or HirOp.Try:
                return BoundMatch.Of(this, node) is { IsCurrent: true, Invalid: false, Pending: false } && this.SlotsStated(node);
            case HirOp.DictionaryLiteral:
                return CallPlan.Of(this, node) is { } find && Complete(find);
            case HirOp.Closure:
                return this.ClosureParts((FunctionKoto)node) && ((FunctionKoto)node).ClosureDefinition!.Signature is not null;
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
                return HirTables.Symbol(node) is not null && this.SlotsStated(node);
            case HirOp.Body:
                return this.SlotsStated(node);
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

        // SPEC 7.7.3 (PLAN G13): a by-case value call (CallPlan.CaseDecider) acquires its callee exclusively, its Closure being Shared
        // case-free, and its two acquisitions take one callee (both Sources null for an owned callee) at one Origin, the result's receiver
        // image (Binding.ReceiverImage); a call without a decider has no Shared acquisition.
        static bool ByCase(CallPlan call) => call.CaseDecider is not { } decider ? call.SharedReceiver == default
            : call is { Kind: CalleeKind.Value, ReceiverKind: SemanticsKind.Uniq } && BoundClosure.Of(decider) is { CaseFreeReceiver: SemanticsKind.Ref, Receiver: SemanticsKind.Uniq } &&
                ReferenceEquals(call.SharedReceiver.Source, call.ReceiverOperation.Source) && ReferenceEquals(call.SharedReceiver.AdaptedType?.Origin, call.ReceiverOperation.AdaptedType?.Origin);

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

    // The facts a Closure's parts are published with (HirPublisher.Parts; DECISIONS R5-M63 (1) as amended by PLAN G1110): the definition
    // this binding published and every slot of the literal stated, whatever the creation. The creation (HasFacts' Closure row) also needs
    // the definition's Signature, which a failed slot leaves null.
    private bool ClosureParts(FunctionKoto literal) => literal.ClosureDefinition is not null && this.SlotsStated(literal);

    // PLAN G1073 (SPEC 23.1.2): whether every slot a node declares is stated (Stated): a declared local's; a definition's result and
    // parameters (an accessor's executing function's, with its self and value; a static's or Field's Type); the bindings of a match's, a
    // try's or a for loop's plan; a Closure's parameters and result as a named function's once its definition is published (PLAN G1110:
    // a literal whose binding failed before its header formed, such as a header refuted by its expected Type, publishes no slot). A node with a slot neither typed
    // nor failed lacks its facts (HirTables.Broken) whatever its state: Binding records every failed slot's cause at its TypeSource. No
    // other node declares a slot the builder reads: a Closure's environment holds typed captures only (a capture of a source whose Type
    // failed forms no environment binding); a static's Place is built only under its declaration's Body (MirBuilder's
    // Static), whose row reads its Type; the default templates of a definition with a body only beside its Body (an Error definition
    // replaces its defaults).
    private bool SlotsStated(Koto node)
    {
        switch (node)
        {
            case PropertyKoto property:
                return HirTables.Symbol(property) is not { } stored || this.Stated(stored);
            case VariableKoto variable:
                return HirTables.Symbol(variable) is not { Kind: BindingSymbolKind.Local } local || this.Stated(local);
            case PropertyAccessorKoto accessor:
                var bound = Accessor(accessor);
                return this.Stated(CallPlan.Accessor(this, accessor)) && (bound.SelfSymbol is not { } self || this.Stated(self)) &&
                    (bound.ValueSymbol is not { } value || this.Stated(value));
            case FunctionKoto function when HirTables.Symbol(function) is { } symbol && (!function.IsAnonymous || function.ClosureDefinition is not null):
                if (!this.Stated(symbol))
                {
                    return false;
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (!this.Stated(this.ParameterSymbol(function, i)))
                    {
                        return false;
                    }
                }

                return true;
            default:
                var positions = BoundMatch.Of(this, node)?.Positions;
                for (var i = 0; i < positions?.Count; i++)
                {
                    if (positions[i].BodySymbol is { } binding && !this.Stated(binding))
                    {
                        return false;
                    }
                }

                return true;
        }
    }
}

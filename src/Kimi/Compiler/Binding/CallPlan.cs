// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the published shape of a selected call and its ordered operands.
// Input: Binding's selection, arguments and default declarations. Output: CallPlan.Of(node, ordinal).
// The selected callee, acquisition and omitted defaults come from the same Binding decision.
// Must not resolve a call again or make consumers inspect syntax to recover its meaning.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The callee a call plan selected: a named function or implementation, a Contract requirement (dispatched through the
/// conformance, SPEC 8.6.1), a virtual slot, or a callable value (whose plan uses only the value fields).</summary>
public enum CalleeKind : byte
{
    Function,
    Requirement,
    Virtual,
    Value,
}

/// <summary>An omitted default evaluated after explicit arguments, in parameter order.
/// Expression and Parameter retain the declaration environment; references to preceding
/// parameters address prepared call slots, never the caller's original variables.
/// This plan is not a certificate of default ownership or cleanup verification.</summary>
public readonly record struct BoundDefaultArgument(Koto Expression, BindingSymbol Parameter, BoundType ParameterType);

/// <summary>The default of parameter <paramref name="Index"/> of a function (DefaultDecl, plan 5.6.2): one template per declaration and
/// default, owned by the declaration a call selects statically, never an override or a specialization. It reads each earlier parameter
/// i as the slot its call prepared (Res PreparedParam) of the declared Type <see cref="Parameter"/>(i) and produces
/// <see cref="Parameter"/>(<paramref name="Index"/>); at a call each is <c>TyCtxt.Apply(Parameter(i), SubstOf(call))</c>.</summary>
/// <param name="Function">The declaration.</param>
/// <param name="Index">The parameter's slot.</param>
internal readonly record struct DefaultDecl(FunctionKoto Function, int Index)
{
    internal Koto Expression => this.Function.Parameters[this.Index].DefaultValue!;

    internal static DefaultDecl Of(BindingSymbol parameter) => new((FunctionKoto)parameter.Scope.Owner, parameter.Slot);

    internal static DefaultDecl Of(in BoundDefaultArgument argument) => Of(argument.Parameter);

    // P_i, the declared Type of parameter i.
    internal BoundType Parameter(int i) => this.Function.Parameters[i].Type.BoundType!;
}

/// <summary>The committed callee and argument mapping of one call; storage is reused when the same call is rebound.</summary>
public sealed class CallPlan
{
    private int[] mapping = [];
    private BoundType?[] typeArguments = [];
    private BoundLength?[] lengthArguments = [];
    private BoundOrigin[] origins = [];
    private int originCount;
    private BoundOrigin[] inputOrigins = [];
    private int inputOriginCount;
    private BoundArgumentOperation[] argumentOperations = [];
    private BoundDefaultArgument[] defaultArguments = [];
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    private byte receiverDependence; // The gate's two facts below in the one byte the plan's layout has room for.
    private Koto? receiver; // The explicit receiver, or a value call's callee.

    public CalleeKind Kind { get; private set; }

    /// <summary>Gets the explicit receiver's acquisition, or a value call's object-callee payload acquisition (SPEC 7.3, 13.5.5).</summary>
    public BoundArgumentOperation ReceiverOperation { get; private set; }

    public ReadOnlySpan<BoundArgumentOperation> ArgumentOperations => this.argumentOperations;

    /// <summary>Gets omitted defaults in parameter declaration order, after explicit acquisitions.</summary>
    public ReadOnlySpan<BoundDefaultArgument> DefaultArguments => this.defaultArguments;

    /// <summary>Gets the selected function Symbol.</summary>
    public BindingSymbol Target { get; private set; } = null!;

    /// <summary>Gets the complete return type after substitution.</summary>
    public BoundType ReturnType { get; private set; } = null!;

    /// <summary>Gets the explicit receiver when member-call syntax supplies it.</summary>
    public Koto? Receiver => this.Kind == CalleeKind.Value ? null : this.receiver;

    /// <summary>Gets the symbolic conforming Type for a definition-bound requirement call.</summary>
    public BoundType? ConformingType { get; private set; }

    /// <summary>Gets the instantiated Type declaring a nominal member, including an inherited member's base Type.</summary>
    public BoundType? DeclaringType { get; private set; }

    /// <summary>Gets the lookup path, including for type-qualified calls that do not project a receiver.</summary>
    public BoundMemberPath? BasePath { get; private set; }

    /// <summary>Gets the committed substitutions for the function's declared Origins.</summary>
    public ReadOnlySpan<BoundOrigin> Origins => this.origins.AsSpan(0, this.originCount);

    /// <summary>Gets the committed input Origins in parameter-slot order.</summary>
    public ReadOnlySpan<BoundOrigin> InputOrigins => this.inputOrigins.AsSpan(0, this.inputOriginCount);

    /// <summary>Gets source-argument index to parameter-slot mappings.</summary>
    public ReadOnlySpan<int> ArgumentToParameter => this.mapping;

    /// <summary>Gets Type substitutions in declaration-slot order; length slots are null.</summary>
    public ReadOnlySpan<BoundType?> TypeArguments => this.typeArguments;

    /// <summary>Gets length substitutions in declaration-slot order, one per slot of <see cref="TypeArguments"/>; Type slots are null.</summary>
    public ReadOnlySpan<BoundLength?> LengthArguments => this.lengthArguments;

    /// <summary>Gets the invoked callable value of a value call.</summary>
    public Koto CalleeValue => this.Kind == CalleeKind.Value ? this.receiver! : null!;

    public BoundType CalleeType => this.CalleeValue.BoundType!;

    /// <summary>Gets a value call's signature after argument instantiation.</summary>
    public BoundType Signature { get; private set; } = null!;

    /// <summary>Gets a value call's signature as its callee declares it.</summary>
    public BoundType DeclaredSignature { get; private set; } = null!;

    /// <summary>Gets a value call's receiver acquisition.</summary>
    public SemanticsKind ReceiverKind { get; internal set; } = SemanticsKind.Ref;

    // The position in ArgumentOperations of the argument that stands for an operand its expansion already evaluated (ExpansionPlan's
    // Evaluated): a key resolution's length of the receiver the selection evaluated, an update setter's computed value; -1 for any other
    // call. Binding fixes it where it creates the call, with the argument list.
    internal int Evaluated { get; init; } = -1;

    // Selected from source syntax, never inferred again after generic substitution.
    internal bool TupleOperator { get; set; }

    internal ConversionKoto? AdaptationSource { get; set; }

    // The Contract reference whose requirement the call selected: a bound reference (Indexable<Name>) when the Contract
    // takes Type arguments, else the Contract itself. Every instance dispatches through the conformance to that reference
    // (SPEC 8.6.1). Null for calls of other functions.
    internal BindingSymbol? RequirementContract { get; set; }

    // A virtual call's semantic selection only; neither a body certificate nor a physical slot index. Target is the original slot.
    internal Binding.VirtualSlot VirtualSlot => new((FunctionKoto)this.Target.Declaration, this.VirtualSlotType!);

    internal BoundType? VirtualSlotType { get; set; }

    internal BoundType? VirtualBaseLookupType { get; set; }

    internal FunctionKoto? VirtualImplementation { get; set; }

    internal BoundType? VirtualImplementingType { get; set; }

    internal bool VirtualIsDirect => this.Receiver is BaseReferenceKoto;

    /// <summary>Gets a call of a node, the one way to reach one: an invocation's call, a macro's, an operator's, an object creation, a
    /// Property's getter and setter, an index expression's key resolution, indexer and view range, the construction of a range or
    /// <c>^</c> value, a for loop's iterator entry and step, a dictionary literal's key lookup, and the calls of a formatting plan
    /// (an interpolation's buffer and adapter, then its writes; a <c>$tryWrite</c>'s writes, then its status).</summary>
    /// <param name="node">The node.</param>
    /// <param name="ordinal">The position of the call among the node's calls, in evaluation order.</param>
    /// <returns>The call, or null when the node has no call at that position.</returns>
    internal static CallPlan? Of(Koto node, int ordinal = 0)
    {
        var binding = node.CodeContext.Compilation.Binding;
        if (node.Formatting is { } format)
        {
            // A $tryWrite's acquisition is no call: it only acquires the writer operand.
            var write = format.Acquisition is null ? ordinal - 2 : ordinal;
            return Plan(write < 0 ? ordinal == 0 ? format.Heap : format.Adapter : write < format.Writes.Count ? format.Writes[write]
                : write == format.Writes.Count && format.Acquisition is not null ? format.Outcome : null);
        }

        return node switch
        {
            InvocationKoto call => Nth(ordinal, Plan(call)),
            MacroKoto { Operand: InvocationKoto call } => Nth(ordinal, Plan(call)),
            ConversionKoto conversion => Nth(ordinal, Plan(conversion.CreationCall)),
            IndexKoto index => Nth(ordinal, Plan(binding.ResolvedKeyCall(index)), Plan(binding.IndexerCall(index, false)), Plan(binding.IndexerCall(index, true)), Plan(binding.ViewRangeCall(index))),
            RangeKoto or FromEndIndexKoto => Nth(ordinal, Plan(binding.RangeValueCall(node))),
            MemberAccessKoto or IdentifierNameKoto => Nth(ordinal, Plan(binding.PropertyCall(node, PropertyAccessorKind.Get)), Plan(binding.PropertyCall(node, PropertyAccessorKind.Set))),
            BinaryKoto binary => Nth(ordinal, Plan(binary.ArithmeticCall ?? binary.ComparisonCall)),
            UnaryKoto unary => Nth(ordinal, Plan(unary.ArithmeticCall)),
            ForKoto { BindingState: BindingState.Resolved } loop => Nth(ordinal, Plan(loop.EntryCall), Plan(loop.Iteration?.Next)),
            DictionaryLiteralKoto { BindingState: BindingState.Resolved, BoundType: { Kind: BoundTypeKind.Dictionary, Components: [var key, var value] } } when ordinal == 0 =>
                binding.DictionaryStorageCall(DictionaryCallee.FindEntry, key, value),
            _ => null,
        };

        static CallPlan? Plan(InvocationKoto? call) => call is { BindingState: BindingState.Resolved } ? call.CallStorage : null;

        static CallPlan? Nth(int ordinal, params ReadOnlySpan<CallPlan?> calls)
        {
            foreach (var call in calls)
            {
                if (call is not null && ordinal-- == 0)
                {
                    return call;
                }
            }

            return null;
        }
    }

    /// <summary>Gets a declaration a node's own selection calls, which a node resting on a failed target reads (SPEC 23.1.2): an
    /// invocation's selected target as presented (SPEC 22.1), a Property use's accessors, getter then setter (SPEC 11.3; the accessor
    /// declarations, not their executing functions). One row per form; a form whose selection calls a declaration adds a row.</summary>
    /// <param name="node">The node.</param>
    /// <param name="ordinal">The position among the declarations the node calls, in evaluation order.</param>
    /// <returns>The declaration, or null when the node calls none at that position.</returns>
    internal static Koto? Called(Koto node, int ordinal = 0) => node switch
    {
        InvocationKoto invocation => ordinal == 0 && invocation.BoundCall is { } call ? node.CodeContext.Compilation.Binding.Library.PresentedTarget(call.Target).Declaration : null,
        MemberAccessKoto or IdentifierNameKoto when node.BoundSymbol?.Property is not null =>
            Of(node, ordinal)?.Target.Declaration is FunctionKoto { Accessor.Declaration: { } accessor } ? accessor : null,
        _ => null,
    };

    /// <summary>Gets the function that executes a custom accessor (SPEC 11.3), which its calls target: its Type is the accessor's result,
    /// and its parameters (<see cref="Parameter"/>) are the receiver <c>self</c>, then the input <c>value</c>.</summary>
    /// <param name="accessor">An accessor with a body.</param>
    /// <returns>The executing function's symbol.</returns>
    internal static BindingSymbol Accessor(PropertyAccessorKoto accessor)
        => accessor.CodeContext.Compilation.Binding.AccessorFunction(Binding.Accessor(accessor)).BoundSymbol!;

    /// <summary>Gets how a call of a function is selected (SPEC 6.4.6, 8.4): a virtual slot dispatches on its receiver's Type, a requirement
    /// to its conforming Type's witness, any other function is called directly.</summary>
    /// <param name="target">The function.</param>
    /// <returns>Virtual, Requirement or Function.</returns>
    internal static CalleeKind KindOf(BindingSymbol target) => target.Declaration switch
    {
        FunctionKoto { IsVirtual: true } => CalleeKind.Virtual,
        FunctionKoto { IsRequirement: true } => CalleeKind.Requirement,
        _ => CalleeKind.Function,
    };

    /// <summary>Gets a function's parameter as its body reads it: slot <paramref name="index"/>, an accessor's <c>self</c> and <c>value</c>
    /// included, or at -1 the receiver <c>self</c> a constructor initializes or a destructor destroys (SPEC 6.3).</summary>
    /// <param name="function">A function, a closure or an accessor's executing function.</param>
    /// <param name="index">The parameter slot, or -1.</param>
    /// <returns>The parameter, or null at -1 of another function.</returns>
    internal static BindingSymbol? Parameter(BindingSymbol function, int index)
    {
        var declaration = (FunctionKoto)function.Declaration;
        var binding = declaration.CodeContext.Compilation.Binding;
        return index < 0 ? binding.SpecialReceiver(declaration) : binding.ParameterSymbol(declaration, index);
    }

    internal void SetReceiverProjection(BoundArgumentOperation operation, BoundMemberPath path)
    {
        this.BasePath = path;
        if (this.Receiver is not null)
        {
            this.ReceiverOperation = operation;
            return;
        }

        for (var i = 0; i < this.argumentOperations.Length; i++)
        {
            if (this.argumentOperations[i].ParameterIndex == operation.ParameterIndex)
            {
                this.argumentOperations[i] = operation;
                return;
            }
        }

        throw new InvalidOperationException("A receiver projection requires an acquired receiver argument.");
    }

    // Pooled body-local calls release semantic/source references while retaining their reusable array shapes.
    internal void Clear()
    {
        this.Kind = default;
        this.AdaptationSource = null;
        this.Target = null!;
        this.ReturnType = null!;
        this.receiver = null;
        this.ConformingType = null;
        this.DeclaringType = null;
        this.BasePath = null;
        this.ReceiverOperation = default;
        this.TupleOperator = false;
        this.RequirementContract = null;
        this.VirtualSlotType = null;
        this.VirtualBaseLookupType = null;
        this.VirtualImplementation = null;
        this.VirtualImplementingType = null;
        Array.Clear(this.typeArguments);
        Array.Clear(this.lengthArguments);
        Array.Clear(this.origins, 0, this.originCount);
        Array.Clear(this.inputOrigins, 0, this.inputOriginCount);
        Array.Clear(this.argumentOperations);
        Array.Clear(this.defaultArguments);
        this.originCount = 0;
        this.inputOriginCount = 0;
    }

    internal void Set(BindingSymbol target, BoundType result, Koto? receiver, ReadOnlySpan<int> mapping, ReadOnlySpan<BoundType?> typeArguments, BoundType? conformingType = null, BoundType? declaringType = null, ReadOnlySpan<BoundOrigin> origins = default, ReadOnlySpan<BoundOrigin> inputOrigins = default, ReadOnlySpan<BoundArgumentOperation> operations = default, BoundArgumentOperation receiverOperation = default, BoundMemberPath? basePath = null, ReadOnlySpan<BoundDefaultArgument> defaults = default, ReadOnlySpan<BoundLength?> lengthArguments = default)
    {
        this.Kind = KindOf(target);
        this.AdaptationSource = null;
        this.Target = target;
        this.ReturnType = result;
        this.receiver = receiver;
        this.ConformingType = conformingType;
        this.TupleOperator = false;
        this.RequirementContract = null;
        this.DeclaringType = declaringType;
        this.BasePath = basePath;
        this.ReceiverOperation = receiverOperation;
        this.SetOperations(operations);
        if (this.defaultArguments.Length != defaults.Length)
        {
            this.defaultArguments = defaults.IsEmpty ? [] : new BoundDefaultArgument[defaults.Length];
        }

        defaults.CopyTo(this.defaultArguments);
        if (this.origins.Length < origins.Length)
        {
            this.origins = new BoundOrigin[origins.Length];
        }
        else if (this.originCount > origins.Length)
        {
            Array.Clear(this.origins, origins.Length, this.originCount - origins.Length);
        }

        if (this.inputOrigins.Length < inputOrigins.Length)
        {
            this.inputOrigins = new BoundOrigin[inputOrigins.Length];
        }
        else if (this.inputOriginCount > inputOrigins.Length)
        {
            Array.Clear(this.inputOrigins, inputOrigins.Length, this.inputOriginCount - inputOrigins.Length);
        }

        this.originCount = origins.Length;
        this.inputOriginCount = inputOrigins.Length;
        origins.CopyTo(this.origins);
        inputOrigins.CopyTo(this.inputOrigins);
        if (this.mapping.Length != mapping.Length)
        {
            this.mapping = new int[mapping.Length];
        }

        mapping.CopyTo(this.mapping);
        if (this.typeArguments.Length != typeArguments.Length)
        {
            this.typeArguments = new BoundType[typeArguments.Length];
            this.lengthArguments = new BoundLength?[typeArguments.Length];
        }

        // SPEC 8.13.3: the plan holds one length entry per slot, the selection key's form. The lengths are empty (the target has no
        // length slot) or one per slot of the Type arguments.
        System.Diagnostics.Debug.Assert(lengthArguments.IsEmpty || lengthArguments.Length == typeArguments.Length);
        typeArguments.CopyTo(this.typeArguments);
        Array.Clear(this.lengthArguments);
        lengthArguments.CopyTo(this.lengthArguments);
        if (target.Declaration is FunctionKoto { IsVirtual: true } original)
        {
            original.CodeContext.Compilation.Binding.SelectVirtualCall(this, original); // The virtual fields are read only for this kind.
        }
    }

    // R1's condition at this value call's latest binding (DECISIONS R4-M152): its result depends on its call receiver, which the old
    // analysis's gate reports as the located Unsupported_Kd (Binding.ValueCallGate).
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    internal bool ReceiverDependentResult
    {
        get => (this.receiverDependence & 1) != 0;
        set => this.receiverDependence = (byte)(value ? this.receiverDependence | 1 : this.receiverDependence & ~1);
    }

    // Whether the pass's list of receiver-dependent calls holds this call (Binding.ValueCallGate).
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    internal bool ReceiverDependentListed
    {
        get => (this.receiverDependence & 2) != 0;
        set => this.receiverDependence = (byte)(value ? this.receiverDependence | 2 : this.receiverDependence & ~2);
    }

    internal void SetValue(Koto callee, BoundType signature, ReadOnlySpan<BoundArgumentOperation> arguments, BoundType declaredSignature, in BoundArgumentOperation receiverOperation, SemanticsKind receiverKind)
    {
        this.Kind = CalleeKind.Value;
        this.receiver = callee;
        this.ReceiverOperation = receiverOperation;
        this.Signature = signature;
        this.DeclaredSignature = declaredSignature;
        this.ReturnType = signature.Components[1];
        this.ReceiverKind = receiverKind;
        this.SetOperations(arguments);
    }

    private void SetOperations(ReadOnlySpan<BoundArgumentOperation> operations)
    {
        if (this.argumentOperations.Length != operations.Length)
        {
            this.argumentOperations = new BoundArgumentOperation[operations.Length];
        }

        operations.CopyTo(this.argumentOperations);
    }
}

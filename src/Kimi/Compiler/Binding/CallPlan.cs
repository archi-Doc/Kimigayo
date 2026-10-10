// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The callee a call plan selected: a named function or implementation, a Contract requirement (dispatched through the
/// conformance, SPEC 8.4.9), a virtual slot, or a callable value (whose plan uses only the value fields).</summary>
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
    private Koto? receiver; // The explicit receiver, or a value call's callee.

    public CalleeKind Kind { get; private set; }

    /// <summary>Gets the explicit receiver's acquisition, or a value call's object-callee payload acquisition (SPEC 7.3, 13.5.5.1).</summary>
    public BoundArgumentOperation ReceiverOperation { get; private set; }

    public ReadOnlySpan<BoundArgumentOperation> ArgumentOperations => this.argumentOperations;

    /// <summary>Gets omitted defaults in parameter declaration order, after explicit acquisitions.</summary>
    public ReadOnlySpan<BoundDefaultArgument> DefaultArguments => this.defaultArguments;

    /// <summary>Gets the selected function Symbol.</summary>
    public BindingSymbol Target { get; private set; } = null!;

    /// <summary>Gets the complete return type after substitution.</summary>
    public BoundType ReturnType { get; private set; } = null!;

    /// <summary>Gets the result category committed with the selected contract.</summary>
    public FunctionResultMode ResultMode { get; internal set; }

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

    /// <summary>Gets length substitutions in declaration-slot order; Type slots are null.</summary>
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

    // Selected from source syntax, never inferred again after generic substitution.
    internal bool TupleOperator { get; set; }

    internal ConversionKoto? AdaptationSource { get; set; }

    // The Contract reference whose requirement the call selected: a bound reference (Indexable<Name>) when the Contract
    // takes Type arguments, else the Contract itself. Every instance dispatches through the conformance to that reference
    // (SPEC 8.4.9). Null for calls of other functions.
    internal BindingSymbol? RequirementContract { get; set; }

    // A virtual call's semantic selection only; neither a body certificate nor a physical slot index. Target is the original slot.
    internal Binding.VirtualSlot VirtualSlot => new((FunctionKoto)this.Target.Declaration, this.VirtualSlotType!);

    internal BoundType? VirtualSlotType { get; set; }

    internal BoundType? VirtualBaseLookupType { get; set; }

    internal FunctionKoto? VirtualImplementation { get; set; }

    internal BoundType? VirtualImplementingType { get; set; }

    internal bool VirtualIsDirect => this.Receiver is BaseReferenceKoto;

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
        this.ResultMode = default;
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
        this.Kind = target.Declaration is FunctionKoto { IsVirtual: true } ? CalleeKind.Virtual
            : target.Declaration is FunctionKoto { IsRequirement: true } ? CalleeKind.Requirement : CalleeKind.Function;
        this.AdaptationSource = null;
        this.Target = target;
        this.ReturnType = result;
        this.ResultMode = target.Declaration is FunctionKoto function ? Binding.ResultModeOf(function.ReturnType) : FunctionResultMode.Value;
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
        }

        typeArguments.CopyTo(this.typeArguments);
        if (this.lengthArguments.Length != lengthArguments.Length)
        {
            this.lengthArguments = new BoundLength?[lengthArguments.Length];
        }

        lengthArguments.CopyTo(this.lengthArguments);
        if (target.Declaration is FunctionKoto { IsVirtual: true } original)
        {
            original.CodeContext.Compilation.Binding.SelectVirtualCall(this, original); // The virtual fields are read only for this kind.
        }
    }

    internal void SetValue(Koto callee, BoundType signature, ReadOnlySpan<BoundArgumentOperation> arguments, BoundType declaredSignature, in BoundArgumentOperation receiverOperation, SemanticsKind receiverKind)
    {
        this.Kind = CalleeKind.Value;
        this.receiver = callee;
        this.ReceiverOperation = receiverOperation;
        this.Signature = signature;
        this.DeclaredSignature = declaredSignature;
        this.ReturnType = signature.Components[1];
        this.ResultMode = signature.ResultMode;
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

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Definition-side callable mapping vocabulary.

/// <summary>Reusable callable correspondence. Availability still requires verification of its conformance path.</summary>
public sealed class BoundFunctionWitness
{
    private BoundOrigin[] origins = [];
    private BoundOrigin[] inputs = [];

    public BoundType DeclaringType { get; internal set; } = null!;

    public BoundType? RequirementReceiver { get; internal set; }

    public BoundType? ImplementationReceiver { get; internal set; }

    public BoundMemberPath? BasePath { get; internal set; }

    public ConstraintProof ObjectCompatibility { get; internal set; }

    public ReadOnlySpan<BoundOrigin> Origins => this.origins;

    public ReadOnlySpan<BoundOrigin> InputOrigins => this.inputs;

    internal void SetOrigins(ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs)
    {
        if (this.origins.Length != origins.Length)
        {
            this.origins = new BoundOrigin[origins.Length];
        }

        if (this.inputs.Length != inputs.Length)
        {
            this.inputs = new BoundOrigin[inputs.Length];
        }

        origins.CopyTo(this.origins);
        inputs.CopyTo(this.inputs);
    }
}

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Path, BindingSymbol Requirement), BoundFunctionWitness> functionWitnesses = new();

    private BoundType? ProjectRequirementReceiver(BoundType required, BoundType self, BoundType? declaringType)
        => declaringType is not null && required is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } && SameType(required.Components[0], self)
            ? this.InternType(BoundTypeKind.Semantics, null, required.Semantics, [declaringType], origin: required.Origin) : null;

    private BoundFunctionWitness FunctionWitness(BoundConformancePath path, BindingSymbol requirement)
    {
        var key = (path, requirement);
        if (!this.functionWitnesses.TryGetValue(key, out var witness))
        {
            this.functionWitnesses.Add(key, witness = new());
        }

        return witness;
    }

    private bool SameFunctionWitness(BoundFunctionWitness? a, BoundFunctionWitness? b, BindingScope scope)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (!this.SameMemberPath(a.BasePath, b.BasePath, scope) || a.ObjectCompatibility != b.ObjectCompatibility || !this.SameConformanceType(a.DeclaringType, b.DeclaringType, scope) || !this.SameConformanceType(a.RequirementReceiver, b.RequirementReceiver, scope) || !this.SameConformanceType(a.ImplementationReceiver, b.ImplementationReceiver, scope) || a.Origins.Length != b.Origins.Length || a.InputOrigins.Length != b.InputOrigins.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Origins.Length; i++)
        {
            if (!ReferenceEquals(a.Origins[i], b.Origins[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < a.InputOrigins.Length; i++)
        {
            if (!ReferenceEquals(a.InputOrigins[i], b.InputOrigins[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool SameMemberPath(BoundMemberPath? a, BoundMemberPath? b, BindingScope scope)
    {
        while (!ReferenceEquals(a, b))
        {
            if (a is null || b is null || !ReferenceEquals(a.Declaration, b.Declaration) || !this.SameConformanceType(a.Type, b.Type, scope))
            {
                return false;
            }

            a = a.Parent;
            b = b.Parent;
        }

        return true;
    }
}

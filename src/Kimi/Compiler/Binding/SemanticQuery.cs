// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The facts one Binding publishes to Hover, the LSP and tests (docs/dev/COMPILER_ARCHITECTURE.md). Readers outside
/// Binding ask these members instead of Koto semantic slots or Binding state, so the slots can move without changing them.</summary>
internal sealed class SemanticQuery(Compilation compilation, Binding binding, List<Koto> nodes, Dictionary<object, BindingSymbol> declarations, Dictionary<BindingSymbol, List<BoundConformance>> conformances)
{
    internal Compilation Compilation => compilation;

    internal KimiLibrary Library => binding.Library;

    /// <summary>Gets every syntax node of the current pass.</summary>
    internal IReadOnlyList<Koto> BoundNodes => nodes;

    /// <summary>Gets each declared symbol by its declaration syntax (a parameter by its parameter syntax). Read only.</summary>
    internal Dictionary<object, BindingSymbol> Declarations => declarations;

    internal BoundType? TypeOf(Koto node) => node.BoundType;

    internal BoundOrigin? OriginOf(Koto node) => node.BoundOrigin;

    internal BindingSymbol? SymbolOf(Koto node) => node.BoundSymbol;

    internal CallPlan? CallOf(InvocationKoto node) => node.BoundCall;

    internal CallPlan? ValueCallOf(InvocationKoto node) => node.BoundValueCall;

    internal BindingState StateOf(Koto node) => node.BindingState;

    internal BindingFailure FailureOf(Koto node) => node.BindingFailure;

    // Resolved by the running or the last completed pass, not by an earlier one.
    internal bool IsCurrent(Koto node) => node.HasCurrentBinding;

    internal BoundConstraint? ConstraintOf(IsKoto node) => node.BoundConstraint;

    internal BoundRuntimeTypeTest? RuntimeTestOf(IsKoto node) => node.BoundRuntimeTest;

    // A recovery node stands for its syntax error, never for a semantic result.
    internal bool IsRecovery(Koto node) => Binding.IsRecovery(node, out _);

    internal IReadOnlyList<BoundConformance>? Conformances(BindingSymbol type) => conformances.GetValueOrDefault(type);

    internal BindingScope ConstraintScope(Koto node) => binding.ConstraintScope(node);

    internal bool TryGetVirtualOverride(FunctionKoto function, out Binding.VirtualOverride result) => binding.TryGetVirtualOverride(function, out result);

    internal bool TryGetObjectErasure(Koto use, out Binding.ObjectErasureEvidence evidence) => binding.TryGetObjectErasure(use, out evidence);

    internal FunctionKoto? SpecializationOriginal(FunctionKoto function) => binding.GetSpecializationOriginal(function);

    internal (bool Confined, bool Preserves) AvailableEffectBounds(FunctionKoto requirement, BoundType? conforming, BindingScope scope, List<EffectEvidence>? evidence, BindingSymbol? reference = null)
        => binding.AvailableEffectBounds(requirement, conforming, scope, evidence, reference);

    internal (bool Confined, bool Preserves) AvailableCallableEffects(BoundType type, BoundType signature, SemanticsKind receiver, BindingScope scope, List<EffectEvidence>? evidence)
        => binding.AvailableCallableEffects(type, signature, receiver, scope, evidence);

    internal BoundType SelfType(BindingSymbol symbol) => binding.SelfType(symbol);

    internal ConstraintProof ProveCopy(BoundType type, Koto context) => binding.ProveCopy(type, context);

    // The complete contextual spelling, including every Origin; diagnostics share the printer.
    internal string TypeName(BoundType type) => Binding.HoverTypeName(type);

    internal string LengthName(BoundLength length, HoverBudget budget) => Binding.DiagnosticLengthName(length, budget);

    internal BoundType? CallableCore(BoundType? type) => Binding.CallableCore(type);

    internal ContractKoto? DeclaringContract(EffectBoundKoto effect) => Binding.DeclaringContract(effect);

    internal BoundType? ItemDeclaringType(BoundType item, FunctionKoto function) => Binding.ItemDeclaringType(item, function);

    // Normalized K after Binding, or -1 for an unverified specialization.
    internal int PositionalParameterCount(FunctionKoto function)
    {
        if (function.IsSpecialization)
        {
            return binding.GetSpecializationOriginal(function) is { } original ? this.PositionalParameterCount(original) : -1;
        }

        var limit = function.NameBoundaryIndex < 0 ? function.Parameters.Count : function.NameBoundaryIndex;
        var receiver = function.BoundSymbol?.ReceiverIndex ?? -1;
        return limit - (receiver >= 0 && receiver < limit ? 1 : 0);
    }
}

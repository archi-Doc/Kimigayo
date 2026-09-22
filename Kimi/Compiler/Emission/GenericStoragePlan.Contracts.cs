// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class GenericStoragePlan
{
    // Associated results and requirement calls use the initial concrete profile. The
    // transitional shared representation has no requirement dispatch or associated ABI.
    private static bool RequiresConcreteContractBody(OwnershipBody body)
        => body.Places.Any(x => HasAssociated(x.Type)) || body.Operations.Any(x => x.Source is InvocationKoto { BoundCall.Target.Declaration: FunctionKoto { IsRequirement: true } });

    private static bool HasAssociated(BoundType type)
        => type.Kind == BoundTypeKind.AssociatedProjection || type.Components.Any(HasAssociated);

    private static Template ConcreteContractTemplate(OwnershipBody body)
    {
        var calls = new List<BoundCall>();
        foreach (var operation in body.Operations)
        {
            if (operation.Kind == OwnershipOperationKind.Call && operation.Source is InvocationKoto { BoundCall: { } call } &&
                call.Target.CompilerFunction == CompilerFunctionKind.None && !calls.Contains(call))
            {
                calls.Add(call);
            }
        }

        // Reuse entry/signature bookkeeping only. This empty shared body is never emitted;
        // every selected entry below must lower its verified concrete ownership plan.
        var unused = new SharedStorageBody(string.Empty, [], [], 0, body.Function.Parameters.Count, [], [], [], [], [], [], 0, [], false, []);
        return new(body, unused, [], [], calls.ToArray(), ConcreteOnly: true);
    }

    private bool PrepareConcreteContractEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, BoundType[] parameters, ValueLowering[] values, List<AbiParameter> abiParameters, ValueLowering? resultValue, bool resultSlot, bool noReturn, int depth, out CallEntry? entry, out string? failure)
    {
        failure = null;
        entry = null;
        var function = template.Body.Function;
        this.entryCounts.TryGetValue(function, out var count);
        if (count >= SubstitutionSetLimit)
        {
            // SPEC 21.3.5: an oversized substitution set exceeds a mandatory generation limit.
            this.ResourceLimitExceeded = true;
            return Fail($"Generic instantiation of '{function.Name}' exceeds the substitution set limit of {SubstitutionSetLimit} distinct closed contexts.", out failure);
        }

        this.entryCounts[function] = count + 1;
        var abi = new FunctionAbi("__kimi_generic_entry" + module.SharedEntries.Count, FunctionAbi.ResultType(call.ReturnType, layouts)!, abiParameters.ToArray(), noReturn: noReturn, resultSlot: resultSlot);
        var adapters = new SharedDirectAdapter[template.DirectCalls.Length];
        var selected = compilation.Binding.SelectSpecialization(call);
        var physical = new SharedStorageEntry(abi, template.Physical, [], [], 0, 1, values, resultValue, adapters, selected is null ? null : this.functions!.GetValueOrDefault(selected));
        entry = new(template, physical, parameters, call.ReturnType, call.DeclaringType, call.TypeArguments.ToArray(), call.LengthArguments.ToArray(), new CallEntry?[adapters.Length])
        {
            ConcreteCalls = new BoundCall[adapters.Length],
        };
        if (selected is not null)
        {
            // SPEC 21.3.4: the selected explicit specialization is the implementation; callers call its
            // ordinary ABI directly, so the generic body is not instantiated for this call.
            if (physical.Selected is null)
            {
                return Fail("Selected specialization has no verified implementation ABI.", out failure);
            }

            module.SharedEntries.Add(physical);
            this.calls.Add(call, entry);
            return true;
        }

        module.SharedEntries.Add(physical);
        this.calls.Add(call, entry);
        for (var i = 0; i < adapters.Length; i++)
        {
            var inner = compilation.Binding.InstantiateForwardedCall(template.DirectCalls[i], call);
            if (inner?.Target.Declaration is not FunctionKoto target)
            {
                return Fail("Concrete requirement call lacks a verified implementation mapping.", out failure);
            }

            entry.ConcreteCalls[i] = inner;
            if (IsGeneric(target))
            {
                if (!this.templates.TryGetValue(target, out var innerTemplate) ||
                    !this.PrepareEntry(compilation, module, layouts, inner, innerTemplate, out var generated, out failure, depth + 1))
                {
                    return Fail(failure ?? "Concrete requirement implementation has no verified generic body.", out failure);
                }

                entry.Direct[i] = generated;
                adapters[i] = new(generated!.Physical.Abi, generated.Physical.Parameters, generated.Physical.Result);
            }
            else if (!this.ConcreteAdapter(inner, target, layouts, out adapters[i]!))
            {
                return Fail("Concrete requirement implementation has no verified ABI.", out failure);
            }
        }

        return true;
    }
}

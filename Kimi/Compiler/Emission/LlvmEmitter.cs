// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>
/// Checked LLVM generation for the implemented windows-x64-v1 execution subset. Every request rechecks the
/// latest analyses; the retained module is scratch storage, not a certificate for later compilations.
/// </summary>
public sealed class LlvmEmitter
{
    private readonly Compilation compilation;
    private readonly EmissionModule module = new();
    private readonly BodyLowering lowering = new();
    private readonly FunctionAbiPool signatures = new();
    private readonly GenericStoragePlan generics = new();
    private readonly ObjectGenerationPlan objects = new();
    private readonly Dictionary<FunctionKoto, FunctionAbi> functions = new(ReferenceEqualityComparer.Instance);

    internal LlvmEmitter(Compilation compilation)
        => this.compilation = compilation;

    /// <summary>Verifies the entire selected input against the implemented execution subset.</summary>
    /// <param name="failure">A concrete reason when generation cannot proceed.</param>
    /// <returns>Whether the latest analysis proves every operation this subset lowers.</returns>
    public bool Validate(out string? failure)
        => this.TryPrepare(out _, out failure);

    /// <summary>Writes inspection IR after checking the latest analysis. Does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        if (!this.TryPrepare(out var module, out failure))
        {
            return false;
        }

        module.WriteIr(writer);
        return true;
    }

    // Consumed synchronously before the next preparation. A failure leaves the module unwritable.
    internal bool TryPrepare(out EmissionModule module, out string? failure)
    {
        module = this.module;
        module.Clear();
        var c = this.compilation;
        failure = null;
        try
        {
            var destructorOrdinal = 0;
            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (!this.SkipGenerated(body) && !body.Function.IsGenerated && !GenericStoragePlan.IsGeneric(body.Function))
                {
                    if (body.Function.IsDestructor)
                    {
                        this.lowering.AggregateLayouts.RegisterDestructor(body.Function, destructorOrdinal);
                    }

                    destructorOrdinal++;
                }
            }

            failure = this.CheckInputs();
            if (failure is not null)
            {
                return false;
            }

            // Register every selected signature first: recursion refers to the same record.
            var ordinal = 0;
            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (this.SkipGenerated(body) || GenericStoragePlan.IsGeneric(body.Function))
                {
                    continue;
                }

                var source = body.Function;
                var abi = source.IsGenerated ? WindowsLowering.Entry : this.signatures.Get(ordinal++, source, this.lowering.AggregateLayouts);
                this.functions.Add(source, abi);
            }

            if (!this.generics.Prepare(c, module, this.lowering.AggregateLayouts, this.functions, out failure))
            {
                return false;
            }

            this.lowering.GenericCalls = this.generics.Calls;
            if (!this.objects.Prepare(c, module, this.lowering.AggregateLayouts, out failure))
            {
                return false;
            }

            this.lowering.ObjectCalls = this.objects.Calls;

            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (this.SkipGenerated(body) || GenericStoragePlan.IsGeneric(body.Function))
                {
                    continue;
                }

                var function = module.AddFunction(this.functions[body.Function], exported: false);
                if (!this.lowering.Lower(c.Library, body, function, module.Constants, c.Project.Directory, this.functions, c.Ownership.ControlFlow!, c.PointerWidth, out failure))
                {
                    return false;
                }

                module.NeedsStringComparison |= function.NeedsStringComparison;
                this.lowering.RegisterAggregates(module);
            }

            if (!this.functions.TryGetValue(c.Binding.Startup.Function!, out var entry))
            {
                failure = "The selected startup has no implementation.";
                return false;
            }

            var start = module.AddFunction(WindowsLowering.Start, exported: true);
            start.AddCall(-1, entry, []);
            start.AddCall(-1, WindowsLowering.Exit, [new(EmissionOperandKind.Integer, 0)]);
            start.Add(EmissionOpcode.Unreachable, -1);
            module.Complete();
            return true;
        }
        finally
        {
            // Never retain a previous parse through the active declaration-to-ABI map.
            this.functions.Clear();
            this.generics.Clear();
            this.objects.Clear();
            this.lowering.ClearFunctionContext();
            this.lowering.AggregateLayouts.ClearDestructors();
            if (!module.IsComplete)
            {
                module.Clear();
            }
        }
    }

    private bool SkipGenerated(OwnershipBody body) => ReferenceEquals(body.Function, this.compilation.Kotonoha.GeneratedFunction) && this.compilation.Binding.Startup.Kind == StartupKind.Explicit;

    private string? CheckInputs()
    {
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout || c.PointerWidth != 64)
        {
            return "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }

        if (!c.Binding.Result.IsComplete || !c.Ownership.SupportsOriginObligations() || !c.Library.IsValid ||
            c.Kotonoha.HasSourceErrors || c.Kotonoha.DiagnosticCollection.HasErrors || !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            return "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }

        if (c.KotonohaArray.Length != 0 || c.SourceModules.Length != 1 || startup.OutputKind != OutputKind.Application || startup.Kind is not (StartupKind.Implicit or StartupKind.Explicit) ||
            !this.SupportedContainers(c.Kotonoha.RootKoto))
        {
            return "This partial emitter supports Applications without external modules or declaration containers.";
        }

        // Declaration-only GeneratedFunction is an analysis wrapper, not an unused user function.
        var members = c.Kotonoha.RootKoto.Members;
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is not (FunctionKoto or AliasKoto))
            {
                return "Additional selected implementation bodies are outside the implemented execution subset.";
            }
        }

        for (var index = 0; index < c.Ownership.Bodies.Count; index++)
        {
            var body = c.Ownership.Bodies[index];
            if (this.SkipGenerated(body))
            {
                continue;
            }

            var function = body.Function;
            if (GenericStoragePlan.IsGeneric(function))
            {
                continue; // Universally verified CFG and concrete entry checks run in GenericStoragePlan.
            }

            if (function.BoundSymbol?.Scope.Owner is StructKoto && !function.IsConstructor && !function.IsDestructor &&
                function.BoundSymbol.ReceiverIndex >= 0 && !ReferenceTypes.IsStruct(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType))
            {
                return "Ordinary structure methods need receiver/call lowering outside this subset.";
            }

            var result = function.BoundSymbol?.Type ?? (function.IsGenerated ? BoundType.Unit : null);
            if ((!body.IsConcrete && !BodyLowering.CanEraseReceiver(body)) || !body.IsVerified || (!function.IsGenerated && function.BoundSymbol is null) ||
                (!FunctionAbi.Supports(result, this.lowering.AggregateLayouts) && !ReferenceEquals(result, BoundType.Never)) || function.AttributeChain is not null ||
                (function.IsAnonymous && function.BoundClosure is null) || (function.IsSpecialization && !c.Binding.IsVerifiedSpecialization(function)) || function.IsRequirement || (function.Captures is { Length: > 0 } && function.BoundClosure is null) ||
                (!function.IsSpecialization && function.GenericArguments.Count != 0) || function.Origins.Count != 0 || function.TypeConstraints.Count != 0)
            {
                return "A selected function requires unsupported signature, capture or implementation lowering.";
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (!FunctionAbi.SupportsParameter(parameter.Type.BoundType, this.lowering.AggregateLayouts) ||
                    ((parameter.IsOptional || parameter.DefaultValue is not null) && !ScalarDefaults.Supports(function, i)) ||
                    (ReferenceTypes.IsString(parameter.Type.BoundType) && (parameter.Type.BoundType!.Origin is not { Kind: OriginKind.Input } origin ||
                        !ReferenceEquals(origin.Binder, function) || origin.Slot != i)))
                {
                    return "Parameters require verified value, owned-slot or shared-string representations and supported scalar defaults.";
                }
            }
        }

        return null;
    }

    private bool SupportedContainers(DeclarationContainerKoto container)
    {
        for (var i = 0; i < container.NestedContainers.Count; i++)
        {
            var nested = container.NestedContainers[i];
            if (nested is not (StructKoto or GroupKoto or EnumKoto or ContractKoto) || !this.SupportedContainers(nested))
            {
                return false;
            }
        }

        return container is not GroupKoto || container.Members.All(x => x is FunctionKoto or AliasKoto ||
            (x is PropertyKoto property && StaticScalar.TryGet(property.BoundSymbol?.Property, out _)));
    }
}

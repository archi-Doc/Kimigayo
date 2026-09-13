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
        failure = this.CheckInputs();
        if (failure is not null)
        {
            return false;
        }

        try
        {
            // Register every selected signature first: recursion refers to the same record.
            var ordinal = 0;
            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (this.SkipGenerated(body))
                {
                    continue;
                }

                var source = body.Function;
                var abi = source.IsGenerated ? WindowsLowering.Entry : this.signatures.Get(ordinal++, source);
                this.functions.Add(source, abi);
            }

            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (this.SkipGenerated(body))
                {
                    continue;
                }

                var function = module.AddFunction(this.functions[body.Function], exported: false);
                if (!this.lowering.Lower(c.Core, body, function, module.Constants, c.Project.Directory, this.functions, c.Ownership.ControlFlow!, c.PointerWidth, out failure))
                {
                    return false;
                }
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
            this.lowering.ClearFunctionContext();
            if (!module.IsComplete)
            {
                module.Clear();
            }
        }
    }

    private static bool ScalarOrUnit(BoundType? type) => ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit);

    private bool SkipGenerated(OwnershipBody body) => ReferenceEquals(body.Function, this.compilation.Kotonoha.GeneratedFunction) && this.compilation.Binding.Startup.Kind == StartupKind.Explicit;

    private string? CheckInputs()
    {
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout || c.PointerWidth != 64)
        {
            return "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }

        if (!c.Binding.Result.IsComplete || c.Binding.Obligations.Count != 0 || !c.Core.IsValid ||
            c.Kotonoha.HasSourceErrors || c.Kotonoha.DiagnosticCollection.HasErrors || !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            return "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }

        if (c.KotonohaArray.Length != 0 || startup.OutputKind != OutputKind.Application || startup.Kind is not (StartupKind.Implicit or StartupKind.Explicit) ||
            c.Kotonoha.RootKoto.NestedContainers.Count != 0)
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
            var result = function.BoundSymbol?.Type ?? (function.IsGenerated ? BoundType.Unit : null);
            if (!body.IsConcrete || !body.IsVerified || (!function.IsGenerated && function.BoundSymbol is null) ||
                (!ScalarOrUnit(result) && !ReferenceEquals(result, BoundType.Never)) || function.AttributeChain is not null ||
                function.IsAnonymous || function.IsSpecialization || function.IsRequirement || function.Captures is { Length: > 0 } ||
                function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.TypeConstraints.Count != 0)
            {
                return "A selected function requires unsupported signature, capture or implementation lowering.";
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (!ScalarOrUnit(parameter.Type.BoundType) || parameter.IsOptional || parameter.DefaultValue is not null)
                {
                    return "Only required bool/8-64-bit integer/Unit value parameters are implemented.";
                }
            }
        }

        return null;
    }
}

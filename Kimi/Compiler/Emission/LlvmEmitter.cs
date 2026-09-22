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

            if (c.IsTestBuild)
            {
                c.Tests.Discover(c);
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

            if (!RegisterImports(c.Binding.LibraryImports, module, this.functions, out failure))
            {
                return false;
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
            this.LowerInstances(c, module);

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

            if (c.IsTestBuild)
            {
                module.TestRuntime = Testing.TestRuntime.Create(c.Tests, this.functions);
                module.Complete();
                return true;
            }

            if (c.Binding.Startup.OutputKind == OutputKind.Library)
            {
                module.Complete();
                return true;
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
            c.Ownership.ClearInstances();
            this.objects.Clear();
            this.lowering.ClearFunctionContext();
            this.lowering.AggregateLayouts.ClearDestructors();
            if (!module.IsComplete)
            {
                module.Clear();
            }
        }
    }

    // SPEC 22.3.2: a direct import calls its external symbol with the Windows x64 C ABI, whose scalar
    // arguments need no extension attributes. Binding already made same-named imports agree on one
    // physical signature and supply kind (SPEC 21.5.2), so they share one declaration.
    private static bool RegisterImports(IReadOnlyList<LibraryImport> imports, EmissionModule module, Dictionary<FunctionKoto, FunctionAbi> functions, out string? failure)
    {
        failure = null;
        for (var i = 0; i < imports.Count; i++)
        {
            var import = imports[i];
            var name = LlvmModuleWriter.ExternalName(import.Symbol);
            FunctionAbi? abi = null;
            for (var e = 0; e < module.Externals.Count && abi is null; e++)
            {
                if (string.Equals(module.Externals[e].Abi.Name, name, StringComparison.Ordinal))
                {
                    abi = module.Externals[e].Abi;
                }
            }

            if (abi is null)
            {
                abi = CreateImportAbi(import, name);
                if (abi is null)
                {
                    failure = "A foreign import needs an unsupported parameter or result representation.";
                    return false;
                }

                module.Externals.Add(new(abi, import.Kind == "import"));
            }

            functions.Add(import.Function, abi);
        }

        return true;

        static FunctionAbi? CreateImportAbi(LibraryImport import, string name)
        {
            var function = import.Function;
            var result = function.BoundSymbol!.Type!;
            var resultType = ReferenceEquals(result, BoundType.Unit) ? WindowsLowering.Unit.ComputationType : ImportType(result);
            var parameters = new AbiParameter[function.Parameters.Count];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (ImportType(function.Parameters[i].Type.BoundType!) is not { } type)
                {
                    return null;
                }

                parameters[i] = new(type, "a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), AbiParameterKind.Value, i);
            }

            return resultType is null ? null : new(name, resultType, parameters);
        }

        // Binding admits only fixed-width integers, f32/f64 and raw pointers (an opaque ptr).
        static string? ImportType(BoundType type)
            => ReferenceTypes.IsPointer(type) || type.Kind == BoundTypeKind.Primitive ? WindowsLowering.GetValue(type)?.ArgumentType : null;
    }

    // Only the selected implicit Application body executes. Other source-module
    // wrappers are checked by ownership but are not callable implementations.
    // SPEC 21.3.1 monomorphization: each concrete call context of a universally verified generic body
    // is analyzed under its closed substitution and lowered as an ordinary concrete body, under the
    // entry ABI its callers already use. A refused instance keeps its transitional shared entry.
    private void LowerInstances(Compilation c, EmissionModule module)
    {
        foreach (var (call, entry) in this.generics.Calls)
        {
            // A selected explicit specialization (SPEC 21.3.4) is the implementation, never the generic body.
            if (entry.Physical.Selected is not null || !module.SharedEntries.Contains(entry.Physical))
            {
                continue;
            }

            if (c.Ownership.AnalyzeInstance(entry.Template.Body, call) is { } body)
            {
                var function = module.AddFunction(entry.Physical.Abi, exported: false);
                var lowered = false;
                this.lowering.SetInstance(c.Binding, call);
                try
                {
                    lowered = this.lowering.Lower(c.Library, body, function, module.Constants, c.Project.Directory, this.functions, c.Ownership.ControlFlow!, c.PointerWidth, out _);
                }
                finally
                {
                    this.lowering.SetInstance(null, null);
                }

                if (lowered)
                {
                    module.NeedsStringComparison |= function.NeedsStringComparison;
                    this.lowering.RegisterAggregates(module);
                    module.SharedEntries.Remove(entry.Physical);
                }
                else
                {
                    module.RemoveLastFunction();
                }
            }
        }
    }

    private bool SkipGenerated(OwnershipBody body)
        => body.Function.IsGenerated && !ReferenceEquals(body.Function, this.compilation.Binding.Startup.Function);

    private string? CheckInputs()
    {
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout || c.PointerWidth != 64)
        {
            return "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }

        if (!c.Binding.Result.IsComplete || !c.Ownership.SupportsOriginObligations() || !c.Library.ValidateDeclarations() || !c.Library.ValidateBoundDeclarations() ||
            !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            return "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }

        var supportedStartup = startup.OutputKind switch
        {
            OutputKind.Application => startup.Kind is StartupKind.Implicit or StartupKind.Explicit or StartupKind.Test,
            OutputKind.Library => startup.Kind == StartupKind.None,
            _ => false,
        };
        if (c.KotonohaArray.Length != 0 || !supportedStartup)
        {
            return "Emission requires resolved source-module inputs and a supported Application or Library startup plan.";
        }

        foreach (var sourceModule in c.SourceModules)
        {
            if (sourceModule.HasSourceErrors || sourceModule.DiagnosticCollection.HasErrors)
            {
                return "Emission requires every source module to be free of source and module errors.";
            }

            if (!this.SupportedContainers(sourceModule.RootKoto))
            {
                return "A selected declaration container requires unsupported implementation lowering.";
            }

            // Declaration-only GeneratedFunction is an analysis wrapper, not an unused user function.
            var members = sourceModule.RootKoto.Members;
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] is not (FunctionKoto or AliasKoto))
                {
                    return "Additional selected implementation bodies are outside the implemented execution subset.";
                }
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
                function.BoundSymbol.ReceiverIndex >= 0 && !ReferenceTypes.IsStruct(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType) &&
                !ObjectTypes.IsBorrow(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType) &&
                !StructStorage.IsStruct(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType))
            {
                return "Ordinary structure methods need receiver/call lowering outside this subset.";
            }

            var result = function.BoundSymbol?.Type ?? (function.IsGenerated ? BoundType.Unit : null);
            if ((!body.IsConcrete && !BodyLowering.CanEraseReceiver(body)) || !body.IsVerified || (!function.IsGenerated && function.BoundSymbol is null) ||
                (!FunctionAbi.Supports(result, this.lowering.AggregateLayouts) && !ReferenceEquals(result, BoundType.Never)) || (function.AttributeChain is not null && !(c.IsTestBuild && TestDefinition.IsValidSyntax(function))) ||
                (function.IsAnonymous && function.BoundClosure is null) || (function.IsSpecialization && !c.Binding.IsVerifiedSpecialization(function)) || function.IsRequirement || (function.Captures is { Length: > 0 } && function.BoundClosure is null) ||
                (!function.IsSpecialization && function.GenericArguments.Count != 0) || function.TypeConstraints.Count != 0)
            {
                return "A selected function requires unsupported signature, capture or implementation lowering.";
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (!FunctionAbi.SupportsParameter(parameter.Type.BoundType, this.lowering.AggregateLayouts) ||
                    (parameter.DefaultValue is not null && !ScalarDefaults.Supports(function, i)) ||
                    (ReferenceTypes.IsString(parameter.Type.BoundType) && parameter.Type.BoundType!.Origin is null))
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

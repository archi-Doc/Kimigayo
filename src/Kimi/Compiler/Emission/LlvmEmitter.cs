// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Checked LLVM generation for the implemented windows-x64-v1 execution subset. Every request rechecks the
/// latest analyses; the retained module is scratch storage, not a certificate for later compilations.
/// </summary>
public sealed partial class LlvmEmitter
{
    private readonly Compilation compilation;
    private readonly EmissionModule module = new();
    private readonly BodyLowering lowering = new();
    private readonly FunctionAbiPool signatures = new();
    private readonly GenericStoragePlan generics = new();
    private readonly ObjectGenerationPlan objects = new();
    private readonly VirtualGenerationPlan virtuals = new();
    private readonly DefaultGenerationPlan defaults = new();
    private readonly CompilerFunctionAdapters compilerEntries = new();
    private readonly Dictionary<FunctionKoto, FunctionAbi> functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundProperty, FunctionAbi> staticGetters = new(ReferenceEqualityComparer.Instance);
    private readonly List<StaticScalarEntry> staticEntries = new();
    private readonly List<(string Symbol, FunctionAbi Abi)> importAbis = new();
    private bool resourceLimit;
    private string? instanceFailureContext;
    private Koto? instanceFailureSite;

    internal LlvmEmitter(Compilation compilation)
    {
        this.compilation = compilation;
        this.lowering.RequireDictionaryOperation = (find, key, value) => this.generics.RequireSourceCall(this.compilation.Binding.DictionaryStorageCall(find, key, value));
        this.lowering.AggregateLayouts.InstantiateDestructor = type => this.generics.RequireDestructor(this.compilation.Binding, type);
        this.lowering.AggregateLayouts.IsSealedObjectTarget = type => this.compilation.Binding.ProveSealed(type, this.compilation.Kotonoha.RootKoto) == ConstraintProof.Proven;
    }

    /// <summary>Verifies the entire selected input against the implemented execution subset.</summary>
    /// <param name="failure">A concrete reason when generation cannot proceed.</param>
    /// <returns>Whether the latest analysis proves every operation this subset lowers.</returns>
    public bool Validate(out string? failure)
        => this.TryPrepare(out _, out failure);

    /// <summary>Gets a value indicating whether the last failure exceeded a generation resource limit (SPEC 21.3.5: generic contexts, instance ownership storage or inline layout depth, size or count), not a semantic or representation obligation.</summary>
    public bool FailureIsResourceLimit => this.resourceLimit;

    internal BoundCall? FailureInstance { get; private set; }

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

    // Both generation commands and diagnostic adapters consume the retained failure before another preparation.
    internal void ReportFailure(string? failure)
    {
        var c = this.compilation;
        c.Diagnostics.Invalidate(DiagnosticPartition.Emission);
        if (c.Ownership.FailedInstance is { } failed)
        {
            c.Ownership.ReportInstanceDiagnostics(failed, this.instanceFailureContext, this.instanceFailureSite);
        }
        else
        {
            c.Diagnostics.Report(DiagnosticPartition.Emission, this.resourceLimit ? DiagnosticCode.GenerationResourceLimit_Kd : DiagnosticCode.GenerationFailed_Kd, c.Project.FilePath, note: failure);
        }
    }

    // Consumed synchronously before the next preparation. A failure leaves the module unwritable.
    internal bool TryPrepare(out EmissionModule module, out string? failure)
    {
        module = this.module;
        module.Clear();
        var c = this.compilation;
        c.Diagnostics.Invalidate(DiagnosticPartition.Emission);
        failure = null;
        this.resourceLimit = false;
        this.instanceFailureContext = null;
        this.instanceFailureSite = null;
        this.FailureInstance = null;
        c.Ownership.ClearInstances();
        this.generics.Clear();
        this.defaults.Clear();
        this.lowering.Defaults = this.defaults;
        this.compilerEntries.Begin(c, module, this.lowering, this.generics, this.objects);
        this.lowering.CompilerEntries = this.compilerEntries;
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

            if (!this.RegisterImports(c.Binding.LibraryImports, module, this.functions, out failure))
            {
                return false;
            }

            for (var i = 0; i < c.Ownership.Bodies.Count; i++)
            {
                var body = c.Ownership.Bodies[i];
                if (body.Function.StaticInitializer is not { } property)
                {
                    continue;
                }

                var abi = this.functions[body.Function];
                var slotOrdinal = module.Statics.Count;
                if (slotOrdinal == this.staticEntries.Count)
                {
                    this.staticEntries.Add(new(abi));
                }
                else if (!ReferenceEquals(this.staticEntries[slotOrdinal].Initializer, abi))
                {
                    this.staticEntries[slotOrdinal] = new(abi);
                }

                var slot = this.staticEntries[slotOrdinal];

                if (!StaticScalar.IsDynamic(property))
                {
                    failure = "Static initializer requires a verified closed scalar declaration.";
                    return false;
                }

                module.Statics.Add(slot);
                this.staticGetters.Add(property, slot.Getter);
            }

            this.lowering.StaticGetters = this.staticGetters;

            if (!this.virtuals.Begin(c, module, this.functions, this.generics, this.lowering.AggregateLayouts))
            {
                failure = "Virtual table entries do not match the verified slot correspondence and ABI.";
                return false;
            }

            this.lowering.Virtuals = this.virtuals;
            this.objects.Virtuals = this.virtuals;

            module.DictionaryAppendSlot = this.functions.GetValueOrDefault(c.Library.DictionaryAppendSlot);
            module.DictionaryInitialize = this.functions.GetValueOrDefault(c.Library.DictionaryInitialize);
            module.DictionaryClearLinks = this.functions.GetValueOrDefault(c.Library.DictionaryClearLinks);
            module.DictionaryRequireAbsent = this.functions.GetValueOrDefault(c.Library.DictionaryRequireAbsent);
            module.DictionaryReserveStorage = this.functions.GetValueOrDefault(c.Library.DictionaryReserveStorage);
            module.DictionaryAppend = this.functions.GetValueOrDefault(c.Library.DictionaryAppend);
            module.DictionaryShrink = this.functions.GetValueOrDefault(c.Library.DictionaryShrink);

            if (!this.generics.Prepare(c, module, this.lowering.AggregateLayouts, this.functions, out failure))
            {
                this.resourceLimit = this.generics.ResourceLimitExceeded;
                return false;
            }

            this.lowering.GenericCalls = this.generics.Calls;
            this.lowering.FormattingCalls = this.generics.FormattingCalls;
            this.lowering.ComparisonCalls = this.generics.ComparisonCalls;
            this.lowering.ComparisonHelpers = this.generics.ComparisonHelpers;
            if (!this.objects.Prepare(c, module, this.lowering.AggregateLayouts, this.generics, out failure))
            {
                return false;
            }

            this.lowering.ObjectCalls = this.objects.Calls;
            this.lowering.ObjectRuntimeTypes = this.objects.RuntimeTypes;
            if (!this.LowerInstances(c, module, out failure))
            {
                return false;
            }

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

            // Destructors can introduce further closed local Types. Drain their ordinary generic entries
            // to a fixed point, after each body has finished using the reusable layout scratch storage.
            while (this.generics.HasPendingSourceCalls || this.defaults.HasPending || module.PendingEntries.Count != 0)
            {
                if (!this.LowerDefaults(c, module, out failure) ||
                    !this.generics.PrepareSourceCalls(c, module, this.lowering.AggregateLayouts, out failure) ||
                    !this.objects.PrepareInstances(c, module, this.lowering.AggregateLayouts, this.generics, out failure) ||
                    !this.LowerInstances(c, module, out failure))
                {
                    this.resourceLimit = this.generics.ResourceLimitExceeded || this.defaults.ResourceLimitExceeded;
                    return false;
                }
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
            this.resourceLimit |= this.defaults.ResourceLimitExceeded || this.generics.ResourceLimitExceeded;
            if (!module.IsComplete && this.virtuals.Failure is { } virtualFailure)
            {
                failure = virtualFailure;
            }

            // Never retain a previous parse through the active declaration-to-ABI map.
            if (!module.IsComplete && this.lowering.AggregateLayouts.ResourceLimitFailure is { } limit)
            {
                this.resourceLimit = true;
                failure = limit;
            }

            if (!module.IsComplete && c.Ownership.InstanceStorageLimit is { } storageLimit)
            {
                this.resourceLimit = true;
                failure = storageLimit;
            }

            this.compilerEntries.Clear();
            this.functions.Clear();
            this.staticGetters.Clear();
            this.generics.Clear();
            c.Ownership.ClearInstances(preserveFailure: true);
            this.objects.Complete();
            this.objects.Clear();
            this.virtuals.Complete();
            this.virtuals.Clear();
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
    private bool RegisterImports(IReadOnlyList<LibraryImport> imports, EmissionModule module, Dictionary<FunctionKoto, FunctionAbi> functions, out string? failure)
    {
        failure = null;
        var ordinal = 0;
        for (var i = 0; i < imports.Count; i++)
        {
            var import = imports[i];
            if (ReferenceEquals(import.Function.CodeContext.Kotonoha, this.compilation.Library.Kotonoha) &&
                !this.compilation.Ownership.UsesImport(import.Function))
            {
                continue;
            }

            var cached = ordinal < this.importAbis.Count && this.importAbis[ordinal].Symbol == import.Symbol ? this.importAbis[ordinal].Abi : null;
            var name = cached?.Name ?? LlvmModuleWriter.ExternalName(import.Symbol);
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
                abi = Matches(cached, import.Function) ? cached : CreateImportAbi(import, name);
                if (abi is null)
                {
                    failure = "A foreign import needs an unsupported parameter or result representation.";
                    return false;
                }

                module.Externals.Add(new(abi, import.Kind == "import"));
            }

            if (ordinal == this.importAbis.Count)
            {
                this.importAbis.Add((import.Symbol, abi));
            }
            else
            {
                this.importAbis[ordinal] = (import.Symbol, abi);
            }

            ordinal++;
            functions.Add(import.Function, abi);
        }

        return true;

        static bool Matches(FunctionAbi? abi, FunctionKoto function)
        {
            if (abi is null || abi.Result != (ReferenceEquals(function.BoundSymbol!.Type, BoundType.Unit) ? "void" : ImportType(function.BoundSymbol.Type!)) || abi.Parameters.Length != function.Parameters.Count)
            {
                return false;
            }

            for (var i = 0; i < abi.Parameters.Length; i++)
            {
                if (abi.Parameters[i].Type != ImportType(function.Parameters[i].Type.BoundType!))
                {
                    return false;
                }
            }

            return true;
        }

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

        // Binding admits fixed-width integers, f32/f64, raw pointers and ref/uniq borrows of C-exchangeable referents, which pass
        // the referent's address (SPEC 22.3.2); no Scalar borrow is passed by value.
        static string? ImportType(BoundType type)
            => ReferenceTypes.IsPointer(type) || ReferenceTypes.IsReference(type) ? "ptr" : type.Kind == BoundTypeKind.Primitive ? WindowsLowering.GetValue(type)?.ArgumentType : null;
    }

    // Only the selected implicit Application body executes. Other source-module
    // wrappers are checked by ownership but are not callable implementations.
    // SPEC 21.3.1 monomorphization: each concrete call context of a universally verified generic body
    // is analyzed under its closed substitution and lowered as an ordinary concrete body, under the
    // entry ABI its callers already use. A refused instance fails generation; it never falls back.
    private bool LowerInstances(Compilation c, EmissionModule module, out string? failure)
    {
        failure = null;
        // A factory Item or a direct base call can discover descriptor dependencies while lowering.
        // The next worklist pass prepares their object calls before lowering the appended entries.
        var count = this.generics.Entries.Count;
        for (var index = 0; index < count; index++)
        {
            var entry = this.generics.Entries[index];
            var call = entry.Context!;
            // A selected explicit specialization (SPEC 21.3.4) is never pending; a reused entry is lowered once.
            if (!module.PendingEntries.Contains(entry))
            {
                continue;
            }

            var lowered = false;
            this.generics.ExpansionParent = entry;
            if (c.Ownership.AnalyzeInstance(entry.Template.Body, call) is { } body &&
                this.objects.PrepareBodyCalls(c, module, this.lowering.AggregateLayouts, body, out failure))
            {
                var function = module.AddFunction(entry.Abi, exported: false);
                this.lowering.SetInstance(c.Binding, call, entry);
                try
                {
                    lowered = this.lowering.Lower(c.Library, body, function, module.Constants, c.Project.Directory, this.functions, c.Ownership.ControlFlow!, c.PointerWidth, out failure);
                }
                finally
                {
                    this.lowering.SetInstance(null, null, null);
                }

                if (lowered)
                {
                    module.NeedsStringComparison |= function.NeedsStringComparison;
                    this.lowering.RegisterAggregates(module);
                    module.PendingEntries.Remove(entry);
                }
                else
                {
                    module.RemoveLastFunction();
                }
            }

            this.generics.ExpansionParent = null;

            // No fallback: every generic call context must reach its concrete instance.
            if (!lowered)
            {
                this.FailureInstance = call;
                var description = Describe(entry);
                this.instanceFailureContext = $"While instantiating {description}";
                this.instanceFailureSite = this.FindInstanceSite(call, entry);
                failure = $"Generic instance {description}: {failure ?? "ownership analysis under the substitution failed."}";
                return false;
            }
        }

        return true;

        // The refused instance uses the declaration's slot order; the parallel Type/length arrays do not add slots.
        static string Describe(GenericStoragePlan.CallEntry entry)
        {
            var arguments = new string[entry.Template.Body.Function.GenericArguments.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = entry.Template.Body.Function.GenericArguments[i] is LengthParameterKoto
                    ? entry.Lengths[i] is { } length ? length.Parameter?.Name ?? $"{length.Value}" : "?"
                    : entry.Arguments[i] is { } type ? Binding.DiagnosticTypeName(type) : "?";
            }

            return arguments.Length == 0 ? entry.Template.Body.Function.Name : entry.Template.Body.Function.Name + "<" + string.Join(", ", arguments) + ">";
        }
    }

    private Koto? FindInstanceSite(BoundCall call, GenericStoragePlan.CallEntry entry)
    {
        var root = entry;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        // Only a failure walks source calls. Forwarded instances retain the root call that requested their expansion.
        foreach (var body in this.compilation.Ownership.Bodies)
        {
            foreach (var operation in body.Operations)
            {
                if (operation.Source is InvocationKoto { BoundCall: { } sourceCall } source &&
                    (ReferenceEquals(sourceCall, call) || (this.generics.Calls.TryGetValue(sourceCall, out var sourceEntry) && ReferenceEquals(sourceEntry, root))))
                {
                    return source;
                }
            }
        }

        return null;
    }

    private bool SkipGenerated(OwnershipBody body)
        => (body.Function.IsGenerated && !ReferenceEquals(body.Function, this.compilation.Binding.Startup.Function)) ||
            this.compilation.Binding.IsInapplicableVirtualBody(body.Function);

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

        // Emission reads every front-end partition: an Error anywhere blocks it.
        if (c.Diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership))
        {
            return "Emission requires a front end without errors.";
        }

        foreach (var sourceModule in c.SourceModules)
        {
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
                continue; // Each closed instance is validated by BodyLowering under its substitution (LowerInstances).
            }

            if (function.BoundSymbol?.Scope.Owner is StructKoto && !function.IsConstructor && !function.IsDestructor &&
                function.BoundSymbol.ReceiverIndex >= 0 && !ReferenceTypes.IsStruct(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType) &&
                !ObjectTypes.IsBorrow(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType) &&
                !StructStorage.IsStruct(function.Parameters[function.BoundSymbol.ReceiverIndex].Type.BoundType))
            {
                return "Ordinary structure methods need receiver/call lowering outside this subset.";
            }

            var result = function.BoundSymbol?.Type ?? (function.IsGenerated ? BoundType.Unit : null);
            if (!body.IsConcrete || !body.IsVerified || (!function.IsGenerated && function.BoundSymbol is null) ||
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
                    (ReferenceTypes.IsString(parameter.Type.BoundType) && parameter.Type.BoundType!.Origin is null))
                {
                    return "Parameters require verified value, owned-slot or shared-string representations.";
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
            (x is PropertyKoto property && (StaticScalar.TryGet(property.BoundSymbol?.Property, out _) || StaticScalar.IsDynamic(property.BoundSymbol?.Property))));
    }
}

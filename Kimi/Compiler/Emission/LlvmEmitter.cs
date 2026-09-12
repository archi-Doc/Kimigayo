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
        failure = this.CheckInputs(out var body);
        if (failure is not null)
        {
            return false;
        }

        // The implicit body lowers to a private function without a source return target (SPEC 22.2.1).
        var entry = module.AddFunction(WindowsLowering.Entry, exported: false);
        if (!this.lowering.Lower(c.Core, body!, entry, module.Constants, c.Project.Directory, out failure))
        {
            module.Clear();
            return false;
        }

        // __kimi_start: run the selected body once, then Runtime.Exit(0); empty init/shutdown helpers are omitted (SPEC 22.2.3).
        var start = module.AddFunction(WindowsLowering.Start, exported: true);
        start.AddCall(-1, WindowsLowering.Entry, []);
        start.AddCall(-1, WindowsLowering.Exit, [new(EmissionOperandKind.Integer, 0)]);
        start.Add(EmissionOpcode.Unreachable, -1);
        module.Complete();
        return true;
    }

    private string? CheckInputs(out OwnershipBody? body)
    {
        body = null;
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout)
        {
            return "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }

        if (!c.Binding.Result.IsComplete || c.Binding.Obligations.Count != 0 || !c.Core.IsValid ||
            c.Kotonoha.HasSourceErrors || c.Kotonoha.DiagnosticCollection.HasErrors || !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            return "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }

        if (c.KotonohaArray.Length != 0 || startup.OutputKind != OutputKind.Application || startup.Kind != StartupKind.Implicit ||
            c.Kotonoha.RootKoto.NestedContainers.Count != 0)
        {
            return "This partial emitter supports one implicit Application body without external modules, explicit main or declaration containers.";
        }

        // Unused selected bodies still require lowering diagnostics (SPEC 21.4.1); no user function is lowered yet.
        var members = c.Kotonoha.RootKoto.Members;
        for (var i = 0; i < members.Count; i++)
        {
            if (!ReferenceEquals(members[i], startup.Function) && members[i] is not AliasKoto)
            {
                return "Additional selected implementation bodies are outside the implemented execution subset.";
            }
        }

        if (c.Ownership.Bodies.Count != 1 || c.Ownership.Bodies[0] is not { IsConcrete: true, IsVerified: true } selected ||
            !ReferenceEquals(selected.Function, startup.Function))
        {
            return "Additional selected implementation bodies are outside the implemented execution subset.";
        }

        body = selected;
        return null;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // The universal (source) plan of each analyzed function, never an instance plan.
    private readonly Dictionary<FunctionKoto, OwnershipBody> templateBodies = new(ReferenceEqualityComparer.Instance);
    private readonly List<Koto> effectViolations = new();

    /// <summary>Gets the universal plan of a function body, the cleanups of which are the values the body destroys.</summary>
    /// <param name="function">The function.</param>
    /// <param name="analyze">Whether a library body not yet reached, such as the destructor of a destroyed value, is analyzed now.</param>
    /// <returns>The plan, or null for a body this analysis does not reach.</returns>
    internal OwnershipBody? TemplateBody(FunctionKoto function, bool analyze)
    {
        if (this.templateBodies.TryGetValue(function, out var body) || !analyze ||
            !ReferenceEquals(function.CodeContext.Kotonoha, this.compilation.Library.Kotonoha))
        {
            return body;
        }

        var start = this.libraryBodies.Count;
        this.CollectLibraryBody(function);
        this.AnalyzeLibraryBodies(start);
        return this.templateBodies.GetValueOrDefault(function);
    }

    // Library bodies are analyzed as calls reach them; analyzing one may reach further ones.
    private void AnalyzeLibraryBodies(int start)
    {
        for (var i = start; i < this.libraryBodies.Count; i++)
        {
            this.flow!.Append(this.libraryBodies[i]);
            this.collector.Visit(this.libraryBodies[i]);
        }
    }

    // SPEC 8.4.5, 22.1.2.4: an effect bound counts the destructions its implementation's bodies perform, which are the
    // cleanups planned here; Binding re-checks each bound with them once every reached body is analyzed.
    private void ValidateEffectBounds()
    {
        this.effectViolations.Clear();
        this.compilation.Binding.ValidateDestructionEffects(this.effectViolations);
        this.AddEffectIssues(OwnershipFailure.EffectBound);
        this.compilation.Binding.ValidateCallableEffects(this.effectViolations);
        this.AddEffectIssues(OwnershipFailure.CallableEffectBound);
        this.compilation.Binding.ValidateVirtualDestructionEffects(this.effectViolations);
        this.AddEffectIssues(OwnershipFailure.VirtualEffectBound);
    }

    // A check that stopped at an effect Binding cannot classify yet is the located limit (plan rule 5).
    private void AddEffectIssues(OwnershipFailure failure)
    {
        for (var i = 0; i < this.effectViolations.Count; i++)
        {
            var use = this.effectViolations[i];
            this.issues.Add(new(use, this.compilation.Binding.IsEffectLimit(use) ? OwnershipFailure.Unsupported : failure));
        }

        this.effectViolations.Clear();
    }
}

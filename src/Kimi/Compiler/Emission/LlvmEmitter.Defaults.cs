// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class LlvmEmitter
{
    private bool LowerDefaults(Compilation c, EmissionModule module, out string? failure)
    {
        failure = null;
        while (this.defaults.HasPending)
        {
            var entry = this.defaults.Next();
            this.defaults.Parent = entry;
            var template = c.Ownership.TemplateBody(entry.Function, analyze: false);
            if (template is null || c.Ownership.AnalyzeInstance(template, entry.Call, entry.Parameter) is not { } body)
            {
                failure = "Default evaluator requires its universally verified declaration and concrete ownership plan.";
                return false;
            }

            if (!this.generics.PrepareDefault(c, module, this.lowering.AggregateLayouts, body, entry, out failure) ||
                !this.objects.PrepareDefault(c, module, this.lowering.AggregateLayouts, entry.Context!, out failure))
            {
                return false;
            }

            var function = module.AddFunction(entry.Abi, exported: false);
            this.lowering.SetInstance(c.Binding, entry.Call, entry.Context);
            try
            {
                if (!this.lowering.Lower(c.Library, body, function, module.Constants, c.Project.Directory, this.functions, c.Ownership.ControlFlow!, c.PointerWidth, out failure))
                {
                    return false;
                }
            }
            finally
            {
                this.lowering.SetInstance(null, null, null);
                this.defaults.Parent = null;
            }

            module.NeedsStringComparison |= function.NeedsStringComparison;
            this.lowering.RegisterAggregates(module);
        }

        return true;
    }
}

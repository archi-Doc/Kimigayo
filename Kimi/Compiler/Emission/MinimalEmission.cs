// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Checked literal-output lowering. Reuses the existing CFG, Symbols, Places and cleanup plans.</summary>
public sealed class MinimalEmission
{
    private readonly Compilation compilation;
    private readonly EmissionPlan plan = new();

    internal MinimalEmission(Compilation compilation) => this.compilation = compilation;

    /// <summary>Verifies the entire selected input against the deliberately small execution subset.</summary>
    /// <param name="failure">A concrete reason when generation cannot proceed.</param>
    /// <returns>Whether the latest analysis proves every operation required by this subset.</returns>
    public bool Validate(out string? failure)
        => this.TryPrepare(out _, out failure);

    /// <summary>Writes inspection IR after checking the latest analysis. Does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        if (!this.TryPrepare(out var plan, out failure))
        {
            return false;
        }

        plan.WriteIr(writer);
        return true;
    }

    // Plans are compilation-local scratch, consumed synchronously before the next preparation.
    // The writer consumes only lowered facts, never the mutable AST or ownership solver.
    internal bool TryPrepare(out EmissionPlan plan, out string? failure)
    {
        plan = this.plan;
        plan.Clear();
        failure = null;
        var c = this.compilation;
        var startup = c.Binding.Startup;
        if (c.BuildMetadata?.TargetTriple != WindowsProfile.Target || c.IrTarget.DataLayout != WindowsProfile.DataLayout)
        {
            failure = "Emission requires the verified windows-x64-v1 target and DataLayout.";
        }
        else if (!c.Binding.Result.IsComplete || c.Binding.Obligations.Count != 0 || !c.Core.IsValid ||
            c.Kotonoha.HasSourceErrors || c.Kotonoha.DiagnosticCollection.HasErrors || !startup.IsComplete || !c.Ownership.Result.IsVerified)
        {
            failure = "Emission requires current final Binding, startup, control-flow and ownership verification without errors.";
        }
        else if (c.KotonohaArray.Length != 0 || startup.Kind != StartupKind.Implicit || startup.OutputKind != OutputKind.Application ||
            c.Kotonoha.SourceDocuments.Count != 1 || c.Ownership.Bodies.Count != 1 || c.Kotonoha.RootKoto.NestedContainers.Count != 0)
        {
            failure = "This partial emitter supports one implicit Application document without external modules or declaration containers.";
        }
        else
        {
            var body = c.Ownership.Bodies[0];
            var items = startup.Function!.Body!.Items;
            if (!ReferenceEquals(body.Function, startup.Function) || !body.IsConcrete || !body.IsVerified || items.Count != 1 ||
                items[0] is not InvocationKoto call || !this.IsLiteralCall(call))
            {
                failure = "This partial emitter requires one Core.writeLine call with a string literal; other selected bodies and operations are unsupported.";
                return false;
            }

            // Do not let unused declarations evade the pre-optimization generation gate.
            var members = c.Kotonoha.RootKoto.Members;
            for (var i = 0; i < members.Count; i++)
            {
                if (!ReferenceEquals(members[i], startup.Function) && members[i] is not AliasKoto)
                {
                    failure = "Additional selected implementation bodies are outside the literal-output execution subset.";
                    return false;
                }
            }

            var literal = (StringLiteralKoto)call.ArgumentNodes[0];
            for (var i = 0; i < body.Places.Count; i++)
            {
                var place = body.Places[i];
                if (!ReferenceEquals(place.Type, BoundType.Unit) &&
                    !(ReferenceEquals(place.Type, BoundType.String) && ReferenceEquals(place.Source, literal) && place.Kind == OwnershipPlaceKind.Temporary))
                {
                    failure = "A Place needs unsupported layout, storage or lifetime verification.";
                    return false;
                }
            }

            var calls = 0;
            var entries = 0;
            for (var i = 0; i < body.Operations.Count; i++)
            {
                var op = body.Operations[i];
                switch (op.Kind)
                {
                    case OwnershipOperationKind.Entry:
                    case OwnershipOperationKind.Exit:
                    case OwnershipOperationKind.Deliver:
                    case OwnershipOperationKind.Produce:
                    case OwnershipOperationKind.Cleanup:
                        break;
                    case OwnershipOperationKind.CallEntry when ReferenceEquals(op.Source, call) && op.Place >= 0 &&
                        ReferenceEquals(body.Places[op.Place].Source, literal) &&
                        (body.GetInputState(i, op.Place) & PlaceState.MustInit) != 0:
                        entries++;
                        break;
                    case OwnershipOperationKind.Call when ReferenceEquals(op.Source, call):
                        calls++;
                        break;
                    default:
                        failure = "An ownership operation needs unsupported lowering or Loan/Origin verification.";
                        return false;
                }

                var normals = 0;
                for (var edge = body.EdgeHeads[i]; edge >= 0; edge = body.Edges[edge].Next)
                {
                    var kind = body.Edges[edge].Kind;
                    if (kind == OwnershipEdgeKind.Abort && op.Kind == OwnershipOperationKind.Call)
                    {
                        continue; // The verified Core body never returns on this edge.
                    }

                    if (kind is not (OwnershipEdgeKind.Normal or OwnershipEdgeKind.Return) || ++normals > 1)
                    {
                        failure = "A control-flow edge needs unsupported lowering.";
                        return false;
                    }
                }
            }

            for (var i = 0; i < body.CleanupSteps.Count; i++)
            {
                var step = body.CleanupSteps[i];
                if (step.Action is not (CleanupAction.Skip or CleanupAction.Destroy))
                {
                    failure = "Cleanup is conditional or unsupported.";
                    return false;
                }
            }

            if (calls != 1 || entries != 1)
            {
                failure = "The verified CFG must acquire and invoke the output argument exactly once.";
            }
            else if (!plan.Lower(body, call, c.Project.Directory, out failure))
            {
                return false;
            }
        }

        return failure is null;
    }

    private bool IsLiteralCall(InvocationKoto call)
    {
        var plan = call.BoundCall;
        return plan is not null && ReferenceEquals(plan.Target, this.compilation.Core.WriteLine) && plan.Receiver is null &&
            plan.TypeArguments.Length == 0 && plan.Origins.Length == 0 && ReferenceEquals(plan.ReturnType, BoundType.Unit) &&
            call.AttributeChain is null && call.ArgumentNodes.Count == 1 && call.ArgumentNodes[0] is StringLiteralKoto { AttributeChain: null } &&
            plan.ArgumentOperations.Length == 1 && plan.ArgumentOperations[0].Kind == ArgumentOperationKind.Value &&
            ReferenceEquals(plan.ArgumentOperations[0].ParameterType, BoundType.String) && plan.ArgumentToParameter[0] == 0;
    }
}

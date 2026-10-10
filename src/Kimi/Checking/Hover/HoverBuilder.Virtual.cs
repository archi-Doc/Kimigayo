// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Checking;

internal sealed partial class HoverBuilder
{
    private HoverKey VirtualIdentity(CallPlan call)
        => call.Kind != CalleeKind.Virtual ? HoverKey.Missing : new(
            call.VirtualIsDirect ? "direct base" : "dynamic slot",
            [this.BinderIdentity(call.VirtualSlot.Original), this.TypeIdentity(call.VirtualSlot.DeclaringType),
            this.TypeIdentity(call.VirtualBaseLookupType), this.BinderIdentity(call.VirtualImplementation), this.TypeIdentity(call.VirtualImplementingType)]);

    private void VirtualCallDetails(StringBuilder text, CallPlan call, Koto syntax)
    {
        if (call.Kind != CalleeKind.Virtual)
        {
            return;
        }

        text.AppendLine().Append("Dispatch: ").Append(call.VirtualIsDirect ? "direct base" : "dynamic");
        text.AppendLine().Append("Bound slot: ").Append(this.TypeName(call.VirtualSlot.DeclaringType)).Append('.').Append(call.VirtualSlot.Original.Name);
        if (call.VirtualBaseLookupType is { } lookup)
        {
            text.AppendLine().Append("Base lookup: ").Append(this.TypeName(lookup));
        }

        if (call.VirtualImplementation is { } implementation && call.VirtualImplementingType is { } implementing)
        {
            text.AppendLine().Append("Implementation: ").Append(this.TypeName(implementing)).Append('.').Append(implementation.Name);
        }

        if (call.ReceiverOperation is { SourceType: { } source, ParameterType: { } target } receiver)
        {
            text.AppendLine().Append("Receiver: ").Append(this.TypeName(source)).Append(" -> ").Append(this.TypeName(target));
            text.AppendLine().Append("Receiver compatibility: ").Append(receiver.ObjectCompatibility);
        }

        if (query.TryGetObjectErasure(syntax, out var erasure))
        {
            text.AppendLine().Append("Base-view erasure: ").Append(this.TypeName(erasure.Source)).Append(" -> ").Append(this.TypeName(erasure.Target));
            text.Append(erasure.Entry is null ? " (Owned proven)" : " (inherited override-entry evidence)");
        }
    }

    private string VirtualItemDetails(BoundType item, FunctionKoto original)
        => "Dispatch: dynamic\nBound slot: " + (query.ItemDeclaringType(item, original) is { } declaring ? this.TypeName(declaring) : this.Owner(original)) + "." + original.Name;

    private string VirtualDeclarationDetails(FunctionKoto function)
    {
        var text = new StringBuilder();
        if (function.IsVirtual)
        {
            text.Append("Public virtual slot: ").Append(this.Qualified(function));
        }
        else if (query.TryGetVirtualOverride(function, out var implementation))
        {
            text.Append("Inherited public contract: ").Append(this.TypeName(implementation.Slot.DeclaringType)).Append('.').Append(implementation.Slot.Original.Name);
        }
        else
        {
            text.Append("Override correspondence: unavailable");
        }

        if (function.ReturnType is null && query.SymbolOf(function)?.Type is { } result)
        {
            text.AppendLine().Append("Implicit result: ").Append(this.TypeName(result));
        }

        // Binding correspondence and ownership completion are distinct; neither implies artifact publication.
        text.AppendLine().Append("Binding checks: ").Append(query.FailureOf(function) != BindingFailure.None ? "rejected" : query.IsCurrent(function) ? "completed" : "pending");
        text.AppendLine().Append("Ownership checks: ").Append(query.Compilation.Ownership.TemplateBody(function, false) is { IsVerified: true } ? "completed" : "pending");
        return text.ToString();
    }
}

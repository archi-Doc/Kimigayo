// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Checking;

internal sealed partial class HoverBuilder
{
    private HoverKey VirtualIdentity(BoundVirtualCall? call)
        => call is null ? HoverKey.Missing : new(
            call.IsDirect ? "direct base" : "dynamic slot",
            [this.BinderIdentity(call.Slot.Original), this.TypeIdentity(call.Slot.DeclaringType),
            this.TypeIdentity(call.BaseLookupType), this.BinderIdentity(call.Implementation), this.TypeIdentity(call.ImplementingType)]);

    private void VirtualCallDetails(StringBuilder text, BoundCall call, Koto syntax)
    {
        if (call.VirtualDispatch is not { } dispatch)
        {
            return;
        }

        text.AppendLine().Append("Dispatch: ").Append(dispatch.IsDirect ? "direct base" : "dynamic");
        text.AppendLine().Append("Bound slot: ").Append(this.TypeName(dispatch.Slot.DeclaringType)).Append('.').Append(dispatch.Slot.Original.Name);
        if (dispatch.BaseLookupType is { } lookup)
        {
            text.AppendLine().Append("Base lookup: ").Append(this.TypeName(lookup));
        }

        if (dispatch.Implementation is { } implementation && dispatch.ImplementingType is { } implementing)
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

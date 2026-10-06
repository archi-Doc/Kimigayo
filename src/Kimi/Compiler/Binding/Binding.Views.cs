// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<IndexKoto, InvocationKoto> viewRangeCalls = new(ReferenceEqualityComparer.Instance);

    internal InvocationKoto? ViewRangeCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto { BindingState: BindingState.Resolved } index &&
            this.viewRangeCalls.TryGetValue(index, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    private bool TryBindViewSelection(IndexKoto source, BindingScope scope, BoundType? receiver, out BoundType? result)
    {
        result = null;
        var core = receiver;
        while (core is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var inner] })
        {
            core = inner;
        }

        if (core?.Symbol?.LibraryDeclaration != KimiDeclarationId.UniqSlice || core.Components is not [var element])
        {
            return false;
        }

        var key = this.BindPositionKey(source.Right, scope);
        if (key is null)
        {
            result = this.CompleteDependent(source, source.Right);
            return true;
        }

        var range = ReferenceTypes.IsResolvedRange(key) || this.ProvesClosedContract(key, this.Library.PositionRange, scope);
        if (!range && !this.ProvesClosedContract(key, this.Library.Position, scope))
        {
            this.RecordMismatch(source.Right, source.Right, key, "a Type proven Position or PositionRange");
            result = this.CompleteDependent(source, source.Right);
            return true;
        }

        this.CheckLiteralKey(source, core);
        var shared = this.BindViewCall(source, scope, core, key, range, false);
        if (!range && shared is not null)
        {
            this.exclusiveIndexers.Add(source);
            if (PathAuthority(source.Left) != SemanticsKind.Ref &&
                (source.Left.BoundType?.Semantics == SemanticsKind.Uniq || Writable(source.Left) || !IsBarePlace(source.Left)))
            {
                this.BindViewCall(source, scope, core, key, false, true);
            }
        }

        result = Complete(source, shared?.BoundType);
        return true;
    }

    private InvocationKoto? BindViewCall(IndexKoto source, BindingScope scope, BoundType receiver, BoundType key, bool range, bool exclusive)
    {
        var found = range ? this.viewRangeCalls.TryGetValue(source, out var call) : this.indexerCalls.TryGetValue((source, exclusive), out call);
        if (!found || call is null || !ReferenceEquals(((SyntheticKoto)((GenericsKoto)call.Method).Identifier!).Receiver, source.Left) || !ReferenceEquals(call.ArgumentNodes[0], source.Right))
        {
            var callee = new SyntheticKoto(source) { Parent = source, Receiver = source.Left };
            call = new InvocationKoto(source, new GenericsKoto(source, callee, [new SyntheticKoto(source)]), [source.Right]);
            if (range)
            {
                this.viewRangeCalls[source] = call;
            }
            else
            {
                this.indexerCalls[(source, exclusive)] = call;
            }
        }

        var generic = (GenericsKoto)call.Method;
        var name = range ? "slice" : exclusive ? "indexPositionUniq" : "indexPosition";
        var member = receiver.Symbol?.Declaration is DeclarationContainerKoto group && this.scopes.TryGetValue(group, out var members) && members.Values.TryGetValue(name, out var selected) ? selected : null;
        ((SyntheticKoto)generic.Identifier!).Resolve(member, null, receiver);
        ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, key, null);
        ResetSynthetic(call);
        ResetSynthetic(generic);
        this.nodes.Add(call);
        return this.BindCall(call, scope, null) is null ? null : call;
    }
}

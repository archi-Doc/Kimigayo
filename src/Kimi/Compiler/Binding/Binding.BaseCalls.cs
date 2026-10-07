// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, string>? baseCallFailures;

    private BoundType? BindBaseReference(BaseReferenceKoto reference, BindingScope scope)
    {
        reference.BasePath = null;
        if (reference.Parent is not MemberAccessKoto { Right: IdentifierNameKoto } member || !ReferenceEquals(member.Left, reference) || !IsCallee(member))
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base has no value; use base.name(arguments)");
        }

        var lexical = scope.Function;
        while (lexical is { IsAnonymous: true })
        {
            lexical = this.scopes[lexical].Parent?.Function;
        }

        if (lexical?.BoundSymbol is not { ReceiverIndex: >= 0, Scope.Owner: StructKoto { BoundSymbol: { } type } structure })
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base requires a lexical derived instance function; a named local function has no implicit receiver");
        }

        if (structure.Bases.Count == 0)
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "the lexical declaring struct has no direct base");
        }

        if (!(lexical.Body is { } body && IsWithin(reference, body)) && !(lexical.ExpressionBody is { } expression && IsWithin(reference, expression)))
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base is available in an instance body, not a parameter default or declaration header");
        }

        var receiver = this.BaseReceiver(scope.Function!, lexical);
        if (receiver is null)
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base in an anonymous function requires an explicit self capture at each function boundary");
        }

        if (structure.BindingState == BindingState.Invalid || this.StoredBase(this.SelfType(type)) is not { } baseType)
        {
            return this.CompleteDependent(reference, structure);
        }

        reference.BasePath = this.MemberPath(null, structure.Bases[0], baseType);
        var actual = this.BindReference(reference, receiver, scope);
        if (actual is not null && IsObjectSemantics(actual.Semantics))
        {
            return this.Fail(reference, BindingFailure.Unsupported, true);
        }

        return actual;
    }

    private BindingSymbol? BaseReceiver(FunctionKoto function, FunctionKoto lexical)
    {
        if (ReferenceEquals(function, lexical))
        {
            return this.ParameterSymbol(lexical, lexical.BoundSymbol!.ReceiverIndex);
        }

        if (!function.IsAnonymous || function.Captures is null || this.scopes[function].Parent?.Function is not { } outer ||
            this.BaseReceiver(outer, lexical) is not { } source || function.ClosureStorage is not { } closure)
        {
            return null;
        }

        for (var i = 0; i < closure.Captures.Count; i++)
        {
            if (ReferenceEquals(closure.Captures[i].Source, source))
            {
                return closure.Captures[i].Environment;
            }
        }

        return null;
    }

    private BindingSymbol? BaseMember(MemberAccessKoto member, BaseReferenceKoto reference, BindingScope scope)
    {
        if (this.BindNode(reference, scope) is not { } actual || reference.BasePath is not { } path || member.Right is not IdentifierNameKoto name)
        {
            return null;
        }

        var selection = this.LookupTypeMember(path.Type, name.IdentifierName, scope, actual, path);
        var selected = selection.Member ?? selection.Hidden;
        if (selection.Pending)
        {
            this.Fail(reference, BindingFailure.Unsupported, true);
            return null;
        }

        if (selection.Ambiguous)
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        if (selected is not null)
        {
            if (selected.Kind != BindingSymbolKind.Function || selected.ReceiverIndex < 0)
            {
                this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base.name(arguments) must name an instance function; use Type qualification for receiverless functions");
                return null;
            }

            this.memberSelections[member] = selection;
            member.BoundSymbol = selected;
            name.BoundSymbol = selected;
            name.BindingState = BindingState.Resolved;
        }

        return selected;
    }

    private void ReportBaseCall(BaseReferenceKoto reference, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (code == DiagnosticCode.InvalidBaseCall_Kd)
        {
            reference.Report(requirement, code, evidence: [this.baseCallFailures![reference]]);
        }
        else
        {
            reference.Report(requirement, code, note: "Object receiver projection and virtual implementation selection for direct base calls are not yet implemented");
        }
    }
}

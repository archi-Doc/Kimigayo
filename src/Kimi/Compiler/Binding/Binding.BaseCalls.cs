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

        // SPEC 6.4.4: lexical self is the receiver of the function whose body holds the reference, an instance accessor's included (DECISIONS
        // BU-53), and the lexical declaring struct is that function's.
        if (this.types.EnclosingFunction(scope) is not
            { ReceiverIndex: >= 0, Declaration: FunctionKoto lexical, Scope.Owner: StructKoto { BoundSymbol: { } type } structure })
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base requires a derived instance function; named locals have no implicit self");
        }

        if (structure.Bases.Count == 0)
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "the lexical declaring struct has no direct base");
        }

        if (!(lexical.Body is { } body && IsWithin(reference, body)) && !(lexical.ExpressionBody is { } expression && IsWithin(reference, expression)))
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base is available in an instance body, not a parameter default or declaration header");
        }

        var receiver = this.BaseReceiver(scope.Function, lexical);
        if (receiver is null)
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base in an anonymous function requires an explicit self capture at each function boundary");
        }

        if (structure.BindingState == BindingState.Invalid || AdtDef.Base(this.SelfType(type)) is not { } baseType)
        {
            return this.CompleteDependent(reference, structure);
        }

        reference.BasePath = this.MemberPath(null, structure.Bases[0], baseType);
        return this.BindReference(reference, receiver, scope);
    }

    // Lexical self as `function`, the innermost activation (a scope's Function), reads it: through the explicit self capture of each
    // anonymous function between them. `lexical` is TyCtxt.EnclosingFunction of that scope, for both callers (base calls, ObjectViews);
    // the first activation that is not anonymous is lexical's own, or null in an accessor's body, whose scopes record no Function.
    private BindingSymbol? BaseReceiver(FunctionKoto? function, FunctionKoto lexical)
    {
        if (function is not { IsAnonymous: true })
        {
            return this.ParameterSymbol(lexical, lexical.BoundSymbol!.ReceiverIndex);
        }

        if (function.Captures is null || this.BaseReceiver(this.scopes[function].Parent?.Function, lexical) is not { } source ||
            function.ClosureStorage is not { } closure)
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
        var selected = selection.Member;
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

        if (selected is null && selection.Hidden is { } hidden)
        {
            member.BoundSymbol = hidden;
            this.Fail(member, BindingFailure.Access);
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
}

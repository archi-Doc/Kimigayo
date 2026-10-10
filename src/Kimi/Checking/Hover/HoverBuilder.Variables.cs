// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Checking;

internal sealed partial class HoverBuilder
{
    private readonly Dictionary<(BindingSymbol Symbol, BoundType Type, ConstraintProof Copy), HoverInfo> variables = new();

    private void IndexVariables()
    {
        foreach (var entry in query.Declarations)
        {
            var symbol = entry.Value;
            if (symbol.Kind is not (BindingSymbolKind.Local or BindingSymbolKind.Parameter) || symbol.Type is not { } type ||
                symbol.Declaration.CodeContext.SourceDocument is not { } source || !this.documents.TryGetValue(source, out var entries))
            {
                continue;
            }

            SourceSpan span;
            Koto context;
            if (entry.Key is FunctionParameterKoto parameter)
            {
                context = parameter.Type;
                span = parameter.InternalNameSpan;
            }
            else
            {
                context = symbol.Declaration;
                span = context switch
                {
                    VariableKoto variable => variable.NameKoto.Span,
                    SyntaxFormKoto { Akind: KotoKind.BindingPattern, Operands: [IdentifierNameKoto name, ..] } => name.Span,
                    IdentifierNameKoto name => name.Span,
                    _ => default,
                };
            }

            this.budget.Charge();
            if (span.Length == 0 || query.StateOf(context) != BindingState.Resolved || query.IsRecovery(context) ||
                !source.AsSpan().Slice(span.Start, span.Length).SequenceEqual(symbol.Name))
            {
                continue;
            }

            var info = this.Variable(symbol, type, context);
            entries[span] = new(context, info, 2);
            if (entry.Key is FunctionParameterKoto { ExternalNameSpan: { Length: > 0 } external } && external != span)
            {
                entries[external] = new(context, info, 2);
            }
        }
    }

    private HoverInfo? Variable(BindingSymbol symbol, BoundType type, Koto context)
    {
        if (symbol.Name is "_" || symbol.Name.StartsWith('$') ||
            (symbol.Scope.Values.TryGetValue(symbol.Name, out var selected) && selected.Next is not null))
        {
            return null;
        }

        var copy = this.Copy(type, context);
        if (this.variables.TryGetValue((symbol, type, copy), out var existing))
        {
            return existing;
        }

        var syntax = symbol.Declaration;
        var source = syntax.CodeContext.SourceDocument;
        var parameter = symbol.Kind == BindingSymbolKind.Parameter && syntax is FunctionKoto function && (uint)symbol.Slot < (uint)function.Parameters.Count
            ? function.Parameters[symbol.Slot] : null;
        if (symbol.Kind == BindingSymbolKind.Parameter && parameter is null)
        {
            return null;
        }

        var nameSpan = parameter?.InternalNameSpan ?? (syntax is VariableKoto local ? local.NameKoto.Span : syntax.Span);
        var owner = this.Owner(syntax);
        if (parameter is not null && syntax is FunctionKoto { IsAnonymous: false } parent)
        {
            owner = owner.Length == 0 ? parent.Name : owner + "." + parent.Name;
        }

        var documentationOwner = parameter is not null ? this.Declaration(syntax) : this.VariableDocumentation(syntax);
        var spelling = this.TypeName(type);
        var declaration = new HoverDeclaration(
            "Variable",
            owner,
            symbol.Name + ": " + spelling,
            source is null ? [] : [this.Origin(syntax, source, nameSpan)],
            documentationOwner.Documentation,
            documentationOwner.DocumentationNotice,
            Parameter: parameter?.ExternalName,
            Details: symbol.Type is { } declared && !ReferenceEquals(type, declared) ? "Declared type: " + this.TypeName(declared) : null,
            Identity: this.SymbolIdentity(symbol));
        var core = type;
        string? referent = null;
        while (core.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication && core.Components.Count == 1)
        {
            this.budget.Charge();
            if (ReferenceEquals(core, type) && core.Components[0].Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication)
            {
                referent = this.TypeName(core.Components[0]);
            }

            core = core.Components[0];
        }

        if (core is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, Type: { } targetProjection } })
        {
            core = targetProjection;
        }

        var coreDeclaration = core.Symbol?.Declaration is StructKoto or EnumKoto or ContractKoto or GenericParameterKoto || core.Symbol?.Kind == BindingSymbolKind.AssociatedType
            ? this.Declaration(core.Symbol.Declaration) : this.Builtin(core);
        var target = (core.Symbol?.Declaration is ContractKoto ? "View target: " : core.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection ? "Type parameter: " : core.Kind == BoundTypeKind.AssociatedProjection ? "Associated type: " : "Core: ") + this.TypeName(core);
        var result = new HoverInfo(
            [coreDeclaration],
            CopyType: spelling,
            Copy: copy,
            TypeIdentity: new("variable", [this.SymbolIdentity(symbol), this.TypeIdentity(type)]),
            Variable: new(declaration, this.Semantics(type), referent, target));
        this.variables.Add((symbol, type, copy), result);
        return result;
    }

    private HoverDeclaration VariableDocumentation(Koto syntax)
    {
        var documentation = this.Documentation(syntax, out var failed);
        var source = syntax.CodeContext.SourceDocument;
        var notice = source is not null && query.Compilation.Diagnostics.HasSyntaxErrors(source) ? "Documentation deferred: syntax errors" : null;
        if (failed || (source is not null && query.Compilation.HasDocumentationFailure(source)))
        {
            notice = notice is null ? "Documentation unavailable: collection failure" : notice + "\nDocumentation unavailable: collection failure";
        }

        return new("Variable", string.Empty, string.Empty, [], documentation, notice);
    }

    private HoverInfo? VariableCall(InvocationKoto syntax, HoverInfo? call)
    {
        if (call is null || Name(syntax.Method) is not { } name || this.ReferenceSymbol(Effective(name)) is not { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter, Type: { } type } symbol ||
            this.Variable(symbol, query.TypeOf(name) ?? type, syntax) is not { } variable)
        {
            return call;
        }

        return call with
        {
            Variable = variable.Variable,
            Declarations = [.. variable.Declarations, .. call.Declarations],
            Copy = variable.Copy,
            CopyType = variable.CopyType,
            TypeIdentity = new("variable call", [variable.TypeIdentity!, call.TypeIdentity!]),
        };
    }

    private ConstraintProof Copy(BoundType type, Koto syntax)
    {
        var scope = query.ConstraintScope(syntax);
        if (!this.copies.TryGetValue((type, scope), out var copy))
        {
            var invalid = false;
            for (var current = scope; current is not null; current = current.Parent)
            {
                invalid |= current.Constraints is { Invalid: true };
            }

            copy = invalid ? ConstraintProof.Error : query.ProveCopy(type, syntax);
            this.copies.Add((type, scope), copy);
        }

        return copy;
    }

    private string Semantics(BoundType type)
    {
        if (type.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation || type.Semantics == SemanticsKind.Parameter ||
            type is { Kind: BoundTypeKind.Parameter, Symbol.Kind: BindingSymbolKind.SemanticsTarget })
        {
            var name = type.Symbol?.Pair?.Name ?? type.Symbol?.Name ?? type.Name;
            var constraints = type.Symbol?.Declaration is GenericParameterKoto parameter ? this.DeclarationDetails(parameter) : null;
            return name + " — Semantics parameter." + (constraints is null ? string.Empty : "\n" + constraints);
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection)
        {
            return "Undetermined for " + this.TypeName(type) + ". See the declared constraints.";
        }

        return HoverExplanations.Semantics(type.Semantics);
    }
}

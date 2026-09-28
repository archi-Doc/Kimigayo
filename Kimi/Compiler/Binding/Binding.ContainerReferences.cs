// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A container keeps its own slots first, followed by its parent's effective slots.
    // Slot symbols retain their original binder and ordinal; spelling is not identity.
    internal static int ContainerSlot(Koto binder, BindingSymbol parameter)
    {
        if (ReferenceEquals(parameter.Scope.Owner, binder))
        {
            return parameter.Slot;
        }

        if (binder is DeclarationContainerKoto && binder.BoundSymbol?.Schema is { } schema)
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                if (ReferenceEquals(schema.GenericSlots[i].Symbol, parameter) || ReferenceEquals(schema.GenericSlots[i].Semantics, parameter))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private BoundType? BindContainerReference(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context, ReadOnlySpan<BoundType> own)
    {
        var declaration = (DeclarationContainerKoto)symbol.Declaration;
        var parent = declaration.Parent as DeclarationContainerKoto;
        var inherited = parent?.BoundSymbol?.Schema;
        if (own.Length != declaration.GenericParameterNodes.Count)
        {
            return Fail(syntax, BindingFailure.TypeMismatch);
        }

        BoundType? environment = this.ImportedEnvironment(syntax);
        if (ReferenceEquals(environment?.Symbol, symbol) && own.IsEmpty)
        {
            return environment;
        }

        var path = UnwrapTypeSyntax(syntax);
        if (path is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            path = UnwrapTypeSyntax(root.Operands[0]);
        }

        if (path is GenericsKoto generic)
        {
            path = UnwrapTypeSyntax(generic.Identifier!);
        }

        if (path is MemberAccessKoto member)
        {
            var qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier?.Declaration is DeclarationContainerKoto container && !container.IsRoot)
            {
                environment = this.BindContainerQualifier(member.Left, qualifier, scope, context);
                if (environment is null)
                {
                    return null;
                }

                if (!ReferenceEquals(container, parent))
                {
                    var selected = this.LookupTypeMember(environment, symbol.Name, scope, typeRole: true);
                    environment = ReferenceEquals(selected.Member, symbol) ? selected.DeclaringType : null;
                }
            }
        }

        if (inherited is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 })
        {
            return this.InternType(own.IsEmpty ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, own);
        }

        if (environment is null)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current.Owner, parent))
                {
                    environment = this.SelfType(parent!.BoundSymbol!);
                    break;
                }
            }
        }

        if (environment is null || environment.Components.Count != inherited.GenericSlots.Count)
        {
            return Fail(syntax, BindingFailure.TypeMismatch);
        }

        var count = own.Length + environment.Components.Count;
        var types = this.RentTypes(count);
        var origins = this.originScratch.Rent(declaration.OriginNames.Count + inherited.Origins.Count);
        try
        {
            own.CopyTo(types);
            for (var i = 0; i < environment.Components.Count; i++)
            {
                types[own.Length + i] = environment.Components[i];
            }

            var originCount = environment.OriginArguments.Count == 0 ? 0 : declaration.OriginNames.Count + inherited.Origins.Count;
            Array.Clear(origins, 0, originCount);
            for (var i = 0; i < environment.OriginArguments.Count; i++)
            {
                origins[declaration.OriginNames.Count + i] = environment.OriginArguments[i];
            }

            return this.InternType(count == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, types.AsSpan(0, count), originArguments: origins.AsSpan(0, originCount));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundType? BindContainerQualifier(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return Complete(syntax, this.BindContainerQualifier(grouped.Type, symbol, scope, context));
        }

        if (symbol.Declaration is GroupKoto)
        {
            var result = this.BindContainerReference(syntax, symbol, scope, context, []);
            return result is null ? null : Complete(syntax, this.CompleteOrigins(result, syntax as TypeSemanticsKoto, syntax, scope, context with { SuppressOuter = true }));
        }

        return this.BindType(syntax, scope, context with { SuppressOuter = true });
    }

    private BoundType? ImportedEnvironment(Koto syntax)
    {
        for (; ;)
        {
            if (this.importedContainerEnvironments.TryGetValue(syntax, out var environment))
            {
                return environment;
            }

            var next = syntax is GenericsKoto generic ? generic.Identifier! : UnwrapTypeSyntax(syntax);
            if (ReferenceEquals(next, syntax))
            {
                return null;
            }

            syntax = next;
        }
    }
}

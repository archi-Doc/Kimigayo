// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private sealed partial class HoverBuilder
    {
        private HoverKey DeclarationIdentity(Koto declaration)
        {
            var parts = new List<HoverKey> { this.BinderIdentity(declaration), this.TypeIdentity(declaration.BoundSymbol?.Type) };
            if (declaration is FunctionKoto function)
            {
                foreach (var parameter in function.Parameters)
                {
                    parts.Add(this.TypeIdentity(parameter.Type.BoundType));
                }

                foreach (var constraint in function.TypeConstraints)
                {
                    parts.Add(this.ConstraintIdentity((constraint as IsKoto)?.BoundConstraint));
                }
            }
            else if (declaration is DeclarationContainerKoto container)
            {
                foreach (var parent in container.Bases)
                {
                    parts.Add(this.TypeIdentity(parent.BoundType));
                    parts.Add(this.SymbolIdentity(parent.BoundSymbol));
                }

                foreach (var constraint in container.ConstraintNodes)
                {
                    parts.Add(this.ConstraintIdentity(constraint.BoundConstraint));
                }
            }
            else if (declaration is PropertyKoto { BoundSymbol.Property: { } property })
            {
                Accessor(property.Getter);
                Accessor(property.Setter);
            }

            return new("declaration", parts.ToArray());

            void Accessor(BoundAccessor accessor)
            {
                parts.Add(new(
                    $"accessor;{accessor.Kind};{accessor.IsPresent};{accessor.IsStandard};{accessor.Access}",
                    [this.TypeIdentity(accessor.Receiver), this.TypeIdentity(accessor.Input), this.TypeIdentity(accessor.Result)]));
            }
        }

        private string? DeclarationDetails(Koto declaration)
        {
            if (declaration is PropertyKoto property && property.BoundSymbol?.Property is { IsVerified: true } contract)
            {
                var text = new StringBuilder(property.TypeKoto is null ? "Inferred type: " : "Type: ").Append(this.TypeName(contract.Type!));
                Accessor(contract.Getter);
                Accessor(contract.Setter);
                return text.ToString();

                void Accessor(BoundAccessor accessor)
                {
                    if (!accessor.IsPresent)
                    {
                        return;
                    }

                    text.AppendLine().Append(accessor.Kind == PropertyAccessorKind.Get ? "get: " : "set: ");
                    text.Append((accessor.Access & (ModifierKind)7) switch
                    {
                        ModifierKind.Public => "public",
                        ModifierKind.Protected => "protected",
                        ModifierKind.Private => "private",
                        ModifierKind.Internal => "internal",
                        ModifierKind.ProtectedOrInternal => "protected or internal",
                        ModifierKind.ProtectedAndInternal => "protected and internal",
                        _ => "default access",
                    });
                    if (accessor.IsStandard)
                    {
                        text.Append("; standard place access");
                    }
                    else
                    {
                        if (accessor.Receiver is { } receiver)
                        {
                            text.Append("; receiver: ").Append(this.TypeName(receiver));
                        }

                        if (accessor.Input is { } input)
                        {
                            text.Append("; input: ").Append(this.TypeName(input));
                        }

                        if (accessor.Result is { } result)
                        {
                            text.Append("; result: ").Append(this.TypeName(result));
                        }
                    }
                }
            }

            if (declaration is FunctionKoto { ReturnType: null, IsConstructor: false, IsDestructor: false, BoundSymbol.Type: { } type })
            {
                return "Implicit result: " + this.TypeName(type);
            }

            if (declaration is GenericParameterKoto { Parent: { } parent })
            {
                var clauses = parent is FunctionKoto function ? function.TypeConstraints : parent is DeclarationContainerKoto container ? container.ConstraintNodes : [];
                if (clauses.Count != 0)
                {
                    var text = new StringBuilder("Constraints in declaration:");
                    foreach (var clause in clauses)
                    {
                        text.AppendLine().Append(clause);
                    }

                    return text.ToString();
                }
            }

            return null;
        }
    }
}

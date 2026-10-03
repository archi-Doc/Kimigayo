// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Diagnostic publication needs structural Types, not BoundType.Name (which is only the declaration's short name).
    // Kimi position/range identities are qualified even when a user declaration hides their normal alias.
    private static string DiagnosticTypeName(object value)
    {
        if (value is not BoundType type)
        {
            return (string)value;
        }

        if (type.Kind is BoundTypeKind.Primitive or BoundTypeKind.Parameter)
        {
            return type.Name;
        }

        var text = new StringBuilder();
        Append(type);
        return text.ToString();

        void Append(BoundType current)
        {
            if (current.Kind == BoundTypeKind.Semantics && current.Components.Count == 1)
            {
                text.Append(current.Semantics.ToText()).Append('/');
                Append(current.Components[0]);
                return;
            }

            if (current.Kind == BoundTypeKind.AssociatedProjection && current.Components.Count != 0)
            {
                // SPEC 8.4.3: a projection reads as written, T.Item or Self.LentItem(step).
                Append(current.Components[0]);
                text.Append('.').Append(current.Name);
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    text.Append(i == 0 ? '(' : ',');
                    if (i != 0)
                    {
                        text.Append(' ');
                    }

                    text.Append(current.OriginArguments[i].Name);
                }

                if (current.OriginArguments.Count != 0)
                {
                    text.Append(')');
                }

                return;
            }

            if (current.Kind == BoundTypeKind.Closure)
            {
                // SPEC 7.6: a closure Type has no written name; its components are the captured environment, so it is shown by
                // the signature of its anonymous function.
                text.Append("closure ");
                if ((current.Symbol?.Declaration as FunctionKoto)?.BoundClosure?.Signature is { } signature)
                {
                    Append(signature);
                }

                return;
            }

            if (current.Kind == BoundTypeKind.Function && current.Components.Count == 2)
            {
                Append(current.Components[0]);
                text.Append(" -> ");
                Append(current.Components[1]);
                return;
            }

            if (current.Kind == BoundTypeKind.FixedArray)
            {
                text.Append('[');
                if (current.LengthExpression is { } length)
                {
                    AppendLength(length);
                }
                else
                {
                    text.Append(current.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                text.Append(" of ");
                Append(current.Components[0]);
                text.Append(']');
                return;
            }

            if (current.Kind != BoundTypeKind.Tuple)
            {
                if (current.Symbol?.LibraryDeclaration is KimiDeclarationId.FromEnd or KimiDeclarationId.Start or KimiDeclarationId.End or
                    KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange)
                {
                    text.Append("Kimi.");
                }

                text.Append(current.Name);
            }

            if (current.Components.Count != 0 || current.Kind == BoundTypeKind.Tuple)
            {
                text.Append(current.Kind == BoundTypeKind.Tuple ? '(' : '<');
                for (var i = 0; i < current.Components.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(", ");
                    }

                    Append(current.Components[i]);
                }

                text.Append(current.Kind == BoundTypeKind.Tuple ? ')' : '>');
            }
        }

        void AppendLength(BoundLength length)
        {
            if (length.Parameter is { } parameter)
            {
                text.Append(parameter.Name);
            }
            else if (length.IsConstant)
            {
                text.Append(length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append('(');
                if (length.Operation is KotoKind.PrefixMinus or KotoKind.PrefixPlus)
                {
                    text.Append(length.Operation == KotoKind.PrefixMinus ? '-' : '+');
                    AppendLength(length.Left!);
                }
                else
                {
                    AppendLength(length.Left!);
                    text.Append(length.Operation switch { KotoKind.Plus => " + ", KotoKind.Minus => " - ", KotoKind.Asterisk => " * ", KotoKind.Slash => " / ", _ => " % " });
                    AppendLength(length.Right!);
                }

                text.Append(')');
            }
        }
    }
}

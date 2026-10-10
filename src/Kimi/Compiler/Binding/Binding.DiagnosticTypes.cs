// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Diagnostic publication needs structural Types, not BoundType.Name (which is only the declaration's short name).
    // Kimi position/range identities are qualified even when a user declaration hides their normal alias.
    internal static string DiagnosticTypeName(object value) => DiagnosticTypeName(value, null, null);

    // SPEC 23.3.6.5: `shown`, when given, is the one Origin the display writes, at the first borrow layer that has it, as `during
    // shownText`; a layer below another borrow layer is parenthesized, as in `uniq/(ref/i32 during static)`.
    internal static string DiagnosticTypeName(object value, BoundOrigin? shown, string? shownText)
        => DisplayTypeName(value, shown, shownText, complete: false);

    // Hover identifies the complete contextual Type, including every Origin; ordinary diagnostic spelling is unchanged.
    internal static string HoverTypeName(BoundType type) => DisplayTypeName(type, null, null, complete: true);

    internal static string DiagnosticLengthName(BoundLength length, HoverBudget? budget = null)
    {
        var text = new StringBuilder();
        AppendDiagnosticLength(text, length, budget);
        return text.ToString();
    }

    private static string DisplayTypeName(object value, BoundOrigin? shown, string? shownText, bool complete)
    {
        if (value is not BoundType type)
        {
            return (string)value;
        }

        // SPEC 23.3.6.5: a pair target is shown as its pair, `s/T`, which an ordinary diagnostic writes without its outer Origin.
        if (type.Kind is BoundTypeKind.Primitive or BoundTypeKind.Parameter && (!complete || (!type.CarriesOrigin && type.Symbol?.Kind != BindingSymbolKind.SemanticsTarget)))
        {
            return type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget, Pair: { } pair } && type.Kind == BoundTypeKind.Parameter ? pair.Name + "/" + type.Name : type.Name;
        }

        var budget = complete ? new HoverBudget() : null;
        var text = new StringBuilder();
        Append(type, false);
        return text.ToString();

        void Append(BoundType current, bool underBorrow = false)
        {
            using var guard = budget?.Enter();
            budget?.Charge(current.Name.Length);
            if (current is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, Pair: { } semantics } })
            {
                text.Append(semantics.Name).Append('/');
            }

            if (current.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication && current.Components.Count == 1)
            {
                var mark = complete ? current.Origin is not null : shown is not null && ReferenceEquals(current.Origin, shown);
                if (mark)
                {
                    shown = null;
                    if (underBorrow)
                    {
                        text.Append('(');
                    }
                }

                text.Append(current.Kind == BoundTypeKind.Semantics ? current.Semantics.ToText() : current.Symbol?.Pair?.Name ?? current.Symbol?.Name ?? current.Name).Append('/');
                var group = complete && current.Components[0].Kind == BoundTypeKind.Function;
                if (group)
                {
                    text.Append('(');
                }

                Append(current.Components[0], true);
                if (group)
                {
                    text.Append(')');
                }

                if (mark)
                {
                    text.Append(" during ").Append(complete ? HoverOriginName(current.Origin!, budget!) : shownText);
                    if (underBorrow)
                    {
                        text.Append(')');
                    }
                }

                return;
            }

            if (current.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                text.Append("case ").Append(current.Symbol?.Pair?.Name ?? current.Symbol?.Name).Append(" { ");
                var index = 0;
                foreach (var mode in SemanticsOrder)
                {
                    if (((SemanticsMask)current.Length).Contains(mode))
                    {
                        if (index != 0)
                        {
                            text.Append("; ");
                        }

                        text.Append(mode.ToText()).Append(" => ");
                        Append(current.Components[index++]);
                    }
                }

                text.Append(" }");
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

                    text.Append(complete ? HoverOriginName(current.OriginArguments[i], budget!) : current.OriginArguments[i].Name);
                }

                if (current.OriginArguments.Count != 0)
                {
                    text.Append(')');
                }

                return;
            }

            if (current.Kind == BoundTypeKind.FunctionItem)
            {
                text.Append("function item ");
                AppendDeclaration(current.Symbol!);
                var slots = current.LengthArguments.Length == 0 ? current.Components.Count : current.LengthArguments.Length;
                var component = 0;
                for (var i = 0; i < slots; i++)
                {
                    text.Append(i == 0 ? "<" : ", ");
                    if (i < current.LengthArguments.Length && current.LengthArguments[i] is { } length)
                    {
                        text.Append(DiagnosticLengthName(length, budget));
                    }
                    else
                    {
                        Append(current.Components[component++]);
                    }
                }

                while (component < current.Components.Count)
                {
                    text.Append(slots++ == 0 ? "<" : ", ");
                    Append(current.Components[component++]);
                }

                if (slots != 0)
                {
                    text.Append('>');
                }

                return;
            }

            if (current.Kind == BoundTypeKind.Closure)
            {
                // SPEC 7.6: a closure Type has no written name; its components are the captured environment, so it is shown by
                // the signature of its anonymous function, also when the closure node itself failed, as at a call argument.
                text.Append("closure ");
                if ((current.Symbol?.Declaration as FunctionKoto)?.ClosureStorage?.Signature is { } signature)
                {
                    Append(signature);
                }

                return;
            }

            if (current.Kind == BoundTypeKind.Function && current.Components.Count == 2)
            {
                Append(current.Components[0]);
                text.Append(current.ResultMode == FunctionResultMode.Value ? " -> " : " -> place ");
                Append(current.Components[1]);
                return;
            }

            if (current.Kind == BoundTypeKind.FixedArray)
            {
                text.Append('[');
                if (current.LengthExpression is { } length)
                {
                    AppendDiagnosticLength(text, length, budget);
                }
                else
                {
                    text.Append(current.Length < 0 ? "_" : current.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                text.Append(" of ");
                Append(current.Components[0]);
                text.Append(']');
                return;
            }

            if (current.Kind != BoundTypeKind.Tuple)
            {
                if (complete && current.Symbol is { Kind: BindingSymbolKind.Type } nominal)
                {
                    AppendDeclaration(nominal);
                }
                else
                {
                    if (current.Symbol?.LibraryDeclaration is KimiDeclarationId.FromEnd or KimiDeclarationId.Start or KimiDeclarationId.End or
                        KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange)
                    {
                        text.Append("Kimi.");
                    }

                    text.Append(current.Name);
                }
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

                if (complete && current.Kind == BoundTypeKind.Tuple && current.Components.Count == 1)
                {
                    text.Append(',');
                }

                text.Append(current.Kind == BoundTypeKind.Tuple ? ')' : '>');
            }

            if (complete && current.OriginArguments.Count != 0)
            {
                text.Append("{");
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(", ");
                    }

                    text.Append(current.Symbol?.Schema?.Origins[i].Name ?? "origin").Append(" = ").Append(HoverOriginName(current.OriginArguments[i], budget!));
                }

                text.Append('}');
            }

            if (complete && current.Origin is { } ownOrigin)
            {
                text.Append(" during ").Append(HoverOriginName(ownOrigin, budget!));
            }
        }

        void AppendDeclaration(BindingSymbol symbol)
        {
            using var guard = budget?.Enter();
            budget?.Charge(symbol.Name.Length);
            // Equal call signatures and short names do not identify equal Item Types. Keep the declaring containers.
            if (symbol.Scope.Owner.BoundSymbol is { Name.Length: > 0 } owner && !ReferenceEquals(owner, symbol) &&
                owner.Kind is BindingSymbolKind.Container or BindingSymbolKind.Type or BindingSymbolKind.Function)
            {
                AppendDeclaration(owner);
                text.Append('.');
            }

            text.Append(symbol.Name);
        }
    }

    private static string HoverOriginName(BoundOrigin origin, HoverBudget budget)
    {
        using var guard = budget.Enter();
        budget.Charge(origin.Name.Length);
        if (origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input)
        {
            var display = OriginDisplay(origin, null, typeLevel: true);
            return display.Kind == "expression" ? display.Text : "<" + display.Kind + " origin: " + display.Text + ">";
        }

        if (origin.Kind == OriginKind.Intersection)
        {
            var text = new StringBuilder("(");
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(" and ");
                }

                text.Append(HoverOriginName(origin.Operands[i], budget));
            }

            return text.Append(')').ToString();
        }

        var at = origin.Occurrence ?? origin.Binder;
        var position = at?.CodeContext.SourceDocument?.GetPosition(at.Span.Start);
        return position is { } location
            ? $"<{origin.Kind.ToString().ToLowerInvariant()} origin at {location.Line + 1}:{location.Character + 1}>"
            : $"<{origin.Kind.ToString().ToLowerInvariant()} origin>";
    }

    private static void AppendDiagnosticLength(StringBuilder text, BoundLength length, HoverBudget? budget = null)
    {
        using var guard = budget?.Enter();
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
                AppendDiagnosticLength(text, length.Left!, budget);
            }
            else
            {
                AppendDiagnosticLength(text, length.Left!, budget);
                text.Append(length.Operation switch { KotoKind.Plus => " + ", KotoKind.Minus => " - ", KotoKind.Asterisk => " * ", KotoKind.Slash => " / ", _ => " % " });
                AppendDiagnosticLength(text, length.Right!, budget);
            }

            text.Append(')');
        }
    }
}

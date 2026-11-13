// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Diagnostic publication needs structural Types, not BoundType.Name (which is only the declaration's short name).
    // Kimi position/range identities are qualified even when a user declaration hides their normal alias.
    internal static string DiagnosticTypeName(object value) => DiagnosticTypeName(value, null, null);

    // SPEC 23.1: `shown`, when given, is the one Origin the display writes, at the first borrow layer that has it, as `during
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

    // The adapter's text of a mismatch side's form (SPEC 23.3).
    private static string FormText(MismatchForm form, int count) => (form, count) switch
    {
        (MismatchForm.AnonymousFunction, < 0) => "an anonymous function",
        (MismatchForm.AnonymousFunction, 1) => "an anonymous function with 1 parameter",
        (MismatchForm.AnonymousFunction, _) => $"an anonymous function with {count} parameters",
        (MismatchForm.IntegerLiteral, _) => "integer literal",
        (MismatchForm.FloatingPointLiteral, _) => "floating-point literal",
        (MismatchForm.PositionType, _) => "a Type proven Position or PositionRange",
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    // The display does not recurse on the Type (DECISIONS BU-153): Show notes a form's parts in reading order (Emit), and
    // the loop shows them from one list used as a stack, so neither a Type's height nor a chain of closure signatures, which the
    // height does not count (BU-71), reaches the host stack. A Primitive or Parameter returns before the list exists: a Bind, and a
    // check without Hover whose records name no structural Type, allocate nothing more; each structural Type displayed adds one list.
    private static string DisplayTypeName(object value, BoundOrigin? shown, string? shownText, bool complete)
    {
        if (value is not BoundType type)
        {
            return (string)value;
        }

        // SPEC 23.1: a pair target is shown as its pair, `s/T`, which an ordinary diagnostic writes without its outer Origin.
        if (type.Kind is BoundTypeKind.Primitive or BoundTypeKind.Parameter && (!complete || (!type.CarriesOrigin && type.Symbol?.Kind != BindingSymbolKind.SemanticsTarget)))
        {
            return type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget, Pair: { } pair } && type.Kind == BoundTypeKind.Parameter ? pair.Name + "/" + type.Name : type.Name;
        }

        var budget = complete ? new HoverBudget() : null;
        var text = new StringBuilder();
        List<DisplayPart> parts = [new(type, 0)];
        var form = 0;
        var depth = 0;
        while (parts.Count != 0)
        {
            var part = parts[^1];
            form = parts.Count - 1;
            parts.RemoveAt(form);
            depth = part.Depth + 1;

            // Hover's depth (HoverLimits.Depth) is the part's nesting, as a recursive walk counts it.
            budget?.At(part.Depth);
            switch (part.Value)
            {
                case BoundType inner:
                    Show(inner, part.UnderBorrow);
                    break;
                case string piece:
                    text.Append(piece);
                    break;
                case BoundOrigin origin:
                    text.Append(HoverOriginName(origin, budget!));
                    break;
                case BoundLength length:
                    AppendDiagnosticLength(text, length, budget);
                    break;
                case BindingSymbol declaration:
                    AppendDeclaration(declaration);
                    break;
                default:
                    throw new UnreachableException();
            }

            parts.Reverse(form, parts.Count - form);
        }

        return text.ToString();

        // Notes the next parts of the form Show is reading: a text is written at once while nothing of its form waits before it; a
        // Type, an Origin (its Hover name), a length or a declaration is shown after the parts noted before it; null adds nothing.
        void Emit(params ReadOnlySpan<object?> items)
        {
            foreach (var item in items)
            {
                if (item is string piece && parts.Count == form)
                {
                    text.Append(piece);
                }
                else if (item is not null)
                {
                    parts.Add(new(item, depth));
                }
            }
        }

        void Show(BoundType current, bool underBorrow)
        {
            using var guard = budget?.Enter();
            budget?.Charge(current.Name.Length);
            if (current is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, Pair: { } semantics } })
            {
                Emit(semantics.Name, "/");
            }

            if (current.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication && current.Components.Count == 1)
            {
                // SPEC 5.7: a raw layer shows the region its Origin names.
                var layer = current.Origin ?? current.RawOrigin;
                var mark = complete ? layer is not null : shown is not null && ReferenceEquals(layer, shown);
                if (mark)
                {
                    shown = null;
                    if (underBorrow)
                    {
                        Emit("(");
                    }
                }

                Emit(
                    current.Kind == BoundTypeKind.Semantics ? current.Semantics.ToText() : current.Symbol?.Pair?.Name ?? current.Symbol?.Name ?? current.Name,
                    "/");
                var group = complete && current.Components[0].Kind == BoundTypeKind.Function;
                if (group)
                {
                    Emit("(");
                }

                parts.Add(new(current.Components[0], depth, UnderBorrow: true));
                if (group)
                {
                    Emit(")");
                }

                if (mark)
                {
                    Emit(" during ", complete ? layer : shownText);
                    if (underBorrow)
                    {
                        Emit(")");
                    }
                }

                return;
            }

            if (current.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                Emit("case ", current.Symbol?.Pair?.Name ?? current.Symbol?.Name, " { ");
                var index = 0;
                foreach (var mode in SemanticsOrder)
                {
                    if (((SemanticsMask)current.Length).Contains(mode))
                    {
                        if (index != 0)
                        {
                            Emit("; ");
                        }

                        Emit(mode.ToText(), " => ", current.Components[index++]);
                    }
                }

                Emit(" }");
                return;
            }

            if (current.Kind == BoundTypeKind.AssociatedProjection && current.Components.Count != 0)
            {
                // SPEC 8.5: a projection reads as written, T.Item or Self.LentItem(step).
                Emit(current.Components[0], ".", current.Name);
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    Emit(i == 0 ? "(" : ", ", complete ? current.OriginArguments[i] : current.OriginArguments[i].Name);
                }

                if (current.OriginArguments.Count != 0)
                {
                    Emit(")");
                }

                return;
            }

            if (current.Kind == BoundTypeKind.FunctionItem)
            {
                Emit("function item ", current.Symbol!);
                var slots = current.LengthArguments.Length == 0 ? current.Components.Count : current.LengthArguments.Length;
                var component = 0;
                for (var i = 0; i < slots; i++)
                {
                    Emit(i == 0 ? "<" : ", ");
                    if (i < current.LengthArguments.Length && current.LengthArguments[i] is { } length)
                    {
                        Emit(length);
                    }
                    else
                    {
                        Emit(current.Components[component++]);
                    }
                }

                while (component < current.Components.Count)
                {
                    Emit(slots++ == 0 ? "<" : ", ", current.Components[component++]);
                }

                if (slots != 0)
                {
                    Emit(">");
                }

                return;
            }

            if (current.Kind == BoundTypeKind.Closure)
            {
                // SPEC 7.7: a closure Type has no written name; its components are the captured environment, so it is shown by
                // the signature of its anonymous function, also when the closure node itself failed, as at a call argument.
                Emit("closure ", BoundClosure.Of(current)?.Signature);
                return;
            }

            if (current.Kind == BoundTypeKind.Function && current.Components.Count == 2)
            {
                Emit(current.Components[0], " -> ", current.Components[1]);
                return;
            }

            if (current.Kind == BoundTypeKind.FixedArray)
            {
                Emit("[");
                if (current.LengthExpression is { } length)
                {
                    Emit(length);
                }
                else
                {
                    Emit(current.Length < 0 ? "_" : current.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                Emit(" of ", current.Components[0], "]");
                return;
            }

            if (current.Kind != BoundTypeKind.Tuple)
            {
                if (complete && current.Symbol is { Kind: BindingSymbolKind.Type } nominal)
                {
                    Emit(nominal);
                }
                else
                {
                    if (current.Symbol?.LibraryDeclaration is KimiDeclarationId.FromEnd or KimiDeclarationId.Start or KimiDeclarationId.End or
                        KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange)
                    {
                        Emit("Kimi.");
                    }

                    Emit(current.Name);
                }
            }

            if (current.Components.Count != 0 || current.Kind == BoundTypeKind.Tuple)
            {
                Emit(current.Kind == BoundTypeKind.Tuple ? "(" : "<");
                for (var i = 0; i < current.Components.Count; i++)
                {
                    Emit(i == 0 ? null : ", ", current.Components[i]);
                }

                if (complete && current.Kind == BoundTypeKind.Tuple && current.Components.Count == 1)
                {
                    Emit(",");
                }

                Emit(current.Kind == BoundTypeKind.Tuple ? ")" : ">");
            }

            if (complete && current.OriginArguments.Count != 0)
            {
                Emit("{");
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    Emit(i == 0 ? null : ", ", current.Symbol?.Schema?.Origins[i].Name ?? "origin", " = ", current.OriginArguments[i]);
                }

                Emit("}");
            }

            if (complete && current.Origin is { } ownOrigin)
            {
                Emit(" during ", ownOrigin);
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
        if (origin is { Kind: OriginKind.Holder } or { Kind: OriginKind.Projection, Slot: ReceiverSlot })
        {
            return "self"; // SPEC 5.7: the holding value, and a constructor's or destructor's receiver.
        }

        if (origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input or OriginKind.ConstraintSet)
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

    // A part of a Type's display still to show: a text, a Type, an Origin (its Hover name), a length or a declaration, at its
    // Hover depth; UnderBorrow marks a borrow layer's referent.
    private readonly record struct DisplayPart(object Value, int Depth, bool UnderBorrow = false);

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
        else if (length.Values is { } values)
        {
            // An interpolation term has no source form (R8-W2): the MIR dump writes its literal bytes, its kind and its value Types.
            text.Append('(').Append(length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(length.Operation == LengthOperation.InterpolationBound ? " + bound" : " + hint").Append(DiagnosticTypeName(values)).Append(')');
        }
        else
        {
            text.Append('(');
            if (length.Operation == LengthOperation.Neg)
            {
                text.Append('-');
                AppendDiagnosticLength(text, length.Left!, budget);
            }
            else
            {
                AppendDiagnosticLength(text, length.Left!, budget);
                text.Append(length.Operation switch { LengthOperation.Add => " + ", LengthOperation.Sub => " - ", LengthOperation.Mul => " * ", LengthOperation.Div => " / ", _ => " % " });
                AppendDiagnosticLength(text, length.Right!, budget);
            }

            text.Append(')');
        }
    }
}

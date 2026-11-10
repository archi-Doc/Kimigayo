// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591

/// <summary>The operation of a <see cref="BoundLength"/>, Binding's closed vocabulary (SPEC 4.2): a constant, a length parameter, and
/// the unary minus and binary operators of a symbolic length, which Binding maps once from the syntax it binds (LengthOperationOf; a unary
/// plus and grouping form no operation); and the interpolation-bound term, which no syntax forms (TyCtxt.InterpolationBound). Its order is
/// <c>CompareLength</c>'s, which orders a commutative operation's operands.</summary>
public enum LengthOperation : byte
{
    Constant,
    Parameter,
    Neg,
    Mul,
    Div,
    Rem,
    Add,
    Sub,
    InterpolationBound,
}

/// <summary>Interned checked length expression. Symbolic arithmetic is validated at instantiation.</summary>
/// <remarks>An interpolation-bound term (R8-W2; SPEC 22.10.5, rule L) is the stack region of a direct interpolation in a generic body:
/// <see cref="Value"/> its literal bytes and <see cref="Values"/> the Tuple of its value Types, erased, at least one undecided
/// (FormattingTypes.StackBound). A substitution folds it again (Binding.SubstituteLength).</remarks>
public sealed class BoundLength(LengthOperation operation, long value, BindingSymbol? parameter, BoundLength? left, BoundLength? right, BoundType? values = null)
{
    public LengthOperation Operation { get; } = operation;

    public long Value { get; } = value;

    public BindingSymbol? Parameter { get; } = parameter;

    public BoundLength? Left { get; } = left;

    public BoundLength? Right { get; } = right;

    /// <summary>Gets an interpolation-bound term's value Types (a Tuple); null for every other length.</summary>
    public BoundType? Values { get; } = values;

    public bool IsConstant => this.Parameter is null && this.Left is null && this.Right is null && this.Values is null;

    /// <summary>Gets a value indicating whether it is a term whose value Types have an abstract part, an abstract part of the fixed array it
    /// is the length of (BoundType.HasAbstractPart, ContainsParameter): only that array's instance folds it.</summary>
    internal bool HasAbstractPart => this.Values is { HasAbstractPart: true };
}

public sealed partial class Binding
{
    private readonly Dictionary<(LengthOperation Operation, long Value, BindingSymbol? Parameter, BoundLength? Left, BoundLength? Right, BoundType? Values), BoundLength> lengths = new();
    private readonly List<BindingSymbol> activeLengthConstants = new();
    private readonly ScratchBuffers<BoundLength?> lengthScratch = new();

    // A length under a binder's substitution, its Type arguments `types` and length arguments `arguments`: a length slot's argument, an
    // interpolation-bound term over its substituted value Types, folded again (TyCtxt.InterpolationBound), arithmetic evaluated once its
    // operands are constant; null where a slot is unbound or the arithmetic fails (SPEC 4.2, 4.4.2).
    internal BoundLength? SubstituteLength(BoundLength expression, Koto binder, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> arguments)
        => this.SubstituteLength(expression, new CallSubstitution(binder, types, arguments) { SlotOwner = binder });

    // A length in the instance a substitution makes, as StorageInstance makes a Type (TyCtxt.Compose's lengths): an interpolation-bound term
    // over its value Types' instance (declaring Type, slots, Self and normalization), folded again; any other length by its slots.
    internal BoundLength? InstanceLength(BoundLength length, Subst subst)
        => length.Values is not { } values ? this.SubstituteLength(length, subst.Binder, subst.Types, subst.Lengths)
            : this.StorageInstance(values, subst) is { } image ? this.types.InterpolationBound(image, length.Value) : null;

    // The one interning of a length: a commutative operation's operands in CompareLength's order, then one instance per key.
    internal BoundLength InternLength(LengthOperation operation, long value = 0, BindingSymbol? parameter = null, BoundLength? left = null, BoundLength? right = null, BoundType? values = null)
    {
        if (operation is LengthOperation.Add or LengthOperation.Mul && CompareLength(left, right) > 0)
        {
            (left, right) = (right, left);
        }

        var key = (operation, value, parameter, left, right, values);
        if (!this.lengths.TryGetValue(key, out var result))
        {
            this.lengths.Add(key, result = new(operation, value, parameter, left, right, values));
        }

        return result;
    }

    // SPEC 4.2: the operation of a length's binary operator, mapped once where a length is bound.
    private static LengthOperation LengthOperationOf(KotoKind operation) => operation switch
    {
        KotoKind.Plus => LengthOperation.Add,
        KotoKind.Minus => LengthOperation.Sub,
        KotoKind.Asterisk => LengthOperation.Mul,
        KotoKind.Slash => LengthOperation.Div,
        KotoKind.Percent => LengthOperation.Rem,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // SPEC 4.2: a BoundLength is a long, the profile's isize, so every nonnegative value is a length.
    private static bool ValidLength(long value) => value >= 0;

    private static bool IsSignatureLength(Koto node, FunctionKoto function)
    {
        for (Koto? current = node; current is not null && !ReferenceEquals(current, function); current = current.Parent)
        {
            if (ReferenceEquals(current, function.ReturnType))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(current, function.Parameters[i].Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SameLengthSignature(BoundLength? a, BoundLength? b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Operation != b.Operation || a.Value != b.Value || !ReferenceEquals(a.Values, b.Values) || !SignatureSlotEquals(a.Parameter, b.Parameter, aBinder, bBinder))
        {
            return false;
        }

        return SameLengthSignature(a.Left, b.Left, aBinder, bBinder) && SameLengthSignature(a.Right, b.Right, aBinder, bBinder);
    }

    private static bool TryLengthArithmetic(LengthOperation operation, BoundType type, UInt128 left, UInt128 right, out UInt128 value)
    {
        value = 0;
        var (signed, width) = (type.IsSigned, type.Width);
        var minimum = unchecked(-(Int128)((UInt128)1 << (width - 1)));
        if (operation is LengthOperation.Div or LengthOperation.Rem && right == 0)
        {
            return false;
        }

        if (signed && (Int128)right == -1)
        {
            // SPEC 13.3.1: a % -1 is 0 for every a, and only the minimum / -1 quotient is unrepresentable.
            if (operation == LengthOperation.Rem)
            {
                return true;
            }

            if (operation == LengthOperation.Div && (Int128)left == minimum)
            {
                return false;
            }
        }

        try
        {
            if (signed)
            {
                var a = (Int128)left;
                var b = (Int128)right;
                var result = operation switch
                {
                    LengthOperation.Add => checked(a + b),
                    LengthOperation.Sub => checked(a - b),
                    LengthOperation.Mul => checked(a * b),
                    LengthOperation.Div => a / b,
                    _ => a % b,
                };
                if (result < minimum || result > ~minimum)
                {
                    return false;
                }

                value = unchecked((UInt128)result);
            }
            else
            {
                value = operation switch
                {
                    LengthOperation.Add => checked(left + right),
                    LengthOperation.Sub => checked(left - right),
                    LengthOperation.Mul => checked(left * right),
                    LengthOperation.Div => left / right,
                    _ => left % right,
                };
                if (width < 128 && value >= ((UInt128)1 << width))
                {
                    return false;
                }
            }

            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int CompareLength(BoundLength? left, BoundLength? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null || right is null)
        {
            return left is null ? -1 : 1;
        }

        var result = left.Operation.CompareTo(right.Operation);
        if (result == 0)
        {
            result = left.Value.CompareTo(right.Value);
        }

        if (result == 0)
        {
            result = (left.Parameter?.Slot ?? -1).CompareTo(right.Parameter?.Slot ?? -1);
        }

        if (result == 0)
        {
            result = (left.Parameter?.Declaration.Span.Start ?? -1).CompareTo(right.Parameter?.Declaration.Span.Start ?? -1);
        }

        if (result == 0)
        {
            result = CompareValues(left.Values, right.Values);
        }

        if (result == 0)
        {
            result = CompareLength(left.Left, right.Left);
        }

        return result == 0 ? CompareLength(left.Right, right.Right) : result;
    }

    // A term's value Types by a key that never reads a reference (CompareLength): their count, then each one's kind and name. Not total: two
    // Types of one kind and name (two binders' T) tie and keep their order, so two such sums intern apart; unreached, since no producer
    // forms arithmetic over a term (R8-W2 a2 uses one only as a whole length).
    private static int CompareValues(BoundType? left, BoundType? right)
    {
        var result = (left?.Components.Count ?? -1).CompareTo(right?.Components.Count ?? -1);
        for (var i = 0; result == 0 && i < (left?.Components.Count ?? 0); i++)
        {
            var (a, b) = (left!.Components[i], right!.Components[i]);
            result = a.Kind != b.Kind ? ((int)a.Kind).CompareTo((int)b.Kind) : string.CompareOrdinal(a.Name, b.Name);
        }

        return result;
    }

    // A length under a substitution (Binding.CallSubstitution): its slot owner's length arguments, and its value Types under the
    // substitution's Type and length columns and its declaring occurrence.
    private BoundLength? SubstituteLength(BoundLength expression, in CallSubstitution substitution)
    {
        if (expression.Parameter is { } parameter)
        {
            return ReferenceEquals(parameter.Scope.Owner, substitution.SlotOwner) ? substitution.Lengths[parameter.Slot] : expression;
        }

        if (expression.IsConstant)
        {
            return expression;
        }

        if (expression.Values is { } values)
        {
            var slots = new CallSubstitution(substitution.SlotOwner, substitution.Types, substitution.Lengths) { SlotOwner = substitution.SlotOwner, Declaring = substitution.Declaring };
            return this.Substitute(values, slots) is { } image ? this.types.InterpolationBound(image, expression.Value) : null;
        }

        var left = this.SubstituteLength(expression.Left!, substitution);
        var right = expression.Right is { } operand ? this.SubstituteLength(operand, substitution) : null;
        if (left is null || (expression.Right is not null && right is null))
        {
            return null;
        }

        if (!left.IsConstant || right is { IsConstant: false })
        {
            return this.InternLength(expression.Operation, left: left, right: right);
        }

        if (expression.Operation == LengthOperation.Neg)
        {
            return left.Value == long.MinValue ? null : this.InternLength(LengthOperation.Constant, -left.Value);
        }

        return TryLengthArithmetic(expression.Operation, BoundType.ISize, unchecked((UInt128)(Int128)left.Value), unchecked((UInt128)(Int128)right!.Value), out var value)
            ? this.InternLength(LengthOperation.Constant, unchecked((long)value)) : null;
    }

    private bool IsLengthArgument(Koto syntax, BindingScope scope)
    {
        syntax = UnwrapTypeSyntax(syntax);
        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return this.IsLengthArgument(grouped.Type, scope);
        }

        if (syntax is NumberLiteralKoto or ParenthesizedKoto ||
            syntax is UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.PrefixPlus } ||
            syntax is BinaryKoto { Akind: KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent })
        {
            return true;
        }

        var name = TypeSpelling(syntax);
        if (syntax is MemberAccessKoto member)
        {
            return this.LengthArgumentSymbol(member, scope)?.Kind == BindingSymbolKind.Property;
        }

        return name is not null && this.Lookup(name, scope, syntax) is { Kind: BindingSymbolKind.LengthParameter or BindingSymbolKind.Local or BindingSymbolKind.Property or BindingSymbolKind.Parameter };
    }

    private bool InferLength(BoundType pattern, BoundType actual, Koto binder, SlotFrame frame)
    {
        if (actual.Length < 0)
        {
            return true; // A local annotation's length hole supplies no evidence for a callee's length parameter.
        }

        var supplied = actual.LengthExpression ?? this.InternLength(LengthOperation.Constant, actual.Length);
        return this.InferLength(pattern.LengthExpression ?? this.InternLength(LengthOperation.Constant, pattern.Length), supplied, binder, frame);
    }

    private bool InferLength(BoundLength pattern, BoundLength supplied, Koto binder, SlotFrame frame)
    {
        if (pattern is { Parameter: { } parameter } && ReferenceEquals(parameter.Scope.Owner, binder))
        {
            var previous = frame.Lengths[parameter.Slot];
            if (previous is null)
            {
                frame.SetLength(parameter.Slot, supplied);
                return true;
            }

            return ReferenceEquals(previous, supplied);
        }

        var required = this.SubstituteLength(pattern, binder, default, frame.Lengths); // A declared length holds no interpolation-bound term.
        // A later input may establish slots used by a compound expression. The
        // completed signature is checked after all direct-slot evidence is known.
        return required is null || ReferenceEquals(required, supplied);
    }

    private bool ProveLength(BoundLength expression, FunctionKoto? function)
    {
        if (expression.IsConstant)
        {
            return ValidLength(expression.Value);
        }

        if (expression.Parameter is not null)
        {
            return true; // Every length slot is inherently nonnegative isize.
        }

        if (function is not null)
        {
            if (Contains(function.BoundSymbol?.Type))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (Contains(function.Parameters[i].Type.BoundType))
                {
                    return true;
                }
            }
        }

        return false;

        bool Contains(BoundType? type)
        {
            if (type is null)
            {
                return false;
            }

            if (ReferenceEquals(type.LengthExpression, expression))
            {
                return true;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Contains(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Whether every length of a Type's parts is proven in `function` (ProveLength); an existence walk for an unproven one, each distinct
    // part visited once (DECISIONS BU-19, BU-55).
    private bool ProveTypeLengths(BoundType type, FunctionKoto? function)
        => this.ProveTypeLengths(type, function, this.visitedParts.Begin(this.storageVersion));

    private bool ProveTypeLengths(BoundType type, FunctionKoto? function, uint query)
    {
        if (type.LengthExpression is { } expression && !this.ProveLength(expression, function))
        {
            return false;
        }

        foreach (var argument in type.LengthArguments)
        {
            if (argument is not null && !this.ProveLength(argument, function))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query) && !this.ProveTypeLengths(type.Components[i], function, query))
            {
                return false;
            }
        }

        return true;
    }

    private BoundLength? CorrespondingLength(BoundLength? length, Koto from, Koto to)
    {
        if (length is null)
        {
            return null;
        }

        var parameter = length.Parameter;
        if (parameter is not null && ReferenceEquals(parameter.Scope.Owner, from))
        {
            parameter = to.BoundSymbol!.Schema!.GenericSlots[parameter.Slot].Symbol;
        }

        return this.InternLength(length.Operation, length.Value, parameter, this.CorrespondingLength(length.Left, from, to), this.CorrespondingLength(length.Right, from, to), length.Values);
    }

    private BoundLength? BindLength(Koto syntax, BindingScope scope)
    {
        if (IsRecovery(syntax, out _))
        {
            this.BindNode(syntax, scope);
            this.Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        if (IsArrayHole(syntax))
        {
            this.Consulted(syntax);
            return this.inferredArrayLengths.GetValueOrDefault(syntax);
        }

        BoundType? type = null;
        if (!this.LengthTypeEvidence(syntax, scope, ref type) ||
            !this.EvaluateLength(syntax, scope, type ?? BoundType.ISize, out var value, out var symbolic))
        {
            this.Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        if (symbolic is not null)
        {
            // SPEC 4.4.1, 4.4.2: a length follows from its declaration where it is formed. Only functions have length slots, so outside
            // a Function scope (a misplaced length list's recovery) nothing proves a compound length: it fails here, and no length
            // obligation reaches Final unproven (DECISIONS BU-85).
            if ((scope.Function is not { } function || !IsSignatureLength(syntax, function)) && !this.ProveLength(symbolic, scope.Function))
            {
                this.Fail(syntax, BindingFailure.InvalidTypeFormation);
                return null;
            }

            this.AddObligation(new(BindingObligationKind.TypeFormation, syntax, BindingDeadline.Instantiation, Length: symbolic));
            return symbolic;
        }

        if (!ScalarTypes.TryLiteral(BoundType.ISize, value, false, out _))
        {
            this.Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        return this.InternLength(LengthOperation.Constant, (long)value);
    }

    // Probe established integer Types before fitting any literal-only subtree.
    // Normal name/member binding supplies lookup, accessibility and capture checks.
    private bool LengthTypeEvidence(Koto syntax, BindingScope scope, ref BoundType? type)
    {
        if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            return this.LengthTypeEvidence(inner, scope, ref type);
        }

        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return this.LengthTypeEvidence(grouped.Type, scope, ref type);
        }

        if (syntax is ParenthesizedKoto parent)
        {
            return this.LengthTypeEvidence(parent.Operand, scope, ref type);
        }

        if (syntax is NumberLiteralKoto { IsInteger: true })
        {
            return true;
        }

        if (syntax is UnaryKoto unary && unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            return this.LengthTypeEvidence(unary.Operand, scope, ref type);
        }

        if (syntax is BinaryKoto binary && binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent)
        {
            return this.LengthTypeEvidence(binary.Left, scope, ref type) && this.LengthTypeEvidence(binary.Right, scope, ref type);
        }

        if (syntax is not (IdentifierNameKoto or MemberAccessKoto or TypeSemanticsKoto { Type: null }))
        {
            return false;
        }

        var established = this.BindLengthName(syntax, scope);
        if (established is not { IsInteger: true, Semantics: SemanticsKind.Owner } || syntax.BindingFailure != BindingFailure.None ||
            (type is not null && !ReferenceEquals(type, established)))
        {
            return false;
        }

        type = established;
        return true;
    }

    private bool EvaluateLength(Koto syntax, BindingScope scope, BoundType type, out UInt128 value, out BoundLength? symbolic)
    {
        value = 0;
        symbolic = null;
        var width = type.Width;
        var signed = type.IsSigned;
        if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            if (!this.EvaluateLength(inner, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (syntax is ParenthesizedTypeKoto grouped)
        {
            if (!this.EvaluateLength(grouped.Type, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (syntax is ParenthesizedKoto parent)
        {
            if (!this.EvaluateLength(parent.Operand, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (KotoHelper.SignedNumber(syntax, out var negative) is { } literal)
        {
            if (!literal.IsInteger || !literal.TryGetIntegerMagnitude(out var magnitude) ||
                !ScalarTypes.TryLiteral(type, magnitude, negative, out _))
            {
                return false;
            }

            value = negative ? unchecked((UInt128)0 - magnitude) : magnitude;
            Complete(literal, type);
        }
        else if (syntax is IdentifierNameKoto or MemberAccessKoto or TypeSemanticsKoto { Type: null })
        {
            if (!ReferenceEquals(this.BindLengthName(syntax, scope), type) || syntax.BoundSymbol is not { } symbol || syntax.BindingFailure != BindingFailure.None)
            {
                return false;
            }

            if (symbol.Kind == BindingSymbolKind.LengthParameter)
            {
                symbolic = this.InternLength(LengthOperation.Parameter, parameter: symbol);
            }
            else
            {
                if (symbol.Declaration is not VariableKoto { VariableKind: VariableKind.Let, InitializerKoto: { } initializer } variable ||
                    !(symbol.Kind == BindingSymbolKind.Local ||
                    (symbol.Property is { IsStored: true, Getter.IsStandard: true } && symbol.Scope.Owner is GroupKoto)) ||
                    this.activeLengthConstants.Contains(symbol))
                {
                    return false;
                }

                this.activeLengthConstants.Add(symbol);
                try
                {
                    // Bind a forward static initializer with its declared Type first.
                    // Its ordinary arithmetic Types must remain fixed during evaluation.
                    if (variable.BindingState != BindingState.Resolved)
                    {
                        this.BindNode(variable, symbol.Scope);
                    }

                    if (variable.BindingFailure != BindingFailure.None ||
                        !this.EvaluateLength(initializer, symbol.Scope, type, out value, out symbolic))
                    {
                        return false;
                    }
                }
                finally
                {
                    this.activeLengthConstants.RemoveAt(this.activeLengthConstants.Count - 1);
                }
            }
        }
        else if (syntax is UnaryKoto unary && unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            if (!this.EvaluateLength(unary.Operand, scope, type, out value, out symbolic))
            {
                return false;
            }

            if (unary.Akind == KotoKind.PrefixMinus)
            {
                if (!signed)
                {
                    return false;
                }

                if (symbolic is not null)
                {
                    symbolic = this.InternLength(LengthOperation.Neg, left: symbolic);
                }
                else if ((Int128)value == -(Int128)((UInt128)1 << (width - 1)))
                {
                    return false;
                }
                else
                {
                    value = unchecked((UInt128)0 - value);
                }
            }
        }
        else if (syntax is BinaryKoto binary && binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent)
        {
            if (!this.EvaluateLength(binary.Left, scope, type, out var left, out var leftSymbolic) ||
                !this.EvaluateLength(binary.Right, scope, type, out var right, out var rightSymbolic))
            {
                return false;
            }

            if (leftSymbolic is not null || rightSymbolic is not null)
            {
                symbolic = this.InternLength(
                    LengthOperationOf(binary.Akind),
                    left: leftSymbolic ?? this.InternLength(LengthOperation.Constant, unchecked((long)left)),
                    right: rightSymbolic ?? this.InternLength(LengthOperation.Constant, unchecked((long)right)));
            }
            else if (!TryLengthArithmetic(LengthOperationOf(binary.Akind), type, left, right, out value))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        Complete(syntax, type);
        return true;
    }

    private BoundType? BindLengthName(Koto syntax, BindingScope scope)
    {
        if (syntax is TypeSemanticsKoto { Type: null } name)
        {
            return this.Lookup(name.Identifier, scope, syntax) is { } symbol ? this.BindReference(syntax, symbol, scope) : null;
        }

        if (syntax is MemberAccessKoto member && member.Right is not IdentifierNameKoto)
        {
            var symbol = this.LengthArgumentSymbol(member, scope);
            if (symbol is null)
            {
                return null;
            }

            var result = this.BindReference(member, symbol, scope);
            member.Right.BoundSymbol = symbol;
            Complete(member.Right, result);
            return result;
        }

        return this.BindNode(syntax, scope);
    }

    private BindingSymbol? LengthArgumentSymbol(MemberAccessKoto member, BindingScope scope)
    {
        if (this.TypeName(member.Left, scope) is not { Declaration: GroupKoto } qualifier ||
            !this.scopes.TryGetValue(qualifier.Declaration, out var members) || TypeSpelling(member.Right) is not { } name)
        {
            return null;
        }

        PublishQualifier(member.Left, qualifier);
        return members.Values.GetValueOrDefault(name);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Closely related analysis contracts.

/// <summary>A type identity used by control-flow analysis; null means unresolved, not Never.</summary>
/// <param name="Name">The canonical type name supplied by the type system.</param>
public record ControlFlowType(string Name)
{
    /// <summary>The Unit type.</summary>
    public static readonly ControlFlowType Unit = new("()");

    /// <summary>The Never type.</summary>
    public static readonly ControlFlowType Never = new("Never");

    /// <summary>The Boolean type.</summary>
    public static readonly ControlFlowType Boolean = new("bool");
}

/// <summary>A result source, including sources excluded from inference by reachability.</summary>
/// <param name="Node">The operand or implicit result expression.</param>
/// <param name="Type">Its known type, or null while Binding is pending.</param>
/// <param name="IsReachable">Whether this source contributes to result inference.</param>
public sealed record ControlFlowResultSource(Koto Node, ControlFlowType? Type, bool IsReachable);

/// <summary>Supplies type-dependent facts without coupling control flow to a particular binder.</summary>
/// <remarks>Null answers are deferred obligations, never successful type or exhaustiveness checks.</remarks>
public abstract class ControlFlowTypeSystem
{
    /// <summary>Gets a bound expression's type, if known.</summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <returns>The type, or null when unresolved.</returns>
    public abstract ControlFlowType? GetExpressionType(Koto expression);

    /// <summary>Resolves type syntax, if known.</summary>
    /// <param name="syntax">The declared type syntax.</param>
    /// <returns>The type, or null when unresolved.</returns>
    public abstract ControlFlowType? GetDeclaredType(Koto? syntax);

    /// <summary>Gets an expected type supplied by overload resolution or another enclosing context.</summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <returns>The expected type, or null.</returns>
    public virtual ControlFlowType? GetExpectedType(Koto expression) => null;

    /// <summary>Gets the expected return component of a function's contextual Function Type.</summary>
    /// <param name="boundary">The function or accessor boundary.</param>
    /// <returns>The expected return type, or null.</returns>
    public virtual ControlFlowType? GetExpectedResultType(Koto boundary) => null;

    /// <summary>Infers a common result type from reachable candidates only.</summary>
    /// <param name="sources">The reachable result sources.</param>
    /// <returns>The inferred type, or null if inference requires further Binding.</returns>
    public virtual ControlFlowType? InferResultType(IReadOnlyList<ControlFlowResultSource> sources)
        => sources.Count == 0 || sources.Any(x => x.Type is null) ? null : sources[0].Type;

    /// <summary>Checks conversion and Origin compatibility.</summary>
    /// <param name="source">The supplied result.</param>
    /// <param name="target">The required type.</param>
    /// <returns>Compatibility, or null when Binding is required.</returns>
    public abstract bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target);

    /// <summary>Determines pattern exhaustiveness without pruning arms by constant subjects.</summary>
    /// <param name="match">The selection.</param>
    /// <returns>Exhaustiveness, or null when subject/pattern Binding is required.</returns>
    public abstract bool? IsExhaustive(MatchKoto match);
}

/// <summary>Provides facts available before general name, overload, and Origin Binding.</summary>
public sealed class SyntaxControlFlowTypes : ControlFlowTypeSystem
{
    /// <inheritdoc/>
    public override ControlFlowType? GetExpressionType(Koto expression) => expression switch
    {
        UnitLiteralKoto => ControlFlowType.Unit,
        BoolLiteralKoto => ControlFlowType.Boolean,
        StringLiteralKoto or InterpolatedStringKoto => new("string"),
        NumberLiteralKoto number => new(number.Literal.Contains('.') || number.Literal.Contains('E') ? "float literal" : "integer literal"),
        ParenthesizedKoto p => this.GetExpressionType(p.Operand),
        ConversionKoto c => this.GetDeclaredType(c.Right),
        _ => null,
    };

    /// <inheritdoc/>
    public override ControlFlowType? GetDeclaredType(Koto? syntax) => syntax switch
    {
        TupleTypeKoto t when t.Elements.Count == 0 => ControlFlowType.Unit,
        TypeSemanticsKoto t when t.SemanticsKind == SemanticsKind.Owner && t.SemanticsParameter is null &&
            t.Type is null && t.OriginName is null && t.OriginExpression is null && t.OriginArguments is null &&
            t.Identifier is "bool" or "string" or "Never" or "i8" or "i16" or "i32" or "i64" or "i128" or
                "u8" or "u16" or "u32" or "u64" or "u128" or "f32" or "f64" => new(t.Identifier),
        _ => null,
    };

    /// <inheritdoc/>
    public override bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target)
    {
        if (source.Type is null)
        {
            return null;
        }

        if (source.Type == ControlFlowType.Never || source.Type == target)
        {
            return true;
        }

        if (source.Type.Name == "integer literal" && target.Name.Length > 1 && target.Name[0] is 'i' or 'u' &&
            int.TryParse(target.Name.AsSpan(1), out var bits))
        {
            var text = source.Node.ToString().Replace(" ", string.Empty);
            while (text.StartsWith('(') && text.EndsWith(')'))
            {
                text = text[1..^1];
            }

            if (!System.Numerics.BigInteger.TryParse(text, out var value))
            {
                return null;
            }

            var signed = target.Name[0] == 'i';
            var limit = System.Numerics.BigInteger.One << (signed ? bits - 1 : bits);
            return value >= (signed ? -limit : System.Numerics.BigInteger.Zero) && value < limit;
        }

        if (source.Type.Name == "float literal" && target.Name is "f32" or "f64")
        {
            return null; // Precision and range conversions belong to numeric Binding.
        }

        // Distinct primitive categories are incompatible; numeric conversions are deferred.
        if (source.Type.Name is "()" or "Never" or "bool" or "string" || target.Name is "()" or "Never" or "bool" or "string")
        {
            return false;
        }

        return null;
    }

    /// <inheritdoc/>
    public override bool? IsExhaustive(MatchKoto match)
    {
        if (match.Arms.Any(x => x.Pattern is IdentifierNameKoto { IdentifierName: "_" }))
        {
            return true;
        }

        if (this.GetExpressionType(match.Expression) == ControlFlowType.Boolean)
        {
            return match.Arms.Any(x => x.Pattern is BoolLiteralKoto { Value: true }) &&
                match.Arms.Any(x => x.Pattern is BoolLiteralKoto { Value: false });
        }

        return null;
    }
}

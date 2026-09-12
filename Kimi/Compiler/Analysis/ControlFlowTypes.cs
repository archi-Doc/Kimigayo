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

/// <summary>A structural result source; reachability never removes its type constraint.</summary>
/// <param name="Node">The operand or implicit result expression.</param>
/// <param name="Type">Its known type, or null while Binding is pending.</param>
/// <param name="IsReachable">Whether execution can reach this source (informational only).</param>
public readonly record struct ControlFlowResultSource(Koto Node, ControlFlowType? Type, bool IsReachable)
{
    internal JumpKoto? Transfer { get; init; }
}

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

    /// <summary>Resolves a Property's default getter result from its semantics and Copy capability.</summary>
    /// <param name="property">The Property declaration.</param>
    /// <returns>The default read result, or null until Binding determines the applicable rule.</returns>
    public virtual ControlFlowType? GetDefaultGetterResultType(PropertyKoto property) => null;

    /// <summary>Gets the function chosen by Binding, without performing or filtering overload resolution.</summary>
    /// <param name="expression">The function-reference expression, including a direct call's callee.</param>
    /// <returns>The selected declaration, or null when unresolved.</returns>
    public virtual FunctionKoto? GetReferencedFunction(Koto expression) => null;

    /// <summary>Identifies a committed value construction whose designator is not evaluated.</summary>
    /// <param name="expression">The construction expression.</param>
    /// <returns>True for a resolved construction.</returns>
    public virtual bool IsBoundConstruction(Koto expression) => false;

    /// <summary>Identifies a selected direct call and its receiver, without treating its callee as a runtime value.</summary>
    /// <param name="call">The call to inspect.</param>
    /// <param name="receiver">The receiver evaluated before explicit arguments, or null for an unbound call.</param>
    /// <returns>True when Binding has committed the direct call; false when its evaluation remains unknown.</returns>
    public virtual bool TryGetCallReceiver(InvocationKoto call, out Koto? receiver)
    {
        receiver = null;
        return false;
    }

    /// <summary>Determines whether a bound operation requires lexical unsafe permission.</summary>
    /// <param name="expression">The operation to check.</param>
    /// <returns>The requirement, or null when operand Types or overloads remain unresolved.</returns>
    public virtual bool? RequiresUnsafeContext(Koto expression) => null;

    /// <summary>Infers a common result type from all structural sources.</summary>
    /// <param name="sources">All structural result sources.</param>
    /// <returns>The inferred type, or null if inference requires further Binding.</returns>
    public virtual ControlFlowType? InferResultType(IReadOnlyList<ControlFlowResultSource> sources)
    {
        ControlFlowType? result = null;
        foreach (var source in sources)
        {
            if (source.Type is null && KotoHelper.UnwrapParentheses(source.Node) is not NullLiteralKoto)
            {
                return null;
            }

            if (source.Type != ControlFlowType.Never)
            {
                result ??= source.Type;
            }
        }

        return result;
    }

    /// <summary>Checks conversion and Origin compatibility.</summary>
    /// <param name="source">The supplied result.</param>
    /// <param name="target">The required type.</param>
    /// <returns>Compatibility, or null when Binding is required.</returns>
    public abstract bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target);

    /// <summary>Determines pattern exhaustiveness without pruning arms by constant subjects.</summary>
    /// <param name="match">The selection.</param>
    /// <returns>Exhaustiveness, or null when subject/pattern Binding is required.</returns>
    public abstract bool? IsExhaustive(MatchKoto match);

    /// <summary>Gets Pattern validity, exhaustiveness, and a reason for a missing proof.</summary>
    /// <param name="match">The selection.</param>
    /// <param name="subject">The known subject type.</param>
    /// <returns>A four-state coverage result; Invalid suppresses cascading coverage errors.</returns>
    public virtual MatchCoverage GetMatchCoverage(MatchKoto match, ControlFlowType? subject)
        => this.IsExhaustive(match) is { } known ? new(known ? MatchCoverageState.Exhaustive : MatchCoverageState.NonExhaustive) : MatchCoverage.FromSyntax(match, subject);
}

/// <summary>Provides facts available before general name, overload, and Origin Binding.</summary>
public sealed class SyntaxControlFlowTypes : ControlFlowTypeSystem
{
    private const string PointerPrefix = "unsafe/";
    private static readonly ControlFlowType CharType = new("char");
    private static readonly ControlFlowType StringType = new("string");
    private static readonly ControlFlowType IntegerLiteralType = new("integer literal");
    private static readonly ControlFlowType FloatLiteralType = new("float literal");
    private static readonly Dictionary<string, ControlFlowType> PrimitiveTypes = CreatePrimitiveTypes();

    // Pointer Types are value-equal records; one instance per pointee keeps repeated queries allocation-free.
    private readonly Dictionary<string, ControlFlowType> pointerTypes = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override bool? RequiresUnsafeContext(Koto expression) => expression switch
    {
        DereferenceKoto => true,
        ConversionKoto conversion when GetOuterSemantics(conversion.Right) == SemanticsKind.Unsafe => true,
        _ => null,
    };

    /// <inheritdoc/>
    public override ControlFlowType? GetDefaultGetterResultType(PropertyKoto property)
    {
        var type = this.GetDeclaredType(property.TypeKoto);
        // String ownership and generic/user-defined Copy capabilities remain unresolved.
        return type is { Name: not ("string" or "Never") } ? type : null;
    }

    /// <inheritdoc/>
    public override ControlFlowType? GetExpressionType(Koto expression) => expression switch
    {
        UnitLiteralKoto => ControlFlowType.Unit,
        BoolLiteralKoto => ControlFlowType.Boolean,
        CharLiteralKoto { Value: not null } => CharType,
        StringLiteralKoto or InterpolatedStringKoto => StringType,
        NumberLiteralKoto number => number.IsInteger ? IntegerLiteralType : FloatLiteralType,
        ParenthesizedKoto p => this.GetExpressionType(p.Operand),
        ConversionKoto c => this.GetDeclaredType(c.Right),
        _ => null,
    };

    /// <inheritdoc/>
    public override ControlFlowType? GetDeclaredType(Koto? syntax) => syntax switch
    {
        ParenthesizedTypeKoto t => this.GetDeclaredType(t.Type),
        TupleTypeKoto t when t.ElementNodes.Count == 0 => ControlFlowType.Unit,
        TypeSemanticsKoto { Type: not null, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } t
            when t.SemanticsKind is SemanticsKind.Unsafe or SemanticsKind.Owner && this.GetDeclaredType(t.Type) is { } core
            => t.SemanticsKind == SemanticsKind.Unsafe ? this.PointerType(core) : core,
        TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, Type: null, OriginName: null, OriginExpression: null, OriginArguments: null } t
            => PrimitiveTypes.GetValueOrDefault(t.Identifier),
        _ => null,
    };

    /// <inheritdoc/>
    public override bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target)
    {
        if (KotoHelper.UnwrapParentheses(source.Node) is NullLiteralKoto)
        {
            return target.Name.StartsWith(PointerPrefix, StringComparison.Ordinal);
        }

        if (source.Type is null)
        {
            return null;
        }

        if (source.Type == ControlFlowType.Never || source.Type == target)
        {
            return true;
        }

        if (source.Type.Name.StartsWith(PointerPrefix, StringComparison.Ordinal) || target.Name.StartsWith(PointerPrefix, StringComparison.Ordinal))
        {
            return false; // Pointer Type changes require an explicit conversion.
        }

        if (source.Type.Name == IntegerLiteralType.Name && target.Name.Length > 1 && target.Name[0] is 'i' or 'u' &&
            int.TryParse(target.Name.AsSpan(1), out var bits) && bits > 0)
        {
            return TryGetIntegerValue(source.Node, out var magnitude, out var negative)
                ? FitsInteger(magnitude, negative, target.Name[0] == 'i', bits)
                : null;
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
        => MatchCoverage.FromSyntax(match, this.GetExpressionType(match.Expression)).IsExhaustive;

    /// <inheritdoc/>
    public override MatchCoverage GetMatchCoverage(MatchKoto match, ControlFlowType? subject)
        => MatchCoverage.FromSyntax(match, subject);

    private static Dictionary<string, ControlFlowType> CreatePrimitiveTypes()
    {
        var result = new Dictionary<string, ControlFlowType>(StringComparer.Ordinal)
        {
            [ControlFlowType.Boolean.Name] = ControlFlowType.Boolean,
            [ControlFlowType.Never.Name] = ControlFlowType.Never,
            [CharType.Name] = CharType,
            [StringType.Name] = StringType,
        };

        foreach (var name in (ReadOnlySpan<string>)["i8", "i16", "i32", "i64", "i128", "u8", "u16", "u32", "u64", "u128", "isize", "usize", "f32", "f64"])
        {
            result.Add(name, new(name));
        }

        return result;
    }

    private static bool FitsInteger(UInt128 magnitude, bool negative, bool signed, int bits)
    {
        if (bits > 128)
        {
            return !negative || signed;
        }

        if (!signed)
        {
            return negative ? magnitude == 0 : bits == 128 || magnitude < (UInt128.One << bits);
        }

        // Signed range is [-2^(bits-1), 2^(bits-1)).
        var limit = UInt128.One << (bits - 1);
        return negative ? magnitude <= limit : magnitude < limit;
    }

    private static SemanticsKind? GetOuterSemantics(Koto syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case ParenthesizedTypeKoto grouped:
                    syntax = grouped.Type;
                    break;
                case TypeSemanticsKoto { Type: not null } wrapper when wrapper.IsTransparentWrapper || wrapper.SemanticsKind == SemanticsKind.Owner:
                    syntax = wrapper.Type;
                    break;
                default:
                    return (syntax as TypeKoto)?.SemanticsKind;
            }
        }
    }

    private static bool TryGetIntegerValue(Koto node, out UInt128 magnitude, out bool negative)
    {
        negative = false;
        while (node is ParenthesizedKoto or PrefixPlusKoto or PrefixMinusKoto)
        {
            negative ^= node is PrefixMinusKoto;
            node = ((UnaryKoto)node).Operand;
        }

        if (node is NumberLiteralKoto literal && literal.TryGetIntegerMagnitude(out magnitude))
        {
            // -0 is not negative for range checks.
            negative &= magnitude != 0;
            return true;
        }

        magnitude = default;
        return false;
    }

    private ControlFlowType PointerType(ControlFlowType pointee)
    {
        if (!this.pointerTypes.TryGetValue(pointee.Name, out var pointer))
        {
            pointer = new(PointerPrefix + pointee.Name);
            this.pointerTypes.Add(pointee.Name, pointer);
        }

        return pointer;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Binding contracts share their vocabulary.
#pragma warning disable CS1591 // Enum members are described by their names.

/// <summary>Describes one node's semantic result, independently of its descendants.</summary>
public enum BindingState : byte
{
    Unvisited,
    Resolved,
    Unresolved,
    Invalid,
}

/// <summary>Selects whether missing information is provisional or subject to final checking.</summary>
public enum BindingMode : byte
{
    Provisional,
    Final,
}

/// <summary>Classifies a declaration independently of its syntax spelling.</summary>
public enum BindingSymbolKind : byte
{
    Container,
    Type,
    Function,
    Property,
    Local,
    Parameter,
    TypeParameter,
    SemanticsTarget,
    SemanticsParameter,
    LengthParameter,
}

/// <summary>Classifies normalized semantic types.</summary>
public enum BoundTypeKind : byte
{
    Primitive,
    Nominal,
    Parameter,
    Semantics,
    Tuple,
    Function,
    FixedArray,
    Constructed,
    TargetProjection,
    SemanticsApplication,
    AssociatedProjection,
}

internal enum BindingFailure : byte
{
    None,
    MissingName,
    MissingType,
    Ambiguous,
    Duplicate,
    TypeMismatch,
    NotCallable,
    NoApplicableCandidate,
    Cycle,
    Unsupported,
    InvalidAssignment,
    InvalidLiteral,
    Access,
    Capture,
    InvalidOrigin,
    MissingOrigin,
    InvalidTypeFormation,
    InvalidConstraint,
    UnprovenConstraint,
    UnsatisfiedConstraint,
    InvalidCore,
}

/// <summary>A stable in-memory declaration identity, shared by all resolved references.</summary>
public sealed class BindingSymbol
{
    internal BindingSymbol(string name, BindingSymbolKind kind, Koto declaration, BindingScope scope)
    {
        this.Name = name;
        this.Kind = kind;
        this.Declaration = declaration;
        this.Scope = scope;
    }

    public string Name { get; }

    public BindingSymbolKind Kind { get; }

    public IntrinsicKind Intrinsic { get; internal init; }

    public Koto Declaration { get; }

    public BoundType? Type { get; internal set; }

    public DeclarationSchema? Schema { get; internal set; }

    internal BoundType? WholeType { get; set; }

    internal BindingSymbol? Pair { get; set; }

    internal BindingScope Scope { get; set; }

    internal BindingSymbol? Next { get; set; }

    internal bool Resolving { get; set; }

    internal bool HeaderBound { get; set; }

    internal int Slot { get; set; }
}

/// <summary>An immutable complete type; constructed types are interned within a compilation.</summary>
public sealed record BoundType : ControlFlowType
{
    internal BoundType(string name, BoundTypeKind kind, BindingSymbol? symbol = null, SemanticsKind semantics = SemanticsKind.Owner, BoundType[]? components = null, long length = 0, BoundOrigin? origin = null, BoundOrigin[]? originArguments = null, BoundLength? lengthExpression = null)
        : base(name)
    {
        this.Kind = kind;
        this.Symbol = symbol;
        this.Semantics = semantics;
        this.Components = components ?? [];
        this.Length = length;
        this.Origin = origin;
        this.OriginArguments = originArguments ?? [];
        this.LengthExpression = lengthExpression;
    }

    public BoundTypeKind Kind { get; }

    public BindingSymbol? Symbol { get; }

    public SemanticsKind Semantics { get; }

    public IReadOnlyList<BoundType> Components { get; }

    public long Length { get; }

    public BoundLength? LengthExpression { get; }

    public BoundOrigin? Origin { get; }

    public IReadOnlyList<BoundOrigin> OriginArguments { get; }

    public bool IsInteger => this.Kind == BoundTypeKind.Primitive && this.Name is "i8" or "i16" or "i32" or "i64" or "i128" or "isize" or "u8" or "u16" or "u32" or "u64" or "u128" or "usize";

    public bool IsNumeric => this.IsInteger || (this.Kind == BoundTypeKind.Primitive && this.Name is "f32" or "f64");

    public static new BoundType Unit { get; } = new("()", BoundTypeKind.Primitive);

    public static new BoundType Never { get; } = new("Never", BoundTypeKind.Primitive);

    public static new BoundType Boolean { get; } = new("bool", BoundTypeKind.Primitive);

    internal static readonly Dictionary<string, BoundType> Primitives = CreatePrimitives();

    private static Dictionary<string, BoundType> CreatePrimitives()
    {
        var result = new Dictionary<string, BoundType>(StringComparer.Ordinal)
        {
            ["()"] = Unit,
            ["Never"] = Never,
            ["bool"] = Boolean,
        };
        foreach (var name in new[] { "char", "string", "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f32", "f64" })
        {
            result.Add(name, new(name, BoundTypeKind.Primitive));
        }

        return result;
    }
}

internal sealed class BindingScope(Koto owner)
{
    internal Koto Owner { get; } = owner;

    internal BindingScope? Parent { get; set; }

    internal FunctionKoto? Function { get; set; }

    internal Dictionary<string, BindingSymbol> Types { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, BindingSymbol> Values { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, BoundOrigin>? Origins { get; set; }

    internal ConstraintEnvironment? Constraints { get; set; }

    internal void Reset()
    {
        this.Types.Clear();
        this.Values.Clear();
        this.Origins?.Clear();
        this.Constraints?.Reset();
    }
}

/// <summary>A final unresolved or invalid node, retaining its original source context.</summary>
public readonly record struct BindingIssue(Koto Node, DiagnosticCode Code);

/// <summary>Summarizes the current pass. Provisional completion never certifies a final program.</summary>
public readonly record struct BindingResult(BindingMode Mode, int ResolvedCount, int UnresolvedCount, int InvalidCount)
{
    public bool IsComplete => this.Mode == BindingMode.Final && this.UnresolvedCount == 0 && this.InvalidCount == 0;
}

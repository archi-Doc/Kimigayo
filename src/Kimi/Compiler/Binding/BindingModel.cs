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

/// <summary>The result category of a Function contract, independent of its physical return Type.</summary>
public enum FunctionResultMode : byte
{
    Value,
    PlaceRef,
    PlaceUniq,
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
    AssociatedType,
    Storage,
    PropertyAccessor,
    EnumCase,
    PatternCandidate,
    Capture,
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
    Slice,
    Range,
    Closure,
    Array,
    Dictionary,
    FunctionItem,
}

/// <summary>How an explicit capture entry initializes its environment binding, as <c>let x = x</c> or <c>let x = x@op</c> would (SPEC 7.6.2).</summary>
internal enum CaptureAcquisition : byte
{
    /// <summary>A bare entry of a proven-Copy binding.</summary>
    Copy,

    /// <summary><c>x@move</c>: the binding is transferred, even when Copy.</summary>
    Move,

    /// <summary>A bare entry of a binding storing <c>uniq/T</c> or <c>objuniq/T</c>: a Reborrow in the same Semantics.</summary>
    Reborrow,

    /// <summary>A bare entry of a binding storing a pair layer whose every admitted case Copies or Reborrows (SPEC 8.9): each case
    /// run and instance acquires it as the binding's case Type does, a Reborrow for an exclusive reference and a Copy otherwise
    /// (SPEC 8.10).</summary>
    Bare,

    /// <summary><c>x@ref</c>: a shared borrow of the outer binding's slot.</summary>
    SharedSlotBorrow,

    /// <summary><c>x@uniq</c>: an exclusive borrow of the outer binding's slot.</summary>
    ExclusiveSlotBorrow,
}

/// <summary>A pair binder in scope of a body with its admitted set (SPEC 8.7); it is resolved into Semantics cases (SPEC 8.10) when
/// the admitted set lies within the Semantics for which the specification defines an operation per case.</summary>
/// <param name="Target">The pair's SemanticsTarget symbol, whose <c>WholeType</c> is <c>W</c>.</param>
/// <param name="Admitted">The admitted Semantics of the binder.</param>
internal readonly record struct PairBinder(BindingSymbol Target, SemanticsMask Admitted)
{
    /// <summary>The Semantics with an operation per case: pair layers exist only for sets within <c>value or valueborrow</c>
    /// (SPEC 13.5.5.1), and conditional plans only for Copy cases in owner, ref, objref or raw with exclusive cases in uniq or
    /// objuniq (SPEC 8.9); a binder admitting obj, rc or arc admits no such operation and stays symbolic, as a Type parameter does.</summary>
    internal const SemanticsMask Resolvable = SemanticsMask.Owner | SemanticsMask.ValueBorrow | SemanticsMask.ObjectBorrow | SemanticsMask.Raw;

    /// <summary>Gets a value indicating whether the binder is analyzed once per admitted Semantics.</summary>
    internal bool Resolved => this.Admitted != SemanticsMask.None && (this.Admitted & ~Resolvable) == 0;
}

/// <summary>One Semantics case of a pair binder (SPEC 8.10): the Semantics its whole Type takes.</summary>
/// <param name="Target">The pair's SemanticsTarget symbol.</param>
/// <param name="Semantics">The admitted Semantics of this case.</param>
internal readonly record struct PairCase(BindingSymbol Target, SemanticsKind Semantics);

internal enum BindingFailure : byte
{
    None,
    MissingName,
    MissingType,
    QualificationRequired,
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
    InvalidSelfClause,
    NotObjectPayload,
    UnprovenConstraint,
    UnsatisfiedConstraint,
    InvalidKimi,
    MissingImplementation,
    IncompatibleImplementation,
    InvalidAssociatedType,
    InvalidPattern,
    NonExhaustiveMatch,
    InvalidTestDefinition,
    InvalidLayoutAttribute,
    ConflictingLayout,
    InvalidLibraryImport,
    MissingNativeRequirement,
    UnsupportedImportSignature,
    ConflictingImportSignature,
    ConflictingRuntimeSymbol,
    ConflictingImportSupply,
    UnsafeFunctionValue,
    UnavailableReservedImport,
    SplitCLayoutStorage,
    InvalidCLayout,
    InvalidInlineLayout,

    // SPEC 15.1.5 lending rule: a bare Non-Copy Place needs @move, and a directly owned Place needs @uniq.
    TransferRequired,
    ExclusiveBorrowRequired,

    // SPEC 15.1.6: a value assigned to a pattern or for binding that is a reference into a Shared or Exclusive Subject.
    SharedBindingAssignment,
    ExclusiveBindingAssignment,

    // SPEC 3.4, 15.1.5: the layer of a Place's path that denies the requested capability.
    SharedPathAccess,
    ExclusivePathTake,
    PlaceRequired,

    // SPEC 7.3: one receiver shape per function group fixed by member lookup.
    ReceiverShapeMismatch,

    // SPEC 11.2: an accessor receiver has the shape of its operation, ref/Self for an instance get and uniq/Self for an instance set.
    AccessorReceiverShape,

    // SPEC 7.3.1: the functions of one Name acquire corresponding parameters of overlapping Types in one mode.
    ParameterShapeMismatch,

    // SPEC 10.5: a reference without a fixed expected call signature needs every Type parameter bound explicitly.
    UnboundTypeArgument,

    /// <summary>Independent call evidence does not establish a required acquisition correlation.</summary>
    UnprovenAcquisitionCorrelation,

    /// <summary>Fixing inferred constructor arguments changes or cannot verify selection.</summary>
    ConstructorSelectionChanged,

    /// <summary>A virtual or override declaration violates slot eligibility.</summary>
    VirtualDeclaration,
    MissingOverrideTarget,
    AmbiguousOverrideTarget,
    OverrideContractMismatch,
    DuplicateOverride,

    // SPEC 7.3: value.method without invocation forms no bound-method value.
    BoundMethodValue,

    // SPEC 15.6.1: a fit whose structural part holds and whose Origin part is Refuted or not proven.
    OriginRelation,

    // SPEC 10.7, 15.6.1: a common Function conversion whose signatures match structurally and whose Origin contract is not proven.
    OriginContract,

    // SPEC 13.5.3: a bare owning shorthand is not an operation, and @copy requires a proven-Copy operand.
    BareOwningShorthand,
    NonCopyOperand,

    // SPEC 13.2, 13.3: an arithmetic, bitwise, shift, sign, increment or decrement operand is numeric; bool, Unit, char and
    // string have no such operators. %, the bitwise and shift operators, increment and decrement also need an integer or
    // wrapping integer operand, and a shift count an integer Type.
    NonNumericOperand,
    NonIntegerOperand,
    InvalidShiftCount,

    // SPEC 13.5.4.3-4: @wrap converts integer and wrapping integer values only; @bits pairs a floating-point Type with a
    // same-width integer Type and needs both Types fixed.
    InvalidWrapConversion,
    InvalidBitConversion,
    GenericBitConversion,

    // SPEC 9.3: the protected access forms apply only to members of a struct and their accessors.
    ProtectedPlacement,
    MissingSpecializationTarget,
    SpecializationInputMismatch,
    DuplicateDictionaryKey,

    // SPEC 8.4.7: the conforming Types of an intrinsic or closed Contract are fixed by the language.
    ClosedContractConformance,

    // SPEC 4.6.9: element indexing needs an Indexable conformance, and range indexing applies only to the sequence Types.
    NotIndexable,

    // SPEC 8.4.10.1, 8.4.10.6: an effect item that declares no bound of its Contract.
    InvalidEffectBound,

    // SPEC 4.3.1: an initialized local annotation obtains every written hole from consistent initializer evidence.
    ArrayAnnotationInference,
    NoInit,
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

    public CompilerFunctionKind CompilerFunction { get; internal init; }

    public Koto Declaration { get; }

    public BoundType? Type { get; internal set; }

    public DeclarationSchema? Schema { get; internal set; }

    /// <summary>Gets effective requirement metadata for a Contract declaration.</summary>
    public BoundContract? Contract { get; internal set; }

    /// <summary>Gets retained Property operation contracts, when this is a Property.</summary>
    public BoundProperty? Property { get; internal set; }

    /// <summary>Gets the enum Case declaration and its shared payload syntax.</summary>
    public BoundEnumCase? EnumCase { get; internal set; }

    /// <summary>Gets the instance receiver's parameter slot, or -1 for ordinary/type functions.</summary>
    public int ReceiverIndex { get; internal set; } = -1;

    internal KimiDeclarationId? LibraryDeclaration { get; init; }

    internal List<BoundOrigin>? AggregateInputOrigins { get; set; }

    internal BoundType? WholeType { get; set; }

    // SPEC 8.4: the Contract whose dedicated Self Type parameter this symbol is; null for any other symbol.
    internal BindingSymbol? SelfOf { get; set; }

    // SPEC 8.4: the dedicated Self Type parameter of a Contract, created at its first use.
    internal BoundType? ContractSelf { get; set; }

    internal BindingSymbol? Pair { get; set; }

    internal BindingScope Scope { get; set; }

    internal SyntaxFormKoto? ConditionalDeclaration { get; set; }

    internal BindingSymbol? Next { get; set; }

    internal bool Resolving { get; set; }

    internal bool HeaderBound { get; set; }

    internal int Slot { get; set; }

    internal bool MutableCapture { get; set; }

    /// <summary>Gets or sets a value indicating whether a Pattern or iteration binding is a reference because its path is shared or exclusive (SPEC 15.1.6).</summary>
    internal bool BindsReference { get; set; }

    /// <summary>Gets or sets how a capture entry initializes its environment binding (SPEC 7.6.2).</summary>
    internal CaptureAcquisition CaptureAcquisition { get; set; }

    /// <summary>Gets a value indicating whether a capture entry was written <c>x@move</c>: the binding is transferred even when Copy (SPEC 7.6.2).</summary>
    internal bool TransferCapture => this.CaptureAcquisition == CaptureAcquisition.Move;

    /// <summary>Gets or sets the struct or enum that declared <c>Self is not ObjectPayload</c> for this Type: itself or an ancestor (SPEC 8.4.7.2), or null when the Type may be an object payload.</summary>
    internal BindingSymbol? ObjectPayloadOptOut { get; set; }
}

/// <summary>An immutable complete type; constructed types are interned within a compilation.</summary>
/// <remarks>Interning makes identity the type equality, so equality and hashing never walk the fields.</remarks>
public sealed record BoundType : ControlFlowType
{
    private readonly NumericCategory numeric;

    // The integer Type whose representation a wrapping integer Scalar shares (SPEC 3.1.1.1); the Type itself otherwise.
    private readonly BoundType underlying;

    // Whole-subtree summaries, computed once at construction (see the constructor).
    private readonly bool carriesOrigin;
    private readonly bool carriesOriginOrSlot;
    private readonly bool containsParameter;
    private readonly bool containsPairLayer;

    internal BoundType(string name, BoundTypeKind kind, BindingSymbol? symbol = null, SemanticsKind semantics = SemanticsKind.Owner, BoundType[]? components = null, long length = 0, BoundOrigin? origin = null, BoundOrigin[]? originArguments = null, BoundLength? lengthExpression = null, BoundLength?[]? lengthArguments = null, FunctionResultMode resultMode = FunctionResultMode.Value)
        : base(name)
    {
        this.Kind = kind;
        this.Symbol = symbol;
        this.Semantics = semantics;
        this.ResultMode = resultMode;
        this.Components = components ?? [];
        this.Length = length;
        this.Origin = origin;
        this.OriginArguments = originArguments ?? [];
        this.LengthExpression = lengthExpression;
        this.LengthArguments = lengthArguments ?? [];
        this.numeric = kind == BoundTypeKind.Primitive ? Categorize(name) : NumericCategory.None;
        this.underlying = this;

        // Components are complete before interning, so these summaries are exact and never revisited.
        var found = origin is not null || originArguments is { Length: > 0 };
        var slot = found || kind == BoundTypeKind.Parameter;
        var parameter = kind == BoundTypeKind.Parameter;
        var pair = (kind == BoundTypeKind.Parameter && symbol?.Kind == BindingSymbolKind.SemanticsTarget) || kind == BoundTypeKind.SemanticsApplication;
        for (var i = 0; components is not null && i < components.Length; i++)
        {
            found |= components[i].carriesOrigin;
            slot |= components[i].carriesOriginOrSlot;
            parameter |= components[i].containsParameter;
            pair |= components[i].containsPairLayer;
        }

        this.carriesOrigin = found;
        this.carriesOriginOrSlot = slot;
        foreach (var argument in this.LengthArguments)
        {
            parameter |= argument is { IsConstant: false };
        }

        this.containsParameter = parameter;
        this.containsPairLayer = pair;
    }

    // SPEC 3.1.1.1: the wrapping integer Scalar Wrapping<T> over one integer Type, which keeps T's representation and
    // signedness. It is a Primitive by Core identity: no declaration, components or storage, and identity by reference.
    private BoundType(string name, BoundType integer)
        : base(name)
    {
        this.Kind = BoundTypeKind.Primitive;
        this.Semantics = SemanticsKind.Owner;
        this.Components = [];
        this.OriginArguments = [];
        this.numeric = integer.numeric;
        this.underlying = integer;
    }

    private enum NumericCategory : byte
    {
        None,
        Signed,
        Unsigned,
        Float,
    }

    public BoundTypeKind Kind { get; }

    public BindingSymbol? Symbol { get; }

    public SemanticsKind Semantics { get; }

    /// <summary>Gets the result category; non-Function Types always use Value.</summary>
    public FunctionResultMode ResultMode { get; }

    public IReadOnlyList<BoundType> Components { get; }

    public long Length { get; }

    public BoundLength? LengthExpression { get; }

    public BoundOrigin? Origin { get; }

    public IReadOnlyList<BoundOrigin> OriginArguments { get; }

    /// <summary>Gets a value indicating whether this is one of the twelve integer Types (SPEC 3.1), which excludes the
    /// wrapping integer Types; positions, lengths, shift counts and PrimitiveInteger need exactly these.</summary>
    public bool IsInteger => this.numeric is NumericCategory.Signed or NumericCategory.Unsigned && !this.IsWrappingInteger;

    /// <summary>Gets a value indicating whether this is a wrapping integer Type <c>Wrapping&lt;T&gt;</c> (SPEC 3.1.1.1).</summary>
    public bool IsWrappingInteger => !ReferenceEquals(this.underlying, this);

    /// <summary>Gets a value indicating whether this is an integer or wrapping integer Type: the Types with the integer
    /// operators, integer literals and integer comparison (SPEC 13.3).</summary>
    public bool HasIntegerArithmetic => this.numeric is NumericCategory.Signed or NumericCategory.Unsigned;

    /// <summary>Gets a value indicating whether this is a numeric Type: integer, wrapping integer or floating-point.</summary>
    public bool IsNumeric => this.numeric != NumericCategory.None;

    public static new BoundType Unit { get; } = new("()", BoundTypeKind.Primitive);

    public static new BoundType Never { get; } = new("Never", BoundTypeKind.Primitive);

    public static new BoundType Boolean { get; } = new("bool", BoundTypeKind.Primitive);

    internal static readonly Dictionary<string, BoundType> Primitives = CreatePrimitives();

    // Frequently requested primitives; they are the same instances as the Primitives entries.
    internal static readonly BoundType I32 = Primitives["i32"];

    internal static readonly BoundType F32 = Primitives["f32"];
    internal static readonly BoundType F64 = Primitives["f64"];

    internal static readonly BoundType ISize = Primitives["isize"];
    internal static readonly BoundType USize = Primitives["usize"];

    internal static readonly BoundType Range = new("Range", BoundTypeKind.Range);

    internal static readonly BoundType Char = Primitives["char"];

    internal static readonly BoundType String = Primitives["string"];

    /// <summary>The interned wrapping integer Scalar of each integer Type (SPEC 3.1.1.1), keyed by that integer Type.</summary>
    internal static readonly Dictionary<BoundType, BoundType> WrappingScalars = CreateWrappingScalars();

    /// <summary>Gets the wrapping integer Scalar over an integer Type.</summary>
    /// <param name="integer">One of the twelve integer Types.</param>
    /// <returns>The interned <c>Wrapping&lt;integer&gt;</c>.</returns>
    internal static BoundType WrappingOf(BoundType integer) => WrappingScalars[integer];

    // A generic closure keeps the enclosing substitution even when none of its slots occupy capture storage.
    internal BoundCall? ClosureContext { get; init; }

    // Function Item length bindings use declaration slots; Type slots are null. Components contain only actual Types.
    internal BoundLength?[] LengthArguments { get; } = [];

    // Refilled by ownership preparation after each final bind; excluded from Type identity.
    internal BoundType[]? StoredFields { get; set; }

    internal BoundType? StoredBase { get; set; }

    internal BoundType[]? StoredCases { get; set; }

    internal ulong StorageVersion { get; set; }

    /// <summary>Gets a value indicating whether this Type or any nested component carries an Origin.</summary>
    /// <remarks>Lets Origin-only traversals skip complete Origin-free subtrees in constant time.</remarks>
    internal bool CarriesOrigin => this.carriesOrigin;

    /// <summary>Gets a value indicating whether this subtree carries an Origin or a Type Parameter.</summary>
    /// <remarks>Requirement accumulation only reads those two, so everything else is skippable.</remarks>
    internal bool CarriesOriginOrSlot => this.carriesOriginOrSlot;

    /// <summary>Gets a value indicating whether this subtree contains a Type parameter, which a Type-identity premise
    /// may substitute (SPEC 8.3).</summary>
    internal bool ContainsParameter => this.containsParameter;

    /// <summary>Gets a value indicating whether this subtree contains a pair layer (SPEC 13.5.5.1): the original <c>s/T</c>, an
    /// annotated occurrence of it or an application <c>s/U</c>; a Semantics case substitutes only such Types (SPEC 8.10).</summary>
    internal bool ContainsPairLayer => this.containsPairLayer;

    // Only an unsigned integer Type rejects unary minus; a wrapping integer Type has it for every argument (SPEC 13.3).
    internal bool IsUnsignedInteger => this.numeric == NumericCategory.Unsigned && !this.IsWrappingInteger;

    /// <summary>Gets the integer Type whose representation, signedness and width a wrapping integer Type shares, or this
    /// Type itself. Every Scalar query about width, signedness, layout or formatting goes through it.</summary>
    internal BoundType Underlying => this.underlying;

    internal bool IsFloatingPoint => this.numeric == NumericCategory.Float;

    /// <inheritdoc/>
    public bool Equals(BoundType? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    private static NumericCategory Categorize(string name) => name switch
    {
        "i8" or "i16" or "i32" or "i64" or "i128" or "isize" => NumericCategory.Signed,
        "u8" or "u16" or "u32" or "u64" or "u128" or "usize" => NumericCategory.Unsigned,
        "f32" or "f64" => NumericCategory.Float,
        _ => NumericCategory.None,
    };

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

    private static Dictionary<BoundType, BoundType> CreateWrappingScalars()
    {
        var result = new Dictionary<BoundType, BoundType>(12);
        foreach (var primitive in Primitives.Values)
        {
            if (primitive.IsInteger)
            {
                result.Add(primitive, new("Wrapping<" + primitive.Name + ">", primitive));
            }
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

    internal Dictionary<string, TypeSemanticsKoto>? OriginSets { get; set; }

    internal ConstraintEnvironment? Constraints { get; set; }

    internal BoundConformancePath? ConformancePath { get; set; }

    internal void Reset()
    {
        this.Types.Clear();
        this.Values.Clear();
        this.Origins?.Clear();
        this.OriginSets?.Clear();
        this.Constraints?.Reset();
    }
}

/// <summary>How a check judged a repair condition from its own facts (SPEC 23.3.6.9); a refuted condition withholds the candidate.</summary>
public enum AcquisitionJudgment : byte
{
    /// <summary>The condition is established.</summary>
    Verified,

    /// <summary>The condition cannot be decided without analyzing the edited input.</summary>
    Required,

    /// <summary>The condition does not hold.</summary>
    Refuted,
}

/// <summary>A final unresolved or invalid node, retaining its original source context.</summary>
public readonly record struct BindingIssue(Koto Node, DiagnosticCode Code)
{
    /// <summary>Gets the failure that the code reports; its requirement identifies the problem.</summary>
    internal BindingFailure Failure { get; init; }
}

/// <summary>Summarizes the current pass. Provisional completion never certifies a final program.</summary>
public readonly record struct BindingResult(BindingMode Mode, int ResolvedCount, int UnresolvedCount, int InvalidCount)
{
    public bool IsComplete => this.Mode == BindingMode.Final && this.UnresolvedCount == 0 && this.InvalidCount == 0;
}

/// <summary>A validated <c>#LibraryImport</c> declaration: its external symbol and the supply kind that
/// selects dllimport (<c>import</c>) or a direct static reference (SPEC 20.8.2.1, 22.3).</summary>
internal readonly record struct LibraryImport(FunctionKoto Function, string Library, string Symbol, string Kind);

/// <summary>An Origin relation that a fit leaves unproven (SPEC 15.6.1): <c>Longer outlives Shorter</c>, or <c>==</c> at an invariant
/// position, at the value that supplies the longer end; <c>Refuted</c> when the longer end is a body-local finite Origin.</summary>
// SPEC 15.6.5: the judgment of an Origin relation that the solver may not prove.
internal enum OriginJudgment : byte
{
    Proven,
    Refuted,
    Unknown,
    Unrepresentable,
}

// SPEC 10.5: why a single generic function reference's slots did not bind from its fixed expected call signature, for the Note of its
// TypeMismatch_Kd: no binding fits structurally, a bound argument fails its Constraints, the bindings of one slot differ only in their
// Origins, or a bound argument would hold an input Origin that is bound at each call.
internal enum ReferenceSlotFailure : byte
{
    None,
    Structure,
    Constraint,
    OriginConflict,
    InputOrigin,
}

// SPEC 15.6.1, 23.3.6.5: the member of a conversion whose Origin part fails, with its relation; both ends are rigid symbols of the
// comparison, never a call-time Origin of the implementation. For an implementation condition (a clause or a result premise), `Input`
// names the required input that a repair can write over the shorter end.
internal readonly record struct OriginContractFact(Koto At, string Member, BoundOrigin Longer, BoundOrigin Shorter, bool Equality, string? Input = null);

// SPEC 15.2.3, 23.3.6.5: the Owned failure of a common Function conversion: the converted value, the member of its OwnedOrigins through
// which a non-static Origin enters (a capture name or a bound Type argument) with its Type, that Origin when one is displayable, the
// capture entry and closure it belongs to, the Borrow that supplies a captured binding's Origin, and whether the failure is Refuted (a
// body-local Origin) rather than Unknown.
internal readonly record struct OwnedConversionFact(Koto At, BoundType Subject, string Member, BoundType MemberType, BoundOrigin? Origin, CaptureKoto? Entry, FunctionKoto? Closure, Koto? Borrow, bool Refuted);

// SPEC 15.6.1: one failed chain of an Origin relation at the value that supplies its longer end. `Clause` is the relation clause of a
// declared relation (source `declared`, related with the role `relation`); null for a fit. When that value's Origin is a meet, `Longer`
// is its failing operand and `Meet` the whole meet, which a result bound must name. At a selected call, `Substituted` marks a callee's
// clause in `Clause` that is judged at the caller's input, where the caller's premises cannot remove it, and `FixedBy` is the input
// whose equality made a fresh Origin of the call equal to a fixed one, related with the role `relation` (SPEC 15.6.1, Location).
internal readonly record struct OriginRelationFact(Koto At, BoundOrigin Longer, BoundOrigin Shorter, bool Equality, BoundType? Destination, bool Refuted, Koto? Clause = null, BoundOrigin? Meet = null, bool Substituted = false, Koto? FixedBy = null);

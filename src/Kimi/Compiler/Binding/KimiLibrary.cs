// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable CS1591 // Compiler-owned library identities.

/// <summary>Owns compilation-local Kimi identities over embedded library sources.</summary>
public sealed partial class KimiLibrary
{
    private static readonly WitnessMembers[] WitnessGroups = Enum.GetValues<WitnessMembers>();

    private readonly BindingSymbol?[] symbols; // By catalog index.
    private readonly List<BindingSymbol> registeredSymbols;

    internal KimiLibrary(Compilation compilation)
    {
        this.Kotonoha = Kotonoha.ForKimiLibrary(compilation, "compiler://Kimi/" + Compilation.CurrentLanguageVersion);
        this.Scope = new(this.Kotonoha.RootKoto);
        this.Module = new("Kimi", BindingSymbolKind.Container, this.Kotonoha.RootKoto, this.Scope);
        var context = new TokenContext(null, ModifierKind.Public, false);
        this.Console = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Console", TokenKind.Group, context, default);
        this.ConsoleScope = new(this.Console) { Parent = this.Scope };
        this.ConsoleSymbol = new("Console", BindingSymbolKind.Container, this.Console, this.Scope);
        this.Intrinsics = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Intrinsics", TokenKind.Group, context, default);
        this.IntrinsicsScope = new(this.Intrinsics) { Parent = this.Scope };
        this.IntrinsicsSymbol = new("Intrinsics", BindingSymbolKind.Container, this.Intrinsics, this.Scope);
        this.Test = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Test", TokenKind.Group, context, default);
        this.TestScope = new(this.Test) { Parent = this.Scope };
        this.TestSymbol = new("Test", BindingSymbolKind.Container, this.Test, this.Scope);
        this.Text = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Text", TokenKind.Group, context, default);
        this.TextScope = new(this.Text) { Parent = this.Scope };
        this.TextSymbol = new("Text", BindingSymbolKind.Container, this.Text, this.Scope);
        this.LoadSources();
        var declarations = new Koto?[KimiLibraryCatalog.LangItemCount]; // By KimiDeclarationId.
        FindLangItems(this.Kotonoha.RootKoto, declarations);
        this.ReadPrivateDeclarations(declarations);
        var entries = KimiLibraryCatalog.Entries;
        this.symbols = new BindingSymbol?[entries.Length];
        // The four containers, then the catalog's symbols in catalog order.
        this.registeredSymbols = new(4 + entries.Length)
        {
            this.ConsoleSymbol, this.IntrinsicsSymbol, this.TestSymbol, this.TextSymbol,
        };
        this.RegisterCatalog(declarations);

        this.Copy = this.GetSymbol(KimiDeclarationId.Copy)!;
        this.Owned = this.GetSymbol(KimiDeclarationId.Owned)!;
        this.Callable = this.GetSymbol(KimiDeclarationId.Callable)!;
        this.Sealed = this.GetSymbol(KimiDeclarationId.Sealed)!;
        this.ObjectPayload = this.GetSymbol(KimiDeclarationId.ObjectPayload)!;
        this.PrimitiveInteger = this.GetSymbol(KimiDeclarationId.PrimitiveInteger)!;
        this.Replace = this.GetSymbol(KimiDeclarationId.Replace)!;
        this.Exchange = this.GetSymbol(KimiDeclarationId.Exchange)!;
        this.Swap = this.GetSymbol(KimiDeclarationId.Swap)!;
        this.Option = this.GetSymbol(KimiDeclarationId.Option)!;
        this.Result = this.GetSymbol(KimiDeclarationId.Result)!;
        this.Iterator = this.GetSymbol(KimiDeclarationId.Iterator)!;
        this.LendingIterator = this.GetSymbol(KimiDeclarationId.LendingIterator)!;
        this.Iterable = this.GetSymbol(KimiDeclarationId.Iterable)!;
        this.UniqIterable = this.GetSymbol(KimiDeclarationId.UniqIterable)!;
        this.IntoIterable = this.GetSymbol(KimiDeclarationId.IntoIterable)!;
        this.Position = this.GetSymbol(KimiDeclarationId.Position)!;
        this.PositionRange = this.GetSymbol(KimiDeclarationId.PositionRange)!;
        this.Indexable = this.GetSymbol(KimiDeclarationId.Indexable)!;
        this.UniqIndexable = this.GetSymbol(KimiDeclarationId.UniqIndexable)!;
        this.Slice = this.GetSymbol(KimiDeclarationId.Slice)!;
        this.DynamicArray = this.GetSymbol(KimiDeclarationId.Array)!;
        this.FromEnd = this.GetSymbol(KimiDeclarationId.FromEnd)!;
        this.Wrapping = this.GetSymbol(KimiDeclarationId.Wrapping)!;
        this.Start = this.GetSymbol(KimiDeclarationId.Start)!;
        this.End = this.GetSymbol(KimiDeclarationId.End)!;
        this.Range = this.GetSymbol(KimiDeclarationId.Range)!;
        this.ClosedRange = this.GetSymbol(KimiDeclarationId.ClosedRange)!;
        this.ResolvedRange = this.GetSymbol(KimiDeclarationId.ResolvedRange)!;
        this.WriteLine = this.GetSymbol(KimiDeclarationId.WriteLine)!;
        this.MakeObj = this.GetSymbol(KimiDeclarationId.MakeObj)!;
        this.Abort = this.CreateAbort();
        this.Restore();
    }

    public Kotonoha Kotonoha { get; }

    public string Version => Compilation.CurrentLanguageVersion;

    public BindingSymbol Module { get; }

    public BindingSymbol Copy { get; }

    public BindingSymbol Owned { get; }

    public BindingSymbol Callable { get; }

    public BindingSymbol WriteLine { get; }

    public BindingSymbol Sealed { get; }

    public BindingSymbol ObjectPayload { get; }

    /// <summary>Gets the compiler-intrinsic requirement of the twelve built-in integer Types (SPEC 8.7.4).</summary>
    public BindingSymbol PrimitiveInteger { get; }

    public BindingSymbol Replace { get; }

    public BindingSymbol Exchange { get; }

    public BindingSymbol Swap { get; }

    public BindingSymbol Option { get; }

    public BindingSymbol Result { get; }

    /// <summary>Gets the recognized static Iterator declaration.</summary>
    public BindingSymbol Iterator { get; }

    public BindingSymbol LendingIterator { get; }

    public BindingSymbol Iterable { get; }

    public BindingSymbol UniqIterable { get; }

    /// <summary>Gets the recognized Indexable Contract declaration (SPEC 4.7.1).</summary>
    public BindingSymbol Indexable { get; }

    /// <summary>Gets the recognized UniqIndexable Contract declaration (SPEC 4.7.1).</summary>
    public BindingSymbol UniqIndexable { get; }

    /// <summary>Gets the recognized owning iteration entry, IntoIterable (SPEC 22.2.1).</summary>
    public BindingSymbol IntoIterable { get; }

    /// <summary>Gets the closed position Contract, Position (SPEC 4.6.1, 8.7).</summary>
    public BindingSymbol Position { get; }

    /// <summary>Gets the closed range Contract, PositionRange (SPEC 4.6.3, 8.7).</summary>
    public BindingSymbol PositionRange { get; }

    /// <summary>Gets the recognized borrowed Slice Type declaration.</summary>
    public BindingSymbol Slice { get; }

    /// <summary>Gets the recognized owning dynamic Array Type declaration (SPEC 4.5, 4.9).</summary>
    public BindingSymbol DynamicArray { get; }

    /// <summary>Gets the designated from-end position Type that prefix <c>^</c> constructs (SPEC 4.6.1).</summary>
    public BindingSymbol FromEnd { get; }

    /// <summary>Gets the declaration that names the wrapping integer Scalars <c>Wrapping&lt;T&gt;</c> (SPEC 3.1.1).</summary>
    public BindingSymbol Wrapping { get; }

    /// <summary>Gets the designated start boundary Type of an omitted range start (SPEC 4.6.1).</summary>
    public BindingSymbol Start { get; }

    /// <summary>Gets the designated end boundary Type of an omitted range end (SPEC 4.6.1).</summary>
    public BindingSymbol End { get; }

    /// <summary>Gets the designated half-open range Type that <c>..</c> constructs (SPEC 4.6.2).</summary>
    public BindingSymbol Range { get; }

    /// <summary>Gets the designated closed range Type that <c>..=</c> constructs (SPEC 4.6.2).</summary>
    public BindingSymbol ClosedRange { get; }

    /// <summary>Gets the designated ResolvedRange Type (SPEC 4.6.2).</summary>
    public BindingSymbol ResolvedRange { get; }

    /// <summary>Gets the implemented concrete object factory, independently of the incomplete ownership family.</summary>
    public BindingSymbol MakeObj { get; }

    internal BindingScope Scope { get; }

    /// <summary>Gets the ordinal of Option's Some Case, its first (SPEC 22.1 declares <c>Some(T)</c>, then <c>None</c>; AdtDef numbers
    /// Cases in declaration order), or -1 before Binding prepared the library's Cases: the one statement of where Some is
    /// (TyCtxt.NonnullSome, the for statement's Some position).</summary>
    internal int OptionSome => this.OptionCase(0);

    /// <summary>Gets the ordinal of Option's None Case, its second, as <see cref="OptionSome"/> is: the one statement of where None is
    /// (the for statement's None position).</summary>
    internal int OptionNone => this.OptionCase(1);

    /// <summary>Gets the internal group whose receiver functions are the members of the built-in fixed array (SPEC 22.1, PLAN G32).</summary>
    internal DeclarationContainerKoto? FixedArrayMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver functions are the fixed array's entry witnesses (SPEC 22.1, 14.6.2), which the
    /// witness table's FixedArrayEntries row selects; a fixed array gains no members from it.</summary>
    internal DeclarationContainerKoto? FixedArrayEntryMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function implements the built-in integer conformance to
    /// Position (SPEC 4.6.1); integers gain no members from it.</summary>
    internal DeclarationContainerKoto? IntegerPositionMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver functions are string's comparison and formatting bodies (SPEC 13.4.1,
    /// 22.10.2), which the witness table's String row selects; string gains no members from it.</summary>
    internal DeclarationContainerKoto? StringWitnessMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is bool's built-in formatting body (SPEC 22.10.2), which the witness
    /// table's BoolFormat row selects; bool gains no members from it.</summary>
    internal DeclarationContainerKoto? BoolFormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is char's built-in formatting body (SPEC 22.10.2), which the witness
    /// table's CharFormat row selects; char gains no members from it.</summary>
    internal DeclarationContainerKoto? CharFormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is Unit's built-in formatting body (SPEC 22.10.2), which the witness
    /// table's UnitFormat row selects; Unit gains no members from it.</summary>
    internal DeclarationContainerKoto? UnitFormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is the view's built-in formatting body (SPEC 22.10.2), which the
    /// witness table's Utf8SliceFormat row selects; <c>Text.Utf8Slice</c> gains no members from it.</summary>
    internal DeclarationContainerKoto? Utf8SliceFormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is f32's built-in formatting body (SPEC 22.10.2), which the witness
    /// table's F32Format row selects; f32 gains no members from it.</summary>
    internal DeclarationContainerKoto? F32FormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is f64's built-in formatting body (SPEC 22.10.2), which the witness
    /// table's F64Format row selects; f64 gains no members from it.</summary>
    internal DeclarationContainerKoto? F64FormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is the wrapping integers' built-in formatting body (SPEC 3.1.1,
    /// 22.10.2), which the witness table's WrappingFormat row selects; <c>Wrapping&lt;T&gt;</c> gains no members from it.</summary>
    internal DeclarationContainerKoto? WrappingFormatMembers { get; private set; }

    /// <summary>Gets the internal Kimi group whose receiver function is every integer Type's built-in formatting body up to 64 bits
    /// (SPEC 22.10.2), which the witness table's IntegerFormat row selects; integers gain no members from it.</summary>
    internal DeclarationContainerKoto? IntegerFormatMembers { get; private set; }

    /// <summary>Gets a value indicating whether <paramref name="owner"/> is an internal group of receiver functions for a
    /// built-in Type: a group of the witness table or the fixed array's member group.</summary>
    /// <param name="owner">A declaration container.</param>
    /// <returns>Whether its functions receive a built-in Type through <c>self</c>.</returns>
    internal bool IsBuiltinMemberGroup(Koto owner) => this.MembersOf(owner) is not null;

    /// <summary>Gets the declared group of the built-in receiver groups, the one lookup.</summary>
    /// <param name="members">The group.</param>
    /// <returns>The declared group, or null when the library lacks it.</returns>
    internal DeclarationContainerKoto? Members(WitnessMembers members) => members switch
    {
        WitnessMembers.FixedArray => this.FixedArrayMembers,
        WitnessMembers.FixedArrayEntries => this.FixedArrayEntryMembers,
        WitnessMembers.IntegerPosition => this.IntegerPositionMembers,
        WitnessMembers.String => this.StringWitnessMembers,
        WitnessMembers.BoolFormat => this.BoolFormatMembers,
        WitnessMembers.CharFormat => this.CharFormatMembers,
        WitnessMembers.UnitFormat => this.UnitFormatMembers,
        WitnessMembers.Utf8SliceFormat => this.Utf8SliceFormatMembers,
        WitnessMembers.F32Format => this.F32FormatMembers,
        WitnessMembers.F64Format => this.F64FormatMembers,
        WitnessMembers.WrappingFormat => this.WrappingFormatMembers,
        WitnessMembers.IntegerFormat => this.IntegerFormatMembers,
        _ => throw new InvalidOperationException($"The group {members} has no declaration."),
    };

    /// <summary>Gets the built-in receiver group a declaration is, the inverse of <see cref="Members"/>.</summary>
    /// <param name="owner">A declaration container.</param>
    /// <returns>Its group, or null.</returns>
    internal WitnessMembers? MembersOf(Koto owner)
    {
        foreach (var members in WitnessGroups)
        {
            if (ReferenceEquals(owner, this.Members(members)))
            {
                return members;
            }
        }

        return null;
    }

    /// <summary>Gets the internal Kimi group whose functions construct prefix <c>^</c> and range syntax values and resolve
    /// position and range keys (SPEC 4.6.1-4.6.4).</summary>
    internal DeclarationContainerKoto? PositionSyntax { get; private set; }

    internal FunctionKoto DictionaryUnlink { get; private set; }

    internal FunctionKoto DictionaryAppendSlot { get; private set; }

    internal FunctionKoto DictionaryClearLinks { get; private set; }

    internal FunctionKoto DictionaryCompact { get; private set; }

    internal GroupKoto Console { get; }

    internal GroupKoto Test { get; }

    internal BindingScope TestScope { get; }

    internal BindingSymbol TestSymbol { get; }

    internal GroupKoto Intrinsics { get; }

    internal BindingScope IntrinsicsScope { get; }

    internal BindingSymbol IntrinsicsSymbol { get; }

    internal BindingScope ConsoleScope { get; }

    internal BindingSymbol ConsoleSymbol { get; }

    internal GroupKoto Text { get; }

    internal BindingScope TextScope { get; }

    internal BindingSymbol TextSymbol { get; }

    // A compiler-only call identity, never entered into Kimi or source name lookup.
    internal BindingSymbol Abort { get; }

    internal ReadOnlySpan<BindingSymbol> RegisteredSymbols => CollectionsMarshal.AsSpan(this.registeredSymbols);

    public BindingSymbol? GetSymbol(KimiDeclarationId id)
        => KimiLibraryCatalog.Index(id) is var index && index >= 0 ? this.symbols[index] : null;

    /// <summary>Gets the internal source function that defines a linked constructor (SPEC 22.1).</summary>
    /// <param name="constructor">The selected public constructor.</param>
    /// <returns>The executed implementation; null when the constructor is not linked.</returns>
    internal BindingSymbol? ConstructorImplementation(BindingSymbol constructor)
        => constructor.LibraryDeclaration is { } id && KimiLibraryCatalog.ImplementationOf(id) is { } implementation ? this.GetSymbol(implementation) : null;

    /// <summary>Gets the declaration that hover, effects and diagnostics name for an executed target: the public declaration an
    /// internal implementation defines, or the target itself.</summary>
    /// <param name="target">The executed target.</param>
    /// <returns>The presented declaration.</returns>
    internal BindingSymbol PresentedTarget(BindingSymbol target)
        => target.LibraryDeclaration is { } id && KimiLibraryCatalog.PresentationOf(id) is { } presented && this.GetSymbol(presented) is { } symbol ? symbol : target;

    internal BindingScope? SignatureScope(DeclarationContainerKoto container)
        => ReferenceEquals(container, this.Console) ? this.ConsoleScope :
            ReferenceEquals(container, this.Intrinsics) ? this.IntrinsicsScope :
            ReferenceEquals(container, this.Test) ? this.TestScope : null;

    internal void Restore()
    {
        this.Scope.Reset();
        // Intrinsic requirements and signature-only group shells are already
        // registered. Their source members and all ordinary helpers use normal indexing.
        foreach (var symbol in this.registeredSymbols)
        {
            if (symbol.Intrinsic != IntrinsicKind.None || (symbol.Kind == BindingSymbolKind.Container && !ReferenceEquals(symbol, this.TextSymbol)))
            {
                this.Scope.Types.Add(symbol.Name, symbol);
                symbol.Declaration.BoundSymbol = symbol;
                symbol.Declaration.BindingState = BindingState.Resolved;
            }
        }

        this.IntrinsicsScope.Reset();
        this.ConsoleScope.Reset();
        this.TestScope.Reset();
        this.TextScope.Reset();
        this.Kotonoha.RootKoto.BoundSymbol = this.Module;
        this.Kotonoha.RootKoto.BindingState = BindingState.Resolved;
    }

    // IMPL 21.3.4: each declaration the library marks `#Lang("Name")`, by KimiDeclarationId, read from the syntax by the Attribute's
    // Kind (the Attribute's own BindingState is not read). A Name that is no lang item, a second declaration of one Name or a `#Lang`
    // without its one string is a defect of the compiler build, whose embedded library KimiLibraryShapeTest checks.
    private static void FindLangItems(DeclarationContainerKoto container, Koto?[] declarations)
    {
        Add(container);
        for (var i = 0; i < container.Members.Count; i++)
        {
            Visit(container.Members[i]);
        }

        for (var i = 0; i < container.NestedContainers.Count; i++)
        {
            FindLangItems(container.NestedContainers[i], declarations);
        }

        void Visit(Koto node)
        {
            if (node is FunctionKoto)
            {
                Add(node);
            }
            else if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Operands: [_, _, CodeBlockKoto block] })
            {
                for (var i = 0; i < block.Items.Count; i++)
                {
                    Visit(block.Items[i]);
                }
            }
        }

        void Add(Koto declaration)
        {
            if (declaration.FindAttribute(AttributeKind.Lang) is not { } attribute)
            {
                return;
            }

            if (attribute.Operand is not InvocationKoto { ArgumentNodes: [StringLiteralKoto literal] } call || call.GetArgumentLabel(0) is not null ||
                KimiLibraryCatalog.LangItem(literal.Literal) is not { } id || declarations[(int)id] is not null)
            {
                throw new InvalidOperationException($"The embedded Kimi library's #Lang at {attribute.Span} names no lang item once (InternalInvariant).");
            }

            declarations[(int)id] = declaration;
        }
    }

    // IMPL 21.3.4: how a call of a library declaration is served, its `#Intrinsic` read from the syntax by the Attribute's Kind:
    // `#Intrinsic("Name")` the IntrinsicId member of that Name, `#Intrinsic("Fail", "Code")` Fail with its SPEC 22.5.2 code; Body without
    // one. Another argument list or a Name that is no IntrinsicId member is a defect of the compiler build, whose embedded library
    // KimiLibraryShapeTest checks.
    private static IntrinsicDisposition DefinitionOf(Koto declaration)
    {
        if (declaration.FindAttribute(AttributeKind.Intrinsic) is not { } attribute)
        {
            return default;
        }

        // Unlabeled string literals: the Name, then Fail's code.
        var (name, code) = attribute.Operand is InvocationKoto call && call.GetArgumentLabel(0) is null ? call.ArgumentNodes switch
        {
            [StringLiteralKoto only] => (only.Literal, null),
            [StringLiteralKoto first, StringLiteralKoto second] when call.GetArgumentLabel(1) is null => (first.Literal, second.Literal),
            _ => (string.Empty, (string?)null),
        } : (string.Empty, null);
        if (!Enum.TryParse<IntrinsicId>(name, out var id) || id.ToString() != name || id == IntrinsicId.None || (id == IntrinsicId.Fail) != (code is not null))
        {
            throw new InvalidOperationException($"The embedded Kimi library's #Intrinsic at {attribute.Span} names no intrinsic (InternalInvariant).");
        }

        return new(DispositionKind.Intrinsic, id, code);
    }

    // The declaration container a library declaration is declared in: its parent, or the container of the conditional conformance
    // whose block holds it.
    private static DeclarationContainerKoto DeclaringContainer(Koto declaration)
    {
        var node = declaration.Parent;
        while (node is not DeclarationContainerKoto)
        {
            node = node!.Parent;
        }

        return (DeclarationContainerKoto)node;
    }

    // The library's private declarations the compiler reads directly, each by its `#Lang`: the Dictionary storage functions, the
    // witness table's groups (R8-M4) and the position syntax group.
    [MemberNotNull(nameof(DictionaryUnlink), nameof(DictionaryAppendSlot), nameof(DictionaryClearLinks), nameof(DictionaryCompact))]
    private void ReadPrivateDeclarations(Koto?[] declarations)
    {
        this.DictionaryUnlink = (FunctionKoto)declarations[(int)KimiDeclarationId.DictionaryStorageUnlink]!;
        this.DictionaryAppendSlot = (FunctionKoto)declarations[(int)KimiDeclarationId.DictionaryStorageAppendSlot]!;
        this.DictionaryClearLinks = (FunctionKoto)declarations[(int)KimiDeclarationId.DictionaryStorageClearLinks]!;
        this.DictionaryCompact = (FunctionKoto)declarations[(int)KimiDeclarationId.DictionaryStorageCompact]!;
        this.FixedArrayMembers = declarations[(int)KimiDeclarationId.FixedArray] as DeclarationContainerKoto;
        this.FixedArrayEntryMembers = declarations[(int)KimiDeclarationId.FixedArrayEntries] as DeclarationContainerKoto;
        this.IntegerPositionMembers = declarations[(int)KimiDeclarationId.IntegerPosition] as DeclarationContainerKoto;
        this.StringWitnessMembers = declarations[(int)KimiDeclarationId.StringWitness] as DeclarationContainerKoto;
        this.BoolFormatMembers = declarations[(int)KimiDeclarationId.BoolFormat] as DeclarationContainerKoto;
        this.CharFormatMembers = declarations[(int)KimiDeclarationId.CharFormat] as DeclarationContainerKoto;
        this.UnitFormatMembers = declarations[(int)KimiDeclarationId.UnitFormat] as DeclarationContainerKoto;
        this.Utf8SliceFormatMembers = declarations[(int)KimiDeclarationId.Utf8SliceFormat] as DeclarationContainerKoto;
        this.F32FormatMembers = declarations[(int)KimiDeclarationId.F32Format] as DeclarationContainerKoto;
        this.F64FormatMembers = declarations[(int)KimiDeclarationId.F64Format] as DeclarationContainerKoto;
        this.WrappingFormatMembers = declarations[(int)KimiDeclarationId.WrappingFormat] as DeclarationContainerKoto;
        this.IntegerFormatMembers = declarations[(int)KimiDeclarationId.IntegerFormat] as DeclarationContainerKoto;
        this.PositionSyntax = declarations[(int)KimiDeclarationId.PositionSyntax] as DeclarationContainerKoto;
    }

    // SPEC 22.1: the catalog's symbols in catalog order, each bound to the declaration its `#Lang` marks, in the scope of the container
    // that declares it: the root's, a signature-only group's or Text's own scope, else one scope per container whose parent is its
    // declaring container's (indexing later gives a member's symbol its container's ordinary member scope). A symbol carries its
    // declaration's `#CallerLocation` and `#Intrinsic` (DefinitionOf).
    private void RegisterCatalog(Koto?[] declarations)
    {
        var entries = KimiLibraryCatalog.Entries;
        var scopes = new Dictionary<Koto, BindingScope>(ReferenceEqualityComparer.Instance)
        {
            [this.Kotonoha.RootKoto] = this.Scope,
            [this.Console] = this.ConsoleScope,
            [this.Intrinsics] = this.IntrinsicsScope,
            [this.Test] = this.TestScope,
            [this.Text] = this.TextScope,
        };
        for (var i = 0; i < entries.Length; i++)
        {
            ref readonly var entry = ref entries[i];
            // SPEC 22.1: a missing required declaration is a defect of the compiler build, whose embedded library KimiLibraryShapeTest checks.
            if (declarations[(int)entry.Id] is not { } declaration)
            {
                if (entry.SourceExpected)
                {
                    throw new InvalidOperationException($"The embedded Kimi library has no declaration of {entry.Id}.");
                }

                continue;
            }

            var function = declaration as FunctionKoto;
            var name = function?.Name ?? ((DeclarationContainerKoto)declaration).Name;
            var symbol = new BindingSymbol(name, function is null ? BindingSymbolKind.Type : BindingSymbolKind.Function, declaration, ScopeOf(DeclaringContainer(declaration)))
            {
                Intrinsic = entry.Intrinsic,
                LibraryDeclaration = entry.Id,
                CallerLocation = declaration.FindAttribute(AttributeKind.CallerLocation) is not null,
                Definition = DefinitionOf(declaration),
            };
            if (IntrinsicContract.Of(entry.Intrinsic).Judged)
            {
                symbol.Contract = new(symbol) { State = 2 }; // SPEC 8.4.3, 8.7.1: a refinement parent without members.
                foreach (var parent in IntrinsicContract.Of(entry.Intrinsic).Parents ?? [])
                {
                    // SPEC 8.7.4: the shape's ancestors are its row's parents, which the catalog registers before it.
                    BindingSymbol? registered = null;
                    foreach (var candidate in this.registeredSymbols)
                    {
                        if (candidate.Intrinsic == parent)
                        {
                            registered = candidate;
                        }
                    }

                    symbol.Contract.AncestorStorage.Add(registered ?? throw new InvalidOperationException($"The parent {parent} of {entry.Id} is not registered before it (InternalInvariant; R7-C2b)."));
                }
            }

            declaration.BoundSymbol = symbol;
            declaration.BindingState = BindingState.Resolved;
            this.symbols[i] = symbol;
            this.registeredSymbols.Add(symbol);
        }

        BindingScope ScopeOf(DeclarationContainerKoto container)
        {
            if (!scopes.TryGetValue(container, out var scope))
            {
                scope = new(container) { Parent = ScopeOf(DeclaringContainer(container)) };
                scopes.Add(container, scope);
            }

            return scope;
        }
    }

    // The ordinal of Option's Case in declaration order, or -1 before Binding prepared the library's Cases.
    private int OptionCase(int ordinal) => ordinal < (this.Option.Adt?.Cases.Length ?? 0) ? ordinal : -1;

    private BindingSymbol CreateAbort()
    {
        // Build ordinary declaration syntax once. Its implementation identity, not a fake
        // executable body or a spelling check at calls, supplies the later lowering hook.
        var source = new SourceDocument("compiler://builtins/abort", "string");
        var context = new CodeContext(this.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            var type = new TypeSemanticsKoto(ref reader, new Token(TokenKind.String));
            var function = new FunctionKoto(ref reader, new(null, ModifierKind.Public, false), default, "$abort", null, [new("text", "text", type, null)], null);
            type.BoundType = BoundType.String;
            type.BindingState = BindingState.Resolved;
            var symbol = new BindingSymbol("$abort", BindingSymbolKind.Function, function, this.Scope) { Definition = new(DispositionKind.Intrinsic, IntrinsicId.Abort), Type = BoundType.Never };
            function.BoundSymbol = symbol;
            function.BoundType = BoundType.Never;
            function.BindingState = BindingState.Resolved;
            return symbol;
        }
        finally
        {
            tokenizer.Dispose();
        }
    }
}

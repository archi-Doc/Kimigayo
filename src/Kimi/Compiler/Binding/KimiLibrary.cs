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
    private readonly Dictionary<KimiLibraryContainer, BindingScope> formattingScopes = new();

    internal KimiLibrary(Compilation compilation)
    {
        this.Kotonoha = new(compilation, "Kimi", "compiler://Kimi/" + Compilation.CurrentLanguageVersion);
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
        this.formattingScopes.Add(KimiLibraryContainer.Text, this.TextScope);
        foreach (var kind in new[] { KimiLibraryContainer.FixedBuffer, KimiLibraryContainer.HeapBuffer })
        {
            if (this.FormattingContainer(kind) is { } container)
            {
                this.formattingScopes.Add(kind, new(container) { Parent = this.TextScope });
            }
        }

        var storageScope = FindDeclaration(this.Kotonoha.RootKoto, "Storage", false) is DeclarationContainerKoto storage ? new BindingScope(storage) { Parent = this.Scope } : this.Scope;
        this.FindPrivateDeclarations(storageScope);
        var entries = KimiLibraryCatalog.Entries;
        this.symbols = new BindingSymbol?[entries.Length];
        // The four containers, then the catalog's symbols in catalog order.
        this.registeredSymbols = new(4 + entries.Length)
        {
            this.ConsoleSymbol, this.IntrinsicsSymbol, this.TestSymbol, this.TextSymbol,
        };
        this.RegisterCatalog(storageScope);

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

    /// <summary>Gets the ordinal of Option's Some Case, taken from the declaration (the Case named Some; AdtDef numbers Cases in
    /// declaration order), or -1 before Binding prepared the library's Cases: the one statement of where Some is (TyCtxt.NonnullSome, the
    /// for statement's Some position).</summary>
    internal int OptionSome => Array.FindIndex(this.Option.Adt?.Cases ?? [], static x => x?.Symbol.Name == "Some");

    /// <summary>Gets the ordinal of Option's None Case, taken from the declaration as <see cref="OptionSome"/> is: the one statement of
    /// where None is (the for statement's None position).</summary>
    internal int OptionNone => Array.FindIndex(this.Option.Adt?.Cases ?? [], static x => x?.Symbol.Name == "None");

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

    // The declaration of a compiler function, which the MIR builder calls where that function serves a built-in form (an Array literal, a
    // slicing, a Dictionary literal); null for None and the compiler-only $abort.
    internal BindingSymbol? Function(CompilerFunctionKind kind)
    {
        foreach (ref readonly var entry in KimiLibraryCatalog.Entries)
        {
            if (entry.Function == kind && kind != CompilerFunctionKind.None)
            {
                return this.GetSymbol(entry.Id);
            }
        }

        return null;
    }

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

    private static Koto? FindDeclaration(DeclarationContainerKoto container, string name, bool function, int overload = -1)
    {
        Koto? found = null;
        if (function)
        {
            // DeclarationContainerKoto exposes read-only list interfaces. Their backing
            // lists are not mutated during recognition; spans avoid virtual access in
            // this small, frequently repeated scan without copying or caching positions.
            var members = container.Members is List<Koto> memberList ? CollectionsMarshal.AsSpan(memberList) : default;
            foreach (var node in members)
            {
                if (Visit(node))
                {
                    return found;
                }
            }
        }
        else
        {
            var declarations = container.NestedContainers is List<DeclarationContainerKoto> declarationList ? CollectionsMarshal.AsSpan(declarationList) : default;
            foreach (var declaration in declarations)
            {
                if (declaration.Name == name)
                {
                    if (found is not null)
                    {
                        return null;
                    }

                    found = declaration;
                }
            }
        }

        return found;

        // SPEC 8.13.3: an explicit specialization has no name, so it is never the declaration a catalog row names.
        bool Visit(Koto node)
        {
            if (node is FunctionKoto { IsSpecialization: false } member && member.Name == name)
            {
                if (overload >= 0)
                {
                    if (overload-- == 0)
                    {
                        found = member;
                        return true;
                    }
                }
                else if (found is not null)
                {
                    found = null;
                    return true; // Several declarations require an explicit catalog overload.
                }
                else
                {
                    found = member;
                }
            }
            else if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Operands: [_, _, CodeBlockKoto block] })
            {
                for (var i = 0; i < block.Items.Count; i++)
                {
                    if (Visit(block.Items[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // The library's private declarations the compiler reads directly: the Dictionary storage functions, the witness table's groups
    // (R8-M4) and the position syntax group.
    [MemberNotNull(nameof(DictionaryUnlink), nameof(DictionaryAppendSlot), nameof(DictionaryClearLinks), nameof(DictionaryCompact))]
    private void FindPrivateDeclarations(BindingScope storageScope)
    {
        this.DictionaryUnlink = (FunctionKoto)FindDeclaration((DeclarationContainerKoto)FindDeclaration(this.Kotonoha.RootKoto, "DictionaryStorage", false)!, "unlink", true)!;
        var dictionaryStorage = (DeclarationContainerKoto)this.DictionaryUnlink.Parent!;
        this.DictionaryAppendSlot = (FunctionKoto)FindDeclaration(dictionaryStorage, "appendSlot", true)!;
        this.DictionaryClearLinks = (FunctionKoto)FindDeclaration(dictionaryStorage, "clearLinks", true)!;
        this.DictionaryCompact = (FunctionKoto)FindDeclaration(dictionaryStorage, "compact", true)!;
        this.FixedArrayMembers = (storageScope.Owner as DeclarationContainerKoto) is { } storageContainer ? FindDeclaration(storageContainer, "FixedArray", false) as DeclarationContainerKoto : null;
        this.FixedArrayEntryMembers = (storageScope.Owner as DeclarationContainerKoto) is { } entryContainer ? FindDeclaration(entryContainer, "FixedArrayEntries", false) as DeclarationContainerKoto : null;
        this.IntegerPositionMembers = FindDeclaration(this.Kotonoha.RootKoto, "IntegerPosition", false) as DeclarationContainerKoto;
        this.StringWitnessMembers = FindDeclaration(this.Kotonoha.RootKoto, "StringWitness", false) as DeclarationContainerKoto;
        this.BoolFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "BoolFormat", false) as DeclarationContainerKoto;
        this.CharFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "CharFormat", false) as DeclarationContainerKoto;
        this.UnitFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "UnitFormat", false) as DeclarationContainerKoto;
        this.Utf8SliceFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "Utf8SliceFormat", false) as DeclarationContainerKoto;
        this.F32FormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "F32Format", false) as DeclarationContainerKoto;
        this.F64FormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "F64Format", false) as DeclarationContainerKoto;
        this.WrappingFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "WrappingFormat", false) as DeclarationContainerKoto;
        this.IntegerFormatMembers = FindDeclaration(this.Kotonoha.RootKoto, "IntegerFormat", false) as DeclarationContainerKoto;
        this.PositionSyntax = FindDeclaration(this.Kotonoha.RootKoto, "PositionSyntax", false) as DeclarationContainerKoto;
    }

    // SPEC 22.1: the catalog's symbols in catalog order, each bound to its declaration in its container's or owner's scope.
    private void RegisterCatalog(BindingScope storageScope)
    {
        // Array operation signatures are members of the Array struct; recognition finds them through this owner scope,
        // and indexing later gives their symbols the struct's ordinary member scope.
        var arrayScope = FindDeclaration(this.Kotonoha.RootKoto, "Array", false) is DeclarationContainerKoto array ? new BindingScope(array) { Parent = this.Scope } : this.Scope;
        var dictionaryScope = FindDeclaration(this.Kotonoha.RootKoto, "Dictionary", false) is DeclarationContainerKoto dictionary ? new BindingScope(dictionary) { Parent = this.Scope } : this.Scope;
        var rawScope = FindDeclaration(this.Kotonoha.RootKoto, "Raw", false) is DeclarationContainerKoto raw ? new BindingScope(raw) { Parent = this.Scope } : this.Scope;
        var entries = KimiLibraryCatalog.Entries;
        var fixedArrayScope = this.FixedArrayMembers is { } fixedArray ? new BindingScope(fixedArray) { Parent = storageScope } : storageScope;
        var sourceScopes = new Dictionary<string, BindingScope>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++)
        {
            ref readonly var entry = ref entries[i];
            var scope = entry.Container switch
            {
                KimiLibraryContainer.Console => this.ConsoleScope,
                KimiLibraryContainer.Intrinsics => this.IntrinsicsScope,
                KimiLibraryContainer.Test => this.TestScope,
                KimiLibraryContainer.Array => arrayScope,
                KimiLibraryContainer.Dictionary => dictionaryScope,
                KimiLibraryContainer.Storage => storageScope,
                KimiLibraryContainer.Raw => rawScope,
                KimiLibraryContainer.FixedArray => fixedArrayScope,
                _ => this.formattingScopes.GetValueOrDefault(entry.Container) ?? this.Scope,
            };
            if (entry.Owner is { } owner)
            {
                if (!sourceScopes.TryGetValue(owner, out var sourceScope))
                {
                    var container = FindDeclaration(this.Kotonoha.RootKoto, owner, false) as DeclarationContainerKoto;
                    sourceScope = container is null ? this.Scope : new(container) { Parent = this.Scope };
                    sourceScopes.Add(owner, sourceScope);
                }

                scope = sourceScope;
            }

            // SPEC 22.1: a missing or duplicate required declaration is a defect of the compiler build, whose embedded library
            // KimiLibraryShapeTest checks; FindDeclaration finds no duplicate.
            if (FindDeclaration((DeclarationContainerKoto)scope.Owner, entry.Name, entry.IsFunction, entry.Overload) is not { } declaration)
            {
                if (entry.SourceExpected)
                {
                    throw new InvalidOperationException($"The embedded Kimi library has no unique declaration of {entry.Id}.");
                }

                continue;
            }

            var symbol = new BindingSymbol(entry.Name, entry.IsFunction ? BindingSymbolKind.Function : BindingSymbolKind.Type, declaration, scope)
            {
                Intrinsic = entry.Intrinsic,
                CompilerFunction = entry.Function,
                LibraryDeclaration = entry.Id,
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
    }

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
            var symbol = new BindingSymbol("$abort", BindingSymbolKind.Function, function, this.Scope) { CompilerFunction = CompilerFunctionKind.Abort, Type = BoundType.Never };
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

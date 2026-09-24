// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable CS1591 // Compiler-owned library identities.

/// <summary>Owns compilation-local Kimi identities over embedded library sources.</summary>
public sealed partial class KimiLibrary
{
    private readonly KimiDeclaration[] declarations;
    private readonly BindingSymbol[] registeredSymbols;
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
        this.DictionaryUnlink = (FunctionKoto)FindDeclaration((DeclarationContainerKoto)FindDeclaration(this.Kotonoha.RootKoto, "DictionaryStorage", false)!, "unlink", true)!;
        this.formattingScopes.Add(KimiLibraryContainer.Text, this.TextScope);
        foreach (var kind in new[] { KimiLibraryContainer.FixedBuffer, KimiLibraryContainer.HeapBuffer, KimiLibraryContainer.WriteWindow, KimiLibraryContainer.Utf8Writer })
        {
            if (this.FormattingContainer(kind) is { } container)
            {
                this.formattingScopes.Add(kind, new(container) { Parent = kind is KimiLibraryContainer.FixedBuffer or KimiLibraryContainer.HeapBuffer ? this.TextScope : this.Scope });
            }
        }

        // Array operation signatures are members of the Array struct; recognition finds them through this owner scope,
        // and indexing later gives their symbols the struct's ordinary member scope.
        this.ArrayScope = FindDeclaration(this.Kotonoha.RootKoto, "Array", false) is DeclarationContainerKoto array ? new(array) { Parent = this.Scope } : this.Scope;
        this.DictionaryScope = FindDeclaration(this.Kotonoha.RootKoto, "Dictionary", false) is DeclarationContainerKoto dictionary ? new(dictionary) { Parent = this.Scope } : this.Scope;
        var entries = KimiLibraryCatalog.Entries;
        this.declarations = new KimiDeclaration[entries.Length];
        var symbolCount = 4;
        for (var i = 0; i < entries.Length; i++)
        {
            ref readonly var entry = ref entries[i];
            var scope = entry.Container switch
            {
                KimiLibraryContainer.Console => this.ConsoleScope,
                KimiLibraryContainer.Intrinsics => this.IntrinsicsScope,
                KimiLibraryContainer.Test => this.TestScope,
                KimiLibraryContainer.Array => this.ArrayScope,
                KimiLibraryContainer.Dictionary => this.DictionaryScope,
                _ => this.formattingScopes.GetValueOrDefault(entry.Container) ?? this.Scope,
            };
            var declaration = FindDeclaration((DeclarationContainerKoto)scope.Owner, entry.Name, entry.IsFunction, entry.Overload);
            BindingSymbol? symbol = null;
            if (declaration is not null)
            {
                symbol = new(entry.Name, entry.IsFunction ? BindingSymbolKind.Function : BindingSymbolKind.Type, declaration, scope)
                {
                    Intrinsic = entry.Intrinsic,
                    CompilerFunction = entry.Function,
                    LibraryDeclaration = entry.Id,
                };
                declaration.BoundSymbol = symbol;
                declaration.BindingState = BindingState.Resolved;
                symbolCount++;
            }

            this.declarations[i] = new(entry.Id, entry.Name, symbol, KimiDeclarationState.Missing);
        }

        this.Copy = this.GetSymbol(KimiDeclarationId.Copy)!;
        this.Owned = this.GetSymbol(KimiDeclarationId.Owned)!;
        this.Callable = this.GetSymbol(KimiDeclarationId.Callable)!;
        this.Sealed = this.GetSymbol(KimiDeclarationId.Sealed)!;
        this.ObjectPayload = this.GetSymbol(KimiDeclarationId.ObjectPayload)!;
        this.Replace = this.GetSymbol(KimiDeclarationId.Replace)!;
        this.Exchange = this.GetSymbol(KimiDeclarationId.Exchange)!;
        this.Swap = this.GetSymbol(KimiDeclarationId.Swap)!;
        this.Option = this.GetSymbol(KimiDeclarationId.Option)!;
        this.Result = this.GetSymbol(KimiDeclarationId.Result)!;
        this.Iterator = this.GetSymbol(KimiDeclarationId.Iterator)!;
        this.Slice = this.GetSymbol(KimiDeclarationId.Slice)!;
        this.DynamicArray = this.GetSymbol(KimiDeclarationId.Array)!;
        this.Index = this.GetSymbol(KimiDeclarationId.Index)!;
        this.WriteLine = this.GetSymbol(KimiDeclarationId.WriteLine)!;
        this.MakeObj = this.GetSymbol(KimiDeclarationId.MakeObj)!;
        var iterator = FindDeclaration(this.Kotonoha.RootKoto, "SliceIterator", false);
        this.SliceIterator = iterator is null ? null! : new("SliceIterator", BindingSymbolKind.Type, iterator, this.Scope);
        symbolCount += iterator is null ? 0 : 1;
        this.registeredSymbols = new BindingSymbol[symbolCount];
        this.registeredSymbols[0] = this.ConsoleSymbol;
        this.registeredSymbols[1] = this.IntrinsicsSymbol;
        this.registeredSymbols[2] = this.TestSymbol;
        this.registeredSymbols[3] = this.TextSymbol;
        var symbolIndex = 4;
        if (this.SliceIterator is { } sliceIterator)
        {
            this.registeredSymbols[symbolIndex++] = sliceIterator;
        }

        foreach (var entry in this.declarations)
        {
            if (entry.Symbol is { } symbol)
            {
                this.registeredSymbols[symbolIndex++] = symbol;
            }
        }

        this.Abort = this.CreateAbort();
        this.Restore();
    }

    public Kotonoha Kotonoha { get; }

    public string Version => Compilation.CurrentLanguageVersion;

    /// <summary>Gets a value indicating whether all required declarations are validated, separately from body, layout and runtime support.</summary>
    public bool IsCompleteLibrary => this.IsValid && this.ValidatedDeclarationCount == this.declarations.Length;

    /// <summary>Gets retained catalog entries, not indexed by numeric ID. States are refreshed by each Bind.</summary>
    public ReadOnlySpan<KimiDeclaration> Declarations => this.declarations;

    public int ValidatedDeclarationCount { get; private set; }

    public BindingSymbol Module { get; }

    public BindingSymbol Copy { get; }

    public BindingSymbol Owned { get; }

    public BindingSymbol Callable { get; }

    public BindingSymbol WriteLine { get; }

    public BindingSymbol Sealed { get; }

    public BindingSymbol ObjectPayload { get; }

    public BindingSymbol Replace { get; }

    public BindingSymbol Exchange { get; }

    public BindingSymbol Swap { get; }

    public BindingSymbol Option { get; }

    public BindingSymbol Result { get; }

    /// <summary>Gets the recognized static Iterator declaration.</summary>
    public BindingSymbol Iterator { get; }

    /// <summary>Gets the recognized borrowed Slice Type declaration.</summary>
    public BindingSymbol Slice { get; }

    /// <summary>Gets the recognized owning dynamic Array Type declaration (SPEC 4.5, 4.7).</summary>
    public BindingSymbol DynamicArray { get; }

    /// <summary>Gets the designated storable sequence Index Type (SPEC 4.6.2).</summary>
    public BindingSymbol Index { get; }

    /// <summary>Gets the implemented concrete object factory, independently of the incomplete ownership family.</summary>
    public BindingSymbol MakeObj { get; }

    internal BindingSymbol SliceIterator { get; }

    internal BindingScope Scope { get; }

    internal BindingScope DictionaryScope { get; }

    internal FunctionKoto DictionaryUnlink { get; }

    internal GroupKoto Console { get; }

    internal GroupKoto Test { get; }

    internal BindingScope TestScope { get; }

    internal BindingSymbol TestSymbol { get; }

    internal GroupKoto Intrinsics { get; }

    internal BindingScope IntrinsicsScope { get; }

    internal BindingSymbol IntrinsicsSymbol { get; }

    internal BindingScope ConsoleScope { get; }

    internal BindingScope ArrayScope { get; }

    internal BindingSymbol ConsoleSymbol { get; }

    internal GroupKoto Text { get; }

    internal BindingScope TextScope { get; }

    internal BindingSymbol TextSymbol { get; }

    // A compiler-only call identity, never entered into Kimi or source name lookup.
    internal BindingSymbol Abort { get; }

    internal bool IsValid { get; private set; }

    internal Koto? InvalidDeclaration { get; private set; }

    internal ReadOnlySpan<BindingSymbol> RegisteredSymbols => this.registeredSymbols;

    public BindingSymbol? GetSymbol(KimiDeclarationId id)
        => KimiLibraryCatalog.Index(id) is var index && index >= 0 ? this.declarations[index].Symbol : null;

    public KimiDeclarationState GetDeclarationState(KimiDeclarationId id)
        => KimiLibraryCatalog.Index(id) is var index && index >= 0 ? this.declarations[index].State : KimiDeclarationState.Missing;

    /// <summary>Gets a value indicating whether all ownership declarations are validated; not a runtime support certificate.</summary>
    public bool IsCompleteOwnershipFamily =>
        this.GetDeclarationState(KimiDeclarationId.MakeObj) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.MakeRc) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.MakeArc) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.Clone) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.Downgrade) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.Upgrade) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.MakeRcCyclic) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.MakeArcCyclic) == KimiDeclarationState.Validated &&
        this.GetDeclarationState(KimiDeclarationId.Weak) == KimiDeclarationState.Validated;

    internal BindingScope? SignatureScope(DeclarationContainerKoto container)
        => ReferenceEquals(container, this.Console) ? this.ConsoleScope :
            ReferenceEquals(container, this.Intrinsics) ? this.IntrinsicsScope :
            ReferenceEquals(container, this.Test) ? this.TestScope : null;

    internal void Restore()
    {
        this.Scope.Reset();
        // Intrinsic requirements and checked signature-only group shells are already
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
                if (node is FunctionKoto member && member.Name == name)
                {
                    if (overload >= 0)
                    {
                        if (overload-- == 0)
                        {
                            return member;
                        }

                        continue;
                    }

                    if (found is not null)
                    {
                        return null;
                    }

                    found = member;
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

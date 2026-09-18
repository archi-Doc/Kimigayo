// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Compiler-owned identity vocabulary.

public enum IntrinsicKind : byte
{
    None,
    Copy,
    Owned,
    Callable,
    Sealed,
}

/// <summary>Identifies a compiler-provided language function implementation.</summary>
public enum CompilerFunctionKind : byte
{
    None,
    WriteLine,
    Abort,
    Replace,
    Exchange,
    Swap,
    MakeObj,
}

/// <summary>The compiler-owned Kimi identities. This is not the complete runtime Kimi library.</summary>
public sealed class KimiLibrary
{
    private readonly KimiDeclaration[] declarations =
    [
        new(KimiDeclarationId.Copy, "Copy", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Owned, "Owned", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Callable, "Callable", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.WriteLine, "writeLine", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Option, "Option", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Result, "Result", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Array, "Array", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Index, "Index", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Range, "Range", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.ResolvedRange, "ResolvedRange", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Slice, "Slice", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Dictionary, "Dictionary", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Stringify, "Stringify", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Equatable, "Equatable", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Comparable, "Comparable", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Iterator, "Iterator", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Iterable, "Iterable", null, KimiDeclarationState.Missing),
        // The broader object-ownership family is incomplete; makeObj is tracked separately.
        new(KimiDeclarationId.ObjectOwnership, string.Empty, null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Sealed, "Sealed", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Replace, "replace", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Exchange, "exchange", null, KimiDeclarationState.Missing),
        new(KimiDeclarationId.Swap, "swap", null, KimiDeclarationState.Missing),
    ];

    internal KimiLibrary(Compilation compilation)
    {
        this.Kotonoha = new(compilation, "Kimi", "compiler://Kimi/" + Compilation.CurrentLanguageVersion);
        var context = new TokenContext(null, ModifierKind.Public, false);
        this.Scope = new(this.Kotonoha.RootKoto);
        this.Module = new("Kimi", BindingSymbolKind.Container, this.Kotonoha.RootKoto, this.Scope);
        for (var i = 0; i < 3; i++)
        {
            this.Kotonoha.RootKoto.GetOrAddGroup(this.declarations[i].Name, TokenKind.Contract, context, default);
            this.declarations[i] = this.declarations[i] with { Symbol = this.Create(i, (IntrinsicKind)(i + 1)) };
        }

        this.CreateEnums();
        this.Console = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Console", TokenKind.Group, context, default);
        this.ConsoleScope = new(this.Console) { Parent = this.Scope };
        this.ConsoleSymbol = new("Console", BindingSymbolKind.Container, this.Console, this.Scope);
        this.WriteLine = this.CreateStringOperation(abort: false);
        this.Abort = this.CreateStringOperation(abort: true);
        this.declarations[(int)KimiDeclarationId.WriteLine] = this.declarations[(int)KimiDeclarationId.WriteLine] with { Symbol = this.WriteLine };
        // Parse the canonical declarations once; every Bind uses the ordinary enum pipeline.
        this.declarations[(int)KimiDeclarationId.Option] = this.declarations[(int)KimiDeclarationId.Option] with { Symbol = this.Create(3, IntrinsicKind.None) };
        this.declarations[(int)KimiDeclarationId.Result] = this.declarations[(int)KimiDeclarationId.Result] with { Symbol = this.Create(4, IntrinsicKind.None) };
        this.Kotonoha.RootKoto.GetOrAddGroup("Sealed", TokenKind.Contract, context, default);
        this.declarations[(int)KimiDeclarationId.Sealed] = this.declarations[(int)KimiDeclarationId.Sealed] with { Symbol = this.Create(6, IntrinsicKind.Sealed) };
        this.ParseDeclarations("public contract Iterator\n    associate Element\n    func next(self: uniq/Self) -> Option<Self.Element>");
        this.declarations[(int)KimiDeclarationId.Iterator] = this.declarations[(int)KimiDeclarationId.Iterator] with { Symbol = this.Create(7, IntrinsicKind.None) };
        this.ParseDeclarations("""
            public struct Slice<T> origin source
                public func iterate(self: Self) -> SliceIterator<T> from source
                    return SliceIterator<T>.init(self)
            public struct SliceIterator<T> origin source
                Self is Iterator
                associate Iterator.Element is ref/T from source
                let values: Slice<T> from source
                var position: isize = 0
                public init(values: Slice<T> from source)
                    self.values = values
                public func next(self: uniq/Self) -> Option<ref/T from source>
                    require self.position < self.values.length else => return .None
                    let index = self.position
                    self.position = self.position + 1
                    return .Some(self.values[index]@ref/T)
            """);
        this.declarations[(int)KimiDeclarationId.Slice] = this.declarations[(int)KimiDeclarationId.Slice] with { Symbol = this.Create(8, IntrinsicKind.None) };
        this.SliceIterator = this.Create(9, IntrinsicKind.None);
        this.Intrinsics = (GroupKoto)this.Kotonoha.RootKoto.GetOrAddGroup("Intrinsics", TokenKind.Group, context, default);
        this.IntrinsicsScope = new(this.Intrinsics) { Parent = this.Scope };
        this.IntrinsicsSymbol = new("Intrinsics", BindingSymbolKind.Container, this.Intrinsics, this.Scope);
        this.ParseDeclarations("public func replace<T>(target: uniq/T, with => value: T) -> ()\npublic func exchange<T>(target: uniq/T, with => value: T) -> T\npublic func swap<T>(first: uniq/T, second: uniq/T) -> ()", this.Intrinsics);
        for (var i = 0; i < 3; i++)
        {
            var declaration = (FunctionKoto)this.Intrinsics.Members[i];
            var symbol = new BindingSymbol(declaration.Name, BindingSymbolKind.Function, declaration, this.IntrinsicsScope) { CompilerFunction = (CompilerFunctionKind)((int)CompilerFunctionKind.Replace + i) };
            var id = (int)KimiDeclarationId.Replace + i;
            this.declarations[id] = this.declarations[id] with { Symbol = symbol };
        }

        this.ParseDeclarations("public func makeObj<T>(value: T) -> obj/T", this.Intrinsics);
        var makeObj = (FunctionKoto)this.Intrinsics.Members[3];
        this.MakeObj = new(makeObj.Name, BindingSymbolKind.Function, makeObj, this.IntrinsicsScope) { CompilerFunction = CompilerFunctionKind.MakeObj };
        this.Restore();
    }

    public Kotonoha Kotonoha { get; }

    public string Version => Compilation.CurrentLanguageVersion;

    /// <summary>Gets a value indicating whether all required declarations are validated, separately from body, layout and runtime support.</summary>
    public bool IsCompleteLibrary => this.IsValid && this.ValidatedDeclarationCount == this.declarations.Length;

    /// <summary>Gets retained catalog entries. States are refreshed by each Bind.</summary>
    public ReadOnlySpan<KimiDeclaration> Declarations => this.declarations;

    public int ValidatedDeclarationCount { get; private set; }

    public BindingSymbol Module { get; }

    public BindingSymbol Copy => this.declarations[(int)KimiDeclarationId.Copy].Symbol!;

    public BindingSymbol Owned => this.declarations[(int)KimiDeclarationId.Owned].Symbol!;

    public BindingSymbol Callable => this.declarations[(int)KimiDeclarationId.Callable].Symbol!;

    public BindingSymbol WriteLine { get; }

    public BindingSymbol Sealed => this.declarations[(int)KimiDeclarationId.Sealed].Symbol!;

    public BindingSymbol Replace => this.declarations[(int)KimiDeclarationId.Replace].Symbol!;

    public BindingSymbol Exchange => this.declarations[(int)KimiDeclarationId.Exchange].Symbol!;

    public BindingSymbol Swap => this.declarations[(int)KimiDeclarationId.Swap].Symbol!;

    public BindingSymbol Option => this.declarations[(int)KimiDeclarationId.Option].Symbol!;

    public BindingSymbol Result => this.declarations[(int)KimiDeclarationId.Result].Symbol!;

    /// <summary>Gets the recognized static Iterator declaration.</summary>
    public BindingSymbol Iterator => this.declarations[(int)KimiDeclarationId.Iterator].Symbol!;

    /// <summary>Gets the recognized borrowed Slice Type declaration.</summary>
    public BindingSymbol Slice => this.declarations[(int)KimiDeclarationId.Slice].Symbol!;

    /// <summary>Gets the implemented concrete object factory, independently of the incomplete ownership family.</summary>
    public BindingSymbol MakeObj { get; }

    internal BindingSymbol SliceIterator { get; }

    internal BindingScope Scope { get; }

    internal GroupKoto Console { get; }

    internal GroupKoto Intrinsics { get; }

    internal BindingScope IntrinsicsScope { get; }

    internal BindingSymbol IntrinsicsSymbol { get; }

    internal BindingScope ConsoleScope { get; }

    internal BindingSymbol ConsoleSymbol { get; }

    // A compiler-only call identity, never entered into Kimi or source name lookup.
    internal BindingSymbol Abort { get; }

    internal bool IsValid
    {
        get
        {
            var valid = this.Kotonoha.GeneratedFunction is null && this.Kotonoha.RootKoto.NestedContainers.Count == 11 &&
                this.Kotonoha.RootKoto.Members.Count == 0 && this.ValidIntrinsics() && this.ValidMakeObj() &&
                ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[5], this.Console) &&
                this.Console is { Name: "Console", Modifier: ModifierKind.Public, AttributeChain: null, HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 0, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Members.Count: 1 } &&
                ReferenceEquals(this.Console.Parent, this.Kotonoha.RootKoto) &&
                ReferenceEquals(this.Console.Members[0], this.WriteLine.Declaration);
            this.ValidatedDeclarationCount = 0;
            for (var i = 0; i < this.declarations.Length; i++)
            {
                var entry = this.declarations[i];
                var state = KimiDeclarationState.Missing;
                if (entry.Symbol is { } symbol)
                {
                    var matches = i < 3 ? this.Valid(symbol, i) : entry.Id switch
                    {
                        KimiDeclarationId.Sealed => this.Valid(symbol, 6),
                        KimiDeclarationId.Replace or KimiDeclarationId.Exchange or KimiDeclarationId.Swap => this.ValidUpdate(symbol, entry.Id),
                        KimiDeclarationId.WriteLine => this.ValidWriteLine(),
                        KimiDeclarationId.Iterator => this.ValidIterator(symbol),
                        KimiDeclarationId.Slice => this.ValidSlice(symbol),
                        _ => this.ValidEnum(symbol, entry.Id),
                    };
                    state = matches ? KimiDeclarationState.Validated : KimiDeclarationState.Invalid;
                    valid &= matches;
                    this.ValidatedDeclarationCount += matches ? 1 : 0;
                }

                this.declarations[i] = entry with { State = state };
            }

            return valid;
        }
    }

    public BindingSymbol? GetSymbol(KimiDeclarationId id) => this.declarations[(int)id].Symbol;

    internal void Restore()
    {
        this.Scope.Reset();
        for (var i = 0; i < this.declarations.Length; i++)
        {
            if (this.declarations[i].Symbol is { Kind: BindingSymbolKind.Type } symbol)
            {
                this.Scope.Types.Add(symbol.Name, symbol);
            }
        }

        this.IntrinsicsScope.Reset();
        this.Intrinsics.BoundSymbol = this.IntrinsicsSymbol;
        this.Scope.Types.Add("Intrinsics", this.IntrinsicsSymbol);
        this.ConsoleScope.Reset();
        this.Console.BoundSymbol = this.ConsoleSymbol;
        this.Scope.Types.Add("Console", this.ConsoleSymbol);
        this.Kotonoha.RootKoto.BoundSymbol = this.Module;
        this.Kotonoha.RootKoto.BindingState = BindingState.Resolved;
    }

    private static Koto? BareType(Koto? node)
    {
        while (node is TypeSemanticsKoto { Type: not null, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } type)
        {
            node = type.Type;
        }

        return node;
    }

    private static bool BareName(Koto? node, string name) => BareType(node) is IdentifierNameKoto identifier ? identifier.IdentifierName == name :
        BareType(node) is TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } type && type.Identifier == name;

    private BindingSymbol Create(int index, IntrinsicKind kind)
    {
        var declaration = this.Kotonoha.RootKoto.NestedContainers[index];
        var symbol = new BindingSymbol(declaration.Name, BindingSymbolKind.Type, declaration, this.Scope) { Intrinsic = kind };
        declaration.BoundSymbol = symbol;
        declaration.BindingState = BindingState.Resolved;
        return symbol;
    }

    private void CreateEnums() => this.ParseDeclarations("public enum Option<T>\n    Self is Copy when T is Copy\n    Some(T)\n    None\npublic enum Result<T, E>\n    Ok(T)\n    Err(E)");

    private void ParseDeclarations(string text, DeclarationContainerKoto? container = null)
    {
        var source = new SourceDocument("compiler://Kimi/declarations", text);
        var context = new CodeContext(this.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            // Compiler-owned, target-independent declarations must not freeze project inputs.
            if (text.StartsWith("public func", StringComparison.Ordinal))
            {
                while (reader.CanRead)
                {
                    Parser.ConsumeAttributeAndModifier(ref reader, out var end);
                    if (end)
                    {
                        break;
                    }

                    // These signatures have compiler implementations, not source bodies.
                    // Use the same signature parser without executable-body validation.
                    reader.Advance(); // func
                    var item = Parser.ParseFuncDeclaration(ref reader);
                    if (item is not null)
                    {
                        (container ?? this.Kotonoha.RootKoto).AddLast(item);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else
            {
                this.Kotonoha.RootKoto.Parse(ref reader);
            }
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private BindingSymbol CreateStringOperation(bool abort)
    {
        // Build ordinary declaration syntax once. Its implementation identity, not a fake
        // executable body or a spelling check at calls, supplies the later lowering hook.
        var source = new SourceDocument(abort ? "compiler://builtins/abort" : "compiler://Kimi/writeLine", "string");
        var context = new CodeContext(this.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            var type = new TypeSemanticsKoto(ref reader, new Token(TokenKind.String));
            var function = new FunctionKoto(ref reader, new(null, ModifierKind.Public, false), default, abort ? "$abort" : "writeLine", null, [new("text", "text", false, type, null)], null);
            if (abort)
            {
                type.BoundType = BoundType.String;
                type.BindingState = BindingState.Resolved;
                var symbol = new BindingSymbol("$abort", BindingSymbolKind.Function, function, this.Scope) { CompilerFunction = CompilerFunctionKind.Abort, Type = BoundType.Never };
                function.BoundSymbol = symbol;
                function.BoundType = BoundType.Never;
                function.BindingState = BindingState.Resolved;
                return symbol;
            }

            this.Console.AddLast(function);
            return new("writeLine", BindingSymbolKind.Function, function, this.ConsoleScope) { CompilerFunction = CompilerFunctionKind.WriteLine };
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private bool ValidEnum(BindingSymbol symbol, KimiDeclarationId id)
    {
        var option = id == KimiDeclarationId.Option;
        var index = option ? 3 : 4;
        if (id is not (KimiDeclarationId.Option or KimiDeclarationId.Result) ||
            this.Kotonoha.RootKoto.NestedContainers.Count <= index || !ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[index], symbol.Declaration) ||
            symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not EnumKoto { HasIncompatibleBindingHeader: false, Modifier: ModifierKind.Public, AttributeChain: null, Bases.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, ConstraintNodes.Count: 0 } declaration ||
            declaration.Name != (option ? "Option" : "Result") || !ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) ||
            declaration.GenericParameterNodes.Count != (option ? 1 : 2) || declaration.Members.Count != (option ? 3 : 2))
        {
            return false;
        }

        for (var i = 0; i < declaration.GenericParameterNodes.Count; i++)
        {
            if (declaration.GenericParameterNodes[i] is not GenericParameterKoto { SemanticsParameter: null, AttributeChain: null } parameter || parameter.Identifier != (i == 0 ? "T" : "E"))
            {
                return false;
            }
        }

        if (option && (declaration.Members[0] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, AttributeChain: null } conditional ||
            conditional.Operands.Length != 2 || !CopyClause(conditional.Operands[0], "Self") ||
            conditional.Operands[1] is not SyntaxFormKoto premises || premises.Operands.Length != 1 || !CopyClause(premises.Operands[0], "T")))
        {
            return false;
        }

        return Case(declaration.Members[option ? 1 : 0], option ? "Some" : "Ok", "T") &&
            Case(declaration.Members[option ? 2 : 1], option ? "None" : "Err", option ? null : "E");

        static bool CopyClause(Koto node, string subject)
            => node is IsKoto { Left: IdentifierNameKoto left, Right: IdentifierNameKoto right, AttributeChain: null } && left.IdentifierName == subject && right.IdentifierName == "Copy";

        static bool Case(Koto node, string name, string? type)
            => node is SyntaxFormKoto { Akind: KotoKind.EnumCase, AttributeChain: null } form && form.Operands.Length == 2 &&
            form.Operands[0] is IdentifierNameKoto identifier && identifier.IdentifierName == name &&
            form.Operands[1] is SyntaxFormKoto payload && payload.Operands.Length == (type is null ? 0 : 1) &&
            (type is null || (payload.Operands[0] is TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } element && element.Identifier == type));
    }

    private bool ValidUpdate(BindingSymbol symbol, KimiDeclarationId id)
    {
        var index = (int)id - (int)KimiDeclarationId.Replace;
        var swap = id == KimiDeclarationId.Swap;
        if ((uint)index >= (uint)this.Intrinsics.Members.Count || symbol.CompilerFunction != (CompilerFunctionKind)((int)CompilerFunctionKind.Replace + index) || !ReferenceEquals(symbol.Scope, this.IntrinsicsScope) ||
            !ReferenceEquals(this.Intrinsics.Members[index], symbol.Declaration) ||
            symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.Intrinsics) ||
            function.Name != symbol.Name || function.Modifier != ModifierKind.Public || function.AttributeChain is not null ||
            function.GenericArguments.Count != 1 || function.GenericArguments[0] is not GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } ||
            function.Origins.Count != 0 || function.Parameters.Count != 2 || function.TypeConstraints.Count != 0 ||
            function.Body is not null || function.ExpressionBody is not null || function.IsRequirement || function.IsGenerated || function.IsSpecialization)
        {
            return false;
        }

        for (var i = 0; i < 2; i++)
        {
            var parameter = function.Parameters[i];
            var name = swap ? (i == 0 ? "first" : "second") : (i == 0 ? "target" : "value");
            if (parameter.InternalName != name || parameter.ExternalName != (!swap && i == 1 ? "with" : name) ||
                parameter.IsOptional || parameter.DefaultValue is not null || parameter.AttributeChain is not null ||
                !Type(parameter.Type, swap || i == 0))
            {
                return false;
            }
        }

        return id == KimiDeclarationId.Exchange ? Type(function.ReturnType, false) : function.ReturnType is TupleTypeKoto { ElementNodes.Count: 0 };

        static bool Type(Koto? node, bool borrow) => node is TypeSemanticsKoto { OriginName: null, OriginExpression: null, OriginArguments: null, SemanticsParameter: null } type &&
            (borrow ? type.SemanticsKind == SemanticsKind.Uniq && Type(type.Type, false) : type is { SemanticsKind: SemanticsKind.Owner, Type: null, Identifier: "T" });
    }

    private bool ValidWriteLine()
        => this.WriteLine.CompilerFunction == CompilerFunctionKind.WriteLine && ReferenceEquals(this.WriteLine.Scope, this.ConsoleScope) &&
        this.WriteLine.Declaration is FunctionKoto function &&
        ReferenceEquals(function.Parent, this.Console) &&
        function.Name == "writeLine" && function.Modifier == ModifierKind.Public &&
        function.GenericArguments.Count == 0 && function.Origins.Count == 0 && function.Parameters.Count == 1 &&
        function.TypeConstraints.Count == 0 && function.ReturnType is null && function.Body is null && function.ExpressionBody is null &&
        function.AttributeChain is null && !function.IsRequirement && !function.IsGenerated && !function.IsSpecialization &&
        function.Parameters[0] is
        {
            ExternalName: "text", InternalName: "text", IsOptional: false, DefaultValue: null, AttributeChain: null,
            Type: TypeSemanticsKoto { Type: null, Identifier: "string", SemanticsKind: SemanticsKind.Owner, OriginExpression: null, OriginName: null, OriginArguments: null },
        };

    private bool ValidIterator(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        this.Kotonoha.RootKoto.NestedContainers.Count > 7 &&
        ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[7], symbol.Declaration) &&
        symbol.Declaration is ContractKoto { Name: "Iterator", HasIncompatibleBindingHeader: false, Members.Count: 2, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        declaration.Members[0] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 1, AttributeChain: null } associated &&
        associated.Operands[0] is IdentifierNameKoto { IdentifierName: "Element" } &&
        declaration.Members[1] is FunctionKoto { Name: "next", IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 1, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function &&
        function.Parameters[0] is { InternalName: "self", ExternalName: "self", IsOptional: false, DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: TypeSemanticsKoto { Identifier: "Self", Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } } } &&
        BareType(function.ReturnType) is GenericsKoto { TypeArguments.Count: 1 } option && BareName(option.Identifier, "Option") &&
        BareType(option.TypeArguments[0]) is MemberAccessKoto element && BareName(element.Left, "Self") && BareName(element.Right, "Element");

    private bool ValidSlice(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) && this.Kotonoha.RootKoto.NestedContainers.Count > 8 &&
        ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[8], symbol.Declaration) &&
        symbol.Declaration is StructKoto { Name: "Slice", HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 1, OriginNames.Count: 1, Members.Count: 1, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } slice &&
        ReferenceEquals(slice.Parent, this.Kotonoha.RootKoto) && slice.OriginNames[0] == "source" &&
        slice.GenericParameterNodes[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null };

    private bool ValidIntrinsics()
        => ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[10], this.Intrinsics) &&
        ReferenceEquals(this.Intrinsics.Parent, this.Kotonoha.RootKoto) &&
        this.Intrinsics is { Name: "Intrinsics", Modifier: ModifierKind.Public, AttributeChain: null, HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 0, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Members.Count: 4 };

    private bool ValidMakeObj()
        => this.MakeObj.CompilerFunction == CompilerFunctionKind.MakeObj && ReferenceEquals(this.MakeObj.Scope, this.IntrinsicsScope) &&
        this.Intrinsics.Members.Count == 4 && ReferenceEquals(this.Intrinsics.Members[3], this.MakeObj.Declaration) &&
        ReferenceEquals(this.MakeObj.Declaration.Parent, this.Intrinsics) &&
        this.MakeObj.Declaration is FunctionKoto { Name: "makeObj", Modifier: ModifierKind.Public, AttributeChain: null, GenericArguments.Count: 1, Parameters.Count: 1, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false } f &&
        f.GenericArguments[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } &&
        f.Parameters[0] is { InternalName: "value", ExternalName: "value", IsOptional: false, DefaultValue: null, AttributeChain: null } p &&
        BareName(p.Type, "T") && f.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: { } inner } && BareName(inner, "T");

    private bool Valid(BindingSymbol symbol, int index)
        => index < this.Kotonoha.RootKoto.NestedContainers.Count && ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[index], symbol.Declaration) &&
        symbol.Intrinsic == (index == 6 ? IntrinsicKind.Sealed : (IntrinsicKind)(index + 1)) && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Members.Count: 0, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == symbol.Name && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto);
}

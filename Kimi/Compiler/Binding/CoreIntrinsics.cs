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
}

/// <summary>Identifies a compiler-provided language function implementation.</summary>
public enum CompilerFunctionKind : byte
{
    None,
    WriteLine,
}

/// <summary>The compiler-owned Core identities. This is not the complete runtime Core library.</summary>
public sealed class CoreIntrinsics
{
    private readonly CoreDeclaration[] declarations =
    [
        new(CoreDeclarationId.Copy, "Copy", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Owned, "Owned", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Callable, "Callable", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.WriteLine, "writeLine", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Option, "Option", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Result, "Result", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Array, "Array", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Index, "Index", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Range, "Range", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.ResolvedRange, "ResolvedRange", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Slice, "Slice", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Dictionary, "Dictionary", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Stringify, "Stringify", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Equatable, "Equatable", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Comparable, "Comparable", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Iterator, "Iterator", null, CoreDeclarationState.Missing),
        new(CoreDeclarationId.Iterable, "Iterable", null, CoreDeclarationState.Missing),
        // The specification defers the public spellings of this operation family.
        new(CoreDeclarationId.ObjectOwnership, string.Empty, null, CoreDeclarationState.Missing),
    ];

    internal CoreIntrinsics(Compilation compilation)
    {
        this.Kotonoha = new(compilation, "Core", "compiler://Core/" + Compilation.CurrentLanguageVersion);
        var context = new TokenContext(null, ModifierKind.Public, false);
        this.Scope = new(this.Kotonoha.RootKoto);
        this.Module = new("Core", BindingSymbolKind.Container, this.Kotonoha.RootKoto, this.Scope);
        for (var i = 0; i < 3; i++)
        {
            this.Kotonoha.RootKoto.GetOrAddGroup(this.declarations[i].Name, TokenKind.Contract, context, default);
            this.declarations[i] = this.declarations[i] with { Symbol = this.Create(i, (IntrinsicKind)(i + 1)) };
        }

        this.WriteLine = this.CreateWriteLine();
        this.declarations[(int)CoreDeclarationId.WriteLine] = this.declarations[(int)CoreDeclarationId.WriteLine] with { Symbol = this.WriteLine };
        this.Restore();
    }

    public Kotonoha Kotonoha { get; }

    public string Version => Compilation.CurrentLanguageVersion;

    /// <summary>Gets a value indicating whether all required declarations are validated, separately from body, layout and runtime support.</summary>
    public bool IsCompleteLibrary => this.IsValid && this.ValidatedDeclarationCount == this.declarations.Length;

    /// <summary>Gets retained catalog entries. States are refreshed by each Bind.</summary>
    public ReadOnlySpan<CoreDeclaration> Declarations => this.declarations;

    public int ValidatedDeclarationCount { get; private set; }

    public BindingSymbol Module { get; }

    public BindingSymbol Copy => this.declarations[(int)CoreDeclarationId.Copy].Symbol!;

    public BindingSymbol Owned => this.declarations[(int)CoreDeclarationId.Owned].Symbol!;

    public BindingSymbol Callable => this.declarations[(int)CoreDeclarationId.Callable].Symbol!;

    public BindingSymbol WriteLine { get; }

    internal BindingScope Scope { get; }

    internal bool IsValid
    {
        get
        {
            var valid = this.Kotonoha.GeneratedFunction is null && this.Kotonoha.RootKoto.NestedContainers.Count == 3 &&
                this.Kotonoha.RootKoto.Members.Count == 1 && ReferenceEquals(this.Kotonoha.RootKoto.Members[0], this.WriteLine.Declaration);
            this.ValidatedDeclarationCount = 0;
            for (var i = 0; i < this.declarations.Length; i++)
            {
                var entry = this.declarations[i];
                var state = CoreDeclarationState.Missing;
                if (entry.Symbol is { } symbol)
                {
                    var matches = i < 3 ? this.Valid(symbol, i) : entry.Id == CoreDeclarationId.WriteLine && this.ValidWriteLine();
                    state = matches ? CoreDeclarationState.Validated : CoreDeclarationState.Invalid;
                    valid &= matches;
                    this.ValidatedDeclarationCount += matches ? 1 : 0;
                }

                this.declarations[i] = entry with { State = state };
            }

            return valid;
        }
    }

    public BindingSymbol? GetSymbol(CoreDeclarationId id) => this.declarations[(int)id].Symbol;

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

        this.Kotonoha.RootKoto.BoundSymbol = this.Module;
        this.Kotonoha.RootKoto.BindingState = BindingState.Resolved;
    }

    private BindingSymbol Create(int index, IntrinsicKind kind)
    {
        var declaration = this.Kotonoha.RootKoto.NestedContainers[index];
        var symbol = new BindingSymbol(declaration.Name, BindingSymbolKind.Type, declaration, this.Scope) { Intrinsic = kind };
        declaration.BoundSymbol = symbol;
        declaration.BindingState = BindingState.Resolved;
        return symbol;
    }

    private BindingSymbol CreateWriteLine()
    {
        // Build ordinary declaration syntax once. Its implementation identity, not a fake
        // executable body or a spelling check at calls, supplies the later lowering hook.
        var source = new SourceDocument("compiler://Core/writeLine", "string");
        var context = new CodeContext(this.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            var type = new TypeSemanticsKoto(ref reader, new Token(TokenKind.String));
            var function = new FunctionKoto(ref reader, new(null, ModifierKind.Public, false), default, "writeLine", null, [new("text", "text", false, type, null)], null);
            this.Kotonoha.RootKoto.AddLast(function);
            return new("writeLine", BindingSymbolKind.Function, function, this.Scope) { CompilerFunction = CompilerFunctionKind.WriteLine };
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    private bool ValidWriteLine()
        => this.WriteLine.CompilerFunction == CompilerFunctionKind.WriteLine && ReferenceEquals(this.WriteLine.Scope, this.Scope) &&
        this.WriteLine.Declaration is FunctionKoto function &&
        ReferenceEquals(function.Parent, this.Kotonoha.RootKoto) &&
        function.Name == "writeLine" && function.Modifier == ModifierKind.Public &&
        function.GenericArguments.Count == 0 && function.Origins.Count == 0 && function.Parameters.Count == 1 &&
        function.TypeConstraints.Count == 0 && function.ReturnType is null && function.Body is null && function.ExpressionBody is null &&
        function.AttributeChain is null && !function.IsRequirement && !function.IsGenerated && !function.IsSpecialization &&
        function.Parameters[0] is
        {
            ExternalName: "text", InternalName: "text", IsOptional: false, DefaultValue: null, AttributeChain: null,
            Type: TypeSemanticsKoto { Type: null, Identifier: "string", SemanticsKind: SemanticsKind.Owner, OriginExpression: null, OriginName: null, OriginArguments: null },
        };

    private bool Valid(BindingSymbol symbol, int index)
        => index < this.Kotonoha.RootKoto.NestedContainers.Count && ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[index], symbol.Declaration) &&
        symbol.Intrinsic == (IntrinsicKind)(index + 1) && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Members.Count: 0, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == symbol.Name && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto);
}

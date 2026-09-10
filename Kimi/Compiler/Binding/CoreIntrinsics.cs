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

/// <summary>The compiler-owned Core requirement identities. This is not the complete runtime Core library.</summary>
public sealed class CoreIntrinsics
{
    internal CoreIntrinsics(Compilation compilation)
    {
        this.Kotonoha = new(compilation, "Core", "compiler://Core/" + Compilation.CurrentLanguageVersion);
        var context = new TokenContext(null, ModifierKind.Public, false);
        this.Kotonoha.RootKoto.GetOrAddGroup("Copy", TokenKind.Contract, context, default);
        this.Kotonoha.RootKoto.GetOrAddGroup("Owned", TokenKind.Contract, context, default);
        this.Kotonoha.RootKoto.GetOrAddGroup("Callable", TokenKind.Contract, context, default);
        this.Scope = new(this.Kotonoha.RootKoto);
        this.Module = new("Core", BindingSymbolKind.Container, this.Kotonoha.RootKoto, this.Scope);
        this.Copy = this.Create(0, IntrinsicKind.Copy);
        this.Owned = this.Create(1, IntrinsicKind.Owned);
        this.Callable = this.Create(2, IntrinsicKind.Callable);
        this.Restore();
    }

    public Kotonoha Kotonoha { get; }

    public string Version => Compilation.CurrentLanguageVersion;

    public bool IsCompleteLibrary => false;

    public BindingSymbol Module { get; }

    public BindingSymbol Copy { get; }

    public BindingSymbol Owned { get; }

    public BindingSymbol Callable { get; }

    internal BindingScope Scope { get; }

    internal bool IsValid => this.Kotonoha.RootKoto.NestedContainers.Count == 3 && this.Kotonoha.RootKoto.Members.Count == 0 && this.Valid(this.Copy, 0) && this.Valid(this.Owned, 1) && this.Valid(this.Callable, 2);

    internal void Restore()
    {
        this.Scope.Reset();
        this.Scope.Types.Add(this.Copy.Name, this.Copy);
        this.Scope.Types.Add(this.Owned.Name, this.Owned);
        this.Scope.Types.Add(this.Callable.Name, this.Callable);
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

    private bool Valid(BindingSymbol symbol, int index)
        => ReferenceEquals(this.Kotonoha.RootKoto.NestedContainers[index], symbol.Declaration) && symbol.Declaration is ContractKoto { Members.Count: 0, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public } declaration && declaration.Name == symbol.Name;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

// ImplTable (R7-V2a; SPEC 8.6, 8.6.1, 8.6.2, 8.8; DECISIONS R7-M16, BU-220; PLAN G949 S1-b1): the conformance declaration table, the one
// store of a Binding's conformance identities and paths.
// - Responsibility: one identity (BoundConformance) per Type and Contract; one path (BoundConformancePath) per Type, Contract,
//   declaration and root Contract; the pass's paths, identities and each Type's identities in registration order; the lookups by key;
//   the store decision: an identity or path is held by its Type symbol's module (SPEC 8.6.1: a conformance is declared only in its
//   Type's own declaration), a library Type's in the base store (LibraryLayer.Impls), any other in the compilation's overlay, which is
//   the one composition point; the pass stamps that replace the reset; the overlay's end-of-Bind sweep and pool.
// - Inputs: Register's Type, Contract, root, declaration, scope and premises, from the two registrars (Self clauses and conditional
//   blocks, Binding.Contracts.Registration; inherited paths, Binding.InheritedConformances); Begin's pass counter and the pass's modules
//   (Binding.ResetContracts);
//   Sweep at the end of a Bind (Binding.Bind).
// - Outputs: Paths, Identities and ByType (LayerList: the overlay's part, then the base's), InPass (the same lists of the running
//   pass's stores only, PLAN G949 S1-r2), OfType(type), Find(type, contract),
//   Path(key) and TryPath(key).
// - Invariants: the overlay never holds a library Type, by construction: StoreOf routes each key by its Type's module, so no write is
//   refused here (refusing program writes to the base is S1-1's); an identity or path carries the pass of its last reset (Stamp) and
//   reads as unregistered while stale (Current); the table resets it at its first access through the table in a pass (Find, Path,
//   TryPath, Register), so no reader sees an earlier pass's state; a path is Active from its registration in the pass to the end of the
//   pass, a per-pass flag the reset clears (not "Stamp current": a lookup can reach a path before the pass registers it); a store's
//   lists are emptied at its first access in a pass; a path joins Paths and its identity's paths only at its first registration in a
//   pass, so both follow the registrars' order and a derived Type's follow its base's; Identities lists the pass's identities in the
//   order the pass first names them; Register names the root path and the root's own call registers it; each key keeps one object
//   while each pass registers it, with the facts other components keep by a path and a second key (its satellite lists); at the end of
//   each Bind the overlay drops every identity and path the pass did not register into its pool (Sweep), from which Named takes,
//   emptied, before allocating. The lists are the table's own: no reader adds to or removes from them.
// - Prohibitions: no verification (Binding.ContractMatching, Binding.InheritedConformances), availability at a use
//   (Binding.ResolveConformance), Self-clause rule or diagnostic here; no reader composes the two stores.
namespace Kimi.Compiler;

/// <summary>The conformance declaration table: one identity per Type and Contract and one path per declaring clause and root.</summary>
internal sealed class ImplTable
{
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract), BoundConformance> identities = new();
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract, IsKoto Declaration, BindingSymbol Root), BoundConformancePath> paths = new();
    private readonly List<BoundConformancePath> active = new();
    private readonly List<BoundConformance> named = new();
    private readonly List<List<BoundConformance>> typeLists = new();
    private readonly Dictionary<BindingSymbol, List<BoundConformance>> byType = new(ReferenceEqualityComparer.Instance);

    // The overlay's base store; null in the base.
    private readonly ImplTable? library;

    // A store no pass registers into, so its lists stay empty: the pass view's part for a store outside the pass (the overlay's; null in
    // the base).
    private readonly ImplTable? none;
    private Stack<BoundConformance>? identityPool;
    private Stack<BoundConformancePath>? pathPool;
    private ulong listPass;

    // The running pass's view (Begin): each store when the pass binds a module it holds, else none.
    private ImplTable? passOverlay;
    private ImplTable? passBase;

    // The base store (LibraryLayer.Impls).
    internal ImplTable()
    {
    }

    // The compilation's overlay over the library's base store, which holds the library module's Types.
    internal ImplTable(ImplTable library)
    {
        this.library = library;
        this.none = new();
    }

    /// <summary>Gets the pass this store is current for (Begin).</summary>
    internal ulong Pass { get; private set; }

    /// <summary>Gets the paths registered in this pass, the overlay's then the base's, each in registration order.</summary>
    internal LayerList<BoundConformancePath> Paths => new(this.Fresh().active, this.library!.Fresh().active);

    /// <summary>Gets the identities this pass named, the overlay's then the base's, each in the order first named; one without paths is
    /// not registered.</summary>
    internal LayerList<BoundConformance> Identities => new(this.Fresh().named, this.library!.Fresh().named);

    /// <summary>Gets each Type's identities registered in this pass, the overlay's Types then the base's, in registration order.</summary>
    internal LayerList<List<BoundConformance>> ByType => new(this.Fresh().typeLists, this.library!.Fresh().typeLists);

    /// <summary>Gets the running pass's view (PLAN G949 S1-r2): the paths, identities and Type lists of the stores of the pass's modules
    /// (Begin), each the overlay's then the base's, in registration order; a store outside the pass lists nothing.</summary>
    internal PassView InPass => new(this.passOverlay!.Fresh(), this.passBase!.Fresh());

    /// <summary>Starts a pass: every identity, path and list of both stores becomes stale, and the pass view holds the stores of the
    /// pass's modules by the store rule (StoreOf: the base for the library module, the overlay for a source module).</summary>
    /// <param name="pass">The pass counter (Binding's storageVersion).</param>
    /// <param name="modules">The running pass's modules.</param>
    internal void Begin(ulong pass, Binding.BindingPass modules)
    {
        this.Pass = this.library!.Pass = pass;
        (this.passOverlay, this.passBase) = (this.none, this.none);
        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i].Syntax.IsLibrary)
            {
                this.passBase = this.library;
            }
            else
            {
                this.passOverlay = this;
            }
        }
    }

    /// <summary>Gets a Type's identities registered in this pass, or null for a Type never registered.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <returns>The list, possibly empty.</returns>
    internal List<BoundConformance>? OfType(BindingSymbol type) => this.StoreOf(type).Fresh().byType.GetValueOrDefault(type);

    /// <summary>Gets the identity of a Type and Contract, registered in this pass or not, or null if not named.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <returns>The identity.</returns>
    internal BoundConformance? Find(BindingSymbol type, BindingSymbol contract)
    {
        var store = this.StoreOf(type);
        return store.identities.GetValueOrDefault((type, contract)) is { } identity ? store.Touch(identity) : null;
    }

    /// <summary>Gets a path named in a registration: an ancestor path of the same declaration and root.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <param name="declaration">The declaring clause.</param>
    /// <param name="root">The clause's root Contract.</param>
    /// <returns>The path.</returns>
    internal BoundConformancePath Path(BindingSymbol type, BindingSymbol contract, IsKoto declaration, BindingSymbol root)
    {
        var store = this.StoreOf(type);
        return store.Touch(store.paths[(type, contract, declaration, root)]);
    }

    /// <summary>Gets a path, registered in this pass or not, or null if not named.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <param name="declaration">The declaring clause.</param>
    /// <param name="root">The clause's root Contract.</param>
    /// <returns>The path.</returns>
    internal BoundConformancePath? TryPath(BindingSymbol type, BindingSymbol contract, IsKoto declaration, BindingSymbol root)
    {
        var store = this.StoreOf(type);
        return store.paths.GetValueOrDefault((type, contract, declaration, root)) is { } path ? store.Touch(path) : null;
    }

    /// <summary>Registers a path in this pass, in its Type's store: on its first registration it joins Paths and its identity's paths.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <param name="root">The declaring clause's root Contract.</param>
    /// <param name="use">The declaring clause.</param>
    /// <param name="scope">The scope the path's premises extend.</param>
    /// <param name="premises">A conditional block's premises, or null.</param>
    /// <returns>The path.</returns>
    internal BoundConformancePath Register(
        BindingSymbol type, BindingSymbol contract, BindingSymbol root, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        var store = this.StoreOf(type).Fresh();
        var result = store.Named(type, contract, use, root);
        if (!result.Active)
        {
            var identity = result.Identity;
            result.Active = true;
            result.InheritedFrom = null;
            result.AdditionalInheritedSources?.Clear();
            result.InheritedBase = null;
            result.DeclaredPath = null;
            result.DeclaredSelf = null;
            result.Use = use;
            result.Declaration = use;
            result.RootContract = root;
            // The root path is named here and registered by its own call, so the lists follow the caller's order whichever path comes
            // first: an inherited base's order, where a refinement may follow its parent Contract's own conformance.
            result.RootPath = ReferenceEquals(contract, root) ? result : store.Named(type, root, use, root);
            result.Premises = premises;
            result.Scope.Parent = scope;
            store.active.Add(result);
            if (identity.PathStorage.Count == 0)
            {
                if (!store.byType.TryGetValue(type, out var list))
                {
                    store.byType.Add(type, list = new());
                }

                if (list.Count == 0)
                {
                    store.typeLists.Add(list);
                }

                list.Add(identity);
            }

            identity.PathStorage.Add(result);
        }

        return result;
    }

    /// <summary>Ends a Bind in the overlay: every identity and path this pass did not register leaves the table for the pool, so no
    /// conformance an edit removed is kept, and Identities keeps only registered ones.</summary>
    internal void Sweep()
    {
        if (this.Fresh().active.Count == this.paths.Count)
        {
            return; // Every path, so every identity, is registered: a warm pass.
        }

        foreach (var (key, path) in this.paths)
        {
            if (!path.Current || !path.Active)
            {
                this.paths.Remove(key);
                (this.pathPool ??= new()).Push(path);
            }
        }

        foreach (var (key, identity) in this.identities)
        {
            if (identity.Paths.Count == 0)
            {
                this.identities.Remove(key);
                (this.identityPool ??= new()).Push(identity);
            }
        }

        var kept = 0;
        for (var i = 0; i < this.named.Count; i++)
        {
            if (this.named[i].PathStorage.Count != 0)
            {
                this.named[kept++] = this.named[i];
            }
        }

        this.named.RemoveRange(kept, this.named.Count - kept);
    }

    // SPEC 8.6.1: the store of a Type's identities and paths is its declaration's module's, the base for a library Type.
    private ImplTable StoreOf(BindingSymbol type)
        => this.library is { } layer && type.Declaration.Kotonoha.Syntax.IsLibrary ? layer : this;

    // The store's lists are the pass's: emptied at the store's first access in a pass.
    private ImplTable Fresh()
    {
        if (this.listPass != this.Pass)
        {
            this.listPass = this.Pass;
            this.active.Clear();
            this.named.Clear();
            foreach (var list in this.typeLists)
            {
                list.Clear();
            }

            this.typeLists.Clear();
        }

        return this;
    }

    private BoundConformance Touch(BoundConformance identity)
    {
        if (identity.Stamp != this.Pass)
        {
            identity.ResetPass(this.Pass);
        }

        return identity;
    }

    private BoundConformancePath Touch(BoundConformancePath path)
    {
        if (path.Stamp != this.Pass)
        {
            path.ResetPass(this.Pass);
        }

        return path;
    }

    // One path per Type, Contract, declaration and root Contract, created when first named or taken from the pool emptied; only its
    // registration activates it. An identity joins the pass's Identities when the pass first names it.
    private BoundConformancePath Named(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingSymbol root)
    {
        if (!this.identities.TryGetValue((type, contract), out var identity))
        {
            identity = this.identityPool is { Count: > 0 } spareIdentities ? spareIdentities.Pop().Reuse(type, contract) : new(type, contract, this);
            this.identities.Add((type, contract), identity);
        }

        if (!this.Touch(identity).Listed)
        {
            identity.Listed = true;
            this.named.Add(identity);
        }

        if (!this.paths.TryGetValue((type, contract, use, root), out var result))
        {
            result = this.pathPool is { Count: > 0 } sparePaths ? sparePaths.Pop().Reuse(type, contract, identity) : new(type, contract, this) { Identity = identity };
            this.paths.Add((type, contract, use, root), result);
        }

        return this.Touch(result);
    }

    /// <summary>The running pass's view of the table (InPass): its stores' lists, the overlay's then the base's.</summary>
    internal readonly struct PassView(ImplTable overlay, ImplTable layer)
    {
        /// <summary>Gets the pass's paths, in registration order.</summary>
        internal LayerList<BoundConformancePath> Paths => new(overlay.active, layer.active);

        /// <summary>Gets the pass's identities, in the order first named.</summary>
        internal LayerList<BoundConformance> Identities => new(overlay.named, layer.named);

        /// <summary>Gets each Type's identities the pass registered, in registration order.</summary>
        internal LayerList<List<BoundConformance>> ByType => new(overlay.typeLists, layer.typeLists);
    }
}

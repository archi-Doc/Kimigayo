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
//   Path(key) and TryPath(key); PathsOf(identity), the one read of an identity's paths inside a pass.
// - Invariants: the overlay never holds a library Type, by construction: StoreOf routes each key by its Type's module; from the library's
//   freeze (Freeze, PLAN G949 S1-1) the base is never begun or registered into again: its Begin and a Register routed to it throw the
//   internal-invariant form (EnsureWritable, InternalInvariant_Kd); an identity or path carries the pass of its last reset (Stamp) and
//   reads as unregistered while stale (Current); the table resets it at its first access through the table in a pass (Find, Path,
//   TryPath, Register), so no reader sees an earlier pass's state; a path is Active from its registration in the pass to the end of the
//   pass, a per-pass flag the reset clears (not "Stamp current": a lookup can reach a path before the pass registers it); a store's lists
//   are emptied at its first access in a pass; a path joins Paths and its identity's paths only at its first registration in a pass, so
//   both follow the registrars' order and a derived Type's follow its base's; Identities lists the pass's identities in the order the
//   pass first names them; Register names the root path and the root's own call registers it; each key keeps one object while each pass
//   registers it, with the facts other components keep by a path and a second key (its satellite lists); at the end of each Bind the
//   overlay drops every identity and path the pass did not register into its pool (Sweep), from which Named takes, emptied, before
//   allocating. The lists are the table's own: no reader adds to or removes from them.
// - Rule (a) (PLAN G949 S1-r1b; DECISIONS BU-220 as amended by BU-219): a pass that does not bind the library reads a base row as
//   registered only from its own registrar step that registers it: a path and its identity from the Self-clause registrar
//   (AdmissionPoint.SelfClauses) or the conditional registrar (ConditionalClauses), a path's associated bindings from
//   PrepareAssociatedBindings (AssociatedBindings). The step is the overlay's (Begin resets it, Advance moves it); the base keeps each
//   row's point, set by the pass that binds the library at the row's registration, and its lists' prefixes at each point. The lookups
//   apply it (OfType, Find, TryPath, Path, PathsOf, the composed lists' base parts): a base row before its point is not found, a Type's
//   base list is its list as it stood at the step, an identity's paths are its paths of the points reached. No base field refers to an
//   overlay; a pass that does not bind the library neither begins nor resets the base. A pass that binds the library reads every row
//   it registered (the library pass). Premise: the base keeps one earlier snapshot per identity (PointOnePaths) and per Type list
//   (pointOneLists), exact while every base row registers at point 1 or 2. The inherited registrar (RegisterInheritedConformances)
//   registers later (point 3, and again at the declarations' end), which no base row reaches while the library declares no struct
//   with a base (KimiLibraryShapeTest.TheLibraryDeclaresNoStructWithABase, the tripwire; the unit that declares one builds per-point
//   prefixes).
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

    // An identity's paths while it reads as unregistered (PathsOf), and a base Type's identities before point 1 (OfType); never written.
    private readonly List<BoundConformancePath> noPaths = new();
    private readonly List<BoundConformance> noIdentities = new();

    // Rule (a), the base's (written only by the pass that binds the library): each list's length at each point that pass reached
    // (Advance), and each Type list a later point extends, as it stood at point 1 (its lists reused).
    private readonly int[] activeAt = new int[4];
    private readonly int[] namedAt = new int[4];
    private readonly int[] typeListsAt = new int[4];
    private readonly Dictionary<BindingSymbol, List<BoundConformance>> pointOneLists = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<List<BoundConformance>> pointOnePool = new();

    private Stack<BoundConformance>? identityPool;
    private Stack<BoundConformancePath>? pathPool;
    private ulong listPass;

    // The running pass's view (Begin): each store when the pass binds a module it holds, else none.
    private ImplTable? passOverlay;
    private ImplTable? passBase;

    // Rule (a), the overlay's: the running pass's registrar step (Advance) and whether the pass binds the library module.
    private AdmissionPoint step;
    private bool holdsBase;

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

    /// <summary>The registrar steps of a pass, in order (rule (a)): a base row registered by the library pass's step is read by another pass
    /// from the same step on.</summary>
    internal enum AdmissionPoint : byte
    {
        /// <summary>The pass's start: no base row is admitted.</summary>
        PassStart,

        /// <summary>The Self-clause registrar ran (RegisterConformances): direct and refinement paths, their identities, the opt-outs.</summary>
        SelfClauses,

        /// <summary>The conditional registrar ran (BindConditionalDeclarations): conditional paths and the ancestors they register.</summary>
        ConditionalClauses,

        /// <summary>PrepareAssociatedBindings ran: the paths' associated bindings.</summary>
        AssociatedBindings,
    }

    /// <summary>Gets the pass this store is current for (Begin).</summary>
    internal ulong Pass { get; private set; }

    /// <summary>Gets a value indicating whether this is the base store (LibraryLayer.Impls), which has no base of its own.</summary>
    internal bool IsBase => this.library is null;

    /// <summary>Gets the paths registered in this pass, the overlay's then the base's, each in registration order.</summary>
    internal LayerList<BoundConformancePath> Paths => new(this.Fresh().active, this.library!.Fresh().active, this.Admitted(this.library.activeAt));

    /// <summary>Gets the identities this pass named, the overlay's then the base's, each in the order first named; one without paths is
    /// not registered.</summary>
    internal LayerList<BoundConformance> Identities => new(this.Fresh().named, this.library!.Fresh().named, this.Admitted(this.library.namedAt));

    /// <summary>Gets each Type's identities registered in this pass, the overlay's Types then the base's, in registration order.</summary>
    internal LayerList<List<BoundConformance>> ByType => new(this.Fresh().typeLists, this.library!.Fresh().typeLists, this.Admitted(this.library.typeListsAt));

    /// <summary>Gets the running pass's view (PLAN G949 S1-r2): the paths, identities and Type lists of the stores of the pass's modules
    /// (Begin), each the overlay's then the base's, in registration order; a store outside the pass lists nothing.</summary>
    internal PassView InPass => new(this.passOverlay!.Fresh(), this.passBase!.Fresh());

    /// <summary>Starts a pass: every identity, path and list of both stores becomes stale, and the pass view holds the stores of the
    /// pass's modules by the store rule (StoreOf: the base for the library module, the overlay for a source module).</summary>
    /// <param name="pass">The pass counter (Binding's storageVersion).</param>
    /// <param name="modules">The running pass's modules.</param>
    internal void Begin(ulong pass, Binding.BindingPass modules)
    {
        this.Pass = pass;
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

        // Rule (a): only the pass that binds the library begins the base (its stamps and its points); another pass reads it by its step.
        this.holdsBase = ReferenceEquals(this.passBase, this.library);
        this.step = AdmissionPoint.PassStart;
        if (this.holdsBase)
        {
            this.library!.EnsureWritable(nameof(this.Begin)); // Its Begin would clear the base's admission points (ruling 203 (3b)).
            this.library.Pass = pass;
            this.library.ClearPoints();
        }
    }

    /// <summary>Records that the running pass's registrar step reached a point (rule (a)): from here a pass that does not bind the library
    /// reads the base rows of that point; the pass that binds it records the base lists' lengths at the point.</summary>
    /// <param name="point">The point reached.</param>
    internal void Advance(AdmissionPoint point)
    {
        this.step = point;
        if (this.holdsBase)
        {
            var store = this.library!.Fresh();
            (store.activeAt[(int)point], store.namedAt[(int)point], store.typeListsAt[(int)point]) = (store.active.Count, store.named.Count, store.typeLists.Count);
        }
    }

    /// <summary>Gets whether the running pass's registrar step reached a point (point 1: the pass's conformance declarations registered).</summary>
    /// <param name="point">The point.</param>
    /// <returns>Whether the step reached it.</returns>
    internal bool Reached(AdmissionPoint point) => this.step >= point;

    /// <summary>Gets whether the running pass reads a Type's rows of a point as registered (rule (a)): a program Type's always, a library
    /// Type's in the pass that binds the library and in another pass from that point on (the ObjectPayload opt-out: point 1).</summary>
    /// <param name="type">The Type declaration.</param>
    /// <param name="point">The point of the rows.</param>
    /// <returns>Whether they are admitted.</returns>
    internal bool Admits(BindingSymbol type, AdmissionPoint point) => this.holdsBase || !ReferenceEquals(this.StoreOf(type), this.library) || this.step >= point;

    /// <summary>Gets a Type's identities registered in this pass, or null for a Type never registered.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <returns>The list, possibly empty.</returns>
    internal List<BoundConformance>? OfType(BindingSymbol type)
    {
        var store = this.StoreOf(type);
        var list = store.Fresh().byType.GetValueOrDefault(type);
        return list is null || this.Admits(type, AdmissionPoint.ConditionalClauses) ? list
            : this.step == AdmissionPoint.PassStart ? store.noIdentities : store.pointOneLists.GetValueOrDefault(type, list);
    }

    /// <summary>Gets an identity's paths as the running pass reads them: its paths while it is current, none while it is stale. Binding reads an
    /// identity's paths only here (the public Paths is for readers after the Bind).</summary>
    /// <param name="identity">An identity this table handed out.</param>
    /// <returns>The paths, in registration order; the list is the table's own, never written by a reader.</returns>
    internal List<BoundConformancePath> PathsOf(BoundConformance identity)
        => !identity.Current || !this.Admits(identity.Type, identity.Point) ? this.noPaths
            : this.Admits(identity.Type, identity.LastPoint) ? identity.PathStorage : identity.PointOnePaths!;

    /// <summary>Gets the identity of a Type and Contract, registered in this pass or not, or null if not named.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <returns>The identity.</returns>
    internal BoundConformance? Find(BindingSymbol type, BindingSymbol contract)
    {
        var store = this.StoreOf(type);
        return store.identities.GetValueOrDefault((type, contract)) is { } identity ? this.Read(store, identity) : null;
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
        var path = store.paths[(type, contract, declaration, root)];
        return this.Read(store, path)
            ?? throw new InvalidOperationException($"A registration named the path of `{type.Name}` to `{contract.Name}` before its registrar step (InternalInvariant; PLAN G949 S1-r1b rule (a)).");
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
        return store.paths.GetValueOrDefault((type, contract, declaration, root)) is { } path ? this.Read(store, path) : null;
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
        var store = this.StoreOf(type);
        store.EnsureWritable(nameof(this.Register));
        var result = store.Fresh().Named(type, contract, use, root);
        if (!result.Active)
        {
            var identity = result.Identity;
            var point = this.step + 1; // The registrar step that ends at the next point (rule (a)).
            result.Active = true;
            result.Point = point;
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

                if (point > AdmissionPoint.SelfClauses && !ReferenceEquals(store, this))
                {
                    store.KeepPointOne(type, list);
                }

                list.Add(identity);
            }

            identity.AddPath(result, point);
        }

        return result;
    }

    /// <summary>Freezes the base store (PLAN G949 S1-1's freeze, after the library pass): its pass, its lists' pass and every identity and
    /// path the pass made current turn Frozen, so they read current and untouched in every later pass (no pass begins the base again);
    /// a row an earlier pass left stale stays stale.</summary>
    internal void Freeze()
    {
        foreach (var identity in this.identities.Values)
        {
            if (identity.Current)
            {
                identity.Freeze();
            }
        }

        foreach (var path in this.paths.Values)
        {
            if (path.Current)
            {
                path.Freeze();
            }
        }

        (this.Pass, this.listPass) = (Binding.Frozen, this.listPass == this.Pass ? Binding.Frozen : this.listPass);
    }

    /// <summary>Gets the first path the pass made current and left unsettled: not verified, or at its OCC-X limit (Unsupported); a path
    /// whose verification is still open is the obligation table's (Binding.Verifying), which the freeze asks too. The library's freeze
    /// refuses one (Binding.FreezeLibrary; PLAN G949 Stage 2 U3, design R3.7 C1): a frozen path stops verification at its IsVerified
    /// return, so no reading compilation verifies one on the shared object.</summary>
    /// <returns>The path, or null when every current path is verified.</returns>
    internal BoundConformancePath? UnsettledPath()
    {
        foreach (var path in this.paths.Values)
        {
            if (path.Current && (!path.IsVerified || path.Unsupported))
            {
                return path;
            }
        }

        return null;
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

    // PLAN G949 S1-1 (s11-design 3.1): a frozen store (Freeze) is never begun or registered into again; such a write is the
    // internal-invariant form. Its other writers (Fresh, Touch, Named, Sweep) are reached only from Begin and Register, or write nothing
    // on a frozen store (its stamps equal the pass), or run on the overlay alone (Sweep).
    private void EnsureWritable(string writer)
    {
        if (this.Pass == Binding.Frozen)
        {
            throw new InvalidOperationException($"{writer} wrote the frozen base store (InternalInvariant; PLAN G949 S1-1).");
        }
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

    // A row of `store` as the running pass reads it: the pass that holds the store resets a stale row at its first access in the pass
    // (Touch); a pass that does not bind the library reads a base row as the library pass left it, from its point on (rule (a)).
    private BoundConformance? Read(ImplTable store, BoundConformance identity)
        => ReferenceEquals(store, this) || this.holdsBase ? store.Touch(identity) : this.step >= identity.Point && identity.Current ? identity : null;

    private BoundConformancePath? Read(ImplTable store, BoundConformancePath path)
        => ReferenceEquals(store, this) || this.holdsBase ? store.Touch(path) : this.step >= path.Point && path.Current ? path : null;

    // The base keeps a Type's list as it stood at point 1 when a later point's registration extends it (rule (a): OfType at point 1).
    private void KeepPointOne(BindingSymbol type, List<BoundConformance> list)
    {
        if (!this.pointOneLists.ContainsKey(type))
        {
            var kept = this.pointOnePool.Count != 0 ? this.pointOnePool.Pop() : new();
            kept.AddRange(list);
            this.pointOneLists.Add(type, kept);
        }
    }

    // The base's points begin again with the pass that binds the library.
    private void ClearPoints()
    {
        foreach (var list in this.pointOneLists.Values)
        {
            list.Clear();
            this.pointOnePool.Push(list);
        }

        this.pointOneLists.Clear();
    }

    // The base part of a composed list the running pass reads: all of it in the pass that binds the library, else its prefix at the step.
    private int Admitted(int[] at) => this.holdsBase ? int.MaxValue : at[(int)this.step];

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

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

// ImplTable (R7-V2a; SPEC 8.6, 8.6.1, 8.6.2, 8.8; DECISIONS R7-M16): the conformance declaration table, the one store of a Binding's
// conformance identities and paths.
// - Responsibility: one identity (BoundConformance) per Type and Contract; one path (BoundConformancePath) per Type, Contract,
//   declaration and root Contract; the pass's paths and each Type's identities in registration order; the lookups by key; the reset
//   at each pass, which also empties the state other components write on its identities and paths (the verification flags, witnesses
//   and their maps, associated and inference storage, Needed, RejectedSelfSignature, the paths' scopes) until R7-D2b's stamped tables.
// - Inputs: Register's Type, Contract, root, declaration, scope and premises, from the two registrars (Self clauses and conditional
//   blocks, Binding.Contracts.Registration; inherited paths, Binding.InheritedConformances).
// - Outputs: Paths (the pass's paths), Identities (every identity ever named), ByType and OfType(type) (the pass's identities of a
//   Type), Find(type, contract), Path(key) and TryPath(key).
// - Invariants: a path joins Paths and its identity's paths only at its first registration in a pass, so both follow the registrars'
//   order and a derived Type's follow its base's; Register names the root path and the root's own call registers it; each key keeps
//   one object across binds; Reset returns every path and identity to unregistered. The lists are the table's own: no reader adds to
//   or removes from them (they stay Lists so that a reader's foreach allocates nothing).
// - Prohibitions: no verification (Binding.ContractMatching, Binding.InheritedConformances), availability at a use
//   (Binding.ResolveConformance), Self-clause rule or diagnostic here.
namespace Kimi.Compiler;

/// <summary>The conformance declaration table: one identity per Type and Contract and one path per declaring clause and root.</summary>
internal sealed class ImplTable
{
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract), BoundConformance> identities = new();
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract, IsKoto Declaration, BindingSymbol Root), BoundConformancePath> paths = new();
    private readonly List<BoundConformancePath> active = new();
    private readonly Dictionary<BindingSymbol, List<BoundConformance>> byType = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the paths registered in this pass, in registration order.</summary>
    internal List<BoundConformancePath> Paths => this.active;

    /// <summary>Gets every identity ever named, in the order first named; one without paths is not registered in this pass.</summary>
    internal Dictionary<(BindingSymbol Type, BindingSymbol Contract), BoundConformance>.ValueCollection Identities => this.identities.Values;

    /// <summary>Gets each Type's identities registered in this pass, in registration order; a list may be empty.</summary>
    internal Dictionary<BindingSymbol, List<BoundConformance>>.ValueCollection ByType => this.byType.Values;

    /// <summary>Gets a Type's identities registered in this pass, or null for a Type never registered.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <returns>The list, possibly empty.</returns>
    internal List<BoundConformance>? OfType(BindingSymbol type) => this.byType.GetValueOrDefault(type);

    /// <summary>Gets the identity of a Type and Contract, registered in this pass or not, or null if never named.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <returns>The identity.</returns>
    internal BoundConformance? Find(BindingSymbol type, BindingSymbol contract) => this.identities.GetValueOrDefault((type, contract));

    /// <summary>Gets a path named in a registration: an ancestor path of the same declaration and root.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <param name="declaration">The declaring clause.</param>
    /// <param name="root">The clause's root Contract.</param>
    /// <returns>The path.</returns>
    internal BoundConformancePath Path(BindingSymbol type, BindingSymbol contract, IsKoto declaration, BindingSymbol root)
        => this.paths[(type, contract, declaration, root)];

    /// <summary>Gets a path, registered in this pass or not, or null if never named.</summary>
    /// <param name="type">The conforming Type declaration.</param>
    /// <param name="contract">The Contract identity.</param>
    /// <param name="declaration">The declaring clause.</param>
    /// <param name="root">The clause's root Contract.</param>
    /// <returns>The path.</returns>
    internal BoundConformancePath? TryPath(BindingSymbol type, BindingSymbol contract, IsKoto declaration, BindingSymbol root)
        => this.paths.GetValueOrDefault((type, contract, declaration, root));

    /// <summary>Registers a path in this pass: on its first registration it joins Paths and its identity's paths.</summary>
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
        var result = this.Named(type, contract, use, root);
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
            result.RootPath = ReferenceEquals(contract, root) ? result : this.Named(type, root, use, root);
            result.Premises = premises;
            result.Scope.Parent = scope;
            this.active.Add(result);
            if (identity.PathStorage.Count == 0)
            {
                if (!this.byType.TryGetValue(type, out var list))
                {
                    this.byType.Add(type, list = new());
                }

                list.Add(identity);
            }

            identity.PathStorage.Add(result);
        }

        return result;
    }

    /// <summary>Returns every path and identity to unregistered at the start of a pass.</summary>
    internal void Reset()
    {
        this.active.Clear();
        foreach (var list in this.byType.Values)
        {
            list.Clear();
        }

        foreach (var conformance in this.identities.Values)
        {
            conformance.IsVerified = false;
            conformance.Invalid = false;
            conformance.DirectClause = null;
            conformance.PathStorage.Clear();
        }

        foreach (var conformance in this.paths.Values)
        {
            conformance.Active = false;
            conformance.IsVerified = false;
            conformance.Checking = false;
            conformance.RejectedSelfSignature = null;
            conformance.Invalid = false;
            conformance.WitnessStorage.Clear();
            conformance.WitnessMap.Clear();
            conformance.PropertyWitnessStorage.Clear();
            conformance.PropertyWitnessMap.Clear();
            conformance.Unsupported = false;
            conformance.Needed = false;
            conformance.AssociatedStorage.Clear();
            conformance.InferenceStorage.Clear();
            conformance.Scope.Reset();
        }
    }

    // One path per Type, Contract, declaration and root Contract, created when first named; only its registration activates it.
    private BoundConformancePath Named(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingSymbol root)
    {
        if (!this.identities.TryGetValue((type, contract), out var identity))
        {
            this.identities.Add((type, contract), identity = new(type, contract));
        }

        if (!this.paths.TryGetValue((type, contract, use, root), out var result))
        {
            this.paths.Add((type, contract, use, root), result = new(type, contract) { Identity = identity });
        }

        return result;
    }
}

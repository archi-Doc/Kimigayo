// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G949 S1-1 (the library pass once per compilation; G949\s11\plan.md, section 3): what the library pass makes for S1-1's freeze. From
// S1-1 the library pass runs once and every later Bind is a program pass, so a fact a library object holds is made by the library pass
// itself, never on demand by a program pass; these rows read the library's facts after the empty program's Final Bind.
public partial class KimiLibraryShapeTest
{
    // k3 (E4's tripwire; F's ruling 167, Q3): no library conformance path has a standard Property witness (SPEC 11.9.2: a stored Field
    // witnessing a Property requirement, which a bridge accessor executes), so no program can make a witness bridge on a library path
    // (PropertyWitnessFunction makes one per path and witness) and S1-1 needs no eager bridge step: every library path's Property
    // witnesses are accessor calls (95 verified paths today). The unit that gives a library Type the first standard witness removes this
    // Fact and adds the eager step over the base paths, returning a library bridge without its refresh (s11-design section 4, E4).
    [Fact]
    public void TheLibraryDeclaresNoStandardPropertyWitness()
    {
        var c = Bound(string.Empty);
        var paths = DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? [])
            .SelectMany(static x => x.Paths)
            .ToArray();
        Assert.NotEmpty(paths);
        Assert.Empty(paths.SelectMany(static x => x.PropertyWitnesses.Where(static w => w.Kind != PropertyWitnessKind.AccessorCall).Select(w => $"{x.Type.Name} is {x.Contract.Name}: {w.Kind}")));
    }

    // k4 (B1: E3's effect bounds, E7, R): the library pass completes the facts a later pass would otherwise form lazily on a library
    // object, so a program pass reads them and writes none: every bound Contract reference of the layer's part has its effect bounds
    // (EffectState 2), every library scope, and every scope of a library path (its own, its requirements' premises, its overlaps'),
    // denotes its current view (SerialOf would write none), and every local region of the layer's part has its sources formed. Before B1,
    // after the empty program's Bind, 4 of 5 bound references had no effect bounds, 595 of 1,187 library scopes and 123 of 171 path
    // scopes a view SerialOf would write, and all 19 regions had no sources (Version -1).
    [Fact]
    public void TheLibraryPassLeavesNoLazyFactOnTheLibrary()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer.Part;
        Assert.NotEmpty(layer.BoundContracts);
        Assert.Empty(layer.BoundContracts.Values.Where(static x => x.Contract!.EffectState != 2).Select(static x => x.Type!.ToString()));
        var scopes = new List<BindingScope>();
        foreach (var scope in c.Library.Kotonoha.Syntax.Hir.Families.Scopes.Enumerate())
        {
            if (scope is not null)
            {
                scopes.Add(scope);
            }
        }

        foreach (var path in DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? []).SelectMany(static x => x.Paths))
        {
            scopes.Add(path.Scope);
            scopes.AddRange((path.WitnessScopes ?? []).Select(static x => x.Premises));
            scopes.AddRange((path.OverlapScopes ?? []).Select(static x => x.Scope));
        }

        Assert.Empty(scopes.Where(static x => State(x) != x.ViewState).Select(static x => x.Owner?.ToString()));
        Assert.NotEmpty(layer.Regions);
        Assert.Empty(layer.Regions.Where(static x => x.Value.Version == -1).Select(static x => x.Key.ToString()));

        // The chain state SerialOf compares with a scope's ViewState, read without writing (ContractSolver.SerialOf).
        static (int, int, int) State(BindingScope scope) => (scope.Constraints?.Version ?? 0, scope.LinkVersion, scope.Parent is { } parent ? Serial(parent) : 0);

        static int Serial(BindingScope scope) => State(scope) != scope.ViewState ? scope.ViewSerial + 1 : scope.ViewSerial;
    }
}

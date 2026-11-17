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
}

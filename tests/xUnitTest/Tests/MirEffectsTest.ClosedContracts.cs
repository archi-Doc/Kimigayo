// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.7.5 (PLAN G666): every implementation of a closed Contract reads only its boundaries and the length and has no other effect,
// which Binding.DispatchBounds trusts for a requirement call on an abstract Self (ClosedContractRequirementsHaveNoEffect), read from A4's
// items of each implementation's own body.
public sealed partial class MirEffectsTest
{
    // Each library conformance's witness of a closed Contract's own requirement, and the witness table's integer group function with every
    // specialization (TyCtxt.Implementation without a substitution), named by its `self` Type: in every view no class, and no atom but a
    // destruction its premises prove Copy, which does nothing (SPEC 16.3.3; A4 resolves it so at the top, AddAtom).
    [Fact]
    public void ClosedContractImplementationsHaveNoEffect()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var (effects, _, program) = Analyzed(c);
        var (implementations, targets) = (new List<BindingSymbol>(), new List<(BindingSymbol Target, Subst Instance)>());
        foreach (var id in (KimiDeclarationId[])[KimiDeclarationId.Position, KimiDeclarationId.PositionRange])
        {
            var contract = c.Library.GetSymbol(id)!;
            Conformers(c.Library.Kotonoha.RootKoto, contract);
            foreach (var integer in BoundType.Primitives.Values.Where(static x => x.IsInteger))
            {
                _ = c.Types.Witnesses(contract.Contract!.OwnRequirement()!, integer, contract, null, null, targets);
            }
        }

        foreach (var target in targets.Select(static x => x.Target).Distinct())
        {
            for (var i = 0; c.Types.Implementation(target, null, i) is { } implementation; i++)
            {
                implementations.Add(implementation);
            }
        }

        var effectful = new List<string>();
        foreach (var implementation in implementations)
        {
            var body = Assert.IsType<MirBody>(program.Callee(new(MirCalleeKind.Fn, implementation)));
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                var atoms = effects.Items(body, view, out var classes).ToArray();
                if (classes != 0 || !atoms.All(x => x.Kind == MirEffects.Drop && c.Types.Copies(implementation).Copy(x.Type)))
                {
                    effectful.Add($"{Self(implementation)} view {view}: classes {classes}, atoms {atoms.Length}");
                }
            }
        }

        Assert.Equal(
            ["Kimi.FromEnd<T>", "Kimi.Start", "Kimi.End", "Kimi.Range<S, E>", "Kimi.ClosedRange<S, E>", "Kimi.ResolvedRange", "I", "isize", "i32", "usize"],
            implementations.Select(Self));
        Assert.Empty(effectful);

        void Conformers(DeclarationContainerKoto container, BindingSymbol contract)
        {
            foreach (var nested in container.NestedContainers)
            {
                var witnesses = (nested.SymbolOf() is { } type ? c.Semantics.Conformances(type) ?? [] : []).SelectMany(static x => x.Witnesses);
                implementations.AddRange(witnesses.Where(x => ReferenceEquals(x.Identity.Contract, contract)).Select(static x => x.Implementation));
                Conformers(nested, contract);
            }
        }

        static string Self(BindingSymbol function) => TyCtxt.Name(TyCtxt.Signature(function, null, 0, out _)!);
    }
}

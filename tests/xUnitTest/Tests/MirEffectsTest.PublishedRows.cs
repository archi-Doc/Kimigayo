// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// SPEC 4.9.6 (R6-check H4, DECISIONS R4-M167): a published operation's row is its complete user-code effect, which callers compose and
// the library implementation stays within, read from A4's items of the operation's own body.
public sealed partial class MirEffectsTest
{
    // R4-M167, PLAN G400: the one item outside its row. appendAll pops every element of the source it consumes before the source's drop,
    // which destroys no element; A2 does not see the emptiness, so A4 keeps T's destruction. Removal: A2 proves that a drop of an Array
    // emptied by pops destroys no element, or the body is rewritten so that its items stay within None.
    private const string AppendAllDropsItsEmptiedSource = "appendAll: Drop T";

    // SPEC 4.9.7 (PLAN G126): destroyValues is a destruction of its Type argument in A4, the item clear's row covers.
    [Fact]
    public void DestroyValuesIsADestructionOfItsTypeArgument()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var (effects, _, program) = Analyzed(c);
        var clear = program.Callee(new(MirCalleeKind.Fn, c.Library.GetSymbol(KimiDeclarationId.ArrayClear)!))!;
        Assert.Contains(effects.Items(clear, 0, out _).ToArray(), static x => x.Kind == MirEffects.Drop && x.Type.Name == "T");
    }

    // SPEC 4.9.6: each Kimi body with a row stays within it in A4's items, with abstract Type parameters: an atom is the row's requirement
    // on the Type below its parameter, a shared call through its Callable input, or a destruction the row's target or Callable input
    // reaches or the premises prove Copy (as A4 resolves an atom at the top); an Unknown only a call of the row's requirement on that Type
    // (A4 judges a direct requirement call by the premises, SPEC 8.11.3), in the body or a body without a row it enters; no other class.
    // A linked constructor's row is its implementation's.
    [Fact]
    public void EachRowOperationsBodyStaysWithinItsRow()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var (effects, flow, program) = Analyzed(c);
        var (outside, bodies) = (new List<string>(), 0);
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            var operation = c.Library.GetSymbol(entry.Id)!;
            if (KimiLibraryCatalog.Row(operation) is null)
            {
                continue;
            }

            if (entry.Implementation is { } implementation)
            {
                Assert.Same(c.Library.GetSymbol(implementation), c.Library.ConstructorImplementation(operation));
            }
            else if (program.Callee(new(MirCalleeKind.Fn, operation)) is { } body)
            {
                Check(entry.Name, operation, body);
                bodies++;
            }
        }

        Assert.Equal([AppendAllDropsItsEmptiedSource], outside);
        Assert.Equal(30, bodies);

        void Check(string name, BindingSymbol operation, MirBody body)
        {
            var row = c.Types.Published(operation)!.Value;
            var (on, calls, reached) = (Below(operation, row.On), Below(operation, row.Calls), new HashSet<BoundType>(ReferenceEqualityComparer.Instance));
            var walk = new Reached(reached);
            MirTypes.Destruction(Below(operation, row.Destroys), ref walk);
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                foreach (var atom in effects.Items(body, view, out _))
                {
                    if (!(atom.Kind == MirEffects.Requires ? ReferenceEquals(atom.Requirement, row.Runs) && ReferenceEquals(atom.Type, on)
                        : atom.Kind == MirEffects.Drop ? c.Types.Copies(operation).Copy(atom.Type) || ReferenceEquals(atom.Type, calls) || reached.Contains(atom.Type)
                        : atom.Kind == MirEffects.Shared && ReferenceEquals(atom.Type, calls)))
                    {
                        outside.Add($"{name}: {(atom.Kind == MirEffects.Drop ? "Drop" : atom.Kind)} {atom.Type.Name}");
                    }
                }

                _ = effects.Items(body, view, out var classes);
                if ((classes & ~MirEffects.Unknown) != 0 || ((classes & MirEffects.Unknown) != 0 &&
                    (row.Runs is not { } runs || on is null || !Attributed(body, static x => x, runs, on, []))))
                {
                    outside.Add($"{name}: classes {classes}");
                }
            }
        }

        // Whether each site of a body whose classes hold Unknown in a view (MirEffects.Keys) calls the requirement on the Type, its Self
        // mapped into the operation's Types through the entered calls' substitutions, or calls a body without a row whose sites do.
        bool Attributed(MirBody body, Func<BoundType, BoundType?> map, BindingSymbol runs, BoundType on, HashSet<MirBody> entered)
        {
            var callees = new List<(MirBody Body, Subst? Subst)>();
            flow.Begin(body);
            for (var block = 0; block < body.Blocks.Length; block++)
            {
                var (kind, callee) = (body.Blocks[block].Terminator.Kind, body.Blocks[block].Terminator.Callee);
                var unknown = false;
                for (var view = 0L; view < MirCases.Count(body) && kind is MirTerminatorKind.Call or MirTerminatorKind.Drop; view++)
                {
                    _ = effects.Keys(body, view, block, out var classes);
                    unknown |= (classes & MirEffects.Unknown) != 0;
                }

                if (!unknown || (callee is { Kind: MirCalleeKind.Requirement, Subst.Self: { } self } && ReferenceEquals(callee.Symbol, runs) && ReferenceEquals(map(self), on)))
                {
                    continue;
                }

                if (kind != MirTerminatorKind.Call || callee.Kind != MirCalleeKind.Fn || c.Types.Published(callee.Symbol!) is not null || program.Callee(callee) is not { } target)
                {
                    return false;
                }

                callees.Add((target, callee.Subst));
            }

            foreach (var (target, subst) in callees)
            {
                Func<BoundType, BoundType?> inner = subst is null ? map : x => c.Types.ReadableInstance(x, subst) is { } instance ? map(instance) : null;
                if (entered.Add(target) && !Attributed(target, inner, runs, on, entered))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // SPEC 4.9.6: a caller composes an Array operation's row, never its body: appendAll and append run no user code, so A4 accepts a
    // confined witness calling them over elements whose destruction writes the console, and truncate destroys the elements it removes.
    // Before appendAll had its row (R6-check H4), A4 composed its body, whose drop of the emptied source keeps T's destruction.
    [Theory]
    [InlineData("        var more: Array<Loud> = []\n        self.items.appendAll(more@move)\n", "")]
    [InlineData("        self.items.append(Loud.init())\n", "")]
    [InlineData("        self.items.truncate(0)\n", Failed)]
    public void ACallerComposesAnArrayOperationsRow(string call, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + "struct S\n    Self is Sink\n    var items: Array<Loud> = []\n" +
            "    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n" + call + "        return .Ok(())\n"));

    // The Type below an operation's parameter, as A4 reads a row's slot with the operation's own Types; null for no slot (-1).
    private static BoundType? Below(BindingSymbol operation, int slot)
    {
        _ = TyCtxt.Layer(slot < 0 ? null : TyCtxt.Signature(operation, null, slot, out _), out var below, out _);
        return below;
    }

    // The Types a destruction walk enters.
    private readonly struct Reached(HashSet<BoundType> types) : IMirDestruction
    {
        public bool First(BoundType type, int depth) => types.Add(type);

        public bool Visit(BoundType type, int depth) => false;

        public bool Deep(BoundType type) => false;
    }
}

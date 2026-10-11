// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class OriginPremiseClosureTest
{
    // SPEC 15.3.6: the closure entails exactly what reflexivity, the maximality of static, the meet laws, the premises and
    // transitivity derive, on every subset of the candidate premises of a small universe.
    [Fact]
    public void ClosureMatchesTheRuleOracleOnEverySmallGraph()
    {
        var a = Atom("a");
        var b = Atom("b");
        var c = Atom("c");
        BoundOrigin[] universe = [a, b, c, Meet(a, b), Meet(b, c), BoundOrigin.Static];
        var candidates = new List<(int Longer, int Shorter)>();
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                if (i != j)
                {
                    candidates.Add((i, j));
                }
            }
        }

        var closure = new OriginPremiseClosure();
        var premises = new List<(int Longer, int Shorter)>();
        for (var subset = 0; subset < 1 << candidates.Count; subset++)
        {
            premises.Clear();
            for (var i = 0; i < candidates.Count; i++)
            {
                if ((subset & (1 << i)) != 0)
                {
                    premises.Add(candidates[i]);
                }
            }

            AssertMatchesOracle(closure, universe, premises);
        }
    }

    // SPEC 15.3.6: the same agreement on generated graphs with nested meets, static endpoints and equalities.
    [Fact]
    public void ClosureMatchesTheRuleOracleOnGeneratedGraphs()
    {
        var closure = new OriginPremiseClosure();
        for (var seed = 0; seed < 300; seed++)
        {
            var (universe, premises) = Generate(seed);
            AssertMatchesOracle(closure, universe, premises);
        }
    }

    // SPEC 15.3.6: a premise over a meet proves that meet without proving either operand.
    [Fact]
    public void ACompositePremiseProvesOnlyTheComposite()
    {
        var a = Atom("a");
        var b = Atom("b");
        var x = Atom("x");
        var ab = Meet(a, b);
        var entails = Entailments([x, a, b, ab], [(0, 3)]);
        Assert.True(entails[0, 3]);
        Assert.False(entails[0, 1]);
        Assert.False(entails[0, 2]);
    }

    // SPEC 15.3.6: a cycle with no supporting premise proves only its own members.
    [Fact]
    public void AnUnsupportedCycleAddsNothing()
    {
        var entails = Entailments([Atom("a"), Atom("b"), Atom("c")], [(0, 1), (1, 0)]);
        Assert.True(entails[0, 1]);
        Assert.True(entails[1, 0]);
        Assert.False(entails[0, 2]);
        Assert.False(entails[1, 2]);
    }

    // SPEC 15.3.3, 15.3.6: `a == b` supplies both directions, and each side then outlives what the other outlives.
    [Fact]
    public void EqualityProvidesBothDirections()
    {
        var entails = Entailments([Atom("a"), Atom("b"), Atom("c")], [(0, 1), (1, 0), (1, 2)]);
        Assert.True(entails[0, 1]);
        Assert.True(entails[1, 0]);
        Assert.True(entails[0, 2]);
        Assert.False(entails[2, 0]);
    }

    // SPEC 5.2.2, 15.2.3: a raw-borrow anchor outlives every Origin, and a meet keeps it, so the meet outlives what its other
    // operands outlive.
    [Fact]
    public void AnAnchorOutlivesEveryTargetInsideAMeet()
    {
        var anchor = new BoundOrigin(OriginKind.Anchor, name: "anchor");
        var x = Atom("x");
        var y = Atom("y");
        var entails = Entailments([anchor, x, y, Meet(anchor, x)], [(1, 2)]);
        Assert.True(entails[0, 2]);
        Assert.True(entails[3, 2]);
        Assert.False(entails[3, 0]);
        Assert.False(entails[2, 1]);
    }

    // SPEC 15.3.6: adding a premise never removes an entailment.
    [Fact]
    public void AddingAPremiseIsMonotone()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var (universe, premises) = Generate(seed);
            var before = Entailments(universe, premises);
            var random = new Random(seed + 10_000);
            premises.Add((random.Next(universe.Length), random.Next(universe.Length)));
            var after = Entailments(universe, premises);
            for (var i = 0; i < universe.Length; i++)
            {
                for (var j = 0; j < universe.Length; j++)
                {
                    Assert.True(!before[i, j] || after[i, j], $"seed {seed}: {universe[i]} >= {universe[j]}");
                }
            }
        }
    }

    // SPEC 15.3.6: permuting or duplicating premises changes no entailment.
    [Fact]
    public void PremiseOrderAndDuplicatesChangeNothing()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var (universe, premises) = Generate(seed);
            var expected = Entailments(universe, premises);
            var shuffled = new List<(int Longer, int Shorter)>(premises);
            shuffled.AddRange(premises);
            new Random(seed + 20_000).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shuffled));
            AssertSameEntailments(expected, Entailments(universe, shuffled));
        }
    }

    // PLAN G74: the same graph yields the same witnesses and work, and each witness leads back to the target or a top seed.
    [Fact]
    public void WitnessesAndWorkAreDeterministic()
    {
        var first = new OriginPremiseClosure();
        var second = new OriginPremiseClosure();
        for (var seed = 0; seed < 100; seed++)
        {
            var (universe, premises) = Generate(seed);
            var left = Build(first, universe, premises);
            var right = Build(second, universe, premises);
            for (var l = 0; l < universe.Length; l++)
            {
                for (var s = 0; s < universe.Length; s++)
                {
                    var entailed = first.Entails(left[l], left[s]);
                    Assert.Equal(entailed, second.Entails(right[l], right[s]));
                    Assert.Equal(first.LastWork, second.LastWork);
                    if (!entailed)
                    {
                        continue;
                    }

                    var steps = 0;
                    var node = left[l];
                    for (var other = right[l]; first.ReachedFrom(node) >= 0; node = first.ReachedFrom(node), other = second.ReachedFrom(other), steps++)
                    {
                        Assert.Equal(first.ReachedRule(node), second.ReachedRule(other));
                        Assert.True(steps < first.NodeCount);
                    }

                    Assert.True(node == left[s] ? first.ReachedRule(node) == OriginPremiseRule.Target : first.ReachedRule(node) == OriginPremiseRule.Top);
                }
            }
        }
    }

    // SPEC 15.3.6: Origin identity belongs to its binder: renaming every atom consistently changes no entailment, and two atoms
    // with the same spelling stay distinct.
    [Fact]
    public void RenamingIsInvariantAndSpellingIsNotIdentity()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var (universe, premises) = Generate(seed);
            var renamed = Rename(universe);
            AssertSameEntailments(Entailments(universe, premises), Entailments(renamed, premises));
        }

        var a1 = Atom("a");
        var a2 = Atom("a");
        var entails = Entailments([a1, a2, Atom("x")], [(0, 2)]);
        Assert.True(entails[0, 2]);
        Assert.False(entails[1, 2]);
    }

    // SPEC 15.3.6: meets are idempotent, commutative and associative: equal meets outlive and are outlived by the same Origins.
    [Fact]
    public void MeetNormalizationChangesNothing()
    {
        var a = Atom("a");
        var b = Atom("b");
        var c = Atom("c");
        var x = Atom("x");
        BoundOrigin[] universe = [a, b, c, x, Meet(a, b), Meet(b, a), Meet(a, a), Meet(Meet(a, b), c), Meet(a, Meet(b, c)), Meet(a, b, c), BoundOrigin.Static];
        (int Same, int As)[] equal = [(5, 4), (6, 0), (8, 7), (9, 7)];
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var premises = new List<(int Longer, int Shorter)>();
            for (var i = random.Next(6); i >= 0; i--)
            {
                premises.Add((random.Next(4), random.Next(4)));
            }

            var entails = Entailments(universe, premises);
            foreach (var (same, @as) in equal)
            {
                for (var i = 0; i < universe.Length; i++)
                {
                    Assert.Equal(entails[@as, i], entails[same, i]);
                    Assert.Equal(entails[i, @as], entails[i, same]);
                }
            }
        }
    }

    // PLAN G74: a warm closure allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmClosureAllocatesNothing()
    {
        var (universe, premises) = Generate(7);
        var closure = new OriginPremiseClosure();
        for (var i = 0; i < 10; i++)
        {
            Run();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Run));

        void Run()
        {
            closure.Clear();
            for (var i = 0; i < universe.Length; i++)
            {
                closure.Node(universe[i], out _);
            }

            for (var i = 0; i < premises.Count; i++)
            {
                closure.AddEdge(closure.Node(universe[premises[i].Longer], out _), closure.Node(universe[premises[i].Shorter], out _), OriginPremiseRule.Declaration);
            }

            for (var i = 0; i < closure.NodeCount; i++)
            {
                closure.Entails(0, i);
            }
        }
    }

    private static BoundOrigin Atom(string name) => new(OriginKind.Parameter, name: name);

    private static BoundOrigin Meet(params BoundOrigin[] operands) => new(OriginKind.Intersection, operands: operands);

    private static int[] Build(OriginPremiseClosure closure, BoundOrigin[] universe, List<(int Longer, int Shorter)> premises)
    {
        closure.Clear();
        var nodes = new int[universe.Length];
        for (var i = 0; i < universe.Length; i++)
        {
            nodes[i] = closure.Node(universe[i], out _);
        }

        foreach (var (longer, shorter) in premises)
        {
            closure.AddEdge(nodes[longer], nodes[shorter], OriginPremiseRule.Declaration);
        }

        return nodes;
    }

    private static bool[,] Entailments(BoundOrigin[] universe, List<(int Longer, int Shorter)> premises)
    {
        var closure = new OriginPremiseClosure();
        var nodes = Build(closure, universe, premises);
        var entails = new bool[universe.Length, universe.Length];
        for (var l = 0; l < universe.Length; l++)
        {
            for (var s = 0; s < universe.Length; s++)
            {
                entails[l, s] = closure.Entails(nodes[l], nodes[s]);
                var (insertions, activations, decrements) = closure.LastWork;
                Assert.True(insertions <= closure.NodeCount && activations <= closure.EdgeCount && decrements <= closure.MeetIncidences);
            }
        }

        return entails;
    }

    private static void AssertSameEntailments(bool[,] expected, bool[,] actual)
    {
        for (var i = 0; i < expected.GetLength(0); i++)
        {
            for (var j = 0; j < expected.GetLength(1); j++)
            {
                Assert.Equal(expected[i, j], actual[i, j]);
            }
        }
    }

    private static void AssertMatchesOracle(OriginPremiseClosure closure, BoundOrigin[] universe, List<(int Longer, int Shorter)> premises)
    {
        var expected = Oracle(universe, premises);
        var nodes = Build(closure, universe, premises);
        for (var l = 0; l < universe.Length; l++)
        {
            for (var s = 0; s < universe.Length; s++)
            {
                if (closure.Entails(nodes[l], nodes[s]) != expected[l, s])
                {
                    Assert.Fail($"{universe[l]} >= {universe[s]}: oracle {expected[l, s]} under [{string.Join(", ", premises.Select(x => $"{universe[x.Longer]} >= {universe[x.Shorter]}"))}]");
                }

                var (insertions, activations, decrements) = closure.LastWork;
                Assert.True(insertions <= closure.NodeCount && activations <= closure.EdgeCount && decrements <= closure.MeetIncidences);
            }
        }
    }

    // SPEC 15.3.6, written from the rules alone: the least relation over the universe closed under reflexivity, the maximality
    // of static and anchors, `operand >= meet`, `meet >= x` when every operand outlives x, the premises and transitivity.
    private static bool[,] Oracle(BoundOrigin[] universe, List<(int Longer, int Shorter)> premises)
    {
        var n = universe.Length;
        var r = new bool[n, n];
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var x = 0; x < n; x++)
            {
                Set(x, x);
                for (var y = 0; y < n; y++)
                {
                    if (universe[x].Kind is OriginKind.Static or OriginKind.Anchor)
                    {
                        Set(x, y);
                    }

                    if (universe[x].Kind == OriginKind.Intersection && universe[x].Operands.All(o => r[Array.IndexOf(universe, o), y]))
                    {
                        Set(x, y);
                    }
                }

                foreach (var operand in universe[x].Operands)
                {
                    Set(Array.IndexOf(universe, operand), x);
                }
            }

            foreach (var (longer, shorter) in premises)
            {
                Set(longer, shorter);
            }

            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    for (var k = 0; r[i, j] && k < n; k++)
                    {
                        if (r[j, k])
                        {
                            Set(i, k);
                        }
                    }
                }
            }
        }

        return r;

        void Set(int longer, int shorter)
        {
            if (!r[longer, shorter])
            {
                r[longer, shorter] = true;
                changed = true;
            }
        }
    }

    // Atoms, meets over atoms and earlier meets, static, and premises among them, two of them equalities.
    private static (BoundOrigin[] Universe, List<(int Longer, int Shorter)> Premises) Generate(int seed)
    {
        var random = new Random(seed);
        var universe = new List<BoundOrigin>();
        for (var i = 3 + random.Next(5); i > 0; i--)
        {
            universe.Add(Atom("a" + universe.Count));
        }

        for (var i = random.Next(4); i > 0; i--)
        {
            var operands = new BoundOrigin[2 + random.Next(2)];
            for (var j = 0; j < operands.Length; j++)
            {
                operands[j] = universe[random.Next(universe.Count)];
            }

            universe.Add(Meet(operands));
        }

        universe.Add(BoundOrigin.Static);
        var premises = new List<(int Longer, int Shorter)>();
        for (var i = random.Next(12); i > 0; i--)
        {
            premises.Add((random.Next(universe.Count), random.Next(universe.Count)));
        }

        for (var i = 0; i < 2 && universe.Count > 1; i++)
        {
            var x = random.Next(universe.Count);
            var y = random.Next(universe.Count);
            premises.Add((x, y));
            premises.Add((y, x));
        }

        return (universe.ToArray(), premises);
    }

    // Fresh atoms with the same spellings, and the meets rebuilt over them.
    private static BoundOrigin[] Rename(BoundOrigin[] universe)
    {
        var renamed = new BoundOrigin[universe.Length];
        for (var i = 0; i < universe.Length; i++)
        {
            var origin = universe[i];
            renamed[i] = origin.Kind switch
            {
                OriginKind.Intersection => Meet(origin.Operands.Select(o => renamed[Array.IndexOf(universe, o)]).ToArray()),
                OriginKind.Static => origin,
                _ => Atom(origin.Name),
            };
        }

        return renamed;
    }
}

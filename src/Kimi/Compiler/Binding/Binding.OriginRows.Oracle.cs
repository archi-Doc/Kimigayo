// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The C1-F4 view's fixed-fixed oracle (2a0-D §4, D8; DECISIONS R4-M101 E9): one premise environment rented at a body's root (a layer's at
// its struct), the catalog's premises from there outward (R1-R4; R5 for the nodes already in the graph), queried by Close from a target
// in one Semantics case and then Outlives per source.
// Input: a body (ExecutableBody). Output: UniversalRelations, disposed after use; oracles open at once are disposed in reverse order.
// Invariants: a request changes no inference or obligation state (originStateVersion); the structural instances (I1-I4) answer first; a
// local region the body carries (CarriedRegion) equals its value (RegionValue, SPEC 15.3.6's candidate; 15.6.5's Closure sentence; R7-O2
// A1), and so does each region that value names, in turn (the owner's O-466 (b)): edges both ways, so it is read so at either end and at
// an inner node; any other region of an enclosing body is a node no premise names; a region of the body itself and a call's open region
// read as their sources, each outliving the target, no source no answer (BU-127's reading, which SolvedStatic asks at Final until R7-O2
// A2 reads RegionValue there).
// Must not: be used after its disposal or after a change of Origin state (an InvalidOperationException).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<UniversalRelations> universalRelations = [];

    // A body's fixed-fixed oracle (2a0-D §4, D8): one premise environment rented at the body's root, the catalog's premises from there
    // outward (R1-R4; R5 for the nodes already in the graph), queried per target and Semantics case.
    internal UniversalRelations UniversalRelationsOf(ExecutableBody body)
    {
        var root = this.BodyRoot(AccessorBody(body)) ?? throw new InvalidOperationException("A body without a root has no premises.");
        foreach (var relations in this.universalRelations)
        {
            if (!relations.Active)
            {
                return relations.Open(root, body);
            }
        }

        var created = new UniversalRelations(this);
        this.universalRelations.Add(created);
        return created.Open(root, body);
    }

    // A body's fixed-fixed oracle (2a0-D §4): Close from a target in one Semantics case, then ask which sources it reached. The structural
    // instances (I1-I4) answer first, as in EntailsOrigin; a request changes no inference or obligation state (originStateVersion).
    internal sealed class UniversalRelations(Binding binding) : IDisposable
    {
        private readonly Dictionary<BoundOrigin, bool> solved = new(ReferenceEqualityComparer.Instance); // Per Close: a region's answer.
        private readonly HashSet<BoundOrigin> equated = new(ReferenceEqualityComparer.Instance); // Per Open: the regions read as values.
        private readonly List<BoundOrigin> equating = new(); // Equate's work.
        private OriginPremiseEnvironment? environment;
        private BoundOrigin target = BoundOrigin.Static;
        private ExecutableBody body;
        private int version;

        internal bool Active => this.environment is not null;

        // Returns the environment; oracles open at once are disposed in the reverse order of opening, as premise environments nest.
        public void Dispose()
        {
            if (this.environment is { } rented)
            {
                this.environment = null;
                binding.ReturnPremises(rented);
            }
        }

        internal void Close(BoundOrigin target, ulong caseBits)
        {
            var closure = this.Rented().Closure;
            this.target = target;
            this.solved.Clear();
            var from = closure.NodeCount;
            var node = closure.Node(target, out _);
            this.Equate(from);
            closure.Close(node, -1, caseBits);
        }

        internal bool Outlives(BoundOrigin source) => this.Outlives(source, false);

        internal UniversalRelations Open(Koto root, ExecutableBody body)
        {
            this.environment = binding.RentPremises(root) ?? throw new InvalidOperationException("The premise environment of a body's oracle is not ready.");
            this.body = body;
            this.equated.Clear();
            binding.CollectOriginResolvers(this.environment);
            binding.ExtractOriginPremises(this.environment, root);
            this.Equate(0);
            this.version = binding.originStateVersion;
            return this;
        }

        // Whether `value` is, or has as a meet operand, a local region that is no region slot (a call's open region).
        private static bool NamesOpenRegion(BoundOrigin value)
        {
            for (var i = 0; i < (value.Kind == OriginKind.Intersection ? value.Operands.Count : 1); i++)
            {
                var atom = value.Kind == OriginKind.Intersection ? value.Operands[i] : value;
                if (IsLocalRegion(atom) && !SlotRegion(atom))
                {
                    return true;
                }
            }

            return false;
        }

        // `inTurn`: `source` is read from a carried region's value (O-466 (b)).
        private bool Outlives(BoundOrigin source, bool inTurn)
        {
            var closure = this.Rented().Closure;
            if (OriginOutlives(source, this.target))
            {
                return true;
            }

            var named = closure.TryNode(source, out var node);
            if (named && closure.Reached(node))
            {
                return true;
            }

            // A carried region the graph does not name, or a region a carried value names, is its value, which the graph may name; a region
            // of the body itself or a call's open region is its sources; a region of an enclosing body the body does not carry has no
            // answer. Each region's answer once per Close.
            var carried = IsLocalRegion(source) && SlotRegion(source) && (inTurn || CarriedRegion(source, this.body.Definition?.Declaration));
            if (IsLocalRegion(source) && (carried ? !named : (!SlotRegion(source) || !EnclosingAtom(source, this.body))))
            {
                if (!this.solved.TryGetValue(source, out var known))
                {
                    this.solved[source] = known = carried ? this.Value(source) is { } value && this.Outlives(value, true) : this.SourcesOutlive(source);
                }

                return known;
            }

            if (named || source.Kind != OriginKind.Intersection)
            {
                return false; // No premise names it.
            }

            for (var i = 0; i < source.Operands.Count; i++)
            {
                if (!this.Outlives(source.Operands[i], inTurn))
                {
                    return false;
                }
            }

            return true;
        }

        // A region of the body itself or a call's open region: each of its sources outlives the target, none no answer.
        private bool SourcesOutlive(BoundOrigin region)
        {
            binding.OriginProofMetrics?.RegionRead();
            var sources = binding.LocalRegionSources(region);
            var all = !sources.IsEmpty;
            for (var i = 0; all && i < sources.Length; i++)
            {
                all = this.Outlives(sources[i]);
            }

            return all;
        }

        // A region slot's value (RegionValue), read where the body carries it or a carried value names it; null without one, and for a
        // value naming a call's open region, whose bounds the waiting part gives after selection: a nested body meets that region as
        // BU-127's located limit (MirOrigins.NestedLimit; PLAN G936's J2), so the value is not read here.
        private BoundOrigin? Value(BoundOrigin origin)
        {
            if (!SlotRegion(origin) || binding.RegionValue(origin) is not { } value || NamesOpenRegion(value))
            {
                return null;
            }

            binding.OriginProofMetrics?.RegionRead();
            return value;
        }

        // Each node from `from` on that is a region slot the body carries equals its value, an edge each way (SPEC 15.3.6's candidate), and
        // so does each region slot that value names, in turn (O-466 (b)), whether the graph named it before or not. Each region once per Open.
        private void Equate(int from)
        {
            var environment = this.environment!;
            var closure = environment.Closure;
            var work = this.equating;
            work.Clear();
            for (var node = from; node < closure.NodeCount; node++)
            {
                if (closure.Origin(node) is { } origin && SlotRegion(origin) && CarriedRegion(origin, this.body.Definition?.Declaration))
                {
                    work.Add(origin);
                }
            }

            while (work.Count != 0)
            {
                var region = work[^1];
                work.RemoveAt(work.Count - 1);
                if (!this.equated.Add(region) || this.Value(region) is not { } value)
                {
                    continue;
                }

                var node = closure.Node(region, out _);
                var equal = closure.Node(value, out _);
                AddPremiseEdge(environment, equal, node, OriginPremiseRule.Value);
                AddPremiseEdge(environment, node, equal, OriginPremiseRule.Value);
                for (var i = 0; i < (value.Kind == OriginKind.Intersection ? value.Operands.Count : 1); i++)
                {
                    var atom = value.Kind == OriginKind.Intersection ? value.Operands[i] : value;
                    if (SlotRegion(atom) && !this.equated.Contains(atom))
                    {
                        work.Add(atom);
                    }
                }
            }
        }

        private OriginPremiseEnvironment Rented()
            => this.environment is { } rented && this.version == binding.originStateVersion ? rented
                : throw new InvalidOperationException("A body's fixed-fixed oracle is used after its disposal or after a change of Origin state.");
    }
}

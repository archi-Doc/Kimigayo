// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The C1-F4 view's fixed-fixed oracle (2a0-D §4, D8; DECISIONS R4-M101 E9): one premise environment rented at a body's root (a layer's at
// its struct), the catalog's premises from there outward (R1-R4; R5 for the nodes already in the graph), queried by Close from a target
// in one Semantics case and then Outlives per source.
// Input: a body (ExecutableBody). Output: UniversalRelations, disposed after use; oracles open at once are disposed in reverse order.
// Invariants: a request changes no inference or obligation state (originStateVersion); the structural instances (I1-I4) answer first.
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
                return relations.Open(root);
            }
        }

        var created = new UniversalRelations(this);
        this.universalRelations.Add(created);
        return created.Open(root);
    }

    // A body's fixed-fixed oracle (2a0-D §4): Close from a target in one Semantics case, then ask which sources it reached. The structural
    // instances (I1-I4) answer first, as in EntailsOrigin; a request changes no inference or obligation state (originStateVersion).
    internal sealed class UniversalRelations(Binding binding) : IDisposable
    {
        private OriginPremiseEnvironment? environment;
        private BoundOrigin target = BoundOrigin.Static;
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
            closure.Close(closure.Node(target, out _), -1, caseBits);
        }

        internal bool Outlives(BoundOrigin source)
        {
            var closure = this.Rented().Closure;
            if (OriginOutlives(source, this.target))
            {
                return true;
            }

            if (closure.TryNode(source, out var node))
            {
                return closure.Reached(node);
            }

            if (source.Kind != OriginKind.Intersection)
            {
                return false; // No premise names it.
            }

            for (var i = 0; i < source.Operands.Count; i++)
            {
                if (!this.Outlives(source.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal UniversalRelations Open(Koto root)
        {
            this.environment = binding.RentPremises(root) ?? throw new InvalidOperationException("The premise environment of a body's oracle is not ready.");
            binding.CollectOriginResolvers(this.environment);
            binding.ExtractOriginPremises(this.environment, root);
            this.version = binding.originStateVersion;
            return this;
        }

        private OriginPremiseEnvironment Rented()
            => this.environment is { } rented && this.version == binding.originStateVersion ? rented
                : throw new InvalidOperationException("A body's fixed-fixed oracle is used after its disposal or after a change of Origin state.");
    }
}

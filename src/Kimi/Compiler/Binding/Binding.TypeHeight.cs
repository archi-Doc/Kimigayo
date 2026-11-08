// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A Type's height limit (IMPL A.4; DECISIONS U-D2's entry): no Type higher than TypeHeightLimit is formed or published.
// - Responsibility: the one check, BoundType.Height (BU-71) against TypeHeightLimit, read where a Type's height grows by more than a
//   written layer: a substitution query's result (SubstituteType), an expression node's Type (BindNode) and a written Type's (BindType);
//   and the refusal's one record, TypeHeightLimit_Kd, at Binding's current site.
// - Inputs: a formed Type and its site: the consultation node (BindNode's node, BindType's syntax), else the declaration phase's
//   formation site (an associated Type's conformance while PrepareAssociatedBindings resolves it).
// - Outputs: the site's failure (BindingFailure.TypeHeightLimit, its first failure only); a refused node publishes no Type, so a node
//   reading it rests on it (SPEC 23.1.2) and a later failure of the same node is not recorded.
// - Invariants: one compare per check, no table; every walk recurses at most the limit plus a written layer's syntax depth (BU-112),
//   inside R4-M178's margin on the compile stack.
// - Prohibitions: no check before a walk from its inputs' heights (it over-approximates); no other suppression than SPEC 23.1.2's rests.
public sealed partial class Binding
{
    /// <summary>H, the greatest height a Type may have (IMPL A.4).</summary>
    internal const int TypeHeightLimit = 8192;

    // The declaration phase's formation site, where no consultation frame is open (PrepareAssociatedBindings' conformance).
    private Koto? heightSite;

    // A refusal of a Type higher than the limit, recorded at the current site; null, the refused formation's image.
    private BoundType? RefuseHeight()
        => (this.consultationNode ?? this.heightSite) is { } site ? this.Fail(site, BindingFailure.TypeHeightLimit) : null;

    // A node's Type under the limit: null when the node was refused while it formed or its own Type passes the limit, which is then
    // refused at the node (a node that failed first keeps that failure); a refused node keeps no Type.
    private BoundType? HeightChecked(Koto node, BoundType? type)
    {
        var over = type is { Height: > TypeHeightLimit };
        if (over)
        {
            this.Fail(node, BindingFailure.TypeHeightLimit);
        }

        if (!over && node.BindingFailure != BindingFailure.TypeHeightLimit)
        {
            return type;
        }

        node.BoundType = null;
        return null;
    }
}

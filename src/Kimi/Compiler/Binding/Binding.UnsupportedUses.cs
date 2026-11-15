// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// UnsupportedUses: SPEC 22.1's use verdict for a declaration written `#Unsupported` (IMPL 21.3.4; R8-G1-3b).
// - Responsibility: every use of an `#Unsupported` declaration is Unsupported at the use (SPEC 23.1.1). A mention inside an
//   `#Unsupported` declaration's own text (its signature, leading region, Fields or body) is part of that declaration, not a use.
// - Inputs: a written Name and the selection a publication hands it. The publications are the callers: PublishPath (its Selected end,
//   its Qualifier steps and its intermediate `::` steps), SelectQualifier, SelectMember and BindLengthName's member arm.
// - Outputs: the selection; or null with the Name failed Unsupported and unresolved, as a publication fails an ambiguous Name, so the
//   reader runs its miss branch and adds nothing to the Name (Fail keeps a node's first failure).
// - Invariants: the one reader of AttributeKind.Unsupported in Binding. Both sides read syntax by Kind (the declaration's Attributes),
//   never a binding state or a table. The use side is read only after the selected side holds, so no other Name pays for the walk.
// - Prohibited: a lookup, gathering candidates, a filter on reports, a verdict at a reader or a miss branch.
// - Temporary: a function head passes unjudged, since a function Name stands for its function group (SPEC 9.5.1) and a head judged by
//   its own declaration would decide a mixed group at the Name. Removal condition: R8-G1-3b-2's first commit, which replaces the
//   conjunct with the group's arm and adds the verdict of one selected function (a call's winner, a completed function item).
public sealed partial class Binding
{
    private static bool IsUnsupported(Koto declaration) => declaration.FindAttribute(AttributeKind.Unsupported) is not null;

    // The nearest of a node and its enclosing declarations that is written `#Unsupported`; null outside every such declaration.
    private static Koto? UnsupportedDeclaration(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (IsUnsupported(current))
            {
                return current;
            }
        }

        return null;
    }

    // What a publication hands a written Name: its selection, unless that names an `#Unsupported` declaration from outside every one.
    private BindingSymbol? Supported(Koto name, BindingSymbol? selected)
    {
        if (selected is null || selected.Kind == BindingSymbolKind.Function || !IsUnsupported(selected.Declaration) || UnsupportedDeclaration(name) is not null)
        {
            return selected;
        }

        this.Fail(name, BindingFailure.Unsupported, unresolved: true);
        return null;
    }
}

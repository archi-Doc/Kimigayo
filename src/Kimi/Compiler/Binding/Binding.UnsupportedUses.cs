// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// UnsupportedUses: SPEC 22.1's use verdict for a declaration written `#Unsupported` (IMPL 21.3.4; R8-G1-3b).
// - Responsibility: every use of an `#Unsupported` declaration is Unsupported at the use (SPEC 23.1.1). A mention inside an
//   `#Unsupported` declaration's own text (its signature, leading region, Fields or body) is part of that declaration, not a use.
// - Inputs: a written Name and the selection handed to it, where it is handed on. In ResolvePath that is a path's component as the walk
//   completes it (ResolvePath, ResolveRootPath and Lookup return through the verdict), before it becomes the next component's qualifier
//   or the path's end. Outside ResolvePath it is each walk's one selection (SelectQualifier, SelectMember, BindLengthName's member arm),
//   until BU-122 c-f bring those walks into ResolvePath.
// - Outputs: a refused component ends its walk at its Name, Unresolved (it names nothing the use may read), with an Unsupported step that
//   PublishPath publishes as the Name's failure, so no later step holds its selection; a walk outside ResolvePath gets null with the Name
//   failed. Either way the Name is failed Unsupported and unresolved, so its reader runs its miss branch and adds nothing to the Name
//   (Fail keeps a node's first failure).
// - Invariants: the one reader of AttributeKind.Unsupported in Binding. Both sides read syntax by Kind (the declaration's Attributes),
//   never a binding state or a table. The use side is read only after the selected side holds, so no other Name pays for the walk. The
//   verdict in ResolvePath writes nothing (its evaluation's contract): its record is the step.
// - Prohibited: a lookup, gathering candidates, a filter on reports, a verdict at a reader, a miss branch or a publication arm.
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

    // The verdict: whether `selected` names an `#Unsupported` declaration the written Name stands outside of.
    private static bool Refused(Koto name, BindingSymbol? selected)
        => selected is { Kind: not BindingSymbolKind.Function } && IsUnsupported(selected.Declaration) && UnsupportedDeclaration(name) is null;

    // A path's component as its walk completes it: its selection, or, refused, the walk's end at the component's Name with its record.
    private ResolvedPath Supported(in ResolvedPath component)
    {
        if (component.End != PathEnd.Selected || !Refused(component.Use, component.Symbol))
        {
            return component;
        }

        this.nameSteps.Add(new(NameStepKind.Unsupported, component.Use, null, null, default, default, default));
        return new(PathEnd.Unresolved, null, component.Use);
    }

    // What a walk outside ResolvePath hands a written Name: its selection, unless the verdict refuses it.
    private BindingSymbol? Supported(Koto name, BindingSymbol? selected)
    {
        if (!Refused(name, selected))
        {
            return selected;
        }

        this.Fail(name, BindingFailure.Unsupported, unresolved: true);
        return null;
    }
}

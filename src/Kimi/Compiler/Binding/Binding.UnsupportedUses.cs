// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// UnsupportedUses: SPEC 22.1's use verdict for a declaration written `#Unsupported` (IMPL 21.3.4; R8-G1-3b).
// - Responsibility: every use of an `#Unsupported` declaration is Unsupported at the use (SPEC 23.1.1). A mention inside an
//   `#Unsupported` declaration's own text (its signature, leading region, Fields or body) is part of that declaration, not a use.
// - Inputs: a written Name and the selection handed to it, where it is handed on. In ResolvePath that is a path's component as the walk
//   completes it (ResolvePath, ResolveRootPath and the value Name's ResolveValue return through the verdict), before it becomes the next
//   component's qualifier or the path's end. Outside ResolvePath it is each walk's one selection (SelectMember,
//   BindLengthName's member arm), until BU-122 c-f bring those walks into ResolvePath. A selected function names its Name's function
//   group (SPEC 9.5.1): its head's chain and each further source the walk gathered for the Name (its ImportGroup steps, which PublishPath
//   publishes after the verdict). A call's winner and a function item hand their Name one selected function (SupportedFunction).
// - Outputs: at a refused component the walk ends Unresolved (it names nothing the use may read), and the refused Name carries the
//   record: an Unsupported step that PublishPath publishes as the Name's failure, so no later step holds its selection; a walk outside ResolvePath gets null with the Name
//   failed, and a refused winner or item fails its Name. Either way the Name is failed Unsupported and unresolved, so its reader runs its
//   miss branch and adds nothing to the Name (Fail keeps a node's first failure).
// - Invariants: the one reader of AttributeKind.Unsupported in Binding. Both sides read syntax by Kind (the declaration's Attributes),
//   never a binding state or a published table: a use's group is its head's chain plus each gathered source's chain, each built by
//   FunctionGroup.Of, read from the walk's own ImportGroup steps (FunctionGroup's contract; GroupOf reads their publication after it).
//   The use side is read only after the selected side holds, so no other Name pays for the walk. The verdict in ResolvePath writes
//   nothing (its evaluation's contract): its record is the step.
// - Prohibited: a lookup, gathering candidates, a filter on reports, a verdict at a reader, a miss branch or a publication arm.
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

    private static bool OnlyUnsupported(in FunctionGroup group)
    {
        foreach (var candidate in group)
        {
            if (!IsUnsupported(candidate.Declaration))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 9.5.1, R5: whether every declaration a Name's selection names is `#Unsupported`: a function head's group, read from the walk's
    // own output (the import stage's ImportGroup steps for the Name, Binding.Imports.cs), or any other symbol's one declaration.
    private bool NamesOnlyUnsupported(Koto name, BindingSymbol selected)
    {
        if (!IsUnsupported(selected.Declaration) || selected.Kind != BindingSymbolKind.Function)
        {
            return IsUnsupported(selected.Declaration);
        }

        if (!OnlyUnsupported(FunctionGroup.Of(selected)))
        {
            return false;
        }

        foreach (var step in this.nameSteps)
        {
            if (step.Kind == NameStepKind.ImportGroup && ReferenceEquals(step.Use, name) && !OnlyUnsupported(FunctionGroup.Of(step.Symbol!)))
            {
                return false;
            }
        }

        return true;
    }

    // The verdict: whether `selected` names only `#Unsupported` declarations the written Name stands outside of.
    private bool Refused(Koto name, BindingSymbol? selected)
        => selected is not null && this.NamesOnlyUnsupported(name, selected) && UnsupportedDeclaration(name) is null;

    // A path's component as its walk completes it: its selection, or, refused, the walk's end at the component's Name with its record.
    private ResolvedPath Supported(in ResolvedPath component)
    {
        if (component.End != PathEnd.Selected || !this.Refused(component.Use, component.Symbol))
        {
            return component;
        }

        this.nameSteps.Add(new(NameStepKind.Unsupported, component.Use, null, null, default, default, default, null, null, null));
        return new(PathEnd.Unresolved, null, component.Use);
    }

    // What a walk outside ResolvePath hands a written Name: its selection, unless the verdict refuses it.
    private BindingSymbol? Supported(Koto name, BindingSymbol? selected)
    {
        if (!this.Refused(name, selected))
        {
            return selected;
        }

        this.Fail(name, BindingFailure.Unsupported, unresolved: true);
        return null;
    }

    // SPEC 7.3, 22.1 (R8): one selected function, a call's winner or a completed function item. False: `name` failed Unsupported.
    private bool SupportedFunction(Koto name, BindingSymbol function)
    {
        if (!IsUnsupported(function.Declaration) || UnsupportedDeclaration(name) is not null)
        {
            return true;
        }

        this.Fail(name, BindingFailure.Unsupported, unresolved: true);
        return false;
    }
}

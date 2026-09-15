// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // Paths are prefixes of existing projections. A dynamic selector widens to its
    // known parent subtree; descendants cannot recover precision from that selector.
    internal bool ElementPathsOverlap(int left, int right)
    {
        var a = this.Projections[left];
        var b = this.Projections[right];
        if (a.Root != b.Root)
        {
            return false;
        }

        left = a.Path;
        right = b.Path;
        var leftDepth = a.PathDepth;
        var rightDepth = b.PathDepth;
        while (leftDepth > rightDepth)
        {
            left = this.Projections[left].Parent;
            leftDepth--;
        }

        while (rightDepth > leftDepth)
        {
            right = this.Projections[right].Parent;
            rightDepth--;
        }

        while (left >= 0 && right >= 0)
        {
            a = this.Projections[left];
            b = this.Projections[right];
            if (a.Selector != b.Selector)
            {
                return false;
            }

            left = a.Parent;
            right = b.Parent;
        }

        return true; // Equal paths, ancestors, or a root-wide unknown footprint.
    }

    private bool ValidateElementPaths()
    {
        if (this.Values.Count != this.Operations.Count)
        {
            return false;
        }

        for (var i = 0; i < this.Projections.Count; i++)
        {
            var plan = this.Projections[i];
            if ((uint)plan.Operation >= (uint)this.Operations.Count || (uint)plan.Root >= (uint)this.Places.Count ||
                plan.Parent < -1 || plan.Parent >= i ||
                this.Operations[plan.Operation] is not { Kind: OwnershipOperationKind.ProjectElement, Source: BinaryKoto source } operation ||
                operation.Projection != i || operation.Place != plan.Root ||
                !ElementAccess.TryType(source, out _, out var element) || element != plan.Element ||
                (plan.Parent >= 0 && (this.Projections[plan.Parent].Root != plan.Root ||
                    !ReferenceEquals(this.Operations[this.Projections[plan.Parent].Operation].Source, KotoHelper.UnwrapParentheses(source.Left)))))
            {
                return false;
            }

            var selector = ElementAccess.StaticSelector(source);
            var path = plan.Parent < 0 ? -1 : this.Projections[plan.Parent].Path;
            var depth = plan.Parent < 0 ? 0 : this.Projections[plan.Parent].PathDepth;
            if (selector >= 0 && path == plan.Parent)
            {
                path = i;
                depth++;
            }

            if (plan.Selector != selector || plan.Path != path || plan.PathDepth != depth)
            {
                return false;
            }
        }

        for (var id = 0; id < this.Operations.Count; id++)
        {
            var operation = this.Operations[id];
            var borrow = operation.Projection >= 0 && operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Borrow;
            var element = borrow || operation.Kind is OwnershipOperationKind.ProjectElement or OwnershipOperationKind.WriteElement || this.Values[id].Kind == OwnershipValueKind.Element;
            if (!element)
            {
                if (operation.Projection != -1)
                {
                    return false;
                }

                continue;
            }

            if ((uint)operation.Projection >= (uint)this.Projections.Count)
            {
                return false;
            }

            var plan = this.Projections[operation.Projection];
            if ((operation.Kind == OwnershipOperationKind.ProjectElement ? plan.Operation :
                operation.Kind == OwnershipOperationKind.WriteElement ? plan.Write : borrow ? plan.Borrow : plan.Output) != id)
            {
                return false;
            }
        }

        return true;
    }
}

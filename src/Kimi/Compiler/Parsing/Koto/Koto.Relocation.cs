// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

public abstract partial class Koto
{
    // Attachment establishes the current module before a consumer can read semantic state. Source documents stay immutable.
    // The ordinary path has no walk or allocation; all operation-local maps below are discarded after a cross-module move.
    private void ChangeModule(CodeContext destination)
    {
        if (!ReferenceEquals(this.codeContext.Kotonoha, destination.Kotonoha))
        {
            new Relocation(destination).Run(this);
        }
    }

    private sealed class Relocation(CodeContext destination) : KotoVisitor
    {
        private readonly HashSet<Koto> moved = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CodeContext, CodeContext?> contexts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<DiagnosticOwner, DiagnosticOwner.SyntaxTransfer> diagnostics = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Kotonoha> modules = new(ReferenceEqualityComparer.Instance);

        public override void Visit(Koto node)
        {
            var context = node.codeContext;
            if (ReferenceEquals(context.Kotonoha, destination.Kotonoha) || !this.moved.Add(node))
            {
                return;
            }

            this.contexts.TryAdd(context, null);
            this.modules.Add(context.Kotonoha);
            if (context.RecoveryCause(node) is { } recovery)
            {
                this.Import(context).Include(recovery);
            }

            if (node is ErrorKoto { Cause: { } error })
            {
                this.Import(context).Include(error);
            }

            node.VisitChildren(this);
        }

        internal void Run(Koto root)
        {
            this.Visit(root);
            foreach (var transfer in this.diagnostics.Values)
            {
                transfer.Copy();
            }

            foreach (var context in this.contexts.Keys)
            {
                this.contexts[context] = context.ForModule(destination);
            }

            this.MoveNodes();
            this.CopyDocumentation();
        }

        private void MoveNodes()
        {
            foreach (var node in this.moved)
            {
                var source = node.codeContext;
                var target = this.contexts[source]!;
                if (source.RecoveryCause(node) is { } recovery)
                {
                    target.RecordRecovery(node, this.Import(source).Key(recovery));
                }

                if (node is ErrorKoto { Cause: { } cause } error)
                {
                    error.Cause = this.Import(source).Key(cause);
                }

                node.codeContext = target;
                // Plan rows can own opaque synthetic syntax; a foreign row is rebuilt, never copied or selectively repaired.
                node.SyntaxOwner = null;
                node.SyntaxId = 0;
            }
        }

        private void CopyDocumentation()
        {
            var copies = new Dictionary<DocumentationSource, DocumentationSource>(ReferenceEqualityComparer.Instance);
            Func<Koto, bool> contains = this.moved.Contains;
            foreach (var module in this.modules)
            {
                foreach (var source in module.DocumentationSources)
                {
                    if (source.CopyDeclarations(destination.Kotonoha, contains) is { } copy)
                    {
                        copies.Add(source, copy);
                    }
                }
            }

            destination.Kotonoha.ReplaceDocumentation(copies.Values, contains);
            foreach (var (source, target) in this.contexts)
            {
                if (source.Documentation is { } documentation && copies.TryGetValue(documentation, out var copy))
                {
                    target!.Documentation = copy;
                }
            }
        }

        private DiagnosticOwner.SyntaxTransfer Import(CodeContext source)
        {
            var owner = source.DiagnosticCollection.Owner;
            if (!this.diagnostics.TryGetValue(owner, out var transfer))
            {
                transfer = new(owner, destination.DiagnosticCollection);
                this.diagnostics.Add(owner, transfer);
            }

            return transfer;
        }
    }
}

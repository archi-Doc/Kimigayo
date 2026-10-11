// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the single file traversal and stack-local declaration measurements.
// Inputs: validated compilation owners and one source tree. Output: one file's observations.
// Each declaration frame is restored at exit; lambdas share statement ownership but reset control depth.
// Must not cache node facts, revisit bodies or give accessors/local functions competing measurement owners.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kimi.Analyzers;

public sealed partial class CodeRulesAnalyzer
{
    internal struct Measurement
    {
        internal SyntaxNode? Owner { get; set; }

        internal ISymbol? Symbol { get; set; }

        internal int Statements { get; set; }

        internal int Depth { get; set; }

        internal int MaximumDepth { get; set; }

        internal bool CheckBooleans { get; set; }

        internal int Booleans { get; set; }

        internal CodeRuleAnnotation? StatementException { get; set; }

        internal CodeRuleAnnotation? NestingException { get; set; }
    }

    private sealed class FileWalker(CodeRulesSource source, CodeRulesArchitecture architecture, CodeRulesConfiguration config, Action<Diagnostic> report, CancellationToken cancellation)
        : CSharpSyntaxWalker(SyntaxWalkerDepth.StructuredTrivia)
    {
        private readonly CodeRulesArchitecture.Layer? layer = architecture.FileLayer(source.Root.SyntaxTree);
        private readonly bool checkArchitecture = config.Applies(CodeRule.Find("KR1001")!, source.Path);
        private readonly bool checkDispatch = config.Applies(CodeRule.Find("KR1006")!, source.Path);
        private readonly bool checkDensity = config.Applies(CodeRule.Find("KR3001")!, source.Path);
        private readonly bool checkAccretion = config.Applies(CodeRule.Find("KR2001")!, source.Path);
        private Measurement frame;

        public override void Visit(SyntaxNode? node)
        {
            if (node is null or DocumentationCommentTriviaSyntax)
            {
                return;
            }

            cancellation.ThrowIfCancellationRequested();
            var owner = this.checkAccretion && MeasurementOwner(node);
            var saved = this.frame;
            if (owner)
            {
                this.frame = new Measurement { Owner = node, CheckBooleans = CodeRulesAccretion.CheckBooleans(node, config) };
                if (node is VariableDeclaratorSyntax && saved.Owner is BaseFieldDeclarationSyntax)
                {
                    this.frame.StatementException = saved.StatementException;
                    this.frame.NestingException = saved.NestingException;
                }
            }

            var depth = this.frame.Depth;
            if (this.checkAccretion && this.frame.Owner is not null)
            {
                if (node is AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax)
                {
                    this.frame.Depth = 0;
                }

                if (node is StatementSyntax and not (BlockSyntax or LocalFunctionStatementSyntax))
                {
                    this.frame.Statements++;
                }

                if (Control(node))
                {
                    this.frame.Depth++;
                    this.frame.MaximumDepth = Math.Max(this.frame.MaximumDepth, this.frame.Depth);
                }
            }

            this.Check(node);
            base.Visit(node);
            if (owner)
            {
                CodeRulesAccretion.Finish(source, this.frame, config);
                this.frame = saved;
            }
            else
            {
                this.frame.Depth = depth;
            }
        }

        internal void Analyze()
        {
            this.Visit(source.Root);
            if (this.checkDensity)
            {
                CodeRulesDensity.CheckLines(source, config, cancellation);
            }

            if (this.checkAccretion)
            {
                CodeRulesAccretion.CheckFile(source, config);
            }
        }

        private static bool MeasurementOwner(SyntaxNode node)
            => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax or DelegateDeclarationSyntax ||
                node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax };

        private static bool Control(SyntaxNode node)
            => (node is IfStatementSyntax && node.Parent is not ElseClauseSyntax) ||
                node is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                    SwitchStatementSyntax or TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax;

        private void Check(SyntaxNode node)
        {
            ISymbol? symbol = null;
            if (node is AttributeSyntax attribute)
            {
                var constructor = source.Semantics.GetSymbolInfo(attribute, cancellation).Symbol as IMethodSymbol;
                CheckSuppression(source, attribute, constructor, report, cancellation);
                if (this.checkArchitecture)
                {
                    architecture.CheckReference(source, attribute, constructor, this.layer);
                }

                if (this.checkAccretion && CodeRuleAnnotation.Read(source, attribute, constructor, config, report, cancellation) is { } annotation)
                {
                    this.Annotate(annotation);
                }
            }
            else if (node is PragmaWarningDirectiveTriviaSyntax pragma)
            {
                CheckSuppression(pragma, report);
            }
            else if (this.checkArchitecture)
            {
                symbol = architecture.CheckNode(source, node, this.layer, cancellation);
            }

            if (this.checkDispatch)
            {
                architecture.CheckDispatch(source, node, cancellation);
            }

            if (this.checkDensity)
            {
                CodeRulesDensity.CheckNode(source, node);
            }

            if (this.checkAccretion)
            {
                if (node == this.frame.Owner)
                {
                    this.frame.Symbol = symbol;
                }

                CodeRulesAccretion.CheckNode(source, node, symbol, ref this.frame, config, cancellation);
            }
        }

        private void Annotate(CodeRuleAnnotation annotation)
        {
            var previous = annotation.Rule == CodeRulesAccretion.Statements ? this.frame.StatementException : this.frame.NestingException;
            if (previous is not null)
            {
                report(Diagnostic.Create(CodeRule.InvalidException, annotation.Location, "Duplicate exception for " + annotation.Rule.Id));
            }
            else if (annotation.Rule == CodeRulesAccretion.Statements)
            {
                this.frame.StatementException = annotation;
            }
            else
            {
                this.frame.NestingException = annotation;
            }
        }
    }
}

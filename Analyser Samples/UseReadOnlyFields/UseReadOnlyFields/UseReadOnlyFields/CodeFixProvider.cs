using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace UseReadOnlyFields
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseReadOnlyFieldsCodeFixProvider)), Shared]
    public class UseReadOnlyFieldsCodeFixProvider : CodeFixProvider
    {
        private const string title = "Make field readonly";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(UseReadOnlyFieldsAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => MakeReadOnlyAsync(context.Document, declaration, c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Document> MakeReadOnlyAsync(Document document, FieldDeclarationSyntax fieldDecl, CancellationToken cancellationToken)
        {
            var roKeyword = SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword);
            var roFieldDecl = fieldDecl.AddModifiers(roKeyword);

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(fieldDecl, roFieldDecl);

            var newDocument = document.WithSyntaxRoot(newRoot);
            return newDocument;
        }
    }
}
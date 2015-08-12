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

namespace NamingConventionAnalyser
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamingConventionAnalyserCodeFixProvider)), Shared]
    public class NamingConventionAnalyserCodeFixProvider : CodeFixProvider
    {
        private const string title = "Make uppercase";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(FieldNamesShouldBeginWithUnderscoreAnalyzer.DiagnosticId); }
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
                    createChangedSolution: c => UnderscoreAsync(context.Document, declaration, diagnostic.Properties["fieldName"], c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Solution> UnderscoreAsync(Document document, FieldDeclarationSyntax fieldDeclaration, string fieldName, CancellationToken cancellationToken)
        {
            var fieldVariable = fieldDeclaration.Declaration.Variables.First(v => v.Identifier.Text == fieldName);

            // Get the symbol representing the field to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var fieldSymbol = semanticModel.GetDeclaredSymbol(fieldVariable, cancellationToken);

            // Compute new underscored name.
            var oldName = fieldVariable.Identifier.Text;
            var newName = "_" + GetCamelCase(oldName);

            // Produce a new solution that has all references to that field renamed, including the declaration.
            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, fieldSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

            // Return the new solution with the now-underscored field name.
            return newSolution;
        }

        private static string GetCamelCase(string token)
        {
            if (String.IsNullOrEmpty(token))
            {
                return token;
            }

            return Char.ToLower(token[0]) + token.Substring(1);
        }
    }
}
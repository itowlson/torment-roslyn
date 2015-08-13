using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UseReadOnlyFields
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UseReadOnlyFieldsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "UseReadOnlyFields";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Field);
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var fieldSymbol = (IFieldSymbol)context.Symbol;

            if (fieldSymbol.IsReadOnly)
            {
                return;
            }
            if (!(fieldSymbol.DeclaredAccessibility == Accessibility.Private || fieldSymbol.DeclaredAccessibility == Accessibility.NotApplicable))
            {
                return;
            }

            if (IsAssignedOutsideConstructor(context, fieldSymbol))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(Rule, fieldSymbol.Locations[0], fieldSymbol.Name);

            context.ReportDiagnostic(diagnostic);
        }

        private static bool IsAssignedOutsideConstructor(SymbolAnalysisContext context, IFieldSymbol fieldSymbol)
        {
            var containingType = fieldSymbol.ContainingType;

            foreach (var typeDecl in containingType.DeclaringSyntaxReferences)
            {
                var isAssignedWalker = new IsAssignedOutsideConstructorWalker(fieldSymbol);
                isAssignedWalker.Visit(typeDecl.GetSyntax());

                if (isAssignedWalker.IsAssigned)
                {
                    return true;
                }
            }

            return false;
        }

        private class IsAssignedOutsideConstructorWalker : CSharpSyntaxWalker
        {
            private bool _isAssigned;
            private IFieldSymbol _testee;

            public IsAssignedOutsideConstructorWalker(IFieldSymbol testee)
            {
                _testee = testee;
            }

            public bool IsAssigned
            {
                get { return _isAssigned; }
            }

            public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                // do not recurse
            }

            public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                base.VisitAssignmentExpression(node);

                var assignee = node.Left;

                if (assignee.Kind() == SyntaxKind.IdentifierName)
                {
                    var identifier = (IdentifierNameSyntax)assignee;

                    if (identifier.Identifier.Text == _testee.Name) // TODO: check it is actually the same symbol
                    {
                        _isAssigned = true;
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OperationsMistakenForInPlace
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class OperationsMistakenForInPlaceAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "OperationsMistakenForInPlace";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeSyntaxNode(SyntaxNodeAnalysisContext context)
        {
            var accessNode = (MemberAccessExpressionSyntax)(context.Node);
            var assignment = accessNode.FirstAncestorOrSelf<EqualsValueClauseSyntax>();
            var args = accessNode.FirstAncestorOrSelf<ArgumentListSyntax>();

            bool resultIsUsed = assignment != null || args != null;
            if (resultIsUsed)
            {
                return;
            }

            var resolutions = context.SemanticModel.GetMemberGroup(accessNode);

            if (resolutions == null || resolutions.IsDefaultOrEmpty)
            {
                return;
            }

            var member = resolutions[0];  // TODO: this might not be the actual overload - but this will typically not be an issue
            var memberName = member.Name;
            var typeName = member.ContainingType.ToDisplayString();
            var memberQName = typeName + "." + memberName;

            if (CommonlyMistakenForInPlace.Contains(memberQName))
            {
                // For all such symbols, produce a diagnostic.
                var diagnostic = Diagnostic.Create(Rule, accessNode.GetLocation(), memberQName);

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static readonly ReadOnlyCollection<string> CommonlyMistakenForInPlace = new ReadOnlyCollection<string>(new []
        {
            "string.Replace",
            "System.DateTime.Add",
            "System.DateTime.AddMilliseconds",
            "System.DateTime.AddSeconds",
            "System.DateTime.AddMinutes",
            "System.DateTime.AddHours",
            "System.DateTime.AddDays",
            "System.DateTime.AddMonths",
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace ExceptionAuditor
{
    static class Program
    {
        static void Main(string[] args)
        {
            var solutionFile = args[0];

            var projects = args.SelectMany(a => IncludedProjects(a)).ToList();

            IEnumerable<string> thrownExceptionTypeNames = GetThrownExceptionTypeNames(solutionFile, projects).Result;

            foreach (var typeName in thrownExceptionTypeNames)
            {
                Console.WriteLine(typeName);
            }

            Console.WriteLine("done");
            Console.ReadKey();
        }

        private static IEnumerable<string> IncludedProjects(string arg)
        {
            const string prefix = "/p:";
            return CsvItems(arg, prefix);
        }

        private static IEnumerable<string> CsvItems(string arg, string prefix)
        {
            if (arg.StartsWith(prefix))
            {
                var projects = arg.Substring(prefix.Length);
                return projects.Split(',').Select(p => p.Trim('"'));
            }
            return Enumerable.Empty<string>();
        }

        private static async Task<IEnumerable<string>> GetThrownExceptionTypeNames(string solutionFile, ICollection<string> projects)
        {
            using (MSBuildWorkspace workspace = MSBuildWorkspace.Create())
            {
                Solution solution = await workspace.OpenSolutionAsync(solutionFile);

                return solution.Projects
                               .Where(p => projects.Contains(p.Name))
                               .SelectMany(p => GetThrownExceptionTypeNames(p).Result)  // TODO: don't mix await and .Result
                               .Distinct();
            }
        }

        private static async Task<IEnumerable<string>> GetThrownExceptionTypeNames(Project project)
        {
            var compilation = await project.GetCompilationAsync();

            return compilation.SyntaxTrees
                              .SelectMany(tree => GetThrownExceptionTypeNames(compilation, tree))
                              .Distinct();
        }

        private static IEnumerable<string> GetThrownExceptionTypeNames(Compilation compilation, SyntaxTree syntaxTree)
        {
            var exceptionAuditor = new ExceptionTypeAuditingWalker(compilation.GetSemanticModel(syntaxTree));
            exceptionAuditor.Visit(syntaxTree.GetRoot());
            return exceptionAuditor.ThrownExceptionTypeNames;
        }

        private class ExceptionTypeAuditingWalker : CSharpSyntaxWalker
        {
            private readonly HashSet<string> _thrownExceptionTypeNames = new HashSet<string>();
            private readonly SemanticModel _semanticModel;

            public ExceptionTypeAuditingWalker(SemanticModel semanticModel)
            {
                _semanticModel = semanticModel;
            }

            public IEnumerable<string> ThrownExceptionTypeNames
            {
                get { return _thrownExceptionTypeNames; }
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                base.VisitObjectCreationExpression(node);

                var typeInfo = _semanticModel.GetTypeInfo(node);

                if (typeInfo.Type.HasBaseType("Exception"))
                {
                    _thrownExceptionTypeNames.Add(typeInfo.Type.Name);
                }
            }

            public override void VisitThrowStatement(ThrowStatementSyntax node)
            {
                base.VisitThrowStatement(node);

                var thrown = node.Expression;

                if (thrown != null)  // it can be null for a rethrow (throw;) expression
                {
                    bool rethrowing = IsRethrowOfCaughtException(thrown);

                    var typeInfo = _semanticModel.GetTypeInfo(thrown);

                    // we should never really be throwing weakly typed expressions, so if we
                    // see such a thing, drill in to check if we can learn more
                    if (typeInfo.Type.Name == "Exception")
                    {
                        // check if the expression is a call to one of our methods (throw Util.BlahBlahBlah):
                        // if so, it will get picked up in the ObjectCreationExpression clause, so we don't
                        // need to report the weak type
                        if (!rethrowing && IsObtainedFromFactory(thrown))
                        {
                            return;
                        }

                        // we can't prove a stronger type, so dump some information to help us investigate
                        // further in case we can improve the tool
                        var span = thrown.GetLocation().GetLineSpan();
                        System.Diagnostics.Debug.WriteLine($">>> {span.Path}: {span.StartLinePosition.Line}");
                        System.Diagnostics.Debug.WriteLine($">>> {thrown.ToString()}");
                    }

                    _thrownExceptionTypeNames.Add(typeInfo.Type.Name + (rethrowing ? " - rethrown" : ""));
                }
            }

            private bool IsObtainedFromFactory(ExpressionSyntax thrown)
            {
                if (thrown.IsKind(SyntaxKind.IdentifierName))
                {
                    // throw SomeException; - exception property or field in same class
                    IdentifierNameSyntax name = (IdentifierNameSyntax)thrown;
                    var referencedSymbol = _semanticModel.GetSymbolInfo(name).Symbol;
                    var referencedSyntax = referencedSymbol.DeclaringSyntaxReferences.Single().GetSyntax();
                    var variableDecl = referencedSyntax as VariableDeclaratorSyntax;
                    if (variableDecl != null && variableDecl.Initializer != null)
                    {
                        var val = variableDecl.Initializer.Value;
                        return IsObtainedFromFactory(val);
                    }
                    else
                    {
                        var propertyDecl = referencedSyntax as PropertyDeclarationSyntax;
                        if (propertyDecl != null)
                        {
                            // since it is a bald declaration, it must be in the same type and hence the same assembly,
                            // and will therefore get picked up in the ObjectCreationExpressionSyntax handler
                            return true;
                        }
                    }
                }
                if (thrown.IsKind(SyntaxKind.InvocationExpression))
                {
                    // throw Util.SomeException(task); - exception factory method
                    InvocationExpressionSyntax expr = (InvocationExpressionSyntax)thrown;
                    if (expr.Expression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        return IsObtainedFromFactory(expr.Expression);
                    }
                }
                if (thrown.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    // throw Util.SomeException - exception property or field in other class
                    MemberAccessExpressionSyntax expr = (MemberAccessExpressionSyntax)thrown;
                    if (expr.Expression.IsKind(SyntaxKind.IdentifierName))
                    {
                        var typeSyntaxWhereThrowIsHappening = thrown.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                        var typeWhereThrowIsHappening = _semanticModel.GetDeclaredSymbol(typeSyntaxWhereThrowIsHappening);
                        var type = _semanticModel.GetTypeInfo(expr.Expression);
                        if (type.Type.ContainingAssembly == typeWhereThrowIsHappening.ContainingAssembly)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            private static bool IsRethrowOfCaughtException(ExpressionSyntax thrown)
            {
                var containingCatch = thrown.FirstAncestorOrSelf<CatchClauseSyntax>();
                if (containingCatch != null)
                {
                    var catchDecl = containingCatch.Declaration;
                    if (catchDecl != null)
                    {
                        var caughtExId = catchDecl.Identifier;
                        if (thrown.IsKind(SyntaxKind.IdentifierName) && ((IdentifierNameSyntax)thrown).Identifier.IsEquivalentTo(caughtExId))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}

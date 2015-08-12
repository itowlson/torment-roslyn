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

namespace MethodCounter
{
    class Program
    {
        static void Main(string[] args)
        {
            var solutionFile = args[0];

            IEnumerable<MethodCountInfo> methodCounts = GetClassMethodCounts(solutionFile).Result;

            var top = methodCounts.OrderByDescending(i => i.MethodCount)
                                  .Take(20)
                                  .Select(i => $"{i.ClassName} : {i.MethodCount}");

            foreach (var s in top)
            {
                Console.WriteLine(s);
            }

            Console.WriteLine("Done");
            Console.ReadKey();
        }

        private static async Task<IEnumerable<MethodCountInfo>> GetClassMethodCounts(string solutionFile)
        {
            var methodCounts = new List<IEnumerable<MethodCountInfo>>();

            using (MSBuildWorkspace workspace = MSBuildWorkspace.Create())
            {
                Solution solution = await workspace.OpenSolutionAsync(solutionFile);

                foreach (var project in solution.Projects)
                {
                    Console.WriteLine("Processing: " + project.Name);

                    var compilation = await project.GetCompilationAsync();

                    foreach (var syntaxTree in compilation.SyntaxTrees)
                    {
                        var methodCounter = new MethodCountingWalker(compilation.GetSemanticModel(syntaxTree));
                        methodCounter.Visit(syntaxTree.GetRoot());
                        methodCounts.Add(methodCounter.Counts());
                    }
                }
            }

            return methodCounts.SelectMany(seq => seq);
        }

        private class MethodCountingWalker : CSharpSyntaxWalker  // NOTE: NOT CSharpSyntaxVisitor as that does not recurse
        {
            private readonly SemanticModel _semanticModel;
            private readonly Dictionary<string, int> _methodCounts = new Dictionary<string, int>();

            public MethodCountingWalker(SemanticModel semanticModel)
            {
                _semanticModel = semanticModel;
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                base.VisitMethodDeclaration(node);

                var method = _semanticModel.GetDeclaredSymbol(node);

                var typeName = method.ContainingType.ToDisplayString();
                var isTest = method.GetAttributes().Any(ad => ad.AttributeClass.Name == "TestAttribute");

                if (!isTest)
                {
                    int prevCount;
                    _methodCounts.TryGetValue(typeName, out prevCount);
                    _methodCounts[typeName] = prevCount + 1;
                }
            }

            public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                base.VisitPropertyDeclaration(node);

                var typeName = _semanticModel.GetDeclaredSymbol(node).ContainingType.ToDisplayString();

                int prevCount;
                _methodCounts.TryGetValue(typeName, out prevCount);
                _methodCounts[typeName] = prevCount + 1;
            }

            public IEnumerable<MethodCountInfo> Counts()
            {
                return _methodCounts.Select(kvp => new MethodCountInfo(kvp.Key, kvp.Value));
            }
        }

        private struct MethodCountInfo
        {
            public MethodCountInfo(string className, int methodCount)
            {
                ClassName = className;
                MethodCount = methodCount;
            }

            public string ClassName { get; }

            public int MethodCount { get; }
        }
    }
}

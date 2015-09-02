using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Roslyniser
{
    [ComVisible(true)]
    [Guid("FD11B453-BCEC-4A65-B1B5-79A6C0B66433")]
    [CodeGeneratorRegistration(typeof(RoslyniserSingleFileGenerator), "Roslyniser", "{FAE04EC1-301F-11D3-BF4B-00C04F79EFBC}" /*vsContextGuids.vsContextGuidVCSProject*/, GeneratesDesignTimeSource = true)]
    [ProvideObject(typeof(RoslyniserSingleFileGenerator))]
    public class RoslyniserSingleFileGenerator : IVsSingleFileGenerator, IObjectWithSite
    {
        public int DefaultExtension(out string pbstrDefaultExtension)
        {
            pbstrDefaultExtension = ".g.cs";
            return VSConstants.S_OK;
        }

        public int Generate(string wszInputFilePath, string bstrInputFileContents, string wszDefaultNamespace, IntPtr[] rgbOutputFileContents, out uint pcbOutput, IVsGeneratorProgress pGenerateProgress)
        {
            string result = Transform(Path.GetFileName(wszInputFilePath), bstrInputFileContents);

            byte[] buf = Encoding.UTF8.GetBytes(result);

            rgbOutputFileContents[0] = Marshal.AllocCoTaskMem(buf.Length);
            Marshal.Copy(buf, 0, rgbOutputFileContents[0], buf.Length);
            pcbOutput = (uint)(buf.Length);

            return VSConstants.S_OK;
        }

        private object _site;

        public void SetSite(object pUnkSite)
        {
            _site = pUnkSite;
        }

        public void GetSite(ref Guid riid, out IntPtr ppvSite)
        {
            if (_site == null)
            {
                throw new COMException("object is not sited", VSConstants.E_FAIL);
            }

            IntPtr pUnknownPointer = Marshal.GetIUnknownForObject(_site);
            IntPtr intPointer = IntPtr.Zero;
            Marshal.QueryInterface(pUnknownPointer, ref riid, out intPointer);

            if (intPointer == IntPtr.Zero)
            {
                throw new COMException("site does not support requested interface", VSConstants.E_NOINTERFACE);
            }

            ppvSite = intPointer;
        }

        private string Transform(string fileName, string source)
        {
            var sp = new ServiceProvider(_site as Microsoft.VisualStudio.OLE.Interop.IServiceProvider);
            var vsproj = ((EnvDTE.ProjectItem)(sp.GetService(typeof(EnvDTE.ProjectItem)))).ContainingProject;
            var cm = (IComponentModel)(Package.GetGlobalService(typeof(SComponentModel)));

            var workspace = cm.GetService<VisualStudioWorkspace>();

            var solution = workspace.CurrentSolution;
            var project = solution.Projects.FirstOrDefault(p => p.FilePath == vsproj.FileName);

            var syntaxTrees = Enumerable.Empty<SyntaxTree>();

            if (project != null)
            {
                var c = project.GetCompilationAsync().Result;
                syntaxTrees = c.SyntaxTrees;
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("temp", syntaxTrees.Concat(new[] { syntaxTree }));

            var rewriter = new NotifyPropertyChangedRewriter(fileName, compilation.GetSemanticModel(syntaxTree, true));

            var result = rewriter.Visit(syntaxTree.GetRoot());

            return result.ToFullString();
        }

        private class NotifyPropertyChangedRewriter : CSharpSyntaxRewriter
        {
            private readonly string _sourceFile;
            private readonly SemanticModel _semanticModel;

            private readonly List<FieldDeclarationSyntax> _fieldsToAdd = new List<FieldDeclarationSyntax>();

            public NotifyPropertyChangedRewriter(string sourceFile, SemanticModel semanticModel)
            {
                _sourceFile = sourceFile;
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                if (node.BaseList != null && node.BaseList.Types.OfType<SimpleBaseTypeSyntax>().Any(t => ((IdentifierNameSyntax)(t.Type)).Identifier.Text == "INotifyPropertyChanged"))
                {
                    var cls = (ClassDeclarationSyntax)(base.VisitClassDeclaration(node));

                    cls = cls.AddMembers(SyntaxFactory.EventFieldDeclaration(SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("PropertyChangedEventHandler"),
                        SyntaxFactory.SingletonSeparatedList<VariableDeclaratorSyntax>(
                            SyntaxFactory.VariableDeclarator("PropertyChanged")
                        ))).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))).NormalizeWhitespace());
                    cls = cls.AddMembers(SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                        "OnPropertyChanged")
                        .WithParameterList(
                            SyntaxFactory.ParameterList(
                                SyntaxFactory.SingletonSeparatedList<ParameterSyntax>(
                                    SyntaxFactory.Parameter(SyntaxFactory.Identifier("propertyName")).WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))
                                )))
                        .WithBody(
                            SyntaxFactory.Block(
                                SyntaxFactory.ParseStatement("PropertyChanged(this, new PropertyChangedEventArgs(propertyName));")  // sometimes you just want to cheat
                            )
                        ).NormalizeWhitespace()
                        );


                    return cls.AddMembers(_fieldsToAdd.ToArray());
                }

                return node;
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                if (node.AccessorList.Accessors.Count == 2 && node.AccessorList.Accessors.All(a => a.Body == null))
                {
                    var backingField = SyntaxFactory.FieldDeclaration(
                        SyntaxFactory.VariableDeclaration(node.Type,
                            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator("_" + node.Identifier.Text))));

                    _fieldsToAdd.Add(backingField);

                    return base.VisitPropertyDeclaration(node);
                }

                return node;
            }

            public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
            {
                var propName = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>().Identifier.Text;
                var fieldName = "_" + propName;

                BlockSyntax body;

                if (node.IsKind(SyntaxKind.GetAccessorDeclaration))
                {
                    body = SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(fieldName)).NormalizeWhitespace());
                }
                else
                {
                    var setField = SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(fieldName), SyntaxFactory.IdentifierName(@"value")));
                    var raiseEvent = SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("OnPropertyChanged"),
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.LiteralExpression(
                                            SyntaxKind.StringLiteralExpression,
                                            SyntaxFactory.Literal(propName)))))));

                    body = SyntaxFactory.Block(SyntaxFactory.SeparatedList<StatementSyntax>(new StatementSyntax[] { setField, raiseEvent }));
                }

                var newDecl = SyntaxFactory.AccessorDeclaration(node.Kind(), body);
                return newDecl;
            }
        }
    }
}

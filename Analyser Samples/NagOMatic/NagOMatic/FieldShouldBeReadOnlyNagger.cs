using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace NagOMatic
{
    internal sealed class FieldShouldBeReadOnlyNagger
    {
        private readonly IAdornmentLayer layer;
        private readonly IWpfTextView view;
        private readonly Brush brush;
        private readonly Pen pen;

        public FieldShouldBeReadOnlyNagger(IWpfTextView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException("view");
            }

            this.layer = view.GetAdornmentLayer("FieldShouldBeReadOnlyNagger");

            this.view = view;
            this.view.LayoutChanged += this.OnLayoutChanged;

            this.brush = new LinearGradientBrush(Colors.Lime, Colors.Purple, 90.0);
            this.brush.Freeze();

            var penBrush = new SolidColorBrush(Colors.HotPink);
            penBrush.Freeze();
            this.pen = new Pen(penBrush, 2.5);
            this.pen.Freeze();

            SetRefreshTimer();
        }

        private void SetRefreshTimer()
        {
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background, this.view.VisualElement.Dispatcher);
            t.Interval = TimeSpan.FromMilliseconds(400);
            t.Tick += OnBackgroundTimer;
            t.Start();
        }

        internal async void OnBackgroundTimer(object sender, EventArgs e)
        {
            await UpdateFieldAdornments(this.view.TextSnapshot, this.view.TextViewLines);
        }

        internal async void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            await UpdateFieldAdornments(e.NewSnapshot, e.NewOrReformattedLines);
        }

        internal async Task UpdateFieldAdornments(ITextSnapshot textSnapshot, IEnumerable<ITextViewLine> lines)
        {
            var workspace = this.view.TextBuffer.GetWorkspace();
            var document = textSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxRoot = await document.GetSyntaxRootAsync();
            var fields = syntaxRoot.DescendantNodes().OfType<FieldDeclarationSyntax>().SelectMany(f => f.Declaration.Variables).ToList();
            var fieldsThatShouldBeReadOnly = fields.Where(f => ShouldBeReadOnly(f, semanticModel, workspace)).ToList();

            foreach (ITextViewLine line in lines)
            {
                this.CreateVisuals(line, fields, fieldsThatShouldBeReadOnly);
            }
        }

        private void CreateVisuals(ITextViewLine line, IList<VariableDeclaratorSyntax> fields, IList<VariableDeclaratorSyntax> fieldsThatShouldBeReadOnly)
        {
            IWpfTextViewLineCollection textViewLines = this.view.TextViewLines;

            var fieldsOnThisLine = fields.Where(f => f.FullSpan.IntersectsWith(new TextSpan(line.Start.Position, line.Length)));

            foreach (var field in fieldsOnThisLine)
            {
                if (fieldsThatShouldBeReadOnly.Contains(field))
                {
                    var startIndex = field.FullSpan.Start;
                    SnapshotSpan span = new SnapshotSpan(this.view.TextSnapshot, Span.FromBounds(startIndex, startIndex + field.FullSpan.Length));

                    var geometry = textViewLines.GetMarkerGeometry(span);

                    if (geometry != null)
                    {
                        var drawing = new GeometryDrawing(this.brush, this.pen, geometry);
                        drawing.Freeze();

                        var drawingImage = new DrawingImage(drawing);
                        drawingImage.Freeze();

                        var image = new Image
                        {
                            Source = drawingImage,
                        };

                        // Align the image with the top of the bounds of the text geometry
                        Canvas.SetLeft(image, geometry.Bounds.Left);
                        Canvas.SetTop(image, geometry.Bounds.Top);

                        this.layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, AdornmentTagFor(field), image, null);
                    }
                }
                else
                {
                    this.layer.RemoveAdornmentsByTag(AdornmentTagFor(field));
                }
            }
        }

        private static string AdornmentTagFor(VariableDeclaratorSyntax field)
        {
            return "__FIELD_RO_NAGOMATIC__" + field.Identifier.Text;
        }

        private static bool ShouldBeReadOnly(VariableDeclaratorSyntax field, SemanticModel semanticModel, Workspace workspace)
        {
            var fieldSymbol = (IFieldSymbol)(semanticModel.GetDeclaredSymbol(field));

            if (fieldSymbol == null)
            {
                return false;
            }

            if (fieldSymbol.IsReadOnly)
            {
                return false;
            }
            if (!(fieldSymbol.DeclaredAccessibility == Accessibility.Private || fieldSymbol.DeclaredAccessibility == Accessibility.NotApplicable))
            {
                return false;
            }

            if (IsAssignedOutsideConstructor(fieldSymbol))
            {
                return false;
            }

            return true;
        }

        private static bool IsAssignedOutsideConstructor(IFieldSymbol fieldSymbol)
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

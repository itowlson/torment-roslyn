using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using UseReadOnlyFields;

namespace UseReadOnlyFields.Test
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {
        [TestMethod]
        public void IfAFieldIsWrittenInAMethod_ThenItIsNotEligibleToBeMadeReadOnly()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private int _size;

            public void Resize(int n) { _size = n; }
        }
    }";
            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void IfAFieldIsWrittenInAMethod_UsingAMutatingAssignmentOperator_ThenItIsNotEligibleToBeMadeReadOnly()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private int _height;
            private int _width;

            public void Resize(int n) { _height += n; _width -= n; }
        }
    }";
            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void IfAFieldIsWrittenInAMethod_UsingAUnaryOperator_ThenItIsNotEligibleToBeMadeReadOnly()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private int _f1;
            private int _f2;
            private int _f3;
            private int _f4;

            public void Resize(int n) { ++_f1; --_f2; _f3++; _f4--; }
        }
    }";
            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void IfAFieldIsNotWrittenInAnyMethod_ThenItIsEligibleToBeMadeReadOnly()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private int _size;

            public int GetSize() { return _size; }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "UseReadOnlyFields",
                Message = "Field '_size' is not written after construction and should be readonly",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[] {
                            new DiagnosticResultLocation("Test0.cs", 8, 25)
                        }
            };

            VerifyCSharpDiagnostic(test, expected);

            var fixtest = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private readonly int _size;

            public int GetSize() { return _size; }
        }
    }";
            VerifyCSharpFix(test, fixtest);
        }

        [TestMethod]
        public void IfAFieldIsWrittenInAConstructorMethod_ThenItIsEligibleToBeMadeReadOnly()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private int _size;

            public Widget(int n) { _size = n; }
            public int GetSize() { return _size; }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "UseReadOnlyFields",
                Message = "Field '_size' is not written after construction and should be readonly",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[] {
                            new DiagnosticResultLocation("Test0.cs", 8, 25)
                        }
            };

            VerifyCSharpDiagnostic(test, expected);

            var fixtest = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private readonly int _size;

            public Widget(int n) { _size = n; }
            public int GetSize() { return _size; }
        }
    }";
            VerifyCSharpFix(test, fixtest);
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new UseReadOnlyFieldsCodeFixProvider();
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new UseReadOnlyFieldsAnalyzer();
        }
    }
}
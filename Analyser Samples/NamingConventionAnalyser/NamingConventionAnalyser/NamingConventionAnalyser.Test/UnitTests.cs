using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using NamingConventionAnalyser;

namespace NamingConventionAnalyser.Test
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {
        [TestMethod]
        public void EmptyText_RaisesNoDiagnostics()
        {
            var code = @"";

            VerifyCSharpDiagnostic(code);
        }

        [TestMethod]
        public void IfFieldHasUnderscore_RaisesNoDiagnostics()
        {
            var code = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _description;
        }
    }";

            VerifyCSharpDiagnostic(code);
        }

        [TestMethod]
        public void IfFieldLacksUnderscore_ThenTheAnalyserReportsIt()
        {
            var code = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string description;
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "FieldNamesShouldBeginWithUnderscore",
                Message = "Field name 'description' does not begin with an underscore",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[] {
                            new DiagnosticResultLocation("Test0.cs", 8, 28)
                        }
            };

            VerifyCSharpDiagnostic(code, expected);
        }

        [TestMethod]
        public void IfFieldLacksUnderscore_ThenTheCodeFixAddsAnUnderscore()
        {
            var originalCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string description;
        }
    }";

            var fixedCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _description;
        }
    }";

            VerifyCSharpFix(originalCode, fixedCode);
        }

        [TestMethod]
        public void IfFieldIsPascalCased_ThenTheCodeFixCamelCasesIt()
        {
            var originalCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string WonderField = ""fie"";
        }
    }";

            var fixedCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _wonderField = ""fie"";
        }
    }";

            VerifyCSharpFix(originalCode, fixedCode);
        }

        [TestMethod]
        public void FieldRenameIsAppliedAcrossTheCode()
        {
            var originalCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string WonderField = ""fie"";

            public string AmazeMe() { return WonderField; }
        }
    }";

            var fixedCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _wonderField = ""fie"";

            public string AmazeMe() { return _wonderField; }
        }
    }";

            VerifyCSharpFix(originalCode, fixedCode);
        }

        [TestMethod]
        public void IfMultipleFieldsAreDeclared_ThenTheCodeFixFixesTheRightOne()
        {
            var originalCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _title, description;
        }
    }";

            var fixedCode = @"
    using System;

    namespace ConsoleApplication1
    {
        class Widget
        {
            private string _title, _description;
        }
    }";

            VerifyCSharpFix(originalCode, fixedCode);
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new NamingConventionAnalyserCodeFixProvider();
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new FieldNamesShouldBeginWithUnderscoreAnalyzer();
        }
    }
}
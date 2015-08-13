using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using OperationsMistakenForInPlace;

namespace OperationsMistakenForInPlace.Test
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {
        [TestMethod]
        public void IfStringReplaceIsCalledAndResultIsAssigned_ItIsOkay()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class C
        {
            public void Huzzah()
            {
                string s = ""Hello world"";
                var t = s.Replace("" "", ""_"");
                Console.WriteLine(t);
            }
        }
    }";

            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void IfStringReplaceIsCalledAndResultIsPassedToAnotherMethod_ItIsOkay()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class C
        {
            public void Huzzah()
            {
                string s = ""Hello world"";
                Console.WriteLine(s.Replace("" "", ""_""));
            }
        }
    }";

            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void IfStringReplaceIsCalledAndResultNotAssigned_ItIsDiagnosed()
        {
            var test = @"
    using System;

    namespace ConsoleApplication1
    {
        class C
        {
            public void Oops()
            {
                string s = ""Hello world"";
                s.Replace("" "", ""_"");
                Console.WriteLine(s);
            }
        }
    }";
            var expected = new DiagnosticResult
            {
                Id = "OperationsMistakenForInPlace",
                Message = "Result of 'string.Replace' is discarded - did you mean to assign it?",
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[] {
                            new DiagnosticResultLocation("Test0.cs", 11, 17)
                        }
            };

            VerifyCSharpDiagnostic(test, expected);
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            throw new NotSupportedException();  // we're not implementing a code fix for this one (at least not for now)
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new OperationsMistakenForInPlaceAnalyzer();
        }
    }
}
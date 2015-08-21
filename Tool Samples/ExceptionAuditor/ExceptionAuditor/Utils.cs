using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace ExceptionAuditor
{
    static class Utils
    {
        public static bool HasBaseType(this ITypeSymbol type, string typeName)
        {
            if (type.Name == typeName)
            {
                return true;
            }
            if (type.BaseType == null)
            {
                return false;
            }
            return type.BaseType.HasBaseType(typeName);
        }
    }
}

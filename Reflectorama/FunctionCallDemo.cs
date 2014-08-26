using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Reflectorama
{
    public static class FunctionCallDemo
    {
        public static void Run()
        {
            while (true)
            {
                Console.Write(">> ");
                var input = Console.ReadLine();
                CallFunc(input);
            }
        }

        private static void CallFunc(string fullName)
        {
            var parts = fullName.Split('.');
            var typeName = string.Join(".", parts.Take(parts.Length - 1));
            var functionName = parts.Last();

            var type = Type.GetType(typeName);

            if (type == null)
            {
                Console.WriteLine(string.Format("Could not find the type '{0}'", typeName));
                return;
            }

            var func = type.GetMethod(functionName, BindingFlags.Static | BindingFlags.Public);

            if (func == null)
            {
                Console.WriteLine(string.Format("Could not find the function '{0}' on type '{1}'", functionName, typeName));
                return;
            }

            var result = func.Invoke(null, new object[] { });
            if (result != null)
            {
                Console.WriteLine(result.ToString());
            }
        }
    }

    public static class Functions
    {
        public static string SayHi()
        {
            return "Hello";
        }

        public static string BlahBlah()
        {
            return "the quick brown fox jumped over the lazy dogs";
        }

        public static void Exit()
        {
            System.Environment.Exit(0);
        }
    }
}

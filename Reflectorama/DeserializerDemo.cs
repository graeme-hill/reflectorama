using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Reflectorama
{
    public class DeserializerDemo
    {
        public const int DESERIALIZATION_SAMPLES = 1000;
        public const int NUM_PROGRAMMERS = 10000;

        public static void Run()
        {
            var testData = GenerateProgrammerData(NUM_PROGRAMMERS);

            Stopwatch.BenchmarkOperation("Hard coded", DESERIALIZATION_SAMPLES, () =>
            {
                StaticDeserializer.DeserializeProgrammers(testData);
            });

            Stopwatch.BenchmarkOperation("Simple reflection", DESERIALIZATION_SAMPLES, () =>
            {
                SimpleDynamicDeserializer.Deserialize<Programmer>(testData);
            });

            Stopwatch.BenchmarkOperation("Dynamic method", DESERIALIZATION_SAMPLES, () =>
            {
                FastDynamicDeserializer.Deserialize<Programmer>(testData);
            });

            Console.WriteLine("\n -- finished --");
            Console.ReadKey();
        }

        private static Dictionary<string, string>[] GenerateProgrammerData(int numProgrammers)
        {
            var programmers = new List<Dictionary<string, string>>();
            for (var i = 0; i < numProgrammers; i++)
            {
                programmers.Add(new Dictionary<string, string>
                    {
                        { "FirstName", Guid.NewGuid().ToString() },
                        { "MiddleName", Guid.NewGuid().ToString() },
                        { "LastName", Guid.NewGuid().ToString() },
                        { "FavoriteLanguage", Guid.NewGuid().ToString() },
                        { "Gender", Guid.NewGuid().ToString() },
                        { "Archetype", Guid.NewGuid().ToString() }
                    });
            }
            return programmers.ToArray();
        }
    }

    public class Programmer
    {
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string FavoriteLanguage { get; set; }
        public string Gender { get; set; }
        public string Archetype { get; set; }
    }

    public static class SimpleDynamicDeserializer
    {
        private static Dictionary<Type, SimpleDynamicDeserializationMachine> _cachedMachines = new Dictionary<Type, SimpleDynamicDeserializationMachine>();

        public static T[] Deserialize<T>(Dictionary<string, string>[] dicts)
        {
            if (!_cachedMachines.ContainsKey(typeof(T)))
                _cachedMachines[typeof(T)] = new SimpleDynamicDeserializationMachine(typeof(T));
            var machine = _cachedMachines[typeof(T)];
            return dicts.Select(d => (T)machine.Deserialize(d)).ToArray();
        }

        private class SimpleDynamicDeserializationMachine
        {
            private Dictionary<string, PropertyInfo> _setters;
            private Type _type;

            public SimpleDynamicDeserializationMachine(Type type)
            {
                _type = type;
                _setters = type.GetProperties().ToDictionary(p => p.Name);
            }

            public object Deserialize(Dictionary<string, string> dict)
            {
                var result = Activator.CreateInstance(_type);
                foreach (var pair in _setters)
                {
                    pair.Value.SetValue(result, dict[pair.Key]);
                }
                return result;
            }
        }
    }

    public static class FastDynamicDeserializer
    {
        private static Dictionary<Type, FastDynamicDeserializationMachine> _cachedMachines = new Dictionary<Type, FastDynamicDeserializationMachine>();

        public static T[] Deserialize<T>(Dictionary<string, string>[] dicts)
        {
            if (!_cachedMachines.ContainsKey(typeof(T)))
                _cachedMachines[typeof(T)] = new FastDynamicDeserializationMachine(typeof(T));
            var machine = _cachedMachines[typeof(T)];
            return dicts.Select(d => (T)machine.Deserialize(d)).ToArray();
        }

        private class FastDynamicDeserializationMachine
        {
            private delegate object DynamicMethodInvoker(Dictionary<string, string> dict);

            private Type _type;
            private DynamicMethodInvoker _dynamicMethodInvoker;

            public FastDynamicDeserializationMachine(Type type)
            {
                _type = type;

                var dictOfStringString = typeof(Dictionary<,>).MakeGenericType(new[] { typeof(string), typeof(string) });
                var dictIndexerMethod = dictOfStringString.GetMethod("get_Item");
                var ctor = type.GetConstructors().First();

                var dynamicMethod = new DynamicMethod("Deserialize" + type.Name, type, new[] { typeof(Dictionary<string, string>) }, this.GetType().Module);
                var ilGen = dynamicMethod.GetILGenerator();

                // create the result object and store in a local
                var resultLocal = ilGen.DeclareLocal(type);
                ilGen.Emit(OpCodes.Newobj, ctor);
                ilGen.Emit(OpCodes.Stloc, resultLocal);
               
                // set every property in the class
                foreach (var property in type.GetProperties())
                {
                    ilGen.Emit(OpCodes.Ldloc, resultLocal);
                    ilGen.Emit(OpCodes.Ldarg_0);
                    ilGen.Emit(OpCodes.Ldstr, property.Name);
                    ilGen.Emit(OpCodes.Callvirt, dictIndexerMethod);
                    ilGen.Emit(OpCodes.Callvirt, property.GetSetMethod());
                }

                // return the result object
                ilGen.Emit(OpCodes.Ldloc, resultLocal);
                ilGen.Emit(OpCodes.Ret);

                _dynamicMethodInvoker = (DynamicMethodInvoker)dynamicMethod.CreateDelegate(typeof(DynamicMethodInvoker));
            }

            public object Deserialize(Dictionary<string, string> dict)
            {
                return _dynamicMethodInvoker(dict);
            }
        }
    }

    public static class StaticDeserializer
    {
        public static Programmer[] DeserializeProgrammers(Dictionary<string, string>[] dicts)
        {
            return dicts.Select(d => new Programmer()
            {
                FirstName = d["FirstName"],
                MiddleName = d["MiddleName"],
                LastName = d["LastName"],
                FavoriteLanguage = d["FavoriteLanguage"],
                Gender = d["Gender"],
                Archetype = d["Archetype"]
            }).ToArray();
        }
    }

    public static class Stopwatch
    {
        public static void BenchmarkOperation(string description, int times, Action action)
        {
            Console.Write(description + "...");
            var start = DateTime.Now;
            for (var i = 0; i < times; i++)
                action();
            var end = DateTime.Now;
            Console.WriteLine(" finished in " + (int)(end - start).TotalMilliseconds + " milliseconds");
        }
    }
}

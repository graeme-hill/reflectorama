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
        private const int DESERIALIZATION_SAMPLES = 1000;
        private const int NUM_PROGRAMMERS = 1000;

        public static void Run()
        {
            var testData = GenerateProgrammerData();

            var start = DateTime.Now;

            for (var i = 0; i < DESERIALIZATION_SAMPLES; i++)
                GenericProgrammerArrayDeserializer.Deserialize(testData, (dict) => StaticDeserializer.DeserializeProgrammer(dict));

            var afterStatic = DateTime.Now;

            for (var i = 0; i < DESERIALIZATION_SAMPLES; i++)
                GenericProgrammerArrayDeserializer.Deserialize(testData, (dict) => SimpleDynamicDeserializer.Deserialize<Programmer>(dict));

            var afterSimpleDynamic = DateTime.Now;

            for (var i = 0; i < DESERIALIZATION_SAMPLES; i++)
                GenericProgrammerArrayDeserializer.Deserialize(testData, (dict) => FastDynamicDeserializer.Deserialize<Programmer>(dict));

            var afterFastDynamic = DateTime.Now;

            Console.WriteLine("static: " + (afterStatic - start).TotalMilliseconds);
            Console.WriteLine("simple dynamic: " + (afterSimpleDynamic - afterStatic).TotalMilliseconds);
            Console.WriteLine("fast dynamic: " + (afterFastDynamic - afterSimpleDynamic).TotalMilliseconds);

            Console.ReadKey();
        }

        private static Dictionary<string, string>[] GenerateProgrammerData()
        {
            var programmers = new List<Dictionary<string, string>>();
            for (var i = 0; i < NUM_PROGRAMMERS; i++)
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

    public static class GenericProgrammerArrayDeserializer
    {
        public static Programmer[] Deserialize(Dictionary<string, string>[] data, Func<Dictionary<string, string>, Programmer> individualDeserializer)
        {
            return data.Select(d => individualDeserializer(d)).ToArray();
        }
    }

    public static class SimpleDynamicDeserializer
    {
        private static Dictionary<Type, SimpleDynamicDeserializationMachine> _cachedMachines = new Dictionary<Type, SimpleDynamicDeserializationMachine>();

        public static T Deserialize<T>(Dictionary<string, string> dict)
        {
            if (!_cachedMachines.ContainsKey(typeof(T)))
                _cachedMachines[typeof(T)] = new SimpleDynamicDeserializationMachine(typeof(T));
            return (T)_cachedMachines[typeof(T)].Deserialize(dict);
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

        public static T Deserialize<T>(Dictionary<string, string> dict)
        {
            if (!_cachedMachines.ContainsKey(typeof(T)))
                _cachedMachines[typeof(T)] = new FastDynamicDeserializationMachine(typeof(T));
            return (T)_cachedMachines[typeof(T)].Deserialize(dict);
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
        public static Programmer DeserializeProgrammer(Dictionary<string, string> dict)
        {
            return new Programmer()
            {
                FirstName = dict["FirstName"],
                MiddleName = dict["MiddleName"],
                LastName = dict["LastName"],
                FavoriteLanguage = dict["FavoriteLanguage"],
                Gender = dict["Gender"],
                Archetype = dict["Archetype"]
            };
        }
    }
}

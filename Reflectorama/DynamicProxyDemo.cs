using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Reflectorama
{
    public static class DynamicProxyDemo
    {
        public static void Run()
        {
            var testProxy = DynamicProxy.CreateProxy<Person>();
            var test = testProxy.Object;

            testProxy.BeforeSet(p => p.FirstName, (object oldValue, object newValue) =>
            {
                Console.WriteLine("changing FirstName from {0} to {1}", oldValue ?? "<NULL>", newValue);
            });

            test.FirstName = "Graeme";
            test.LastName = "Hill";
            test.FirstName = "foo";
            test.FirstName = "bar";

            Console.WriteLine(test);
            Console.ReadKey();
        }
    }

    public class Person
    {
        public virtual string FirstName { get; set; }
        public virtual string LastName { get; set; }

        public override string ToString()
        {
            return string.Format("FirstName: {0} LastName: {1}", FirstName, LastName);
        }
    }

    public class PersonProxy : Person
    {
        private Proxy<Person> _proxy;

        public PersonProxy(Proxy<Person> proxy)
        {
            _proxy = proxy;
        }

        public override string FirstName
        {
            get
            {
                //_proxy.BeforeGet("FirstName");
                return base.FirstName;
            }
            set
            {
                //_proxy.BeforeSet("FirstName");
                base.FirstName = value;
            }
        }
    }

    public class Proxy<T>
    {
        public T Object { get; set; }

        private Dictionary<string, List<Action<object, object>>> _beforeSetCallbacks = new Dictionary<string, List<Action<object, object>>>();
        private Dictionary<string, List<Action<object, object>>> _afterSetCallbacks = new Dictionary<string, List<Action<object, object>>>();

        public void BeforeSet(Expression<Func<T, object>> propertyExpr, Action<object, object> callback)
        {
            var propertyName = ((MemberExpression)propertyExpr.Body).Member.Name;
            if (!_beforeSetCallbacks.ContainsKey(propertyName))
            {
                _beforeSetCallbacks[propertyName] = new List<Action<object, object>>();
            }
            _beforeSetCallbacks[propertyName].Add(callback);
        }

        public void AfterSet(Expression<Func<T, object>> propertyExpr, Action<object, object> callback)
        {
            var propertyName = ((MemberExpression)propertyExpr.Body).Member.Name;
            if (!_afterSetCallbacks.ContainsKey(propertyName))
            {
                _afterSetCallbacks[propertyName] = new List<Action<object, object>>();
            }
            _afterSetCallbacks[propertyName].Add(callback);
        }

        public void HandleBeforeSet(string propertyName, object oldValue, object newValue)
        {
            if (_beforeSetCallbacks.ContainsKey(propertyName))
                foreach (var callback in _beforeSetCallbacks[propertyName])
                    callback(oldValue, newValue);
        }

        public void HandleAfterSet(string propertyName, object oldValue, object newValue)
        {
            if (_afterSetCallbacks.ContainsKey(propertyName))
                foreach (var callback in _afterSetCallbacks[propertyName])
                    callback(oldValue, newValue);
        }

    }

    public class DynamicProxy
    {
        private static int _typeCounter = 0;

        public static Proxy<T> CreateProxy<T>()
        {
            var proxy = new Proxy<T>();
            TypeBuilder typeBuilder = GetTypeBuilder(typeof(T));
            ConstructorBuilder constructor = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
            var genericProxyType = typeof(Proxy<>).MakeGenericType(typeof(T));
            FieldBuilder proxyFieldBuilder = typeBuilder.DefineField("_proxy", genericProxyType, FieldAttributes.Private);

            foreach (var prop in typeof(T).GetProperties())
            {
                CreateProperty(typeBuilder, prop, proxy, proxyFieldBuilder);
            }

            var newType = typeBuilder.CreateType();
            proxy.Object = CreateInstanceOfProxiedType<T>(proxy, newType);
            return proxy;
        }

        private static T CreateInstanceOfProxiedType<T>(Proxy<T> proxy, Type dynamicType)
        {
            var instance = Activator.CreateInstance(dynamicType);
            var proxyField = (FieldInfo)instance.GetType().GetMember("_proxy", BindingFlags.Instance | BindingFlags.NonPublic).First();
            proxyField.SetValue(instance, proxy);
            return (T)instance;
        }

        private static TypeBuilder GetTypeBuilder(Type parentType)
        {
            var typeSignature = parentType.Name + (_typeCounter++);
            var an = new AssemblyName(typeSignature);
            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
            TypeBuilder tb = moduleBuilder.DefineType(
                typeSignature, 
                TypeAttributes.Public 
                | TypeAttributes.Class 
                | TypeAttributes.AutoClass 
                | TypeAttributes.AnsiClass 
                | TypeAttributes.BeforeFieldInit 
                | TypeAttributes.AutoLayout, 
                parentType);
            return tb;
        }

        private static void CreateProperty<T>(TypeBuilder typeBuilder, PropertyInfo parentProperty, Proxy<T> proxy, FieldBuilder proxyFieldBuilder)
        {
            var genericProxyType = typeof(Proxy<>).MakeGenericType(typeof(T));
            var beforeSetInterceptor = genericProxyType.GetMethod("HandleBeforeSet", BindingFlags.Instance | BindingFlags.Public);
            var afterSetInterceptor = genericProxyType.GetMethod("HandleAfterSet", BindingFlags.Instance | BindingFlags.Public);

            var propertyBuilder = typeBuilder.DefineProperty(parentProperty.Name, PropertyAttributes.HasDefault, parentProperty.PropertyType, null);
            var getPropertyMethodBuilder = typeBuilder.DefineMethod(
                parentProperty.GetGetMethod().Name, 
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual, 
                parentProperty.PropertyType, 
                Type.EmptyTypes);
            
            var getterIL = getPropertyMethodBuilder.GetILGenerator();
            // call parent getter
            getterIL.Emit(OpCodes.Ldarg_0);
            getterIL.Emit(OpCodes.Call, parentProperty.GetGetMethod());
            // return result
            getterIL.Emit(OpCodes.Ret);

            var setPropertyMethodBuilder = typeBuilder.DefineMethod(
                parentProperty.GetSetMethod().Name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                null, 
                new[] { parentProperty.PropertyType });

            var setterIL = setPropertyMethodBuilder.GetILGenerator();
            // create variable to store old value
            var oldValueBuilder = setterIL.DeclareLocal(parentProperty.PropertyType);
            // invoke BeforeSet()
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Ldfld, proxyFieldBuilder); // push _proxy
            setterIL.Emit(OpCodes.Ldstr, parentProperty.Name); // push propertyName argument
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Call, parentProperty.GetGetMethod()); // push oldValue argument
            setterIL.Emit(OpCodes.Ldarg_1); // push newValue argument
            setterIL.Emit(OpCodes.Callvirt, beforeSetInterceptor);
            // store the old value in local variable
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Call, parentProperty.GetGetMethod());
            setterIL.Emit(OpCodes.Stloc, oldValueBuilder);
            // call parent setter
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Ldarg_1);
            setterIL.Emit(OpCodes.Call, parentProperty.GetSetMethod());
            // invoke AfterSet()
            setterIL.Emit(OpCodes.Ldarg_0);
            setterIL.Emit(OpCodes.Ldfld, proxyFieldBuilder); // push _proxy
            setterIL.Emit(OpCodes.Ldstr, parentProperty.Name); // push propertyName argument
            setterIL.Emit(OpCodes.Ldloc, oldValueBuilder); // push oldValue argumanet
            setterIL.Emit(OpCodes.Ldarg_1); // push newValue argument
            setterIL.Emit(OpCodes.Callvirt, afterSetInterceptor);
            // return
            setterIL.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getPropertyMethodBuilder);
            propertyBuilder.SetSetMethod(setPropertyMethodBuilder);
        }
    }

}

using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    public static class MpReflection
    {
        public static IEnumerable<Assembly> AllAssemblies
        {
            get
            {
                yield return Assembly.GetAssembly(typeof(Game));

                foreach (ModContentPack mod in LoadedModManager.RunningMods)
                    foreach (Assembly assembly in mod.assemblies.loadedAssemblies)
                        yield return assembly;

                if (Assembly.GetEntryAssembly() != null)
                    yield return Assembly.GetEntryAssembly();
            }
        }

        private static Dictionary<string, Type> types = new Dictionary<string, Type>();
        private static Dictionary<string, Type> pathType = new Dictionary<string, Type>();
        private static Dictionary<string, Func<object, object>> getters = new Dictionary<string, Func<object, object>>();
        private static Dictionary<string, Action<object, object>> setters = new Dictionary<string, Action<object, object>>();

        /// <summary>
        /// Get the value of a static property/field specified by memberPath
        /// </summary>
        public static object GetPropertyOrField(string memberPath)
        {
            return GetPropertyOrField(null, memberPath);
        }

        /// <summary>
        /// Get the value of the property/field specified by memberPath
        /// Type specification in path is not required if instance is provided
        /// </summary>
        public static object GetPropertyOrField(object instance, string memberPath)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            return getters[memberPath](instance);
        }

        public static void SetPropertyOrField(object instance, string memberPath, object value)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            if (setters[memberPath] == null)
                throw new Exception("The value of " + memberPath + " can't be set");

            setters[memberPath](instance, value);
        }

        public static Type PropertyOrFieldType(string memberPath)
        {
            InitPropertyOrField(memberPath);
            return pathType[memberPath];
        }

        public static string AppendType(string memberPath, Type type)
        {
            string[] parts = memberPath.Split(new[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1 || !parts[0].Contains('.'))
                memberPath = type + "/" + memberPath;

            return memberPath;
        }

        private static void InitPropertyOrField(string memberPath)
        {
            if (getters.ContainsKey(memberPath))
                return;

            string[] parts = memberPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new Exception("Path requires at least the type and one member: " + memberPath);

            Type type = GetTypeByName(parts[0]);
            if (type == null)
                throw new Exception("Type " + parts[0] + " not found for path: " + memberPath);

            List<MemberInfo> members = new List<MemberInfo>();
            Type currentType = type;
            bool hasSetter = false;

            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];

                if (!currentType.IsInterface)
                {
                    FieldInfo field = AccessTools.Field(currentType, part);
                    if (field != null)
                    {
                        members.Add(field);
                        currentType = field.FieldType;
                        hasSetter = true;
                        continue;
                    }

                    PropertyInfo property = AccessTools.Property(currentType, part);
                    if (property != null)
                    {
                        members.Add(property);
                        currentType = property.PropertyType;
                        hasSetter = property.GetSetMethod(true) != null;
                        continue;
                    }
                }

                MethodInfo method = AccessTools.Method(currentType, part);
                if (method != null)
                {
                    members.Add(method);
                    currentType = method.ReturnType;
                    hasSetter = false;
                    continue;
                }

                throw new Exception("Member " + part + " not found in path: " + memberPath + ", current type: " + currentType);
            }

            int last = members.Count - 1;
            MemberInfo lastMember = members[last];

            pathType[memberPath] = currentType;

            string methodName = memberPath.Replace('/', '_');
            DynamicMethod getter = new DynamicMethod("MP_Reflection_Getter_" + methodName, typeof(object), new[] { typeof(object) }, true);
            ILGenerator getterGen = getter.GetILGenerator();

            DynamicMethod setter = new DynamicMethod("MP_Reflection_Setter_" + methodName, null, new[] { typeof(object), typeof(object) }, true);
            ILGenerator setterGen = setter.GetILGenerator();

            EmitAccess(type, members, members.Count, getterGen);
            getterGen.Emit(OpCodes.Ret);
            getters[memberPath] = (Func<object, object>)getter.CreateDelegate(typeof(Func<object, object>));

            if (hasSetter)
            {
                EmitAccess(type, members, last, setterGen);

                setterGen.Emit(OpCodes.Ldarg_1);

                if (currentType.IsValueType)
                    setterGen.Emit(OpCodes.Unbox_Any, currentType);
                else
                    setterGen.Emit(OpCodes.Castclass, currentType);

                if (lastMember is FieldInfo field)
                {
                    if (field.IsStatic)
                        setterGen.Emit(OpCodes.Stsfld, field);
                    else
                        setterGen.Emit(OpCodes.Stfld, field);
                }
                else if (lastMember is PropertyInfo prop)
                {
                    MethodInfo setterMethod = prop.GetSetMethod(true);
                    setterGen.Emit(setterMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setterMethod);
                }

                setterGen.Emit(OpCodes.Ret);
                setters[memberPath] = (Action<object, object>)setter.CreateDelegate(typeof(Action<object, object>));
            }
            else
            {
                setters[memberPath] = null;
            }
        }

        private static void EmitAccess(Type type, List<MemberInfo> members, int count, ILGenerator gen)
        {
            if (!members[0].IsStatic())
            {
                gen.Emit(OpCodes.Ldarg_0);

                if (type.IsValueType)
                    gen.Emit(OpCodes.Unbox_Any, type);
                else
                    gen.Emit(OpCodes.Castclass, type);
            }

            for (int i = 0; i < count; i++)
            {
                MemberInfo member = members[i];

                if (member is FieldInfo field)
                {
                    if (field.IsStatic)
                        gen.Emit(OpCodes.Ldsfld, field);
                    else
                        gen.Emit(OpCodes.Ldfld, field);

                    BoxIfNeeded(field.FieldType, gen);
                }
                else if (member is PropertyInfo prop)
                {
                    MethodInfo m = prop.GetGetMethod(true);
                    gen.Emit(m.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, m);
                    BoxIfNeeded(m.ReturnType, gen);
                }
                else if (member is MethodInfo method)
                {
                    gen.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
                    BoxIfNeeded(method.ReturnType, gen);
                }
            }
        }

        private static void BoxIfNeeded(Type type, ILGenerator gen)
        {
            if (type.IsValueType)
                gen.Emit(OpCodes.Box, type);
        }

        public static Type GetTypeByName(string name)
        {
            if (types.TryGetValue(name, out Type cached))
                return cached;

            foreach (Assembly assembly in AllAssemblies)
            {
                Type type = assembly.GetType(name, false);
                if (type != null)
                {
                    types[name] = type;
                    return type;
                }
            }

            types[name] = null;
            return null;
        }
    }

    public static class ReflectionExtensions
    {
        public static object GetPropertyOrField(this object obj, string memberPath)
        {
            return MpReflection.GetPropertyOrField(obj, memberPath);
        }

        public static void SetPropertyOrField(this object obj, string memberPath, object value)
        {
            MpReflection.SetPropertyOrField(obj, memberPath, value);
        }

        private static readonly MethodInfo exposeSmallComps = AccessTools.Method(typeof(Game), "ExposeSmallComponents");
        public static void ExposeSmallComponents(this Game game)
        {
            exposeSmallComps.Invoke(game, null);
        }

        public static bool IsStatic(this MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.IsStatic;
            else if (member is PropertyInfo prop)
                return prop.GetGetMethod(true).IsStatic;
            else if (member is MethodInfo method)
                return method.IsStatic;
            else
                throw new Exception("Invalid member " + member?.GetType());
        }
    }
}

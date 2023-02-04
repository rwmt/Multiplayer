using HarmonyLib;
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
        public delegate object Getter(object instance, object index);
        public delegate void Setter(object instance, object value, object index);

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

        private static Dictionary<string, Type> types = new();
        private static Dictionary<string, Type> pathTypes = new();
        private static Dictionary<string, Type> indexTypes = new();
        private static Dictionary<string, Getter> getters = new();
        private static Dictionary<string, Setter> setters = new();

        /// <summary>
        /// Get the value of a static property/field in type specified by memberPath
        /// </summary>
        public static object GetValueStatic(Type type, string memberPath, object index = null)
        {
            return GetValue(null, type + "/" + memberPath, index);
        }

        /// <summary>
        /// Get the value of a static property/field specified by memberPath
        /// </summary>
        public static object GetValueStatic(string memberPath, object index = null)
        {
            return GetValue(null, memberPath, index);
        }

        /// <summary>
        /// Get the value of a property/field specified by memberPath
        /// Type specification in path is not required if instance is provided
        /// </summary>
        public static object GetValue(object instance, string memberPath, object index = null)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            return getters[memberPath](instance, index);
        }

        public static void SetValueStatic(Type type, string memberPath, object value, object index = null)
        {
            SetValue(null, type + "/" + memberPath, value, index);
        }

        public static void SetValue(object instance, string memberPath, object value, object index = null)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            if (setters[memberPath] == null)
                throw new Exception($"The value of {memberPath} can't be set");

            setters[memberPath](instance, value, index);
        }

        public static Type PathType(string memberPath)
        {
            InitPropertyOrField(memberPath);
            return pathTypes[memberPath];
        }

        public static Type IndexType(string memberPath)
        {
            InitPropertyOrField(memberPath);
            return indexTypes.TryGetValue(memberPath, out Type indexType) ? indexType : null;
        }

        /// <summary>
        /// Appends the type name to the path if needed
        /// </summary>
        public static string AppendType(string memberPath, Type type)
        {
            string[] parts = memberPath.Split(new[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1 || !parts[0].Contains('.'))
                memberPath = type + "/" + memberPath;

            return memberPath;
        }

        public static string RemoveType(string memberPath)
        {
            string[] parts = memberPath.Split(new[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1 || !parts[0].Contains('.'))
                return memberPath;

            return parts[1];
        }

        private static void InitPropertyOrField(string memberPath)
        {
            if (getters.ContainsKey(memberPath))
                return;

            string[] parts = memberPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new Exception($"Path requires at least the type and one member: {memberPath}");

            Type type = GetTypeByName(parts[0]);
            if (type == null)
                throw new Exception($"Type {parts[0]} not found for path: {memberPath}");

            List<MemberInfo> members = new List<MemberInfo>();
            Type currentType = type;
            bool hasSetter = false;

            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                MemberInfo memberFound = null;

                if (part == "[]")
                {
                    if (currentType.IsArray || currentType == typeof(Array))
                    {
                        currentType = currentType.GetElementType();
                        memberFound = new ArrayAccess() { ElementType = currentType };
                        members.Add(memberFound);
                        hasSetter = true;
                        indexTypes[memberPath] = typeof(int);

                        continue;
                    }

                    PropertyInfo indexer = currentType.GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length == 1);
                    if (indexer == null) continue;

                    Type indexType = indexer.GetIndexParameters()[0].ParameterType;

                    memberFound = indexer;
                    members.Add(indexer);
                    currentType = indexer.PropertyType;
                    hasSetter = indexer.GetSetMethod(true) != null;
                    indexTypes[memberPath] = indexType;

                    continue;
                }

                if (!currentType.IsInterface)
                {
                    FieldInfo field = AccessTools.Field(currentType, part);
                    if (field != null)
                    {
                        memberFound = field;
                        members.Add(field);
                        currentType = field.FieldType;
                        hasSetter = true;
                        continue;
                    }

                    PropertyInfo property = AccessTools.Property(currentType, part);
                    if (property != null)
                    {
                        memberFound = property;
                        members.Add(property);
                        currentType = property.PropertyType;
                        hasSetter = property.GetSetMethod(true) != null;
                        continue;
                    }
                }

                MethodInfo method = AccessTools.Method(currentType, part);
                if (method != null)
                {
                    memberFound = method;
                    members.Add(method);
                    currentType = method.ReturnType;
                    hasSetter = false;
                    continue;
                }

                throw new Exception($"Member {part} not found in path: {memberPath}, current type: {currentType}");
            }

            MemberInfo lastMember = members.Last();
            pathTypes[memberPath] = currentType;

            string methodName = memberPath.Replace('/', '_');
            DynamicMethod getter = new DynamicMethod("MP_Reflection_Getter_" + methodName, typeof(object), new[] { typeof(object), typeof(object) }, true);
            ILGenerator getterGen = getter.GetILGenerator();

            EmitAccess(type, members, members.Count, getterGen, 1, lastMember);
            getterGen.Emit(OpCodes.Ret);
            getters[memberPath] = (Getter)getter.CreateDelegate(typeof(Getter));

            if (!hasSetter)
            {
                setters[memberPath] = null;
            }
            else
            {
                DynamicMethod setter = new DynamicMethod("MP_Reflection_Setter_" + methodName, null, new[] { typeof(object), typeof(object), typeof(object) }, true);
                ILGenerator setterGen = setter.GetILGenerator();

                // Load the instance
                EmitAccess(type, members, members.Count - 1, setterGen, 2, lastMember);

                // Load the index
                if (lastMember is ArrayAccess)
                {
                    setterGen.Emit(OpCodes.Ldarg_2);
                    setterGen.Emit(OpCodes.Unbox_Any, type);
                }
                else if (lastMember is PropertyInfo prop && prop.GetIndexParameters().Length == 1)
                {
                    Type indexType = prop.GetIndexParameters()[0].ParameterType;
                    setterGen.Emit(OpCodes.Ldarg_2);
                    setterGen.Emit(OpCodes.Unbox_Any, indexType);
                }

                // Load and unbox/cast the value
                setterGen.Emit(OpCodes.Ldarg_1);
                setterGen.Emit(OpCodes.Unbox_Any, currentType);

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
                else if (lastMember is ArrayAccess)
                {
                    setterGen.Emit(OpCodes.Stelem, currentType);
                }

                setterGen.Emit(OpCodes.Ret);
                setters[memberPath] = (Setter)setter.CreateDelegate(typeof(Setter));
            }
        }

        private static void EmitAccess(Type type, List<MemberInfo> members, int count, ILGenerator gen, int indexArg, MemberInfo lastMember)
        {
            if (!members[0].IsStatic())
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Unbox_Any, type);
            }

            for (int i = 0; i < count; i++)
            {
                MemberInfo member = members[i];
                Type memberType;

                bool dontBox = false;

                if (member is FieldInfo field)
                {
                    gen.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
                    memberType = field.FieldType;
                }
                else if (member is PropertyInfo prop)
                {
                    if (prop.GetIndexParameters().Length == 1)
                    {
                        Type indexType = prop.GetIndexParameters()[0].ParameterType;
                        gen.Emit(OpCodes.Ldarg, indexArg);
                        gen.Emit(OpCodes.Unbox_Any, indexType);
                    }

                    MethodInfo m = prop.GetGetMethod(true);
                    gen.Emit(m.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, m);
                    memberType = m.ReturnType;
                }
                else if (member is MethodInfo method)
                {
                    gen.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
                    memberType = method.ReturnType;
                }
                else if (member is ArrayAccess arr)
                {
                    memberType = arr.ElementType;

                    gen.Emit(OpCodes.Ldarg, indexArg);
                    gen.Emit(OpCodes.Unbox_Any, typeof(int));

                    if (memberType.IsValueType && member != lastMember)
                    {
                        gen.Emit(OpCodes.Ldelema, memberType);
                        dontBox = true;
                    }
                    else
                    {
                        gen.Emit(OpCodes.Ldelem, memberType);
                    }
                }
                else
                {
                    throw new Exception("Unsupported member type " + member.GetType());
                }

                if (!dontBox && memberType.IsValueType)
                    gen.Emit(OpCodes.Box, memberType);
            }
        }

        public static Type GetTypeByName(string name)
        {
            if (types.TryGetValue(name, out Type cached))
                return cached;

            Type type =
                AllAssemblies.Select(a => a.GetType(name)).AllNotNull().FirstOrDefault() ??
                Type.GetType(name) ??
                AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).AllNotNull().FirstOrDefault();

            types[name] = type;
            return type;
        }
    }

    public class ArrayAccess : MemberInfo
    {
        public Type ElementType { get; set; }

        public override MemberTypes MemberType => throw new NotImplementedException();

        public override string Name => throw new NotImplementedException();

        public override Type DeclaringType => throw new NotImplementedException();

        public override Type ReflectedType => throw new NotImplementedException();

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }
    }

    public static class ReflectionExtensions
    {
        public static object GetPropertyOrField(this object obj, string memberPath, object index = null)
        {
            return MpReflection.GetValue(obj, memberPath, index);
        }

        public static void SetPropertyOrField(this object obj, string memberPath, object value, object index = null)
        {
            MpReflection.SetValue(obj, memberPath, value, index);
        }

        public static bool IsStatic(this MemberInfo member) => member switch
        {
            FieldInfo field => field.IsStatic,
            PropertyInfo prop => prop.GetGetMethod(true).IsStatic,
            MethodInfo method => method.IsStatic,
            TypeInfo type => type.IsAbstract && type.IsSealed,
            _ => false,
        };
    }
}

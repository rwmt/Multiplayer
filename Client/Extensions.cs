using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
            }
        }

        private static Dictionary<string, Type> types = new Dictionary<string, Type>();
        private static Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();
        private static Dictionary<string, Func<object, object>> getters = new Dictionary<string, Func<object, object>>();
        private static Dictionary<string, Action<object, object>> setters = new Dictionary<string, Action<object, object>>();

        public static object GetPropertyOrField(object obj, string memberPath)
        {
            InitPropertyOrField(memberPath);
            return getters[memberPath](obj);
        }

        public static void SetPropertyOrField(object obj, string memberPath, object value)
        {
            InitPropertyOrField(memberPath);
            setters[memberPath](obj, value);
        }

        public static MemberInfo PropertyOrField(string memberPath)
        {
            InitPropertyOrField(memberPath);
            return members[memberPath];
        }

        public static Type PropertyOrFieldType(string memberPath)
        {
            InitPropertyOrField(memberPath);
            MemberInfo member = members[memberPath];
            return member is PropertyInfo prop ? prop.PropertyType : (member as FieldInfo).FieldType;
        }

        private static void InitPropertyOrField(string memberPath)
        {
            string[] parts = memberPath.Split('/');
            if (parts.Length < 2)
                throw new Exception("Path requires at least the type and one member");

            Type type = GetTypeByName(parts[0]);
            if (type == null)
                throw new Exception("Type " + parts[0] + " not found");

            if (!getters.TryGetValue(memberPath, out Func<object, object> del))
            {
                ParameterExpression instParam = Expression.Parameter(type, "obj");
                Expression evalExpr = parts.Skip(1).Take(parts.Length - 2).Aggregate((Expression)instParam, PropertyOrFieldExpression);
                Delegate eval = Expression.Lambda(evalExpr, instParam).Compile();
                MemberInfo lastMember = (MemberInfo)AccessTools.Property(evalExpr.Type, parts.Last()) ?? AccessTools.Field(evalExpr.Type, parts.Last());
                if (lastMember == null)
                    throw new Exception("Member " + memberPath + " not found");

                if (lastMember is PropertyInfo property)
                {
                    getters[memberPath] = inst => property.GetValue(eval.DynamicInvoke(inst), null);
                    setters[memberPath] = (inst, val) => property.SetValue(eval.DynamicInvoke(inst), val, null);
                }
                else if (lastMember is FieldInfo field)
                {
                    getters[memberPath] = inst => field.GetValue(eval.DynamicInvoke(inst));
                    setters[memberPath] = (inst, val) => field.SetValue(eval.DynamicInvoke(inst), val);
                }

                members[memberPath] = lastMember;
            }
        }

        private static Expression PropertyOrFieldExpression(Expression input, string name)
        {
            FieldInfo field = AccessTools.Field(input.Type, name);
            if (field != null)
                return Expression.Field(field.IsStatic ? null : input, field);

            PropertyInfo property = AccessTools.Property(input.Type, name);
            if (property != null)
                return Expression.Property(property.GetGetMethod().IsStatic ? null : input, property);

            throw new Exception("Property or field " + name + " not found");
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

            return null;
        }
    }

    public static class Extensions
    {
        private static readonly MethodInfo exposeSmallComps = AccessTools.Method(typeof(Game), "ExposeSmallComponents");

        public static void ExposeSmallComponents(this Game game)
        {
            exposeSmallComps.Invoke(game, null);
        }

        public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
        {
            return t.AllSubclasses().Concat(t);
        }

        public static IEnumerable<Type> AllImplementing(this Type t)
        {
            return from x in GenTypes.AllTypes where t.IsAssignableFrom(x) select x;
        }

        // sets the current Faction.OfPlayer
        // applies faction's map components if map not null
        public static void PushFaction(this Map map, Faction faction)
        {
            FactionContext.Push(faction);
            if (map != null && faction != null)
                map.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }

        public static void PushFaction(this Map map, string factionId)
        {
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
            map.PushFaction(faction);
        }

        public static void PopFaction(this Container<Map> c)
        {
            PopFaction(c.Value);
        }

        public static void PopFaction(this Map map)
        {
            Faction faction = FactionContext.Pop();
            if (map != null && faction != null)
                map.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }

        public static Map GetMap(this ScheduledCommand cmd)
        {
            return Find.Maps.FirstOrDefault(map => map.uniqueID == cmd.mapId);
        }
    }
}

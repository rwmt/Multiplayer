using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using Multiplayer.API;
using Multiplayer.Client.Saving;
using Multiplayer.Common;

using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    #region Implementation

    public class PersistentDialog_NodeTree : PersistentDialog<Dialog_NodeTree>
    {
        public PersistentDialog_NodeTree(Map map) : base(map)
        {
        }

        public PersistentDialog_NodeTree(Map map, Dialog_NodeTree dialog) : base(map, dialog)
        {
        }

        protected override void ExposeDataClean()
        {

        }

        protected override Dialog_NodeTree ExposeDataInstance(DiaNode startNode, bool radioMode, string title)
        {
            return new Dialog_NodeTree(startNode, false, radioMode, title);
        }

        protected override void ExposeDataSaveLoad()
        {

        }
    }

    public class PersistentDialog_NodeTreeWithFactionInfo : PersistentDialog<Dialog_NodeTreeWithFactionInfo>
    {
        // temp vars, keep them clean
        Faction faction;

        public PersistentDialog_NodeTreeWithFactionInfo(Map map) : base(map)
        {
            // Used by ScribeExtractor
        }

        public PersistentDialog_NodeTreeWithFactionInfo(Map map, Dialog_NodeTreeWithFactionInfo dialog) : base(map, dialog)
        {
            // Used by PersistentDialog
        }

        protected override void ExposeDataSaveLoad()
        {
            if (Scribe.mode == LoadSaveMode.Saving) {
                faction = dialog.faction;
            }
            Scribe_References.Look(ref faction, "faction");
        }

        protected override Dialog_NodeTreeWithFactionInfo ExposeDataInstance(DiaNode startNode, bool radioMode, string title)
        {
            return new Dialog_NodeTreeWithFactionInfo(startNode, faction, false, radioMode, title);
        }

        protected override void ExposeDataClean()
        {
            faction = null;
        }
    }

    public class PersistentDialog_Negotiation : PersistentDialog<Dialog_Negotiation>
    {
        // temp vars, keep them clean
        Pawn negotiator;
        Faction faction;

        public PersistentDialog_Negotiation(Map map) : base(map)
        {
            // Used by ScribeExtractor
        }

        public PersistentDialog_Negotiation(Map map, Dialog_Negotiation dialog) : base(map, dialog)
        {
            // Used by PersistentDialog
        }

        protected override void ExposeDataSaveLoad()
        {
            if (Scribe.mode == LoadSaveMode.Saving) {
                negotiator = dialog.negotiator;
                faction = dialog.commTarget.GetFaction();
            }

            Scribe_References.Look(ref negotiator, "negotiator");
            Scribe_References.Look(ref faction, "commTarget");
        }

        protected override Dialog_Negotiation ExposeDataInstance(DiaNode startNode, bool radioMode, string title)
        {
            return new Dialog_Negotiation(negotiator, faction, startNode, radioMode);
        }

        protected override void ExposeDataClean()
        {
            negotiator = null;
            faction = null;
        }
    }

    #endregion

    #region Definition

    [StaticConstructorOnStartup]
    public abstract class PersistentDialog : IExposable
    {
        protected static readonly Dictionary<Type, Type> bindings = new Dictionary<Type, Type>();

        public Map map;
        public int id;

        public int ver;

        protected PersistentDialog(Map map)
        {
            this.map = map;
        }

        public static PersistentDialog CreateInstance(Map map, Dialog_NodeTree dialog)
        {
            Type target = bindings.TryGetValue(dialog.GetType());

            if (target == null) {
                Log.Warning($"Unknown Window Type {target}");

                return null;
            }

            return (PersistentDialog) Activator.CreateInstance(target, map, dialog);
        }

        public abstract Dialog_NodeTree Dialog { get; }

        [SyncMethod]
        public void Click(int ver, int opt)
        {
            if (ver != this.ver) return;
            Dialog.curNode.options[opt].Activate();
            this.ver++;
        }

        /// <summary>
        /// Finds the given PersistentDialog given any dialog.
        /// </summary>
        /// <remarks>Used by Harmony injections to find the reverse bind</remarks>
        /// <returns>PersistentDialog that wraps the dialog</returns>
        /// <param name="dialog">Any window</param>
        public static PersistentDialog FindDialog(Window dialog)
        {
            return Find.Maps?.SelectMany(m => m.MpComp().mapDialogs).FirstOrDefault(d => d.Dialog == dialog);
        }

        /// <summary>
        /// Finds implementations of PersistentDialog and adds them with their generic type
        /// in the given assembly
        /// </summary>
        /// <param name="assembly">The assembly</param>
        public static void BindAll(Assembly assembly)
        {
            var types = assembly.GetTypes()
                .Select(type => new { dialog = FindDialogForType(type), proxy = type })
                .Where(kvp => kvp.dialog != null);

            foreach (var kvp in types) {
                bindings.Add(kvp.dialog, kvp.proxy);
            }
        }

        /// <summary>
        /// Helper filter for BindAll
        /// </summary>
        /// <see cref="BindAll(Assembly)"/>
        /// <returns>The generic Type that implements PersistentDialog or null</returns>
        /// <param name="type">Any type</param>
        static Type FindDialogForType(Type type)
        {
            var proxy = type.GetTypeWithGenericDefinition(typeof(PersistentDialog<>));

            if (proxy == null || type.IsAbstract || type.IsInterface) {
                return null;
            }

            return proxy.GetGenericArguments().FirstOrDefault();
        }

        /// <summary>
        /// Manually bind a Dialog_Nodetree to its Proxy
        /// </summary>
        /// <remarks>Isn't used internally. It's here for modders.</remarks>
        /// <param name="target">Target must be implementation of Dialog_NodeTree.</param>
        /// <param name="proxy">Proxy must wrap the Target.</param>
        public static void Bind(Type target, Type proxy)
        {
            if (!(typeof(Dialog_NodeTree).IsAssignableFrom(target))) {
                throw new ArgumentException($"Registered target, {target}, was not assignable from {typeof(Dialog_NodeTree)}");
            }

            if (proxy.IsInterface || proxy.IsAbstract) {
                throw new ArgumentException($"Registered type, {proxy}, is interface or abstract and cannot be registered");
            }

            if (!(typeof(PersistentDialog)).IsAssignableFrom(proxy)) {
                throw new ArgumentException($"Registered type, {proxy}, was not assignable from {typeof(PersistentDialog)}");
            }

            bindings.Add(target, proxy);
        }

        public abstract void ExposeData();

        protected static MethodInfo ScribeValues = typeof(Scribe_Values).GetMethod("Look");
        protected static MethodInfo ScribeDefs = typeof(Scribe_Defs).GetMethod("Look");
        protected static MethodInfo ScribeReferences = typeof(Scribe_References).GetMethods().First(m => m.Name == "Look" && (m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericParameter ?? false));
        protected static MethodInfo ScribeDeep = typeof(Scribe_Deep).GetMethods().First(m => m.Name == "Look" && m.GetParameters().Length == 3);
    }

    public abstract class PersistentDialog<T> : PersistentDialog where T : Dialog_NodeTree
    {
        public T dialog;

        protected PersistentDialog(Map map) : base(map)
        {

        }

        protected PersistentDialog(Map map, T dialog) : this(map)
        {
            id = Multiplayer.GlobalIdBlock.NextId();
            this.dialog = dialog;
        }

        bool radioMode;
        string title;

        // tmp vars, always clean
        DiaNode rootNode;
        List<DiaNodeSave> saveNodes;
        List<FieldSave> fieldValues;

        public override Dialog_NodeTree Dialog => dialog;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) {
                Scribe_Values.Look(ref id, "id");
                Scribe_Custom.LookValue(dialog.soundAmbient == SoundDefOf.RadioComms_Ambience, "radioMode");
                Scribe_Values.Look(ref dialog.title, "title");

                ExposeDataSaveLoad();

                var nodes = dialog.curNode.TraverseNodes().ToList();

                rootNode = nodes[0];

                saveNodes = new List<DiaNodeSave>();
                foreach (var node in nodes)
                    saveNodes.Add(new DiaNodeSave(this, node));

                fieldValues = nodes
                    .SelectMany(n => n.options)
                    .SelectMany(o => DelegateValues(o.action).Concat(DelegateValues(o.linkLateBind)))
                    .Distinct(new FieldSaveEquality())
                    .ToList();

                Scribe_Collections.Look(ref fieldValues, "fieldValues", LookMode.Deep);
                Scribe_Collections.Look(ref saveNodes, "nodes", LookMode.Deep);

                Clean();
            } else {
                Scribe_Values.Look(ref id, "id");
                Scribe_Values.Look(ref radioMode, "radioMode");
                Scribe_Values.Look(ref title, "title");

                ExposeDataSaveLoad();

                Scribe_Collections.Look(ref fieldValues, "fieldValues", LookMode.Deep, this);
                Scribe_Collections.Look(ref saveNodes, "nodes", LookMode.Deep, this);

                rootNode = saveNodes[0].node;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit) {
                dialog = ExposeDataInstance(rootNode, radioMode, title);
                dialog.doCloseX = true;
                dialog.closeOnCancel = true;

                Clean();
            }
        }

        void Clean()
        {
            saveNodes = null;
            fieldValues = null;
            rootNode = null;

            ExposeDataClean();
        }

        protected abstract void ExposeDataSaveLoad();

        protected abstract T ExposeDataInstance(DiaNode startNode, bool radioMode, string title);

        protected abstract void ExposeDataClean();

        private IEnumerable<FieldSave> DelegateValues(Delegate del)
        {
            if (del == null) yield break;

            yield return new FieldSave(this, del.GetType(), del);

            var target = del.Target;
            if (target == null) yield break;

            yield return new FieldSave(this, target.GetType(), target);

            foreach (var field in target.GetType().GetDeclaredInstanceFields())
                if (!field.FieldType.IsCompilerGenerated())
                    yield return new FieldSave(this, field.FieldType, field.GetValue(target));
        }

        class DiaNodeSave : IExposable
        {
            public PersistentDialog<T> parent;
            public DiaNode node;

            public DiaNodeSave(PersistentDialog<T> parent)
            {
                this.parent = parent;
            }

            public DiaNodeSave(PersistentDialog<T> parent, DiaNode node) : this(parent)
            {
                this.node = node;
            }

            private string text;
            private List<DiaOptionSave> saveOptions;

            public void ExposeData()
            {
                if (Scribe.mode == LoadSaveMode.Saving) {
                    Scribe_Values.Look(ref node.text, "text");

                    var saveOptions = node.options.Select(o => new DiaOptionSave(parent, o)).ToList();
                    Scribe_Collections.Look(ref saveOptions, "options", LookMode.Deep);
                }

                if (Scribe.mode == LoadSaveMode.LoadingVars) {
                    Scribe_Values.Look(ref text, "text");
                    Scribe_Collections.Look(ref saveOptions, "options", LookMode.Deep, parent);

                    node = new DiaNode(text);
                }

                if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                    node.options = saveOptions.Select(o => o.opt).ToList();
            }
        }

        class DiaOptionSave : IExposable
        {
            public PersistentDialog<T> parent;
            public DiaOption opt;

            public DiaOptionSave(PersistentDialog<T> parent)
            {
                this.parent = parent;
            }

            public DiaOptionSave(PersistentDialog<T> parent, DiaOption opt) : this(parent)
            {
                this.opt = opt;
            }

            private string text;
            private bool resolveTree;
            private int linkIndex;
            private bool disabled;
            private string disabledReason;
            private SoundDef clickSound;

            private int actionIndex;
            private int linkLateBindIndex;

            public void ExposeData()
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    Scribe_Values.Look(ref opt.text, "text");
                    Scribe_Values.Look(ref opt.resolveTree, "resolveTree");
                    Scribe_Custom.LookValue(parent.saveNodes.FindIndex(n => n.node == opt.link), "linkIndex", true);
                    Scribe_Values.Look(ref opt.disabled, "disabled");
                    Scribe_Values.Look(ref opt.disabledReason, "disabledReason");
                    Scribe_Defs.Look(ref opt.clickSound, "clickSound");

                    Scribe_Custom.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, opt.action)), "actionIndex", true);
                    Scribe_Custom.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, opt.linkLateBind)), "linkLateBindIndex", true);
                }

                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    Scribe_Values.Look(ref text, "text");
                    Scribe_Values.Look(ref resolveTree, "resolveTree");
                    Scribe_Values.Look(ref linkIndex, "linkIndex", -1);
                    Scribe_Values.Look(ref disabled, "disabled");
                    Scribe_Values.Look(ref disabledReason, "disabledReason");
                    Scribe_Defs.Look(ref clickSound, "clickSound");

                    Scribe_Values.Look(ref actionIndex, "actionIndex");
                    Scribe_Values.Look(ref linkLateBindIndex, "linkLateBindIndex");

                    opt = new DiaOption()
                    {
                        text = text,
                        resolveTree = resolveTree,
                        disabled = disabled,
                        disabledReason = disabledReason,
                        clickSound = clickSound
                    };
                }

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    opt.link = parent.saveNodes.ElementAtOrDefault(linkIndex)?.node;
                    opt.action = (Action)parent.fieldValues.ElementAtOrDefault(actionIndex)?.value;
                    opt.linkLateBind = (Func<DiaNode>)parent.fieldValues.ElementAtOrDefault(linkLateBindIndex)?.value;
                }
            }
        }

        class FieldSave : IExposable
        {
            public PersistentDialog<T> parent;
            public Type type;
            public string typeName;
            public object value;
            private LookMode mode;

            public FieldSave(PersistentDialog<T> parent)
            {
                this.parent = parent;
            }

            public FieldSave(PersistentDialog<T> parent, Type type, object value) : this(parent)
            {
                this.type = type;
                typeName = type.FullName;
                this.value = value;

                if (typeof(Delegate).IsAssignableFrom(type))
                    mode = (LookMode)101;
                else if (ParseHelper.HandlesType(type))
                    mode = LookMode.Value;
                else if (typeof(Def).IsAssignableFrom(type))
                    mode = LookMode.Def;
                else if (value is Thing thing && !thing.Spawned)
                    mode = LookMode.Deep;
                else if (typeof(ILoadReferenceable).IsAssignableFrom(type))
                    mode = LookMode.Reference;
                else if (typeof(IExposable).IsAssignableFrom(type))
                    mode = LookMode.Deep;
                else
                    mode = (LookMode)100;
            }

            private Dictionary<string, int> fields;

            private string methodType;
            private string methodName;
            private int targetIndex;

            public void ExposeData()
            {
                Scribe_Values.Look(ref mode, "mode");

                Scribe_Values.Look(ref typeName, "type");
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                    type = MpReflection.GetTypeByName(typeName);

                if (mode == LookMode.Value)
                {
                    var args = new[] { value, "value", type.GetDefaultValue(), false };
                    ScribeValues.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == LookMode.Def)
                {
                    var args = new[] { value, "value" };
                    ScribeDefs.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == LookMode.Reference)
                {
                    var args = new[] { value, "value", false };
                    ScribeReferences.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                        value = args[0];
                }
                else if (mode == LookMode.Deep)
                {
                    var args = new[] { value, "value", new object[0] };
                    ScribeDeep.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == (LookMode)100)
                {
                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        fields = new Dictionary<string, int>();
                        foreach (var field in type.GetDeclaredInstanceFields())
                            if (!field.FieldType.IsCompilerGenerated())
                                fields[field.Name] = parent.fieldValues.FindIndex(v => Equals(v.value, field.GetValue(value)));

                        Scribe_Collections.Look(ref fields, "fields");
                    }

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        value = Activator.CreateInstance(type);
                        Scribe_Collections.Look(ref fields, "fields");
                    }

                    if (Scribe.mode == LoadSaveMode.PostLoadInit)
                    {
                        foreach (var kv in fields)
                            value.SetPropertyOrField(kv.Key, parent.fieldValues[kv.Value].value);
                    }
                }
                else if (mode == (LookMode)101)
                {
                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        var del = (Delegate)value;

                        Scribe_Custom.LookValue(del.Method.DeclaringType.FullName, "methodType");
                        Scribe_Custom.LookValue(del.Method.Name, "methodName");

                        if (del.Target != null)
                            Scribe_Custom.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, del.Target)), "targetIndex", true);
                    }

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        Scribe_Values.Look(ref methodType, "methodType");
                        Scribe_Values.Look(ref methodName, "methodName");
                        Scribe_Values.Look(ref targetIndex, "targetIndex");
                    }

                    if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                    {
                        if (targetIndex != -1)
                        {
                            object target = parent.fieldValues[targetIndex].value;
                            value = Delegate.CreateDelegate(type, target, methodName);
                        }
                        else
                        {
                            value = Delegate.CreateDelegate(type, GenTypes.GetTypeInAnyAssembly(methodType), methodName);
                        }
                    }
                }
            }
        }

        class FieldSaveEquality : IEqualityComparer<FieldSave>
        {
            public bool Equals(FieldSave x, FieldSave y)
            {
                return Equals(x.value, y.value);
            }

            public int GetHashCode(FieldSave obj)
            {
                return obj.value?.GetHashCode() ?? 0;
            }
        }
    }

    #endregion

    #region Harmony

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogNodeTree
    {
        static bool Prefix(Window window)
        {
            var map = Multiplayer.MapContext;

            if (map == null) return true;

            PersistentDialog persistentDialog = null;

            if (window is Dialog_NodeTree dialog_NodeTree) {
                persistentDialog = PersistentDialog.CreateInstance(map, dialog_NodeTree);
            }

            if (persistentDialog == null) return true;

            window.doCloseX = true;
            window.closeOnCancel = true;

            map.MpComp().mapDialogs.Add(persistentDialog);

            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PreClose))]
    static class DialogNodeTreePreClose
    {
        static bool Prefix(Dialog_NodeTree __instance)
        {
            if (Multiplayer.Client == null) return true;

            var type = __instance.GetType();
            if ((__instance is Dialog_NodeTree)) return true;

            return !Multiplayer.InInterface;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PostClose))]
    static class DialogNodeTreePostClose
    {
        static bool Prefix(Dialog_NodeTree __instance)
        {
            if (Multiplayer.Client == null) return true;
            if (!Multiplayer.InInterface) return true;

            if (PersistentDialog.FindDialog(__instance) == null) {
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.TryRemove), new[] { typeof(Window), typeof(bool) })]
    static class WindowStackTryRemove
    {
        static void Postfix(Window window)
        {
            if (Multiplayer.Client == null) return;
            if (Multiplayer.InInterface) return;

            var session = PersistentDialog.FindDialog(window);
            session?.map.MpComp().mapDialogs.Remove(session);
        }
    }

    [HarmonyPatch(typeof(DiaOption), nameof(DiaOption.Activate))]
    static class DiaOptionActivate
    {
        static bool Prefix(DiaOption __instance)
        {
            if (Multiplayer.InInterface)
            {
                var session = PersistentDialog.FindDialog(__instance.dialog);
                if (session == null) return true;

                session.Click(session.ver, session.Dialog.curNode.options.IndexOf(__instance));

                return false;
            }

            return true;
        }
    }

    #endregion
}

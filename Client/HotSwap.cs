using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;
using Harmony;
using Harmony.ILCopying;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HotSwappableAttribute : Attribute
    {
    }

    static class HotSwap
    {
        static Dictionary<Assembly, FileInfo> AssemblyFiles = new Dictionary<Assembly, FileInfo>();
        static Dictionary<string, Assembly> AssembliesByName = new Dictionary<string, Assembly>();

        static HotSwap()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                AssembliesByName[a.FullName] = a;

            int i = 0;

            foreach (var mod in LoadedModManager.RunningMods)
            {
                string path = Path.Combine(mod.RootDir, "Assemblies");
                string path2 = Path.Combine(GenFilePaths.CoreModsFolderPath, path);
                DirectoryInfo directoryInfo = new DirectoryInfo(path2);
                if (!directoryInfo.Exists)
                    continue;

                foreach (FileInfo fileInfo in directoryInfo.GetFiles("*.*", SearchOption.AllDirectories))
                {
                    if (fileInfo.Extension.ToLower() != ".dll") continue;

                    // This assumes that all assemblies were loaded without any errors
                    AssemblyFiles[mod.assemblies.loadedAssemblies[i]] = fileInfo;
                    i++;
                }
            }
        }

        private static Dictionary<MethodBase, DynamicMethod> dynMethods = new Dictionary<MethodBase, DynamicMethod>();
        private static int count;

        private static MethodInfo AddRef = AccessTools.Method(typeof(DynamicMethod), "AddRef");

        public static void DoHotSwap()
        {
            var asms = AssemblyFiles.Where(kv => kv.Key.GetTypes().Any(t => t.HasAttribute<HotSwappableAttribute>()));

            foreach (var kv in asms)
            {
                var asm = kv.Key;
                var module = asm.GetModules()[0];

                using (var dnModule = ModuleDefMD.Load(kv.Value.FullName))
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!type.HasAttribute<HotSwappableAttribute>()) continue;

                        var dnType = dnModule.FindReflection(type.FullName);
                        var flags = BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

                        foreach (var method in type.GetMethods(flags))
                        {
                            if (method.GetMethodBody() == null) continue;

                            byte[] code = method.GetMethodBody().GetILAsByteArray();
                            var dnMethod = dnType.FindMethod(method.Name);

                            var methodBody = dnMethod.Body;
                            byte[] newCode = SerializeInstructions(methodBody);

                            if (code.SequenceEqual(newCode)) continue;

                            Log.Message("Patching " + method.FullDescription());

                            var replacement = DynamicTools.CreateDynamicMethod(method, $"_HotSwap{count++}");
                            var ilGen = replacement.GetILGenerator();

                            foreach (var local in methodBody.Variables)
                            {
                                var localType = Type.GetType(local.Type.AssemblyQualifiedName);
                                Log.Message($"local {local.Type.AssemblyQualifiedName} / {localType}");
                                ilGen.DeclareLocal(localType);
                            }

                            int pos = 0;

                            foreach (var inst in methodBody.Instructions)
                            {
                                switch (inst.OpCode.OperandType)
                                {
                                    case dnlib.DotNet.Emit.OperandType.InlineString:
                                    case dnlib.DotNet.Emit.OperandType.InlineType:
                                    case dnlib.DotNet.Emit.OperandType.InlineMethod:
                                    case dnlib.DotNet.Emit.OperandType.InlineField:
                                    case dnlib.DotNet.Emit.OperandType.InlineSig:
                                        pos += inst.OpCode.Size;
                                        object refe = TranslateRef(module, inst.Operand);
                                        int token = (int)AddRef.Invoke(replacement, new[] { refe });
                                        newCode[pos++] = (byte)(token & 255);
                                        newCode[pos++] = (byte)(token >> 8 & 255);
                                        newCode[pos++] = (byte)(token >> 16 & 255);
                                        newCode[pos++] = (byte)(token >> 24 & 255);
                                        break;
                                    default:
                                        pos += inst.GetSize();
                                        break;
                                }
                            }

                            // todo convert to reflection

                            /*ilGen.code = newCode;
                            ilGen.code_len = newCode.Length;
                            ilGen.max_stack = methodBody.MaxStack;*/

                            foreach (var ex in methodBody.ExceptionHandlers)
                            {
                                int start = (int)ex.TryStart.Offset;
                                int end = (int)ex.TryEnd.Offset;
                                int len = end - start;
                                int handlerStart = (int)ex.HandlerStart.Offset;
                                int handlerEnd = (int)ex.HandlerEnd.Offset;
                                int handlerLen = handlerEnd - handlerStart;

                                Type catchType = null;
                                int filterOffset = 0;

                                if (ex.CatchType != null)
                                    catchType = module.ResolveType(ex.CatchType.MDToken.ToInt32());
                                else if (ex.FilterStart != null)
                                    filterOffset = (int)ex.FilterStart.Offset;

                                // todo convert to reflection

                                /*if (ilGen.ex_handlers == null)
                                    ilGen.ex_handlers = new ILExceptionInfo[0];

                                ILExceptionInfo exInfo = ilGen.ex_handlers.FirstOrDefault(e => e.start == start && e.len == len);
                                if (exInfo.handlers == null)
                                {
                                    ilGen.ex_handlers = ilGen.ex_handlers.AddToArray(new ILExceptionInfo()
                                    {
                                        start = start,
                                        len = len,
                                        handlers = new ILExceptionBlock[0]
                                    });
                                }

                                exInfo.handlers = exInfo.handlers.AddToArray(new ILExceptionBlock()
                                {
                                    type = (int)ex.HandlerType,
                                    start = handlerStart,
                                    len = handlerLen,
                                    extype = catchType,
                                    filter_offset = filterOffset
                                });*/
                            }

                            DynamicTools.PrepareDynamicMethod(replacement);

                            dynMethods[method] = replacement;
                            Memory.DetourMethod(method, replacement);
                        }
                    }
                }
            }
        }

        public class TokenProvider : ITokenProvider
        {
            public void Error(string message)
            {
            }

            public MDToken GetToken(object o)
            {
                if (o is string str)
                    return new MDToken((Table)0x70, 1);
                else if (o is IMDTokenProvider token)
                    return token.MDToken;
                else if (o is StandAloneSig sig)
                    return sig.MDToken;

                return new MDToken();
            }

            public MDToken GetToken(IList<TypeSig> locals, uint origToken)
            {
                return new MDToken(origToken);
            }
        }

        class FullNameFactoryHelper : IFullNameFactoryHelper
        {
            public bool MustUseAssemblyName(IType type)
            {
                return true;
            }
        }

        static FieldInfo codeSizeField = AccessTools.Field(typeof(MethodBodyWriter), "codeSize");

        private static byte[] SerializeInstructions(CilBody body)
        {
            var writer = new MethodBodyWriter(new TokenProvider(), body);
            writer.Write();
            int codeSize = (int)(uint)codeSizeField.GetValue(writer);
            return writer.Code.SubArray(writer.Code.Length - codeSize, codeSize);
        }

        private static object TranslateRef(Module module, object refe)
        {
            if (refe is IMemberRef member)
            {
                if (member.IsField)
                {
                    Type type = Type.GetType(member.DeclaringType.AssemblyQualifiedName);
                    return AccessTools.Field(type, member.Name);
                }
                else if (member.IsMethod && member is IMethod method)
                {
                    Type type = Type.GetType(member.DeclaringType.AssemblyQualifiedName);
                    var members = type.GetMembers(AccessTools.all);

                    Type[] genericForMethod = null;
                    if (method.IsMethodSpec && method is MethodSpec spec)
                    {
                        method = spec.Method;
                        var generic = spec.GenericInstMethodSig;
                        genericForMethod = generic.GenericArguments.Select(t => Type.GetType(t.AssemblyQualifiedName)).ToArray();
                    }

                    if (type.IsGenericType)
                        type = type.GetGenericTypeDefinition();

                    var genericMembers = type.GetMembers(AccessTools.all);

                    for (int i = 0; i < genericMembers.Length; i++)
                    {
                        var typeMember = genericMembers[i];
                        if (!(typeMember is MethodBase m)) continue;
                        if (new SigComparer().Equals(m, method))
                        {
                            if (genericForMethod != null)
                                return (members[i] as MethodInfo).MakeGenericMethod(genericForMethod);

                            return members[i];
                        }
                    }

                    return null;
                }
                else if (member.IsType && member is IType type)
                {
                    return Type.GetType(type.AssemblyQualifiedName);
                }
            }
            else if (refe is string)
            {
                return refe;
            }

            return null;
        }
    }
}

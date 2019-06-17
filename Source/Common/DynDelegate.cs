/*
    The MIT License (MIT)

    Copyright (c) 2019 Pecius

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;


#if DEBUG && DYNVERBOSE
using ILGenerator = DynDelegate.VerboseILGenerator;
#endif

namespace DynDelegate
{
#if DEBUG && DYNVERBOSE
    internal class VerboseILGenerator
    {
        System.Reflection.Emit.ILGenerator gen;

        public VerboseILGenerator(System.Reflection.Emit.ILGenerator gen)
        {
            this.gen = gen;
        }

        public void Emit(OpCode a)
        {
            Console.WriteLine($"{a}");
            gen.Emit(a);
        }

        public void Emit(OpCode a, LocalBuilder b)
        {
            Console.WriteLine($"{a} {b}");
            gen.Emit(a, b);
        }

        public void Emit(OpCode a, int b)
        {
            Console.WriteLine($"{a} {b}");
            gen.Emit(a, b);
        }

        public void Emit(OpCode a, Type b)
        {
            Console.WriteLine($"{a} {b}");
            gen.Emit(a, b);
        }

        public void EmitCall(OpCode a, MethodInfo b, Type[] c)
        {
            Console.WriteLine($"{a} {b}");
            gen.EmitCall(a, b, c);
        }

        public LocalBuilder DeclareLocal(Type a, bool b)
        {
            return gen.DeclareLocal(a, b);
        }
    }
#endif

    public static class DynamicDelegate
    {
        private static void LoadArg(ILGenerator il, int arg)
        {
            switch (arg) {
            case 0:
                il.Emit(OpCodes.Ldarg_0);
                break;
            case 1:
                il.Emit(OpCodes.Ldarg_1);
                break;
            case 2:
                il.Emit(OpCodes.Ldarg_2);
                break;
            case 3:
                il.Emit(OpCodes.Ldarg_3);
                break;
            default:
                il.Emit(OpCodes.Ldarg, arg);
                break;
            }
        }

        private static void Convert(ILGenerator il, Type from, Type to, MethodInfo fallbackConverter)   // TODO: add number conversion + array conversion
        {
            if (from == typeof(object)) {
                if (to.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, to);
                else
                    il.Emit(OpCodes.Castclass, to);

                return;
            }

            MethodInfo converter = getConversionOperator(from, to);

            if (converter == null && fallbackConverter != null)
                converter = fallbackConverter.MakeGenericMethod(new Type[] { from, to });

            if (converter == null)
                throw new InvalidCastException($"Cannot cast from {from.Name} to {to.Name}, converter not found");

            if (converter.IsGenericMethod)
                try {
                    converter.Invoke(null, new object[] { null });
                } catch (InvalidCastException) {
                    throw new InvalidCastException($"Cannot cast from {from.Name} to {to.Name}");
                } catch {
                }

            il.EmitCall(OpCodes.Call, converter, null);
        }

        public static MethodInfo getConversionOperator(Type from, Type to)
        {
            IEnumerable<MethodInfo> ops = from.GetMethods(BindingFlags.Public | BindingFlags.Static);
            ops = ops.Union(to.GetMethods(BindingFlags.Public | BindingFlags.Static));

            var op = ops.Where(p => (p.Name == "op_Implicit" || p.Name == "op_Explicit"))
                .Where(p => (p.ReturnType == to && p.GetParameters().FirstOrDefault().ParameterType == from)).FirstOrDefault();

            if (op != null)
                return op;

            var generics = ops.Where(p => (p.IsGenericMethodDefinition && p.Name == "GenericCast"));

            var gOp = generics.Where(p => p.ReturnType == to).FirstOrDefault();

            if (gOp != null)
                return gOp.MakeGenericMethod(new Type[] { from });

            gOp = generics.Where(p => p.GetParameters()[0].ParameterType == from).FirstOrDefault();

            if (gOp != null)
                return gOp.MakeGenericMethod(new Type[] { to });

            return null;
        }

        public static T Create<T>(MethodInfo method, MethodInfo converter = null)// where T: MulticastDelegate
        {
            List<Type> delegateArgs;
            Type delegateRet;

            MethodInfo m = typeof(T).GetMethod("Invoke");
            delegateArgs = m.GetParameters().Select(p => p.ParameterType).ToList();
            delegateRet = m.ReturnType;

            Type[] methodArgs = method.GetParameters().Select(p => p.ParameterType).ToArray();

            DynamicMethod dynamicMethod = new DynamicMethod("DynamicDelegate_" + method.Name, delegateRet, delegateArgs.ToArray(), method.DeclaringType.Module, true);

#if DEBUG && DYNVERBOSE
            ILGenerator il = new VerboseILGenerator(dynamicMethod.GetILGenerator());
#else
            ILGenerator il = dynamicMethod.GetILGenerator();
#endif

            int offset = method.IsStatic ? 0 : 1;

            LocalBuilder[] refLocals = new LocalBuilder[methodArgs.Length];

            for (int i = 0; i < methodArgs.Length; i++) {
                Type arg = methodArgs[i];
                Type dArg = delegateArgs[i + offset];

                if (dArg.IsByRef && (dArg.GetElementType() != typeof(object) || arg.GetElementType().IsValueType) && dArg.GetElementType() != arg.GetElementType()) {
                    if (!arg.IsByRef)
                        throw new Exception();

                    var local = il.DeclareLocal(arg.GetElementType(), true);

                    LoadArg(il, i + offset);
                    il.Emit(OpCodes.Ldind_Ref);

                    if (dArg.GetElementType() != arg.GetElementType())
                        Convert(il, dArg.GetElementType(), arg.GetElementType(), converter);


                    il.Emit(OpCodes.Stloc, local);

                    refLocals[i + offset] = local;
                }
            }


            if (!method.IsStatic)
                il.Emit(OpCodes.Ldarg_0);

            for (int i = 0; i < methodArgs.Length; i++) {
                Type arg = methodArgs[i];
                Type dArg = delegateArgs[i + offset];


                if (dArg.IsByRef) {
                    var refLocal = refLocals[i + offset];

                    if (refLocal != null)
                        il.Emit(OpCodes.Ldloca_S, refLocal);
                    else
                        LoadArg(il, i + offset);

                    continue;
                }

                LoadArg(il, i + offset);

                if (arg != dArg) {
                    Convert(il, dArg, arg, converter);
                    /*if (dArg == typeof(object))
                    {
                        if (arg.IsValueType)
                            il.Emit(OpCodes.Unbox_Any, arg);
                        else
                            il.Emit(OpCodes.Castclass, arg);

                        continue;
                    }

                    Convert(il, dArg, arg, converter);*/
                }

            }

            if (method.IsStatic)
                il.EmitCall(OpCodes.Call, method, null);
            else
                il.EmitCall(OpCodes.Callvirt, method, null);

            for (int i = 0; i < methodArgs.Length; i++) {
                var refLocal = refLocals[i + offset];
                if (refLocal != null) {
                    LoadArg(il, i + offset);
                    il.Emit(OpCodes.Ldloc, refLocal);
                    il.Emit(OpCodes.Stind_Ref);
                }
            }

            Type returnType = method.ReturnType;

            if (delegateRet != returnType) {
                if (delegateRet == typeof(void))
                    il.Emit(OpCodes.Pop);

                else if (delegateRet == typeof(object)) {
                    if (returnType.IsValueType)
                        il.Emit(OpCodes.Box, returnType);
                } else if (returnType == typeof(void)) {
                    if (returnType.IsValueType)
                        il.Emit(OpCodes.Ldc_I4_1);  // temp
                    else
                        il.Emit(OpCodes.Ldnull);
                } else
                    Convert(il, returnType, delegateRet, null);
            }

            il.Emit(OpCodes.Ret);

            return (T) (object) dynamicMethod.CreateDelegate(typeof(T));
        }


        /*public static T Create<T>(FieldInfo method)
        {

        }*/
    }
}
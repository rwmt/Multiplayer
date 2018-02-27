using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Verse;

namespace Multiplayer
{
    public static class ProfilerExperiment
    {
        [DllImport("__Internal")]
        private static extern void mono_profiler_load(string args);

        [DllImport("__Internal")]
        private static unsafe extern void mono_profiler_install(void* prof, void* shutdown);

        [DllImport("__Internal")]
        private static unsafe extern void mono_profiler_install_enter_leave(void* enter, void* leave);

        [DllImport("__Internal")]
        public static unsafe extern void mono_profiler_install_exception(void* a, void* b, void* c);

        [DllImport("__Internal")]
        public static extern void mono_profiler_set_events(int flags);

        [DllImport("__Internal")]
        private static extern int mono_profiler_get_events();

        [DllImport("__Internal")]
        private static unsafe extern char* mono_signature_get_desc(void* signature, bool namespaces);

        [DllImport("__Internal")]
        private static unsafe extern void* mono_method_signature(void* method);

        [DllImport("__Internal")]
        private static unsafe extern void g_free(void* data);

        [DllImport("__Internal")]
        private static extern int GetCurrentThreadId();

        [DllImport("__Internal")]
        private static unsafe extern void merge_thread_data(void* p, void* p2);

        [DllImport("kernel32.dll")]
        public static extern bool QueryPerformanceCounter(out long value);

        [DllImport("ProfilerNative.dll")]
        public static extern float test();

        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string dllToLoad);

        public unsafe delegate void ProfilerMethodCallback(void* method);

        public unsafe static ProfilerMethodCallback enter_callback;
        public unsafe static ProfilerMethodCallback leave_callback;

        public unsafe static void enter(void* prof, void* method_ptr)
        {
            mono_profiler_set_events(0);
            enter_callback(method_ptr);
            mono_profiler_set_events(1 << 12 | 1 << 6);
        }

        public unsafe static void leave(void* prof, void* method_ptr)
        {
            mono_profiler_set_events(0);
            leave_callback(method_ptr);
            mono_profiler_set_events(1 << 12 | 1 << 6);
        }

        public unsafe static void EnterCallback(void* method)
        {
            if (GetCurrentThreadId() != mainThread)
                return;

            QueryPerformanceCounter(out long time);
            stack_head++;
            long[] call = call_stack[stack_head];
            call[0] = call[1] = time;
            call[2] = (int)method;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct MonoMethod
        {
            public ushort flags;
            public ushort iflags;
            public uint token;
            public int* klass;
            public void* signature;
            public string name;
        }

        public class MethodData
        {
            public long self_time;
            public long total_time;
            public int calls;
        }

        public unsafe static void LeaveCallback(void* method)
        {
            if (GetCurrentThreadId() != mainThread || stack_head == -1 || call_stack[stack_head][2] != (int)method)
                return;

            QueryPerformanceCounter(out long now);

            if (!calls.TryGetValue((int)method, out MethodData data))
                data = calls[(int)method] = new MethodData();

            long[] current = call_stack[stack_head];
            stack_head--;
            long elapsed = now - current[0];

            if (stack_head >= 0)
                call_stack[stack_head][1] += elapsed;

            data.calls++;
            data.self_time += now - current[1];
            data.total_time += elapsed;
        }

        public unsafe static void shutdown(void* prof)
        {
        }

        public static long[][] call_stack;
        public static int stack_head = -1;

        static MethodData data = new MethodData();
        public static Dictionary<int, MethodData> calls = new Dictionary<int, MethodData>();
        public static int mainThread;
        public static int callc;

        public unsafe static void Run()
        {
            call_stack = new long[256][];
            for (int i = 0; i < 256; i++)
                call_stack[i] = new long[3];

            mainThread = GetCurrentThreadId();

            Log.Message("Main thread " + mainThread);

            enter_callback = (ProfilerMethodCallback)Marshal.GetDelegateForFunctionPointer(typeof(Multiplayer).GetMethod("EnterCallback").MethodHandle.GetFunctionPointer(), typeof(ProfilerMethodCallback));
            leave_callback = (ProfilerMethodCallback)Marshal.GetDelegateForFunctionPointer(typeof(Multiplayer).GetMethod("LeaveCallback").MethodHandle.GetFunctionPointer(), typeof(ProfilerMethodCallback));

            void* leave_ptr = typeof(Multiplayer).GetMethod("leave").MethodHandle.GetFunctionPointer().ToPointer();
            mono_profiler_install((void*)0, typeof(Multiplayer).GetMethod("shutdown").MethodHandle.GetFunctionPointer().ToPointer());
            mono_profiler_install_enter_leave(typeof(Multiplayer).GetMethod("enter").MethodHandle.GetFunctionPointer().ToPointer(), leave_ptr);
            mono_profiler_install_exception(null, leave_ptr, null);

            mono_profiler_set_events(1 << 12 | 1 << 6);
        }

        public static void Print()
        {
            mono_profiler_set_events(0);
            Log.Message("entries " + calls.Count + " " + calls.Keys.Count + " " + calls.Values.Count);

            foreach (KeyValuePair<int, MethodData> pair in calls.OrderByDescending(p => p.Value.self_time))
            {
                unsafe
                {
                    MonoMethod method = (MonoMethod)Marshal.PtrToStructure(new IntPtr(pair.Key), typeof(MonoMethod));
                    string class_name = Marshal.PtrToStringAnsi(new IntPtr(*(method.klass + 12)));

                    Log.Message(class_name + "::" + method.name + " " + pair.Value.total_time + " " + pair.Value.self_time + " " + pair.Value.calls);
                }
            }

            mono_profiler_set_events(1 << 12 | 1 << 6);
        }
    }
}
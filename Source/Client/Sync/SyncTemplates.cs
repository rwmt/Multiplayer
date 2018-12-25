using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Multiplayer.Client
{
    public static class SyncTemplates
    {
        static bool General(MethodBase method, object instance, object[] args)
        {
            if (Multiplayer.ShouldSync)
            {
                Sync.syncMethods[method].DoSync(instance, args);
                return false;
            }

            return true;
        }

        static bool Prefix_0(MethodBase __originalMethod, object __instance) => General(__originalMethod, __instance, new object[0]);
        static bool Prefix_1(MethodBase __originalMethod, object __instance, object __0) => General(__originalMethod, __instance, new[] { __0 });
        static bool Prefix_2(MethodBase __originalMethod, object __instance, object __0, object __1) => General(__originalMethod, __instance, new[] { __0, __1 });
        static bool Prefix_3(MethodBase __originalMethod, object __instance, object __0, object __1, object __2) => General(__originalMethod, __instance, new[] { __0, __1, __2 });
        static bool Prefix_4(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3) => General(__originalMethod, __instance, new[] { __0, __1, __2, __3 });
        static bool Prefix_5(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4) => General(__originalMethod, __instance, new[] { __0, __1, __2, __3, __4 });
    }
}

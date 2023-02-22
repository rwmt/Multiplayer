using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client.Experimental;

public abstract record ThingFilterContext : ISyncSimple
{
    public abstract ThingFilter Filter { get; }
    public abstract ThingFilter ParentFilter { get; }
    public virtual IEnumerable<SpecialThingFilterDef> HiddenFilters => null;
}

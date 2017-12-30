using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer
{
    public enum LongActionType
    {
        PLAYER_JOIN, ENCOUNTER, GENERATING_MAP
    }

    public abstract class LongAction : AttributedExposable
    {
        public LongActionType type;

        public bool shouldRun = Multiplayer.server != null;

        public virtual string Text => "Waiting";

        public abstract void Run();

        public virtual bool Equals(LongAction other)
        {
            if (other == null || GetType() != other.GetType()) return false;
            return type == other.type;
        }
    }

    public class LongActionPlayerJoin : LongAction
    {
        private static readonly MethodInfo exposeSmallComps = typeof(Game).GetMethod("ExposeSmallComponents", BindingFlags.NonPublic | BindingFlags.Instance);

        [ExposeValue]
        public string username;

        public override string Text
        {
            get
            {
                if (shouldRun) return "Saving the world for " + username;
                else return "Waiting for " + username + " to load";
            }
        }

        public LongActionPlayerJoin()
        {
            type = LongActionType.PLAYER_JOIN;
        }

        public override void Run()
        {
            Connection conn = Multiplayer.server.GetByUsername(username);

            // catch them up
            conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, ScribeUtil.WriteSingle(OnMainThread.currentLongAction)));
            foreach (LongAction other in OnMainThread.longActions)
                conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, ScribeUtil.WriteSingle(other)));
            foreach (ScheduledServerAction action in OnMainThread.scheduledActions)
                conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(action.action, action.data));

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            if (!comp.playerFactions.TryGetValue(username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = username + "'s faction";
                faction.def = Multiplayer.factionDef;

                Find.FactionManager.Add(faction);
                comp.playerFactions[username] = faction;

                object[] data = new object[] { username, ScribeUtil.WriteSingle(faction) };
                Multiplayer.server.SendToAll(Packets.SERVER_NEW_FACTION, data, conn, Multiplayer.localServerConnection);

                Log.Message("New faction: " + faction.Name);
            }

            conn.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(Multiplayer.NextIdBlock()));

            ScribeUtil.StartWriting();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            sbyte visibleMapIndex = -1;
            Scribe_Values.Look<sbyte>(ref visibleMapIndex, "visibleMapIndex", -1, false);
            exposeSmallComps.Invoke(Current.Game, null);
            World world = Current.Game.World;
            Scribe_Deep.Look(ref world, "world");
            List<Map> emptyList = new List<Map>();
            Scribe_Collections.Look(ref emptyList, "maps", LookMode.Deep);
            Scribe.ExitNode();

            Multiplayer.savedWorld = ScribeUtil.FinishWriting();

            string mapsFile = ServerPlayingState.GetPlayerMapsPath(username);
            byte[] mapsData = new byte[0];
            if (File.Exists(mapsFile))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    XmlDocument mapsXml = new XmlDocument();
                    mapsXml.Load(mapsFile);
                    mapsXml.Save(stream);
                    mapsData = stream.ToArray();
                }
            }

            conn.Send(Packets.SERVER_WORLD_DATA, new object[] { Multiplayer.savedWorld, mapsData });
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && username == ((LongActionPlayerJoin)other).username;
        }
    }

    public class LongActionEncounter : LongAction
    {
        [ExposeValue]
        public string defender;
        [ExposeValue]
        public string attacker;
        [ExposeValue]
        public int tile;

        public HashSet<string> waitingFor = new HashSet<string>();

        public override string Text
        {
            get
            {
                return "Setting up an encounter between " + defender + " and " + attacker;
            }
        }

        public LongActionEncounter()
        {
            type = LongActionType.ENCOUNTER;
        }

        public override void Run()
        {
            Connection conn = Multiplayer.server.GetByUsername(defender);
            IdBlock block = Multiplayer.NextIdBlock();
            block.mapTile = tile;

            conn.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(block));
            conn.Send(Packets.SERVER_MAP_REQUEST, new object[] { tile });
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && defender == ((LongActionEncounter)other).defender && tile == ((LongActionEncounter)other).tile;
        }
    }

    public class LongActionGenerating : LongAction
    {
        [ExposeValue]
        public string username;

        public override string Text => username + " is generating a map";

        public LongActionGenerating()
        {
            type = LongActionType.GENERATING_MAP;
        }

        public override void Run()
        {
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && username == ((LongActionGenerating)other).username;
        }
    }
}

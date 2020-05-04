#define DEBUG
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Real Teleport", "RFC1920", "1.0.0")]
    [Description("Nextgen Teleportation plugin")]
    class RealTeleport : RustPlugin
    {
        #region vars
        //private SortedDictionary<string, RTP_Point> serverPoints = new SortedDictionary<string, RTP_Point>();
        //private SortedDictionary<string, RTP_Point> userPoints = new SortedDictionary<string, RTP_Point>();
        private SortedDictionary<string, Vector3> monPos  = new SortedDictionary<string, Vector3>();
        private SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();
        private SortedDictionary<string, Vector3> cavePos  = new SortedDictionary<string, Vector3>();

        private readonly Dictionary<ulong, TPTimer> TeleportTimers = new Dictionary<ulong, TPTimer>();
        private const string permRealTeleportUse = "realteleport.use";
        private const string permRealTeleportAdmin = "realteleport.admin";

        private ConfigData configData;
        private SQLiteConnection sqlConnection;
        private string connStr;

        private readonly string logfilename = "log";
        private bool dolog = false;

        private readonly Plugin Friends, Clans, RustIO;

        public class TPTimer
        {
            public Timer timer;
            public float countdown;
            public BasePlayer source;
            public string targetName;
            public Vector3 targetLocation;
        }

        public class RTP_Point
        {
            public string userid;
            public Vector3 location;
            public Time lastused;
            public Time nextused;
        }
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region init
        private void Init()
        {
            Puts("Creating database connection for main thread.");
            DynamicConfigFile dataFile = Interface.Oxide.DataFileSystem.GetDatafile(Name + "/realteleport");
            dataFile.Save();
            connStr = $"Data Source={Interface.Oxide.DataDirectory}{Path.DirectorySeparatorChar}{Name}{Path.DirectorySeparatorChar}realteleport.db";
            sqlConnection = new SQLiteConnection(connStr);
            Puts("Opening...");
            sqlConnection.Open();

            LoadConfigVariables();
            LoadData();

            AddCovalenceCommand("home", "CmdHomeTeleport");
            AddCovalenceCommand("town", "CmdTownTeleport");
            AddCovalenceCommand("bandit", "CmdTownTeleport");
            AddCovalenceCommand("outpost", "CmdTownTeleport");
            AddCovalenceCommand("tpb", "CmdTpb");
            AddCovalenceCommand("tpc", "CmdTpc");
            AddCovalenceCommand("tpr", "CmdTpr");

            permission.RegisterPermission(permRealTeleportUse, this);
            permission.RegisterPermission(permRealTeleportAdmin, this);

            FindMonuments();
        }

        public void HandleTimer(ulong userid, bool start = false)
        {
            if(start)
            {
                if(TeleportTimers.ContainsKey(userid))
                {
                    TeleportTimers[userid].timer = timer.Once(TeleportTimers[userid].countdown, () => { Teleport(TeleportTimers[userid].source, TeleportTimers[userid].targetLocation); });
                }
            }
            else
            {
                if (TeleportTimers.ContainsKey(userid))
                {
                    TeleportTimers[userid].timer.Destroy();
                }
            }
        }

        private void OnServerInitialized()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You are not authorized for this command!",
                ["banditnotset"] = "Bandit location has not been set!",
                ["outpostnotset"] = "Bandit location has not been set!",
                ["townnotset"] = "Bandit location has not been set!",
                ["townset"] = "Town location has been set to {0}!",
                ["cavetooclose"] = "You cannot use /{0} so close to a cave.",
                ["montooclose"] = "You cannot use /{0} so close to {1}.",
                ["onhurt"] = "You cannot use /{0} while injured.",
                ["oncold"] = "You are too cold to use /{0}!",
                ["onhot"] = "You are too hot to use /{0}!",
                ["onhostile"] = "You are marked as hostile and cannot use /{0} for {1} minutes...",
                ["onballoon"] = "You cannot use /{0} while on a balloon.",
                ["oncargo"] = "You cannot use /{0} while on the cargo ship.",
                ["onlift"] = "You cannot use /{0} while on a lift.",
                ["onmounted"] = "You cannot use /{0} while mounted.",
                ["onswimming"] = "You cannot use /{0} while swimming.",
                ["onwater"] = "You cannot use /{0} above water.",
                ["safezone"] = "You cannot use /{0} from a safe zone.",
                ["teleporting"] = "Teleporting to {0} in {1} seconds...",
                ["tpcancelled"] = "Teleport cancelled!"
            }, this);
        }
        private void OnNewSave()
        {
            // Wipe homes and town, etc.
            CreateOrClearTables(true);
            // Set outpost and bandit
            FindMonuments();
        }

        void LoadData()
        {
            bool found = false;
            using (SQLiteConnection c = new SQLiteConnection(connStr))
            {
                c.Open();
                using (SQLiteCommand r = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='rtp_server'", c))
                {
                    using (SQLiteDataReader rtbl = r.ExecuteReader())
                    {
                        while (rtbl.Read()) { found = true; }
                    }
                }
            }
            if (!found) CreateOrClearTables(true);
        }

//        public class RTP_Point
//        {
//            public string userid;
//            public Vector3 location;
//            public Time lastused;
//            public Time nextused;
//        }

        private void DoLog(string message, int indent = 0)
        {
            if (dolog) LogToFile(logfilename, "".PadLeft(indent, ' ') + message, this);
        }
        #endregion

        #region commands
        [Command("home")]
        private void CmdHomeTeleport(IPlayer player, string command, string[] args)
        {
            if(args.Length < 1)
            {
                // List
            }
            else if(args.Length == 2 && args[1] == configData.Options.SetCommand)
            {
                // Set home
            }
            else if(args.Length == 1)
            {
                // use an already set home
            }
        }

        [Command("town")]
        private void CmdTownTeleport(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(args.Length > 0)
            {
                if (args[0] == configData.Options.SetCommand)
                {
                    if (!iplayer.HasPermission(permRealTeleportAdmin)) { Message(iplayer, "notauthorized"); return; }
                    RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('town', '{player.transform.position.ToString()}')");
                    Message(iplayer, "townset", player.transform.position.ToString());
                    return;
                }
            }

            switch (command)
            {
                case "town":
                    List<string> town = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='town'");
                    if (town != null)
                    {
                        if (CanTeleport(player, town[0], "town"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { countdown = configData.Town.CountDown, source = player, targetName = "town", targetLocation = StringToVector3(town[0]) });
                                HandleTimer(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Town.CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(town[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "townnotset");
                    break;
                case "bandit":
                    List<string> bandit = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='bandit'");
                    if (bandit != null)
                    {
                        if (CanTeleport(player, bandit[0], "bandit"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { countdown = configData.Bandit.CountDown, source = player, targetName = "bandit", targetLocation = StringToVector3(bandit[0]) });
                                HandleTimer(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Bandit.CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(bandit[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "banditnotset");
                    break;
                case "outpost":
                    List<string> outpost = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='outpost'");
                    Puts($"Outpost location: {outpost[0]}");
                    if (outpost != null)
                    {
                        if (CanTeleport(player, outpost[0], "outpost"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { countdown = configData.Bandit.CountDown, source = player, targetName = "outpost", targetLocation = StringToVector3(outpost[0]) });
                                HandleTimer(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Outpost.CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(outpost[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "outpostnotset");
                    break;
            }
        }

        [Command("tpb")]
        private void CmdTpb(IPlayer iplayer, string command, string[] args)
        {
        }

        [Command("tpc")]
        private void CmdTpc(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            HandleTimer(player.userID);
            Message(iplayer, "tpcancelled");
        }

        [Command("tpr")]
        private void CmdTpr(IPlayer iplayer, string command, string[] args)
        {
        }
        #endregion

        #region main
        private bool CanTeleport(BasePlayer player, string location, string type, bool requester = true)
        {
            var oncargo = player.GetComponentInParent<CargoShip>();
            var onballoon = player.GetComponentInParent<HotAirBalloon>();
            var onlift = player.GetComponentInParent<Lift>();

            string monName = NearMonument(player);
            string cave = NearCave(player);
            switch(type)
            {
                case "home":
                    if(monName != null && monName.Contains("Oilrig") && configData.Home.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.Home.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.Home.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.Home.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.Home.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.Home.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.Home.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.Home.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.Home.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.Home.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.Home.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.Home.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.Home.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.Home.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    break;
                case "town":
                    if(monName != null && monName.Contains("Oilrig") && configData.Town.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.Town.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.Town.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.Town.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.Town.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.Town.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.Town.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.Town.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.Town.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.Town.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.Town.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.Town.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.Town.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.Town.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    break;
                case "bandit":
                    if(monName != null && monName.Contains("Oilrig") && configData.Bandit.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.Bandit.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.Bandit.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.Bandit.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.Bandit.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.Bandit.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.Bandit.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.Bandit.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.Bandit.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.Bandit.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.Bandit.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.Bandit.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.Bandit.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.Bandit.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    if (player.IsHostile() && configData.Bandit.BlockOnHostile)
                    {
                        float unHostileTime = (float)player.State.unHostileTimestamp;
                        float currentTime = (float)Network.TimeEx.currentTimestamp;
                        string hostilecd = ((int)Math.Abs(unHostileTime - currentTime) / 60).ToString();
                        if ((unHostileTime - currentTime) < 60) hostilecd = "<1";
                        Message(player.IPlayer, "onhostile", type, hostilecd);
                        return false;
                    }
                    break;
                case "outpost":
                    if(monName != null && monName.Contains("Oilrig") && configData.Outpost.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.Outpost.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.Outpost.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.Outpost.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.Outpost.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.Outpost.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.Outpost.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.Outpost.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.Outpost.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.Outpost.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.Outpost.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.Outpost.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.Outpost.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.Outpost.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    if (player.IsHostile() && configData.Outpost.BlockOnHostile)
                    {
                        float unHostileTime = (float)player.State.unHostileTimestamp;
                        float currentTime = (float)Network.TimeEx.currentTimestamp;
                        string hostilecd = ((int)Math.Abs(unHostileTime - currentTime) / 60).ToString();
                        if ((unHostileTime - currentTime) < 60) hostilecd = "<1";
                        Message(player.IPlayer, "onhostile", type, hostilecd);
                        return false;
                    }
                    break;
                case "tpb":
                    if(monName != null && monName.Contains("Oilrig") && configData.TPB.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.TPB.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.TPB.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.TPB.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.TPB.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.TPB.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.TPB.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.TPB.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.TPB.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.TPB.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.TPB.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.TPB.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.TPB.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.TPB.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    break;
                case "tpr":
                    if(monName != null && monName.Contains("Oilrig") && configData.TPR.BlockOnRig)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if(monName != null && monName.Contains("Excavator") && configData.TPR.BlockOnExcavator)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    else if (monName != null && configData.TPR.BlockOnMonuments)
                    {
                        Message(player.IPlayer, "montooclose", type, monName);
                        return false;
                    }
                    if (cave != null && configData.TPR.BlockOnCave)
                    {
                        Message(player.IPlayer, "cavetooclose", cave);
                        return false;
                    }
                    if (player.InSafeZone() && configData.TPR.BlockOnSafe)
                    {
                        Message(player.IPlayer, "safezone", type);
                        return false;
                    }
                    if(oncargo && configData.TPR.BlockOnCargo)
                    {
                        Message(player.IPlayer, "oncargo", type);
                        return false;
                    }
                    if(onballoon && configData.TPR.BlockOnBalloon)
                    {
                        Message(player.IPlayer, "onballoon", type);
                        return false;
                    }
                    if(onlift && configData.TPR.BlockOnLift)
                    {
                        Message(player.IPlayer, "onlift", type);
                        return false;
                    }
                    if(AboveWater(player) && configData.TPR.BlockOnWater)
                    {
                        Message(player.IPlayer, "onwater", type);
                        return false;
                    }
                    if(player.IsSwimming() && configData.TPR.BlockOnSwimming)
                    {
                        Message(player.IPlayer, "onswimming", type);
                        return false;
                    }
                    if((player.IsWounded() && requester) && configData.TPR.BlockOnHurt)
                    {
                        Message(player.IPlayer, "onhurt", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.TPR.BlockOnCold)
                    {
                        Message(player.IPlayer,"oncold", type);
                        return false;
                    }
                    if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.TPR.BlockOnHot)
                    {
                        Message(player.IPlayer,"onhot", type);
                        return false;
                    }
                    if(player.isMounted && configData.TPR.BlockOnMounted)
                    {
                        Message(player.IPlayer,"onmounted", type);
                        return false;
                    }
                    break;
            }
            return true;
        }

        public bool AboveWater(BasePlayer player)
        {
            var pos = player.transform.position;
#if DEBUG
            Puts($"Player position: {pos.ToString()}.  Checking for water...");
#endif
            if((TerrainMeta.HeightMap.GetHeight(pos) - TerrainMeta.WaterMap.GetHeight(pos)) >= 0)
            {
#if DEBUG
                Puts("Player not above water.");
#endif
                return false;
            }
            else
            {
#if DEBUG
                Puts("Player is above water!");
#endif
                return true;
            }
        }

        private string NearMonument(BasePlayer player)
        {
            var pos = player.transform.position;

            foreach(KeyValuePair<string, Vector3> entry in monPos)
            {
                var monname = entry.Key;
                var monvector = entry.Value;
                float realdistance = monSize[monname].z;
                monvector.y = pos.y;
                float dist = Vector3.Distance(pos, monvector);
#if DEBUG
                Puts($"Checking {monname} dist: {dist.ToString()}, realdistance: {realdistance.ToString()}");
#endif
                if(dist < realdistance)
                {
#if DEBUG
                    Puts($"Player in range of {monname}");
#endif
                    return monname;
                }
            }
            return null;
        }

        private string NearCave(BasePlayer player)
        {
            var pos = player.transform.position;

            foreach(KeyValuePair<string, Vector3> entry in cavePos)
            {
                var cavename = entry.Key;
                float realdistance = 0f;

                if(cavename.Contains("Small"))
                {
                    realdistance = configData.Options.CaveDistanceSmall;
                }
                else if(cavename.Contains("Large"))
                {
                    realdistance = configData.Options.CaveDistanceLarge;
                }
                else if(cavename.Contains("Medium"))
                {
                    realdistance = configData.Options.CaveDistanceMedium;
                }

                var cavevector = entry.Value;
                cavevector.y = pos.y;
                var cpos = cavevector.ToString();
                float dist = Vector3.Distance(pos, cavevector);

                if(dist < realdistance)
                {
#if DEBUG
                    Puts($"NearCave: {cavename} nearby.");
#endif
                    return cavename;
                }
                else
                {
#if DEBUG
                    Puts("NearCave: Not near this cave.");
#endif
                }
            }
            return null;
        }
        #endregion

        #region helpers
        public void CountDown(BasePlayer player, string target)
        {

        }

        public static Vector3 StringToVector3(string sVector)
        {
            // Remove the parentheses
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }

            // split the items
            string[] sArray = sVector.Split(',');

            // store as a Vector3
            Vector3 result = new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]));

            return result;
        }

        void FindMonuments()
        {
            Vector3 extents = Vector3.zero;
            float realWidth = 0f;
            string name = null;
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (monument.name.Contains("power_sub")) continue;
                realWidth = 0f;
                name = null;

                if(monument.name == "OilrigAI")
                {
                    name = "Small Oilrig";
                    realWidth = 100f;
                }
                else if(monument.name == "OilrigAI2")
                {
                    name = "Large Oilrig";
                    realWidth = 200f;
                }
                else
                {
                    name = Regex.Match(monument.name, @"\w{6}\/(.+\/)(.+)\.(.+)").Groups[2].Value.Replace("_", " ").Replace(" 1", "").Titleize();
                }
                if(monPos.ContainsKey(name)) continue;
                if(cavePos.ContainsKey(name)) name = name + RandomString();

                extents = monument.Bounds.extents;
//#if DEBUG
//                Puts($"Found {name}, extents {extents.ToString()}");
//#endif

                if(realWidth > 0f)
                {
                    extents.z = realWidth;
//#if DEBUG
//                    Puts($"  corrected to {extents.ToString()}");
//#endif
                }

                if(monument.name.Contains("cave"))
                {
#if DEBUG
                    Puts("  Adding to cave list");
#endif
                    cavePos.Add(name, monument.transform.position);
                }
                else if(monument.name.Contains("compound") && configData.Options.AutoGenOutpost)
                {
#if DEBUG
                    Puts("  Adding Outpost target");
#endif
                    List<BaseEntity> ents = new List<BaseEntity>();
                    Vis.Entities<BaseEntity>(monument.transform.position, 50, ents);
                    foreach(BaseEntity entity in ents)
                    {
                        if(entity.PrefabName.Contains("piano"))
                        {
                            Vector3 outpost = entity.transform.position + new Vector3(1f, 0.1f, 1f);
                            RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('outpost', '{outpost.ToString()}')");
                        }
                    }
                }
                else if(monument.name.Contains("bandit") && configData.Options.AutoGenBandit)
                {
#if DEBUG
                    Puts("  Adding BanditTown target");
#endif
                    List<BaseEntity> ents = new List<BaseEntity>();
                    Vis.Entities<BaseEntity>(monument.transform.position, 50, ents);
                    foreach(BaseEntity entity in ents)
                    {
                        if(entity.PrefabName.Contains("workbench"))
                        {
                            Vector3 bandit = Vector3.Lerp(monument.transform.position, entity.transform.position, 0.45f) + new Vector3(0, 1.5f, 0);
                            RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('bandit', '{bandit.ToString()}')");
                        }
                    }
                }
                else
                {
                    if(extents.z < 1)
                    {
                        extents.z = configData.Options.DefaultMonumentSize;
                    }
                    monPos.Add(name, monument.transform.position);
                    monSize.Add(name, extents);
#if DEBUG
                    Puts($"Adding Monument: {name}, pos: {monument.transform.position.ToString()}, size: {extents.ToString()}");
#endif
                }
            }
            monPos.OrderBy(x => x.Key);
            monSize.OrderBy(x => x.Key);
            cavePos.OrderBy(x => x.Key);
        }

        private bool RunUpdateQuery(string query)
        {
            using (SQLiteConnection c = new SQLiteConnection(connStr))
            {
                c.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(query, c))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            return true;
        }

        private object RunSingleSelectQuery(string query)
        {
            List<string> output = new List<string>();
            using (SQLiteConnection c = new SQLiteConnection(connStr))
            {
                c.Open();
                using (SQLiteCommand q = new SQLiteCommand(query, c))
                {
                    using (SQLiteDataReader rtbl = q.ExecuteReader())
                    {
                        while(rtbl.Read())
                        {
                            string test = rtbl.GetValue(0).ToString();
                            if (test != "")
                            {
                                Puts($"Found {test}");
                                output.Add(test);
                            }
                        }
                    }
                }
            }
            if (output.Count > 0) return output;
            return null;
        }

        string RandomString()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            List<char> charList = chars.ToList();

            string random = "";

            for (int i = 0; i <= UnityEngine.Random.Range(5, 10); i++)
                random = random + charList[UnityEngine.Random.Range(0, charList.Count - 1)];

            return random;
        }

        // playerid = active player, ownerid = owner of camera, who may be offline
        bool IsFriend(ulong playerid, ulong ownerid)
        {
            if(configData.Options.useFriends && Friends != null)
            {
                var fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if(fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if(configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan  = (string)Clans?.CallHook("GetClanOf", ownerid);
                if(playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if(configData.Options.useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if(player.currentTeam != (long)0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);
                    if(playerTeam == null) return false;
                    if(playerTeam.members.Contains(ownerid))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void TeleportToPlayer(BasePlayer player, BasePlayer target) => Teleport(player, target.transform.position);
        public void TeleportToPosition(BasePlayer player, float x, float y, float z) => Teleport(player, new Vector3(x, y, z));

        public void Teleport(BasePlayer player, Vector3 position)
        {
            //SaveLocation(player);
            //teleporting.Add(player.userID);
            if (TeleportTimers.ContainsKey(player.userID)) TeleportTimers.Remove(player.userID);

            if(player.net?.connection != null) player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);

            player.SetParent(null, true, true);
            player.EnsureDismounted();
            player.Teleport(position);
            player.UpdateNetworkGroup();
            player.StartSleeping();
            player.SendNetworkUpdateImmediate(false);

            if(player.net?.connection != null) player.ClientRPCPlayer(null, player, "StartLoading");
        }

        private void StartSleeping(BasePlayer player)
        {
            if (player.IsSleeping()) return;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
            if (!BasePlayer.sleepingPlayerList.Contains(player)) BasePlayer.sleepingPlayerList.Add(player);
            player.CancelInvoke("InventoryUpdate");
            //player.inventory.crafting.CancelAll(true);
            //player.UpdatePlayerCollider(true, false);
        }
        #endregion

        #region config
        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData
            {
                Version = Version
            };
            config.Options.SetCommand = "set";
            config.Options.DefaultMonumentSize = 120f;
            config.Options.CaveDistanceSmall = 40f;
            config.Options.CaveDistanceMedium = 60f;
            config.Options.CaveDistanceLarge = 100f;
            config.Options.AutoGenBandit = true;
            config.Options.AutoGenOutpost = true;
            config.Options.MinimumTemp = 0f;
            config.Options.MaximumTemp = 40f;
            config.Home.CountDown = 5f;
            config.Home.CoolDown = 120f;
            config.Town.CountDown = 5f;
            config.Town.CoolDown = 120f;
            config.Bandit.CountDown = 5f;
            config.Bandit.CoolDown = 120f;
            config.Bandit.BlockOnHostile = true;
            config.Outpost.CountDown = 5f;
            config.Outpost.CoolDown = 120f;
            config.Outpost.BlockOnHostile = true;
            config.TPB.CountDown = 5f;
            config.TPB.CoolDown = 120f;
            config.TPC.CountDown = 5f;
            config.TPC.CoolDown = 120f;
            config.TPR.CountDown = 5f;
            config.TPR.CoolDown = 120f;

            SaveConfig(config);
        }
        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            public Options Options = new Options();
            public CmdOptions Home = new CmdOptions();
            public CmdOptions Town = new CmdOptions();
            public CmdOptions Bandit = new CmdOptions();
            public CmdOptions Outpost = new CmdOptions();
            public CmdOptions TPB = new CmdOptions();
            public CmdOptions TPC = new CmdOptions();
            public CmdOptions TPR = new CmdOptions();

            public VersionNumber Version;
        }

        private class Options
        {
            public bool useClans = false;
            public bool useFriends = false;
            public bool useTeams = false;
            public bool HonorBuildingPrivilege = true;
            public bool HonorRelationships = false;
            public bool AutoGenBandit;
            public bool AutoGenOutpost;
            public float DefaultMonumentSize;
            public float CaveDistanceSmall;
            public float CaveDistanceMedium;
            public float CaveDistanceLarge;
            public float MinimumTemp;
            public float MaximumTemp;
            public string SetCommand;
        }

        private class VIPSetting : CmdOptions
        {
            public string perm;
            public float VIPDailyLimit = 10f;
            public float VIPCountDown = 0f;
            public float VIPCoolDown = 0f;
        }
        private class CmdOptions
        {
            public bool BlockOnHurt = false;
            public bool BlockOnCold = false;
            public bool BlockOnHot = false;
            public bool BlockOnCave = false;
            public bool BlockOnRig = false;
            public bool BlockOnMonuments = false;
            public bool BlockOnHostile = false;
            public bool BlockOnSafe = false;
            public bool BlockOnBalloon = false;
            public bool BlockOnCargo = false;
            public bool BlockOnExcavator = false;
            public bool BlockOnLift = false;
            public bool BlockOnMounted = false;
            public bool BlockOnSwimming = false;
            public bool BlockOnWater = false;
            public float DailyLimit;
            public float CountDown;
            public float CoolDown;
        }
        #endregion

        #region defaults
        private void CreateOrClearTables(bool drop = true)
        {
            if (drop)
            {
                SQLiteCommand cd = new SQLiteCommand("DROP TABLE IF EXISTS rtp_server", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("CREATE TABLE rtp_server (name VARCHAR(255) NOT NULL UNIQUE, location VARCHAR(255))", sqlConnection);
                cd.ExecuteNonQuery();

                cd = new SQLiteCommand("DROP TABLE IF EXISTS rtp_player", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("CREATE TABLE rtp_player (userid VARCHAR(255), name VARCHAR(255) NOT NULL UNIQUE, location VARCHAR(255))", sqlConnection);
                cd.ExecuteNonQuery();
            }
            else
            {
                SQLiteCommand cd = new SQLiteCommand("DELETE FROM rtp_server", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("DELETE FROM rtp_player", sqlConnection);
                cd.ExecuteNonQuery();
            }
        }
        #endregion
    }
}

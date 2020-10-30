//#define DEBUG
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

// TODO:
//    Cooldown verification and typing
//    Economics for bypass
//    Auto TPA

namespace Oxide.Plugins
{
    [Info("Teleportication", "RFC1920", "1.0.1")]
    [Description("NextGen Teleportation plugin")]
    class Teleportication : RustPlugin
    {
        #region vars
        private SortedDictionary<ulong, Vector3> SavedPoints = new SortedDictionary<ulong, Vector3>();
        private SortedDictionary<ulong, ulong> TPRRequests = new SortedDictionary<ulong, ulong>();
        private SortedDictionary<string, Vector3> monPos  = new SortedDictionary<string, Vector3>();
        private SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();
        private SortedDictionary<string, Vector3> cavePos  = new SortedDictionary<string, Vector3>();

        private readonly Dictionary<ulong, TPTimer> TeleportTimers = new Dictionary<ulong, TPTimer>();
        private readonly Dictionary<ulong, TPTimer> CooldownTimers = new Dictionary<ulong, TPTimer>();
        private const string permTP_Use = "teleportication.use";
        private const string permTP_TPB = "teleportication.tpb";
        private const string permTP_TPR = "teleportication.tpr";
        private const string permTP_Town = "teleportication.town";
        private const string permTP_Bandit = "teleportication.bandit";
        private const string permTP_Outpost = "teleportication.outpost";
        private const string permTP_Admin = "teleportication.admin";

        private ConfigData configData;
        private SQLiteConnection sqlConnection;
        private string connStr;

        private readonly string logfilename = "log";
        private bool dolog = false;

        private readonly Plugin Friends, Clans, RustIO, Economics, ServerRewards;
        private readonly int blockLayer = LayerMask.GetMask("Construction");

        public class TPTimer
        {
            public Timer timer;
            public float start;
            public float countdown;
            public string type;
            public BasePlayer source;
            public string targetName;
            public Vector3 targetLocation;
        }
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region init
        private void Init()
        {
            // Dummy file, creates the directory for us.
            DynamicConfigFile dataFile = Interface.Oxide.DataFileSystem.GetDatafile(Name + "/teleportication");
            dataFile.Save();
#if DEBUG
            Puts("Creating database connection for main thread.");
#endif
            connStr = $"Data Source={Interface.Oxide.DataDirectory}{Path.DirectorySeparatorChar}{Name}{Path.DirectorySeparatorChar}teleportication.db";
            sqlConnection = new SQLiteConnection(connStr);
#if DEBUG
            Puts("Opening database...");
#endif
            sqlConnection.Open();

            LoadConfigVariables();
            LoadData();

            AddCovalenceCommand("home", "CmdHomeTeleport");
            AddCovalenceCommand("sethome", "CmdSetHome");
            AddCovalenceCommand("town", "CmdTownTeleport");
            AddCovalenceCommand("bandit", "CmdTownTeleport");
            AddCovalenceCommand("outpost", "CmdTownTeleport");
            AddCovalenceCommand("tpa", "CmdTpa");
            AddCovalenceCommand("tpb", "CmdTpb");
            AddCovalenceCommand("tpc", "CmdTpc");
            AddCovalenceCommand("tpr", "CmdTpr");
            AddCovalenceCommand("tpadmin", "CmdTpAdmin");

            permission.RegisterPermission(permTP_Use, this);
            permission.RegisterPermission(permTP_TPB, this);
            permission.RegisterPermission(permTP_TPR, this);
            permission.RegisterPermission(permTP_Town, this);
            permission.RegisterPermission(permTP_Bandit, this);
            permission.RegisterPermission(permTP_Outpost, this);
            permission.RegisterPermission(permTP_Admin, this);

            FindMonuments();
        }

        private void OnServerInitialized()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["serverinfo"] = "Server Info - ",
                ["locations"] = "Locations:",
                ["flags"] = "Flags:",
                ["notauthorized"] = "You are not authorized for this command!",
                ["homesavail"] = "The following homes have been set:",
                ["homesavailfor"] = "The following homes have been set by {0}:",
                ["nohomes"] = "No homes have been set.",
                ["hometooclose"] = "Too close to another home - minimum distance {0}",
                ["homeset"] = "Home {0} has been set.",
                ["homeremoved"] = "Home {0} has been removed.",
                ["setblocked"] = "Home cannot be set here - {0}",
                ["invalidhome"] = "Home invalid - {0}",
                ["lastused"] = " Last used: {0} minutes ago",
                ["lastday"] = " Not used since server restart",
                ["list"] = "list",
                ["home"] = "Home",
                ["tpb"] = "old location",
                ["tpr"] = "another player",
                ["town"] = "Town",
                ["outpost"] = "Outpost",
                ["bandit"] = "Bandit",
                ["cooldown"] = "Currently in cooldown for {0} for another {1} seconds.",
                ["reqdenied"] = "Request to teleport to {0} was denied!",
                ["reqaccepted"] = "Request to teleport to {0} was accepted!",
                ["homemissing"] = "No such home...",
                ["notowned"] = "No privileges at the target location!",
                ["missingfoundation"] = "Foundation missing or offset.",
                ["locationnotset"] = "{0} location has not been set!",
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
                ["noprevious"] = "No previous location saved.",
                ["teleportinghome"] = "Teleporting to home {0} in {1} seconds...",
                ["BackupDone"] = "Teleportication database has been backed up to {0}",
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
        #endregion

        #region commands
        [Command("tpadmin")]
        private void CmdTpAdmin(IPlayer iplayer, string command, string[] args)
        {
#if DEBUG
            string debug = string.Join(",", args); Puts($"{debug}");
#endif
            if (!iplayer.HasPermission(permTP_Admin)) { Message(iplayer, "notauthorized"); return; }
            if (args.Length == 1)
            {
                switch (args[0])
                {
                    case "wipe":
                        Message(iplayer, "Wiping data!");
                        CreateOrClearTables(true);
                        FindMonuments();
                        break;
                    case "info":
                        Message(iplayer, Title);
                        Message(iplayer, "locations");
                        string loc = null;
                        using (SQLiteConnection c = new SQLiteConnection(connStr))
                        {
                            c.Open();
                            using (SQLiteCommand q = new SQLiteCommand($"SELECT name, location FROM rtp_server ORDER BY name", c))
                            {
                                using (SQLiteDataReader svr = q.ExecuteReader())
                                {
                                    while (svr.Read())
                                    {
                                        string nm = svr.GetValue(0).ToString();
                                        string lc = svr.GetValue(1).ToString();
                                        loc += "\t" + FirstCharToUpper(nm) + ": " + lc.TrimEnd() + "\n";
                                    }
                                }
                            }
                        }
                        Message(iplayer, loc);

                        string flags = "\tHomeRequireFoundation:\t" + configData.Options.HomeRequireFoundation.ToString() + "\n"
                            + "\tStrictFoundationCheck:\t" + configData.Options.StrictFoundationCheck.ToString() + "\n"
                            + "\tHomeRemoveInvalid:\t" + configData.Options.HomeRemoveInvalid.ToString() + "\n"
                            + "\tHonorBuildingPrivilege:\t" + configData.Options.HonorBuildingPrivilege.ToString() + "\n"
                            + "\tHonorRelationships:\t" + configData.Options.HonorRelationships.ToString() + "\n"
                            + "\tAutoGenBandit:\t" + configData.Options.AutoGenBandit.ToString() + "\n"
                            + "\tAutoGenOutpost:\t" + configData.Options.AutoGenOutpost.ToString() + "\n"
                            + "\tHomeMinimumDistance:\t" + configData.Options.HomeMinimumDistance.ToString() + "\n"
                            + "\tDefaultMonoumentSize:\t" + configData.Options.DefaultMonumentSize.ToString() + "\n"
                            + "\tCaveDistanceSmall:\t" + configData.Options.CaveDistanceSmall.ToString() + "\n"
                            + "\tCaveDistanceMedium:\t" + configData.Options.CaveDistanceMedium.ToString() + "\n"
                            + "\tCaveDistanceLarge:\t" + configData.Options.CaveDistanceLarge.ToString() + "\n"
                            + "\tMinimumTemp:\t" + configData.Options.MinimumTemp.ToString() + "\n"
                            + "\tMaximumTemp:\t" + configData.Options.MaximumTemp.ToString() + "\n"
                            + "\tSetCommand:\t" + configData.Options.SetCommand.ToString() + "\n"
                            + "\tListCommand:\t" + configData.Options.ListCommand.ToString() + "\n"
                            + "\tRemoveCommand:\t" + configData.Options.RemoveCommand.ToString();
                        Message(iplayer, "flags");
                        Message(iplayer, flags);

                        break;
                    case "backup":
                        string backupfile = "teleportication_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".db";
                        if(args.Length > 1)
                        {
                            backupfile = args[1] + ".db";
                        }
                        using (SQLiteConnection c = new SQLiteConnection(connStr))
                        {
                            c.Open();
                            string bkup = $"Data Source={Interface.Oxide.DataDirectory}{Path.DirectorySeparatorChar}{Name}{Path.DirectorySeparatorChar}{backupfile};";
                            using (SQLiteConnection d = new SQLiteConnection(bkup))
                            {
                                d.Open();
                                c.BackupDatabase(d, "main", "main", -1, null, -1);
                            }
                            Message(iplayer, "BackupDone", backupfile);
                        }
                        break;
                }
            }
        }

        [Command("sethome")]
        private void CmdSetHome(IPlayer iplayer, string command, string[] args)
        {
            if(args.Length == 1) CmdHomeTeleport(iplayer, "home", new string[] { "set", args[0] });
        }

        [Command("home")]
        private void CmdHomeTeleport(IPlayer iplayer, string command, string[] args)
        {
            string debug = string.Join(",", args); Puts($"{debug}");
            if (!iplayer.HasPermission(permTP_Use)) { Message(iplayer, "notauthorized"); return; }

            var player = iplayer.Object as BasePlayer;
            if(args.Length < 1 || (args.Length == 1 && args[0] == configData.Options.ListCommand))
            {
                // List homes
                string available = Lang("homesavail") + "\n";
                bool hashomes = false;
                using (SQLiteConnection c = new SQLiteConnection(connStr))
                {
                    c.Open();
                    using (SQLiteCommand q = new SQLiteCommand($"SELECT name, location, lastused FROM rtp_player WHERE userid='{player.userID}'", c))
                    {
                        using (SQLiteDataReader home = q.ExecuteReader())
                        {
                            while (home.Read())
                            {
                                string test = home.GetValue(0).ToString();
                                Vector3 position = StringToVector3(home.GetValue(1).ToString());
                                string pos = PositionToGrid(position);

                                if (test != "")
                                {
                                    string timesince = Math.Floor(Time.realtimeSinceStartup / 60 - Convert.ToSingle(home.GetString(2)) / 60).ToString();
                                    if (int.Parse(timesince) < 0)
                                    {
                                        available += test + ": " + position + " [" + pos + "]" + " " + Lang("lastday") + "\n";
                                    }
                                    else
                                    {
                                        available += test + ": " + position + " [" + pos + "]" + " " + Lang("lastused", null, timesince) + "\n";
                                    }
                                    hashomes = true;
                                }
                            }
                        }
                    }
                }
                if (hashomes)
                {
                    Message(iplayer, available);
                }
                else
                {
                    Message(iplayer, "nohomes");
                }
            }
            else if(args.Length == 2 && (args[0] == configData.Options.ListCommand) && configData.Options.HonorRelationships)
            {
                var target = BasePlayer.Find(args[1]);
                if(IsFriend(player.userID, target.userID))
                {
                    string available = Lang("homesavailfor", null, target.displayName) + "\n";
                    bool hashomes = false;
                    using (SQLiteConnection c = new SQLiteConnection(connStr))
                    {
                        c.Open();
                        using (SQLiteCommand q = new SQLiteCommand($"SELECT name, location, lastused FROM rtp_player WHERE userid='{target.userID}'", c))
                        {
                            using (SQLiteDataReader home = q.ExecuteReader())
                            {
                                while (home.Read())
                                {
                                    string test = home.GetValue(0).ToString();
                                    if (test != "")
                                    {
                                        string timesince = Math.Floor(Time.realtimeSinceStartup / 60 - Convert.ToSingle(home.GetString(2)) / 60).ToString();
                                        //Puts($"Time since {timesince}");
                                        available += test + ": " + home.GetString(1) + " " + Lang("lastused", null, timesince) + "\n";
                                        hashomes = true;
                                    }
                                }
                            }
                        }
                    }
                    if(hashomes)
                    {
                        Message(iplayer, available);
                    }
                    else
                    {
                        Message(iplayer, "nohomes");
                    }
                }
                else
                {
                    Message(iplayer, "notauthorized");
                }
            }
            else if(args.Length == 2 && args[0] == configData.Options.SetCommand)
            {
                // Set home
                string reason;
                if (CanSetHome(player, player.transform.position, out reason))
                {
                    string home = args[1];
                    RunUpdateQuery($"INSERT OR REPLACE INTO rtp_player VALUES('{player.userID}', '{home}', '{player.transform.position.ToString()}', '{Time.realtimeSinceStartup}', 0)");
                    Message(iplayer, "homeset", home);
                }
                else
                {
                    Message(iplayer, "setblocked", reason);
                }
            }
            else if(args.Length == 2 && args[0] == configData.Options.RemoveCommand)
            {
                // Remove home
                string home = args[1];
                List<string> found = (List<string>) RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                if (found != null)
                {
                    RunUpdateQuery($"DELETE FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                    Message(iplayer, "homeremoved", home);
                }
                else
                {
                    Message(iplayer, "homemissing");
                }
            }
            else if(args.Length == 1)
            {
                // Use an already set home
                string home = args[0];
                List<string> homes = (List<string>) RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                if(homes == null)
                {
                    Message(iplayer, "homemissing");
                    return;
                }

                //string reason;
                //if(!CanSetHome(player, StringToVector3(homes[0]), out reason))
                //{
                //    if(configData.Options.HomeRemoveInvalid)
                //    {
                //        RunUpdateQuery($"DELETE FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                //    }
                //    Message(iplayer, "invalidhome", reason);
                //    return;
                //}
                if (CanTeleport(player, homes[0], "Home"))
                {
                    if (!TeleportTimers.ContainsKey(player.userID))
                    {
                        TeleportTimers.Add(player.userID, new TPTimer() { type="home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CountDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                        HandleTimer(player.userID, true);
                        CooldownTimers.Add(player.userID, new TPTimer() { type="home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CoolDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                        HandleCooldown(player.userID, true);
                        Message(iplayer, "teleportinghome", home, configData.Types["Home"].CountDown.ToString());
                    }
                    else if (TeleportTimers[player.userID].countdown == 0)
                    {
                        Teleport(player, StringToVector3(homes[0]));
                    }
                }
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
                    if (!iplayer.HasPermission(permTP_Admin)) { Message(iplayer, "notauthorized"); return; }
                    RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('{command}', '{player.transform.position.ToString()}')");
                    switch (command)
                    {
                        case "town":
                            Message(iplayer, "townset", player.transform.position.ToString());
                            break;
                        case "bandit":
                            Message(iplayer, "banditset", player.transform.position.ToString());
                            break;
                        case "outpost":
                            Message(iplayer, "outpostset", player.transform.position.ToString());
                            break;
                    }
                    return;
                }
            }

            switch (command)
            {
                case "town":
                    if (!iplayer.HasPermission(permTP_Town)) { Message(iplayer, "notauthorized"); return; }
                    List<string> town = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='town'");
                    if (town != null)
                    {
                        if (CanTeleport(player, town[0], "Town"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { type="town", start = Time.realtimeSinceStartup, countdown = configData.Types["Town"].CountDown, source = player, targetName = Lang("town"), targetLocation = StringToVector3(town[0]) });
                                HandleTimer(player.userID, true);
                                CooldownTimers.Add(player.userID, new TPTimer() { type = "town", start = Time.realtimeSinceStartup, countdown = configData.Types["Town"].CoolDown, source = player, targetName = Lang("town"), targetLocation = StringToVector3(town[0]) });
                                HandleCooldown(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Types["Town"].CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(town[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "locationnotset", Lang("town"));
                    break;
                case "bandit":
                    if (!iplayer.HasPermission(permTP_Bandit)) { Message(iplayer, "notauthorized"); return; }
                    List<string> bandit = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='bandit'");
                    if (bandit != null)
                    {
                        if (CanTeleport(player, bandit[0], "Bandit"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { type="bandit", start = Time.realtimeSinceStartup, countdown = configData.Types["Bandit"].CountDown, source = player, targetName = Lang("bandit"), targetLocation = StringToVector3(bandit[0]) });
                                HandleTimer(player.userID, true);
                                CooldownTimers.Add(player.userID, new TPTimer() { type = "bandit", start = Time.realtimeSinceStartup, countdown = configData.Types["Bandit"].CoolDown, source = player, targetName = Lang("bandit"), targetLocation = StringToVector3(bandit[0]) });
                                HandleCooldown(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Types["Bandit"].CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(bandit[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "locationnotset", Lang("bandit"));
                    break;
                case "outpost":
                    if (!iplayer.HasPermission(permTP_Outpost)) { Message(iplayer, "notauthorized"); return; }
                    List<string> outpost = (List<string>) RunSingleSelectQuery("SELECT location FROM rtp_server WHERE name='outpost'");
                    if (outpost != null)
                    {
                        if (CanTeleport(player, outpost[0], "Outpost"))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { type="outpost", start = Time.realtimeSinceStartup, countdown = configData.Types["Outpost"].CountDown, source = player, targetName = Lang("outpost"), targetLocation = StringToVector3(outpost[0]) });
                                HandleTimer(player.userID, true);
                                CooldownTimers.Add(player.userID, new TPTimer() { type = "outpost", start = Time.realtimeSinceStartup, countdown = configData.Types["Outpost"].CoolDown, source = player, targetName = Lang("outpost"), targetLocation = StringToVector3(outpost[0]) });
                                HandleCooldown(player.userID, true);
                                Message(iplayer, "teleporting", command, configData.Types["Outpost"].CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(outpost[0]));
                            }
                        }
                        break;
                    }
                    Message(iplayer, "locationnotset", Lang("outpost"));
                    break;
            }
        }

        [Command("tpb")]
        private void CmdTpb(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permTP_TPB)) { Message(iplayer, "notauthorized"); return; }
            var player = iplayer.Object as BasePlayer;
            if(SavedPoints.ContainsKey(player.userID))
            {
                Vector3 oldloc = SavedPoints[player.userID];

                if (CanTeleport(player, oldloc.ToString(), "TPB"))
                {
                    TeleportTimers.Add(player.userID, new TPTimer() { type="tpb", start = Time.realtimeSinceStartup, countdown = configData.Types["TPB"].CountDown, source = player, targetName = Lang("tpb"), targetLocation = oldloc });
                    HandleTimer(player.userID, true);
                    CooldownTimers.Add(player.userID, new TPTimer() { type = "tpb", start = Time.realtimeSinceStartup, countdown = configData.Types["TPB"].CoolDown, source = player, targetName = Lang("tpb"), targetLocation = oldloc });
                    HandleCooldown(player.userID, true);
                    Message(iplayer, "teleporting", Lang("tpb"), configData.Types["TPB"].CountDown.ToString());
                    SavedPoints.Remove(player.userID);
                }
            }
            else
            {
                Message(iplayer, "noprevious");
            }
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
            if (!iplayer.HasPermission(permTP_TPR)) { Message(iplayer, "notauthorized"); return; }
            //configData.TPR.AutoAccept
        }

        [Command("tpa")]
        private void CmdTpa(IPlayer iplayer, string command, string[] args)
        {
            //configData.TPR.AutoAccept
        }
        #endregion

        #region main
        private bool CanTeleport(BasePlayer player, string location, string type, bool requester = true)
        {
            if (CooldownTimers.ContainsKey(player.userID))
            {
                Puts("Found a cooldown timer");
                if (CooldownTimers[player.userID].type == type.ToLower())
                {
                    string timesince = Math.Floor(CooldownTimers[player.userID].start + CooldownTimers[player.userID].countdown - Time.realtimeSinceStartup).ToString();
                    Message(player.IPlayer, "cooldown", type.ToLower(), timesince);
                    return false;
                }
            }
            var oncargo = player.GetComponentInParent<CargoShip>();
            var onballoon = player.GetComponentInParent<HotAirBalloon>();
            var onlift = player.GetComponentInParent<Lift>();

            string monName = NearMonument(player);
            string cave = NearCave(player);

            if (monName != null && monName.Contains("Oilrig") && configData.Types[type].BlockOnRig)
            {
                Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                return false;
            }
            else if (monName != null && monName.Contains("Excavator") && configData.Types[type].BlockOnExcavator)
            {
                Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                return false;
            }
            else if (monName != null && configData.Types[type].BlockOnMonuments)
            {
                Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                return false;
            }
            if (cave != null && configData.Types[type].BlockOnCave)
            {
                Message(player.IPlayer, "cavetooclose", cave);
                return false;
            }
            if (player.InSafeZone() && configData.Types[type].BlockOnSafe)
            {
                Message(player.IPlayer, "safezone", type.ToLower());
                return false;
            }
            if (oncargo && configData.Types[type].BlockOnCargo)
            {
                Message(player.IPlayer, "oncargo", type.ToLower());
                return false;
            }
            if (onballoon && configData.Types[type].BlockOnBalloon)
            {
                Message(player.IPlayer, "onballoon", type.ToLower());
                return false;
            }
            if (onlift && configData.Types[type].BlockOnLift)
            {
                Message(player.IPlayer, "onlift", type.ToLower());
                return false;
            }
            if (AboveWater(player) && configData.Types[type].BlockOnWater)
            {
                Message(player.IPlayer, "onwater", type.ToLower());
                return false;
            }
            if (player.IsSwimming() && configData.Types[type].BlockOnSwimming)
            {
                Message(player.IPlayer, "onswimming", type.ToLower());
                return false;
            }
            if (player.IsWounded() && requester && configData.Types[type].BlockOnHurt)
            {
                Message(player.IPlayer, "onhurt", type.ToLower());
                return false;
            }
            if (player.metabolism.temperature.value <= configData.Options.MinimumTemp && configData.Types[type].BlockOnCold)
            {
                Message(player.IPlayer, "oncold", type.ToLower());
                return false;
            }
            if (player.metabolism.temperature.value >= configData.Options.MaximumTemp && configData.Types[type].BlockOnHot)
            {
                Message(player.IPlayer, "onhot", type.ToLower());
                return false;
            }
            if (player.isMounted && configData.Types[type].BlockOnMounted)
            {
                Message(player.IPlayer, "onmounted", type.ToLower());
                return false;
            }

            // Passed!
            return true;
        }

        private bool CanSetHome(BasePlayer player, Vector3 position, out string reason)
        {
            reason = null;
            bool rtrn = false;

            List<string> checkhome = (List<string>)RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{player.userID}'") ?? null;
            if (checkhome != null)
            {
                foreach (var home in checkhome)
                {
                    if (Vector3.Distance(player.transform.position, StringToVector3(home)) < configData.Options.HomeMinimumDistance)
                    {
                        reason = Lang("hometooclose", null, configData.Options.HomeMinimumDistance.ToString());
                        return false;
                    }
                }
            }
            if (configData.Options.HomeRequireFoundation)
            {
#if DEBUG
                Puts($"Checking for foundation at target {position.ToString()}");
#endif
                RaycastHit hitinfo;
                if (Physics.Raycast(position, Vector3.down, out hitinfo, 0.1f, blockLayer))
                {
                    var entity = hitinfo.GetEntity();
                    if (entity.PrefabName.Contains("foundation") || position.y < entity.WorldSpaceBounds().ToBounds().max.y)
                    {
#if DEBUG
                        Puts("  Found one.  Checking block perms, etc...");
#endif
                        rtrn = true;
                        if (!BlockCheck(entity, player, position, out reason, configData.Options.HonorBuildingPrivilege))
                        {
                            rtrn = false;
                        }
                    }
                }
                else
                {
                    reason = Lang("missingfoundation");
                    rtrn = false;
                }
            }

            return rtrn;
        }

        private bool BlockCheck(BaseEntity entity, BasePlayer player, Vector3 position, out string reason, bool checktc = false)
        {
            reason = null;
#if DEBUG
            Puts($"BlockCheck() called for {entity.ShortPrefabName}");
#endif
            if (configData.Options.StrictFoundationCheck)
            {
                Vector3 center = entity.CenterPoint();

                List<BaseEntity> ents = new List<BaseEntity>();
                Vis.Entities<BaseEntity>(center, 1.5f, ents);
                foreach (BaseEntity wall in ents)
                {
                    if (wall.name.Contains("external.high"))
                    {
#if DEBUG
                        Puts($"    Found: {wall.name} @ center {center.ToString()}, pos {position.ToString()}");
#endif
                        reason = Lang("highwall");
                        return false;
                    }
                }
#if DEBUG
                Puts($"  Checking block: {entity.name} @ center {center.ToString()}, pos: {position.ToString()}");
#endif
                if (entity.PrefabName.Contains("triangle.prefab"))
                {
                    if (Math.Abs(center.x - position.x) < 0.46f && Math.Abs(center.z - position.z) < 0.46f)
                    {
#if DEBUG
                        Puts($"    Found: {entity.ShortPrefabName} @ center: {center.ToString()}, pos: {position.ToString()}");
#endif
                        if (checktc)
                        {
                            if (!CheckCupboardBlock(entity as BuildingBlock, player))
                            {
                                reason = Lang("notowned");
                                return false;
                            }
                        }

                        return true;
                    }
                }
                else if (entity.PrefabName.Contains("foundation.prefab") || entity.PrefabName.Contains("floor.prefab"))
                {
                    if (Math.Abs(center.x - position.x) < 0.7f && Math.Abs(center.z - position.z) < 0.7f)
                    {
#if DEBUG
                        Puts($"    Found: {entity.ShortPrefabName} @ center: {center.ToString()}, pos: {position.ToString()}");
#endif
                        if (checktc)
                        {
                            if (!CheckCupboardBlock(entity as BuildingBlock, player))
                            {
                                reason = Lang("notowned");
                                return false;
                            }
                        }

                        return true;
                    }
                }
            }
            else if (checktc)
            {
                if (!CheckCupboardBlock(entity as BuildingBlock, player))
                {
#if DEBUG
                    Puts("No strict foundation check, but HonorBuildingPrivilege true - no perms");
#endif
                    reason = Lang("notowned");
                    return false;
                }
            }

            return false;
        }

        // Check that a building block is owned by/attached to a cupboard and that the user has privileges
        private bool CheckCupboardBlock(BuildingBlock block, BasePlayer player)
        {
            BuildingManager.Building building = block.GetBuilding();

            if (building != null)
            {
                if (building.GetDominatingBuildingPrivilege() == null)
                {
                    return false;
                }

                if (building.buildingPrivileges == null)
                {
                    return false;
                }
#if DEBUG
                Puts("Building priv not null, checking authorizedPlayers...");
#endif
                foreach (var priv in building.buildingPrivileges)
                {
                    foreach (var auth in priv.authorizedPlayers.Select(x => x.userid).ToArray())
                    {
                        // If the player is authed, or is a friend of the authed player, return true if HonorRelationships is enabled.
                        // This should avoid TP to a home location where building priv has been lost (PVP).
                        if (auth == player.userID || (configData.Options.HonorRelationships && IsFriend(player.userID, auth)))
                        {
#if DEBUG
                            Puts($"Player {player.userID} has privileges...");
#endif
                            return true;
                        }
                    }
                }
                // No matching priv
#if DEBUG
                Puts("NO BUILDING PRIV");
#endif
                return false;
            }
#if DEBUG
            Puts("NO BUILDING AT ALL");
#endif
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
                float realDistance = monSize[monname].z;
                monvector.y = pos.y;
                float dist = Vector3.Distance(pos, monvector);
#if DEBUG
                Puts($"Checking {monname} dist: {dist.ToString()}, realDistance: {realDistance.ToString()}");
#endif
                if(dist < realDistance)
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
                float realDistance = 0f;

                if(cavename.Contains("Small"))
                {
                    realDistance = configData.Options.CaveDistanceSmall;
                }
                else if(cavename.Contains("Large"))
                {
                    realDistance = configData.Options.CaveDistanceLarge;
                }
                else if(cavename.Contains("Medium"))
                {
                    realDistance = configData.Options.CaveDistanceMedium;
                }

                var cavevector = entry.Value;
                cavevector.y = pos.y;
                var cpos = cavevector.ToString();
                float dist = Vector3.Distance(pos, cavevector);

                if(dist < realDistance)
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
        public static string FirstCharToUpper(string s)
        {
            // Check for empty string.  
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            // Return char and concat substring.  
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        private void DoLog(string message, int indent = 0)
        {
            if (dolog) LogToFile(logfilename, "".PadLeft(indent, ' ') + message, this);
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

        public static string PositionToGrid(Vector3 position) // From GrTeleport
        {
            var r = new Vector2(World.Size / 2 + position.x, World.Size / 2 + position.z);
            var x = Mathf.Floor(r.x / 146.3f) % 26;
            var z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

            return $"{(char)('A' + x)}{z - 1}";
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
                if(realWidth > 0f)
                {
                    extents.z = realWidth;
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
                    Vis.Entities(monument.transform.position, 50, ents);
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
            Puts($"Comparing player {playerid.ToString()} to owner {ownerid.ToString()}");
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

        public void HandleTimer(ulong userid, bool start = false)
        {
            if (TeleportTimers.ContainsKey(userid))
            {
                if (start)
                {
                    TeleportTimers[userid].timer = timer.Once(TeleportTimers[userid].countdown, () => { Teleport(TeleportTimers[userid].source, TeleportTimers[userid].targetLocation); });
                }
                else
                {
                    RunUpdateQuery($"UPDATE rtp_player SET lastused='{Time.realtimeSinceStartup}' WHERE userid='{userid}' AND name='{TeleportTimers[userid].targetName}'");
                    TeleportTimers[userid].timer.Destroy();
                    TeleportTimers.Remove(userid);
                }
            }
        }

        public void HandleCooldown(ulong userid, bool start = false, bool canbypass = false, double bypassamount = 0, bool dobypass = false)
        {
            if (CooldownTimers.ContainsKey(userid))
            {
                if (start)
                {
                    // HandleMoney(string userid, double bypass, bool withdraw = false, bool deposit = false)
                    // Check available funds
                    if (canbypass && HandleMoney(userid.ToString(), bypassamount, dobypass))
                    {

                    }
                    else
                    {
#if DEBUG
                        Puts($"Creating a cooldown timer for {userid}, timer will be set to {CooldownTimers[userid].countdown.ToString()}");
#endif
                        CooldownTimers[userid].timer = timer.Once(CooldownTimers[userid].countdown, () => { HandleCooldown(userid, false, canbypass, bypassamount, dobypass); });
                    }
                }
                else
                {
#if DEBUG
                    Puts($"Destroying cooldown timer for {userid}");
#endif
                    CooldownTimers[userid].timer.Destroy();
                    CooldownTimers.Remove(userid);
                }
            }
        }
        public bool HasCooldown(ulong userid, string type)
        {
            if(CooldownTimers.ContainsKey(userid))
            {
                if (CooldownTimers[userid].type == type) return true;
            }
            return false;
        }

        private bool HandleMoney(string userid, double bypass, bool withdraw = false, bool deposit = false)
        {
            double balance = 0;
            bool hasmoney = false;

            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if(configData.Options.useEconomics && Economics)
            {
                balance = (double)Economics?.CallHook("Balance", userid);
                if(balance >= bypass)
                {
                    hasmoney = true;
                    if(withdraw)
                    {
                        return (bool)Economics?.CallHook("Withdraw", userid, bypass);
                    }
                    else if(deposit)
                    {
                        return (bool)Economics?.CallHook("Deposit", userid, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if(configData.Options.useServerRewards && ServerRewards)
            {
                object bal = ServerRewards?.Call("CheckPoints", userid);
                balance = Convert.ToDouble(bal);
                if(balance >= bypass)
                {
                    hasmoney = true;
                    if(withdraw)
                    {
                        return (bool)ServerRewards?.Call("TakePoints", userid, (int)bypass);
                    }
                    else if(deposit)
                    {
                        return (bool)ServerRewards?.Call("AddPoints", userid, (int)bypass);
                    }
                }
            }

            // Just checking balance without withdrawal or deposit - did we find anything?
            return hasmoney;
        }

        public void SaveLocation(BasePlayer player)
        {
            SavedPoints[player.userID] = player.transform.position;
        }

        public void TeleportToPlayer(BasePlayer player, BasePlayer target) => Teleport(player, target.transform.position);
        public void TeleportToPosition(BasePlayer player, float x, float y, float z) => Teleport(player, new Vector3(x, y, z));

        public void Teleport(BasePlayer player, Vector3 position)
        {
            SaveLocation(player);
            HandleTimer(player.userID);
            HandleCooldown(player.userID);

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
            config.Options.ListCommand = "list";
            config.Options.RemoveCommand = "remove";
            config.Options.HomeMinimumDistance = 10f;
            config.Options.DefaultMonumentSize = 120f;
            config.Options.CaveDistanceSmall = 40f;
            config.Options.CaveDistanceMedium = 60f;
            config.Options.CaveDistanceLarge = 100f;
            config.Options.AutoGenBandit = true;
            config.Options.AutoGenOutpost = true;
            config.Options.MinimumTemp = 0f;
            config.Options.MaximumTemp = 40f;
            config.Types["Home"].CountDown = 5f;
            config.Types["Home"].CoolDown = 120f;
            config.Types["Town"].CountDown = 5f;
            config.Types["Town"].CoolDown = 120f;
            config.Types["Bandit"].CountDown = 5f;
            config.Types["Bandit"].CoolDown = 120f;
            config.Types["Bandit"].BlockOnHostile = true;
            config.Types["Outpost"].CountDown = 5f;
            config.Types["Outpost"].CoolDown = 120f;
            config.Types["Outpost"].BlockOnHostile = true;
            config.Types["TPB"].CountDown = 5f;
            config.Types["TPB"].CoolDown = 120f;
            config.Types["TPC"].CountDown = 5f;
            config.Types["TPC"].CoolDown = 120f;
            config.Types["TPR"].CountDown = 5f;
            config.Types["TPR"].CoolDown = 120f;

            SaveConfig(config);
        }
        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            public Options Options = new Options();
            public Dictionary<string,CmdOptions> Types = new Dictionary<string, CmdOptions>();
            public VersionNumber Version;

            public ConfigData()
            {
                Types.Add("Home", new CmdOptions());
                Types.Add("Town", new CmdOptions());
                Types.Add("Bandit", new CmdOptions());
                Types.Add("Outpost", new CmdOptions());
                Types.Add("TPB", new CmdOptions());
                Types.Add("TPC", new CmdOptions());
                Types.Add("TPR", new CmdOptions());
            }
        }

        private class Options
        {
            public bool useClans = false;
            public bool useFriends = false;
            public bool useTeams = false;
            public bool useEconomics = false;
            public bool useServerRewards = false;
            public bool HomeRequireFoundation = true;
            public bool StrictFoundationCheck = true;
            public bool HomeRemoveInvalid = true;
            public bool HonorBuildingPrivilege = true;
            public bool HonorRelationships = false;
            public bool AutoGenBandit;
            public bool AutoGenOutpost;
            public float HomeMinimumDistance;
            public float DefaultMonumentSize;
            public float CaveDistanceSmall;
            public float CaveDistanceMedium;
            public float CaveDistanceLarge;
            public float MinimumTemp;
            public float MaximumTemp;
            public string SetCommand;
            public string ListCommand;
            public string RemoveCommand;
        }

        private class VIPSetting : CmdOptions
        {
            public string perm;
            public float VIPDailyLimit = 10f;
            public float VIPCountDown = 0f;
            public float VIPCoolDown = 0f;
            public bool VIPAllowBypass = true;
            public double VIPBypassAmount = 0f;
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
            public bool AutoAccept = false;
            public float DailyLimit;
            public float CountDown;
            public float CoolDown;
            public bool AllowBypass;
            public double BypassAmount;
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
                cd = new SQLiteCommand("CREATE TABLE rtp_player (userid VARCHAR(255), name VARCHAR(255) NOT NULL UNIQUE, location VARCHAR(255), lastused VARCHAR(255), total INTEGER(32))", sqlConnection);
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

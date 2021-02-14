//#define DEBUG
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// TODO:
// Economics for bypass
namespace Oxide.Plugins
{
    [Info("Teleportication", "RFC1920", "1.1.5")]
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
        private readonly Dictionary<string, Dictionary<ulong, TPTimer>> CooldownTimers = new Dictionary<string, Dictionary<ulong, TPTimer>>();
        private Dictionary<string, Dictionary<ulong, float>> DailyLimits = new Dictionary<string, Dictionary<ulong, float>>();
        private readonly Dictionary<ulong, TPRTimer> TPRTimers = new Dictionary<ulong, TPRTimer>();
        private int ts;

        private const string permTP_Use = "teleportication.use";
        private const string permTP_TP  = "teleportication.tp";
        private const string permTP_TPB = "teleportication.tpb";
        private const string permTP_TPR = "teleportication.tpr";
        private const string permTP_Town = "teleportication.town";
        private const string permTP_Bandit = "teleportication.bandit";
        private const string permTP_Outpost = "teleportication.outpost";
        private const string permTP_Admin = "teleportication.admin";

        private ConfigData configData;
        private SQLiteConnection sqlConnection;
        private TextInfo TI = CultureInfo.CurrentCulture.TextInfo;
        private string connStr;

        private readonly string logfilename = "log";
        private bool dolog = false;

        [PluginReference]
        private readonly Plugin Friends, Clans, Economics, ServerRewards, GridAPI, NoEscape, Vanish;
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

        public class TPRTimer
        {
            public Timer timer;
            public float start;
            public float countdown;
            public string type;
        }
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region init
        private void Init()
        {
            ts = Convert.ToInt32(DateTime.Now.ToString("HHmmss"));
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
#if DEBUG
            Puts("Setting up cooldown timer dictionary");
#endif
            CooldownTimers.Add("Home", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Town", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPA", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPB", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPR", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TP", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Bandit", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Outpost", new Dictionary<ulong, TPTimer>());
#if DEBUG
            Puts("Setting up daily limits dictionary");
#endif
            DailyLimits.Add("Home", new Dictionary<ulong, float>());
            DailyLimits.Add("Town", new Dictionary<ulong, float>());
            DailyLimits.Add("TPA", new Dictionary<ulong, float>());
            DailyLimits.Add("TPB", new Dictionary<ulong, float>());
            DailyLimits.Add("TPR", new Dictionary<ulong, float>());
            DailyLimits.Add("TP", new Dictionary<ulong, float>());
            DailyLimits.Add("Bandit", new Dictionary<ulong, float>());
            DailyLimits.Add("Outpost", new Dictionary<ulong, float>());

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
            AddCovalenceCommand("tp", "CmdTp");
            AddCovalenceCommand("tpadmin", "CmdTpAdmin");

            permission.RegisterPermission(permTP_Use, this);
            permission.RegisterPermission(permTP_TPB, this);
            permission.RegisterPermission(permTP_TPR, this);
            permission.RegisterPermission(permTP_TP,  this);
            permission.RegisterPermission(permTP_Town, this);
            permission.RegisterPermission(permTP_Bandit, this);
            permission.RegisterPermission(permTP_Outpost, this);
            permission.RegisterPermission(permTP_Admin, this);
#if DEBUG
            Puts("Setting up vip permissions");
#endif
            // Setup permissions from VIPSettings
            foreach(KeyValuePair<string, CmdOptions> ttype in configData.Types)
            {
                if (ttype.Value.VIPSettings == null) continue;
                if(ttype.Value.VIPSettings.Count > 0)
                {
                    foreach(var x in ttype.Value.VIPSettings)
                    {
                        if(!permission.PermissionExists(x.Key,this)) permission.RegisterPermission(x.Key, this);
                    }
                }
            }

            FindMonuments();
        }

        private void Unload()
        {
            sqlConnection.Close();
        }

        protected override void LoadDefaultMessages()
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
                ["blocked"] = "You cannot teleport while blocked!",
                ["blockedinvis"] = "You cannot teleport while invisible!",
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
                ["limit"] = "You have hit the daily limit for {0}: ({1} of {2})",
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
                ["remaining"] = "You have {0} {1} teleports remaining for today.",
                ["teleporting"] = "Teleporting to {0} in {1} seconds...",
                ["noprevious"] = "No previous location saved.",
                ["teleportinghome"] = "Teleporting to home {0} in {1} seconds...",
                ["BackupDone"] = "Teleportication database has been backed up to {0}",
                ["importhelp"] = "/tpadmin import {r/n} {y/1/yes/true}\n\t import RTeleportion or NTeleportation\n\tadd y or 1 or true to actually import\n\totherwise display data only",
                ["tphelp"] = "/tp X,Z OR /tp X,Y,Z -- e.g. /tp 121,-535 will teleport the player to that location on the map.\nIf Y is not specified, player will be moved to ground level.",
                ["cannottp"] = "Cannot teleport to desired location.",
                ["obstructed"] = "The target location is too close to construction.",
                ["importdone"] = "Homes have been imported from datafile '{0}'",
                ["importing"] = "Importing data for {0}",
                ["tpcancelled"] = "Teleport cancelled!",
                ["tprself"] = "You cannot tpr to yourself.",
                ["tprnotify"] = "{0} has requested to be teleported to you.\nType /tpa to accept.",
                ["tpanotify"] = "{0} has accepted your teleport request.  You will be teleported in {1} seconds.",
                ["tprreject"] = "{0} rejected your request.  Or, the request timed out."
            }, this);
        }

        private void OnNewSave()
        {
            if (configData.Options.WipeOnNewSave)
            {
                // Wipe homes and town, etc.
                CreateOrClearTables(true);
                // Set outpost and bandit
                FindMonuments();
            }
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
        [Command("tp")]
        private void CmdTp(IPlayer iplayer, string command, string[] args)
        {
#if DEBUG
            string debug = string.Join(",", args); Puts($"{debug}");
#endif
            if (!iplayer.HasPermission(permTP_TP)) { Message(iplayer, "notauthorized"); return; }
            if (args.Length > 0)
            {
                string[] input = args[0].Split(',');
                if (input.Count() > 1)
                {
                    ulong userid = ulong.Parse(iplayer.Id);
                    string parsed = null;
                    Vector3 pos = new Vector3();
                    if(input.Count() == 3)
                    {
                        parsed = input[0] + "," + input[1] + "," + input[2];
                        pos = StringToVector3(parsed);
                    }
                    else
                    {
                        parsed = input[0] + ",0," + input[1];
                        pos = StringToVector3(parsed);
                        if (TerrainMeta.HeightMap.GetHeight(pos) > pos.y)
                        {
                            // Ensure they are sent above the terrain
                            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                        }
                    }

                    if (CanTeleport(iplayer.Object as BasePlayer, parsed, "TP"))
                    {
                        if (!TeleportTimers.ContainsKey(userid))
                        {
                            TeleportTimers.Add(userid, new TPTimer() { type = "TP", start = Time.realtimeSinceStartup, countdown = configData.Types["TP"].CountDown, source = iplayer.Object as BasePlayer, targetName = "TP", targetLocation = pos });
                            HandleTimer(userid, "TP", true);
                                if (CooldownTimers["TP"].ContainsKey(userid))
                                {
                                    CooldownTimers["TP"][userid].timer.Destroy();
                                    CooldownTimers["TP"].Remove(userid);
                                }
                                CooldownTimers["TP"].Add(userid, new TPTimer() { type = "TP", start = Time.realtimeSinceStartup, countdown = configData.Types["TP"].CoolDown, source = iplayer.Object as BasePlayer, targetName = "TP", targetLocation = pos });
                                HandleCooldown(userid, "TP", true);
                        }
                        else if (TeleportTimers[userid].countdown == 0)
                        {
                            Teleport(iplayer.Object as BasePlayer, pos, "TP");
                        }
                    }
                }
            }
            else
            {
                Message(iplayer, "tphelp");
            }
        }

        [Command("tpadmin")]
        private void CmdTpAdmin(IPlayer iplayer, string command, string[] args)
        {
#if DEBUG
            string debug = string.Join(",", args); Puts($"{debug}");
#endif
            if (!iplayer.HasPermission(permTP_Admin)) { Message(iplayer, "notauthorized"); return; }
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "import":
                        // args[1] == r/n
                        // Import from N/RTeleportation
                        string otpplug = null;
                        bool doit = false;
                        if (args.Length > 1)
                        {
                            switch (args[1])
                            {
                                case "r":
                                    otpplug = "RTeleportation";
                                    break;
                                case "n":
                                    otpplug = "NTeleportation";
                                    break;
                                default:
                                    Message(iplayer, "importhelp");
                                    return;
                            }
                        }
                        if(args.Length > 2)
                        {
                            doit = GetBoolValue(args[2]);
                        }

                        if (otpplug != null)
                        {
                            try
                            {
                                // Get user homes from data file
                                var tpfile = Interface.Oxide.DataFileSystem.GetFile(otpplug + "Home");
                                tpfile.Settings.NullValueHandling = NullValueHandling.Ignore;
                                tpfile.Settings.Converters = new JsonConverter[] { new UnityVector3Converter(), new CustomComparerDictionaryCreationConverter<string>(StringComparer.OrdinalIgnoreCase) };
                                Dictionary<ulong, HomeData> tphomes = tpfile.ReadObject<Dictionary<ulong, HomeData>>();
                                foreach (KeyValuePair<ulong, HomeData> userHomes in tphomes)
                                {
                                    foreach (var home in userHomes.Value.Locations)
                                    {
                                        if (doit)
                                        {
                                            RunUpdateQuery($"INSERT OR REPLACE INTO rtp_player VALUES('{userHomes.Key}', '{home.Key}', '{home.Value}', '0', 0)");
                                            Message(iplayer, "importing", userHomes.Key);
                                        }
                                        else
                                        {
                                            Message(iplayer, $"{userHomes.Key}: Home {home.Key} location {home.Value}");
                                        }
                                    }
                                }
                                if (doit) Message(iplayer, "importdone", otpplug + "Home");
                            }
                            catch
                            {
                                Puts($"Failed to open datafile, {otpplug}Home");
                            }

                            try
                            {
                                // Get town location from config
                                var d = new DataFileSystem(Interface.Oxide.ConfigDirectory);
                                var x = d.GetFiles("", $"{otpplug}.json");
                                OtherConfigData otpcfg = d.GetFile(otpplug).ReadObject<OtherConfigData>();
                                string townloc = otpcfg.Town.Location.ToString().Replace("  ", "").Replace(" ", ",");

                                if (townloc != null)
                                {
                                    if (doit)
                                    {
                                        RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('town', '({townloc})')");
                                        Message(iplayer, "importing", $"Town: ({townloc})");
                                    }
                                    else
                                    {
                                        Message(iplayer, $"Found town location ({townloc})");
                                    }
                                }
                            }
                            catch
                            {
                                Puts($"Failed to open cfgfile for {otpplug}");
                            }
                        }
                        break;
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
                                        loc += "\t" + TI.ToTitleCase(nm) + ": " + lc.TrimEnd() + "\n";
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
#if DEBUG
            string debug = string.Join(",", args); Puts($"{debug}");
#endif
            if (!iplayer.HasPermission(permTP_Use)) { Message(iplayer, "notauthorized"); return; }
            if (iplayer.Id == "server_console") return;

            var player = iplayer.Object as BasePlayer;
            if (args.Length < 1 || (args.Length == 1 && args[0] == configData.Options.ListCommand))
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
            else if (args.Length == 2 && (args[0] == configData.Options.ListCommand) && configData.Options.HonorRelationships)
            {
                // List a friend's homes
                var target = BasePlayer.Find(args[1]);
                if (IsFriend(player.userID, target.userID) && target != null)
                {
                    string available = Lang("homesavailfor", null, RemoveSpecialCharacters(target.displayName)) + "\n";
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
                    if (hashomes)
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
            else if (args.Length == 2 && args[0] == configData.Options.SetCommand)
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
            else if (args.Length == 2 && args[0] == configData.Options.RemoveCommand)
            {
                // Remove home
                string home = args[1];
                List<string> found = (List<string>)RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
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
            else if (args.Length == 2)
            {
                // Use a friend's home: /home Playername home1
                var target = BasePlayer.Find(args[0]);
                if (IsFriend(player.userID, target.userID) && target != null)
                {
                    string home = args[1];
                    List<string> homes = (List<string>)RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{target.userID}' AND name='{home}'");

                    if (CanTeleport(player, homes[0], "Home"))
                    {
                        if (!TeleportTimers.ContainsKey(player.userID))
                        {
                            TeleportTimers.Add(player.userID, new TPTimer() { type = "Home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CountDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                            HandleTimer(player.userID, "Home", true);
                            if (CooldownTimers["Home"].ContainsKey(player.userID))
                            {
                                CooldownTimers["Home"][player.userID].timer.Destroy();
                                CooldownTimers["Home"].Remove(player.userID);
                            }
                            CooldownTimers["Home"].Add(player.userID, new TPTimer() { type = "Home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CoolDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                            HandleCooldown(player.userID, "Home", true);

                            float limit = GetDailyLimit(player.userID, "Home");
                            if (limit > 0)
                            {
                                Message(iplayer, "remaining", limit.ToString(), "Home");
                            }

                            Message(iplayer, "teleportinghome", home + "(" + RemoveSpecialCharacters(target.displayName) + ")", configData.Types["Home"].CountDown.ToString());
                        }
                        else if (TeleportTimers[player.userID].countdown == 0)
                        {
                            Teleport(player, StringToVector3(homes[0]), "home");
                        }
                    }
                }
            }
            else if (args.Length == 1)
            {
                // Use an already set home
                string home = args[0];
                List<string> homes = (List<string>)RunSingleSelectQuery($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                if (homes == null)
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
                        TeleportTimers.Add(player.userID, new TPTimer() { type = "Home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CountDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                        HandleTimer(player.userID, "Home", true);
                        if (CooldownTimers["Home"].ContainsKey(player.userID))
                        {
                            CooldownTimers["Home"][player.userID].timer.Destroy();
                            CooldownTimers["Home"].Remove(player.userID);
                        }
                        CooldownTimers["Home"].Add(player.userID, new TPTimer() { type = "Home", start = Time.realtimeSinceStartup, countdown = configData.Types["Home"].CoolDown, source = player, targetName = home, targetLocation = StringToVector3(homes[0]) });
                        HandleCooldown(player.userID, "Home", true);

                        float limit = GetDailyLimit(player.userID, "Home");
                        if (limit > 0)
                        {
                            Message(iplayer, "remaining", limit.ToString(), "Home");
                        }

                        Message(iplayer, "teleportinghome", home, configData.Types["Home"].CountDown.ToString());
                    }
                    else if (TeleportTimers[player.userID].countdown == 0)
                    {
                        Teleport(player, StringToVector3(homes[0]), "home");
                    }
                }
            }
        }

        [Command("town")]
        private void CmdTownTeleport(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
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
                            if (configData.Options.AddTownMapMarker)
                            {
                                foreach (var mm in BaseEntity.FindObjectsOfType<MapMarkerGenericRadius>().Where(x => x.name == "town").ToList())
                                {
                                    mm.Kill();
                                }
                                MapMarkerGenericRadius marker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", player.transform.position) as MapMarkerGenericRadius;
                                if (marker != null)
                                {
                                    marker.alpha = 0.6f;
                                    marker.color1 = Color.green;
                                    marker.color2 = Color.white;
                                    marker.name = "town";
                                    marker.radius = 0.2f;
                                    marker.Spawn();
                                    marker.SendUpdate();
                                }
                            }
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
                    goto case "all";
                case "bandit":
                    if (!iplayer.HasPermission(permTP_Bandit)) { Message(iplayer, "notauthorized"); return; }
                    goto case "all";
                case "outpost":
                    if (!iplayer.HasPermission(permTP_Outpost)) { Message(iplayer, "notauthorized"); return; }
                    goto case "all";
                case "all":
                    List<string> target = (List<string>) RunSingleSelectQuery($"SELECT location FROM rtp_server WHERE name='{command}'");
                    string type = TI.ToTitleCase(command);
                    if (target != null)
                    {
                        if (CanTeleport(player, target[0], type))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                TeleportTimers.Add(player.userID, new TPTimer() { type = type, start = Time.realtimeSinceStartup, countdown = configData.Types[type].CountDown, source = player, targetName = Lang("town"), targetLocation = StringToVector3(target[0]) });
                                HandleTimer(player.userID, type, true);
                                if (CooldownTimers[type].ContainsKey(player.userID))
                                {
                                    CooldownTimers[type][player.userID].timer.Destroy();
                                    CooldownTimers[type].Remove(player.userID);
                                }
                                CooldownTimers[type].Add(player.userID, new TPTimer() { type = type, start = Time.realtimeSinceStartup, countdown = configData.Types[type].CoolDown, source = player, targetName = Lang("town"), targetLocation = StringToVector3(target[0]) });
                                HandleCooldown(player.userID, type, true);
                                float limit = GetDailyLimit(player.userID, type);
                                if(limit > 0)
                                {
                                    Message(iplayer, "remaining", limit.ToString(), type);
                                }

                                Message(iplayer, "teleporting", command, configData.Types[type].CountDown.ToString());
                            }
                            else if(TeleportTimers[player.userID].countdown == 0)
                            {
                                Teleport(player, StringToVector3(target[0]), command);
                            }
                        }
                        break;
                    }
                    Message(iplayer, "locationnotset", Lang(command));
                    break;
                default:
                    Message(iplayer, "locationnotset", Lang(command));
                    break;
            }
        }

        [Command("tpb")]
        private void CmdTpb(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
            if (!iplayer.HasPermission(permTP_TPB)) { Message(iplayer, "notauthorized"); return; }
            var player = iplayer.Object as BasePlayer;
            if(SavedPoints.ContainsKey(player.userID))
            {
                Vector3 oldloc = SavedPoints[player.userID];

                if (CanTeleport(player, oldloc.ToString(), "TPB"))
                {
                    if (TeleportTimers.ContainsKey(player.userID)) TeleportTimers.Remove(player.userID);
                    TeleportTimers.Add(player.userID, new TPTimer() { type="TPB", start = Time.realtimeSinceStartup, countdown = configData.Types["TPB"].CountDown, source = player, targetName = Lang("tpb"), targetLocation = oldloc });
                    HandleTimer(player.userID, "TPB", true);
                    if (CooldownTimers["TPB"].ContainsKey(player.userID))
                    {
                        CooldownTimers["TPB"][player.userID].timer.Destroy();
                        CooldownTimers["TPB"].Remove(player.userID);
                    }
                    CooldownTimers["TPB"].Add(player.userID, new TPTimer() { type = "TPB", start = Time.realtimeSinceStartup, countdown = configData.Types["TPB"].CoolDown, source = player, targetName = Lang("tpb"), targetLocation = oldloc });
                    HandleCooldown(player.userID, "TPB", true);
                    float limit = GetDailyLimit(player.userID, "TPB");
                    if (limit > 0)
                    {
                        Message(iplayer, "remaining", limit.ToString(), "TPB");
                    }

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
            if (iplayer.Id == "server_console") return;
            var player = iplayer.Object as BasePlayer;
            HandleTimer(player.userID, "tpc");
            Message(iplayer, "tpcancelled");
        }

        [Command("tpr")]
        private void CmdTpr(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
#if DEBUG
            string debug = string.Join(",", args); Puts($"{debug}");
#endif
            if (!iplayer.HasPermission(permTP_TPR)) { Message(iplayer, "notauthorized"); return; }
            if (args.Length == 1)
            {
                var target = FindPlayerByName(args[0]);
                if (target != null)
                {
                    var sourceId = Convert.ToUInt64(iplayer.Id);
                    var targetId = target.userID;
                    if (sourceId == targetId)
                    {
#if DEBUG
                        Puts("Allowing tpr to self in debug mode.");
#else
                        Message(iplayer, "tprself");
                        return;
#endif
                    }
                    if (configData.Types["TPR"].AutoAccept)
                    {
                        if (IsFriend(sourceId, targetId))
                        {
#if DEBUG
                            Puts("AutoTPA!");
#endif
                            if (TeleportTimers.ContainsKey(sourceId)) TeleportTimers.Remove(sourceId);
                            TeleportTimers.Add(sourceId, new TPTimer() { type = "TPR", start = Time.realtimeSinceStartup, countdown = configData.Types["TPR"].CountDown, source = (iplayer.Object as BasePlayer), targetName = iplayer.Name, targetLocation = target.transform.position });
                            HandleTimer(sourceId, "TPR", true);
                        }
                    }
                    else
                    {
                        TPRSetup(sourceId, targetId);
                    }
                }
            }
        }

        [Command("tpa")]
        private void CmdTpa(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
#if DEBUG
            Puts($"Checking for tpr request for {iplayer.Id}");
#endif
            if (TPRRequests.ContainsValue(Convert.ToUInt64(iplayer.Id)))
            {
                var sourceId = TPRRequests.FirstOrDefault(x => x.Value == Convert.ToUInt64(iplayer.Id)).Key;
#if DEBUG
                Puts($"Found a request from {sourceId.ToString()}");
#endif
                IPlayer src = covalence.Players.FindPlayerById(sourceId.ToString());
                if (src != null)
                {
#if DEBUG
                    Puts($"Setting timer for {src.Name} to tp to {iplayer.Name}");
#endif
                    if (TeleportTimers.ContainsKey(sourceId)) TeleportTimers.Remove(sourceId);
                    TeleportTimers.Add(sourceId, new TPTimer() { type = "TPR", start = Time.realtimeSinceStartup, countdown = configData.Types["TPR"].CountDown, source = (src.Object as BasePlayer), targetName = iplayer.Name, targetLocation = (iplayer.Object as BasePlayer).transform.position });
                    HandleTimer(sourceId, "TPR", true);

                    float limit = GetDailyLimit((src.Object as BasePlayer).userID, "TPR");
                    if (limit > 0)
                    {
                        Message(src, "remaining", limit.ToString(), "TPR");
                    }

                    Message(src, "tpanotify", iplayer.Name, configData.Types["TPR"].CountDown.ToString());
                }
            }
        }
        #endregion

        #region main
        void TPRSetup(ulong sourceId, ulong targetId)
        {
            if (TPRRequests.ContainsValue(targetId))
            {
                foreach (var item in TPRRequests.Where(kvp => kvp.Value == targetId).ToList())
                {
                    TPRRequests.Remove(item.Key);
                }
            }
            TPRRequests.Add(sourceId, targetId);
            TPRTimers.Add(sourceId, new TPRTimer() { type = "TPR", start = Time.realtimeSinceStartup, countdown = configData.Types["TPR"].CountDown });
            HandleTimer(sourceId, "TPR", true);
            NextTick(() => { TPRNotification(); });
        }

        void TPRNotification(bool reject = false)
        {
            foreach(var req in TPRRequests)
            {
                if (TPRTimers.ContainsKey(req.Key))
                {
                    IPlayer src = covalence.Players.FindPlayerById(req.Key.ToString());
                    IPlayer tgt = covalence.Players.FindPlayerById(req.Value.ToString());
                    if(reject)
                    {
                        Message(src, "tprreject", req.Value.ToString());
                        TPRTimers[req.Key].timer.Destroy();
                        TPRTimers.Remove(req.Key);
                        return;
                    }
                    Message(tgt, "tprnotify", src.Name);
                    TPRTimers[req.Key].timer.Destroy();
                }
            }
        }

        private bool CanTeleport(BasePlayer player, string location, string type, bool requester = true)
        {
            // OBSTRUCTION
            if (type == "TP" && Obstructed(StringToVector3(location)))
            {
                Message(player.IPlayer, "obstructed");
                return false;
            }
            // LIMIT
            var userLimits = new Dictionary<ulong, float>();
            DailyLimits.TryGetValue(type, out userLimits);
            if(userLimits.Count == 0)
            {
                DailyLimits[type].Add(player.userID, 0);
            }
            if (AtLimit(player.userID, type, DailyLimits[type][player.userID]))
            {
                Message(player.IPlayer, "limit", type.ToLower(), DailyLimits[type][player.userID].ToString(), GetDailyLimit(player.userID, type).ToString());
                return false;
            }
            DailyLimits[type][player.userID] += 1f;

            // COOLDOWN
            if (HasCooldown(player.userID, type))
            {
                string timesince = Math.Floor(CooldownTimers[type][player.userID].start + CooldownTimers[type][player.userID].countdown - Time.realtimeSinceStartup).ToString();
                Message(player.IPlayer, "cooldown", type.ToLower(), timesince);
                return false;
            }

            // HOSTILE
            if((player as BaseCombatEntity).IsHostile() && configData.Types[type].BlockOnHostile)
            {
                float unHostileTime = (float)player.State.unHostileTimestamp;
                float currentTime = (float)Network.TimeEx.currentTimestamp;
                string pt = ((int)Math.Abs(unHostileTime - currentTime) / 60).ToString();
                if ((unHostileTime - currentTime) < 60) pt = "<1";
                Message(player.IPlayer, "onhostile", type, pt);
                return false;
            }

            string monName = NearMonument(player);
            if (monName != null)
            {
                if (monName.Contains("Oilrig") && configData.Types[type].BlockOnRig)
                {
                    Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                    return false;
                }
                else if (monName.Contains("Excavator") && configData.Types[type].BlockOnExcavator)
                {
                    Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                    return false;
                }
                else if (configData.Types[type].BlockOnMonuments)
                {
                    Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                    return false;
                }
            }

            string cave = NearCave(player);
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

            var oncargo = player.GetComponentInParent<CargoShip>();
            if (oncargo && configData.Types[type].BlockOnCargo)
            {
                Message(player.IPlayer, "oncargo", type.ToLower());
                return false;
            }
            var onballoon = player.GetComponentInParent<HotAirBalloon>();
            if (onballoon && configData.Types[type].BlockOnBalloon)
            {
                Message(player.IPlayer, "onballoon", type.ToLower());
                return false;
            }
            var onlift = player.GetComponentInParent<Lift>();
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

            if (configData.Types[type].BlockForNoEscape && configData.Options.useNoEscape && NoEscape != null)
            {
                bool isblocked = (bool)NoEscape?.Call("IsBlocked", player);
                if (isblocked)
                {
                    Message(player.IPlayer, "blocked");
                    return false;
                }
            }

            if (configData.Types[type].BlockIfInvisible && configData.Options.useVanish && Vanish != null)
            {
                bool isblocked = Vanish.Call<bool>("IsInvisible", player);
                if (isblocked)
                {
                    Message(player.IPlayer, "blockedinvis");
                    return false;
                }
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
                Puts($"Checking for foundation/floor at target {position.ToString()}");
#endif
                RaycastHit hitinfo;
                if (Physics.Raycast(position, Vector3.down, out hitinfo, 0.2f, blockLayer))
                {
                    var entity = hitinfo.GetEntity();
                    if (entity.ShortPrefabName.Equals("foundation") || entity.ShortPrefabName.Equals("floor")
                        || entity.ShortPrefabName.Equals("foundation.triangle") || entity.ShortPrefabName.Equals("floor.triangle")
                        || position.y < entity.WorldSpaceBounds().ToBounds().max.y)
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
                Vis.Entities(center, 1.5f, ents);
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
                else if (entity.ShortPrefabName.Equals("foundation") || entity.ShortPrefabName.Equals("floor"))
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

        // Check a location to verify that it is not obstructed by construction.
        public bool Obstructed(Vector3 location)
        {
            var ents = new List<BaseEntity>();
            Vis.Entities(location, 1, ents, blockLayer);
            foreach(var ent in ents)
            {
                return true;
            }
            return false;
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
        public static string RemoveSpecialCharacters(string str)
        {
            return Regex.Replace(str, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
        }

        private static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    result = current;
            }
            return result;
        }

        private static bool GetBoolValue(string value)
        {
            if (value == null) return false;
            value = value.Trim().ToLower();
            switch (value)
            {
                case "t":
                case "true":
                case "1":
                case "yes":
                case "y":
                case "on":
                    return true;
                default:
                    return false;
            }
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

        public string PositionToGrid(Vector3 position) // From GrTeleport for display only
        {
            if (GridAPI != null)
            {
                var g = (string[]) GridAPI.CallHook("GetGrid", position);
                return string.Join("", g);
            }
            else
            {
                var r = new Vector2(World.Size / 2 + position.x, World.Size / 2 + position.z);
                var x = Mathf.Floor(r.x / 146.3f) % 26;
                var z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        public void FindMonuments()
        {
            Vector3 extents = Vector3.zero;
            float realWidth = 0f;
            string name = null;
            bool ishapis =  ConVar.Server.level.Contains("Hapis");
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
                    if (ishapis)
                    {
                        var elem = Regex.Matches(monument.name, @"\w{4,}|\d{1,}");
                        foreach (Match e in elem)
                        {
                            if (e.Value.Equals("MONUMENT")) continue;
                            if (e.Value.Contains("Label")) continue;
                            name += e.Value + " ";
                        }
                        name = name.Trim();
                    }
                    else
                    {
                        name = Regex.Match(monument.name, @"\w{6}\/(.+\/)(.+)\.(.+)").Groups[2].Value.Replace("_", " ").Replace(" 1", "").Titleize();
                    }
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
                    Vis.Entities(monument.transform.position, 50, ents);
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

        // playerid = requesting player, ownerid = target or owner of a home
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
                if(player.currentTeam != 0)
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

        public void HandleTimer(ulong userid, string type, bool start = false)
        {
            if (TeleportTimers.ContainsKey(userid))
            {
                if (start)
                {
                    TeleportTimers[userid].timer = timer.Once(TeleportTimers[userid].countdown, () => { Teleport(TeleportTimers[userid].source, TeleportTimers[userid].targetLocation, type); });
                }
                else
                {
                    RunUpdateQuery($"UPDATE rtp_player SET lastused='{Time.realtimeSinceStartup}' WHERE userid='{userid}' AND name='{TeleportTimers[userid].targetName}'");
                    if (TeleportTimers.ContainsKey(userid))
                    {
                        TeleportTimers[userid].timer.Destroy();
                        TeleportTimers.Remove(userid);
                    }
                    if (TPRTimers.ContainsKey(userid))
                    {
                        TPRTimers[userid].timer.Destroy();
                        TPRTimers.Remove(userid);
                    }
                }
            }
            else if(TPRTimers.ContainsKey(userid))
            {
                if(start)
                {
                    TPRTimers[userid].timer = timer.Once(TPRTimers[userid].countdown, () => { TPRNotification(true); });
                }
                else
                {
                    TPRTimers[userid].timer.Destroy();
                    TPRTimers.Remove(userid);
                }
            }
        }

        public bool HasCooldown(ulong userid, string type)
        {
            if(CooldownTimers[type].ContainsKey(userid))
            {
#if DEBUG
                Puts("Found a cooldown timer");
#endif
                return true;
            }
            return false;
        }
        public void HandleCooldown(ulong userid, string type, bool start = false, bool canbypass = false, double bypassamount = 0, bool dobypass = false, bool kill = false)
        {
            TPTimer check = new TPTimer();
            if(!CooldownTimers[type].ContainsKey(userid))
            {
                CooldownTimers[type].Add(userid, new TPTimer());
            }
#if DEBUG
            Puts($"HandleCooldown found a {type} timer for {userid.ToString()}");
#endif
            if (start)
            {
#if DEBUG
                Puts($"Creating a cooldown timer for {userid}, timer will be set to {configData.Types[type].CoolDown.ToString()} seconds.");
#endif
                CooldownTimers[type][userid].timer = timer.Once(configData.Types[type].CoolDown, () => { HandleCooldown(userid, type, false, canbypass, bypassamount, dobypass, true); });
            }
            else if (kill)
            {
#if DEBUG
                Puts($"Destroying {type} cooldown timer for {userid}");
#endif
                CooldownTimers[type][userid].timer.Destroy();
                CooldownTimers[type].Remove(userid);
            }
        }

        // Check limit for any userid and type based on current activity
        public bool AtLimit(ulong userid, string type, float current)
        {
            float limit = GetDailyLimit(userid, type);
            if (current >= limit && limit != 0) return true;
            return false;
        }
        private float GetDailyLimit(ulong userid, string type)
        {
            float limit = configData.Types[type].DailyLimit;

            IPlayer iplayer = covalence.Players.FindPlayerById(userid.ToString());

            if(configData.Types[type].VIPSettings == null)
            {
                return limit;
            }
            // Check for player VIP permissions
            foreach (var perm in configData.Types[type].VIPSettings)
            {
                if (iplayer.HasPermission(perm.Key))
                {
                    float newlimit = perm.Value.VIPDailyLimit;
                    if (newlimit == 0)
                    {
                        limit = 0;
                    }
                    else if (newlimit > limit)
                    {
                        limit = newlimit;
                    }
                }
            }

            return limit;
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

        // For TPB
        public void SaveLocation(BasePlayer player)
        {
            SavedPoints[player.userID] = player.transform.position;
        }

        private void NextDay()
        {
            // Has midnight passed since the last plugin load?
            int now = Convert.ToInt32(DateTime.Now.ToString("HHmmss"));
            if(now > ts)
            {
                return;
            }
#if DEBUG
            Puts("Clearing the daily limits.");
#endif
            // Day changed.  Reset the daily limits.
            ts = now;
            DailyLimits = new Dictionary<string, Dictionary<ulong, float>>
            {
                { "Home", new Dictionary<ulong, float>() },
                { "Town", new Dictionary<ulong, float>() },
                { "TPA", new Dictionary<ulong, float>() },
                { "TPB", new Dictionary<ulong, float>() },
                { "TPR", new Dictionary<ulong, float>() },
                { "Bandit", new Dictionary<ulong, float>() },
                { "Outpost", new Dictionary<ulong, float>() }
            };
        }

//        public void TeleportToPlayer(BasePlayer player, BasePlayer target) => Teleport(player, target.transform.position);
//        public void TeleportToPosition(BasePlayer player, float x, float y, float z) => Teleport(player, new Vector3(x, y, z));

        public void Teleport(BasePlayer player, Vector3 position, string type="")
        {
            SaveLocation(player);
            HandleTimer(player.userID, type);
            HandleCooldown(player.userID, type);
            NextDay();

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
#if DEBUG
            Puts("Loading configuration...");
#endif
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
            config.Types["Home"].DailyLimit = 30f;
            config.Types["Town"].CountDown = 5f;
            config.Types["Town"].CoolDown = 120f;
            config.Types["Town"].DailyLimit = 30f;
            config.Types["Bandit"].CountDown = 5f;
            config.Types["Bandit"].CoolDown = 120f;
            config.Types["Bandit"].DailyLimit = 30f;
            config.Types["Bandit"].BlockOnHostile = true;
            config.Types["Outpost"].CountDown = 5f;
            config.Types["Outpost"].CoolDown = 120f;
            config.Types["Outpost"].DailyLimit = 30f;
            config.Types["Outpost"].BlockOnHostile = true;
            config.Types["TPB"].CountDown = 5f;
            config.Types["TPB"].CoolDown = 120f;
            config.Types["TPB"].DailyLimit = 30f;
            config.Types["TPC"].CountDown = 5f;
            config.Types["TPC"].CoolDown = 120f;
            config.Types["TPC"].DailyLimit = 30f;
            config.Types["TPR"].CountDown = 5f;
            config.Types["TPR"].CoolDown = 120f;
            config.Types["TPR"].DailyLimit = 30f;
            config.Types["TP"].CountDown = 2f;
            config.Types["TP"].CoolDown = 10f;
            config.Types["TP"].DailyLimit = 30f;

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
                Types.Add("TP", new CmdOptions());
            }
        }

        private class Options
        {
            public bool useClans = false;
            public bool useFriends = false;
            public bool useTeams = false;
            public bool useEconomics = false;
            public bool useServerRewards = false;
            public bool useNoEscape = false;
            public bool useVanish = false;
            public bool HomeRequireFoundation = true;
            public bool StrictFoundationCheck = true;
            public bool HomeRemoveInvalid = true;
            public bool HonorBuildingPrivilege = true;
            public bool HonorRelationships = false;
            public bool WipeOnNewSave = true;
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
            public bool AddTownMapMarker;
        }
        private class CmdOptions : VIPOptions
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
            public bool BlockForNoEscape = false;
            public bool BlockIfInvisible = false;
            public bool AutoAccept = false;
            public float DailyLimit = 0;
            public float CountDown = 5;
            public float CoolDown = 30;
            public bool AllowBypass = false;
            public double BypassAmount = 0;
        }
        private class VIPOptions
        {
            public Dictionary<string, VIPSetting> VIPSettings { get; set; }
        }
        public class VIPSetting
        {
            public float VIPDailyLimit { get; set; }
            public float VIPCountDown { get; set; }
            public float VIPCoolDown { get; set; }
            public bool VIPAllowBypass { get; set; }
            public double VIPBypassAmount { get; set; }

            public VIPSetting()
            {
                VIPDailyLimit = 100f;
                VIPCountDown = 0f;
                VIPCoolDown = 0f;
                VIPAllowBypass = true;
                VIPBypassAmount = 0f;
            }
        }
        #endregion

        #region IMPORT
        /// <summary>
        ///  Classes for import of data from N/RTeleportation
        /// </summary>
        class OtherConfigData
        {
            public SettingsData Settings { get; set; }
            public GameVersionData GameVersion { get; set; }
            public AdminSettingsData Admin { get; set; }
            public HomesSettingsData Home { get; set; }
            public TPRData TPR { get; set; }
            public TownData Town { get; set; }
            public TownData Outpost { get; set; }
            public TownData Bandit { get; set; }
            public VersionNumber Version { get; set; }
        }
        class SettingsData
        {
            public string ChatName { get; set; }
            public bool HomesEnabled { get; set; }
            public bool TPREnabled { get; set; }
            public bool TownEnabled { get; set; }
            public bool OutpostEnabled { get; set; }
            public bool BanditEnabled { get; set; }
            public bool InterruptTPOnHurt { get; set; }
            public bool InterruptTPOnCold { get; set; }
            public bool InterruptTPOnHot { get; set; }
            public bool InterruptTPOnHostile { get; set; }
            public bool InterruptTPOnSafe { get; set; }
            public bool InterruptTPOnBalloon { get; set; }
            public bool InterruptTPOnCargo { get; set; }
            public bool InterruptTPOnRig { get; set; }
            public bool InterruptTPOnExcavator { get; set; }
            public bool InterruptTPOnLift { get; set; }
            public bool InterruptTPOnMonument { get; set; }
            public bool InterruptTPOnMounted { get; set; }
            public bool InterruptTPOnSwimming { get; set; }
            public bool InterruptAboveWater{ get; set; }
            public bool StrictFoundationCheck { get; set; }
            public float CaveDistanceSmall { get; set; }
            public float CaveDistanceMedium { get; set; }
            public float CaveDistanceLarge { get; set; }
            public float DefaultMonumentSize { get; set; }
            public float MinimumTemp { get; set; }
            public float MaximumTemp { get; set; }
            public Dictionary<string, string> BlockedItems { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string BypassCMD { get; set; }
            public bool UseEconomics { get; set; }
            public bool UseServerRewards { get; set; }
            public bool WipeOnUpgradeOrChange { get; set; }
            public bool AutoGenOutpost { get; set; }
            public bool AutoGenBandit { get; set; }
        }
        class GameVersionData
        {
            public int Network { get; set; }
            public int Save { get; set; }
            public string Level { get; set; }
            public string LevelURL { get; set; }
            public int WorldSize { get; set; }
            public int Seed { get; set; }
        }
        class AdminSettingsData
        {
            public bool AnnounceTeleportToTarget { get; set; }
            public bool UseableByAdmins { get; set; }
            public bool UseableByModerators { get; set; }
            public int LocationRadius { get; set; }
            public int TeleportNearDefaultDistance { get; set; }
        }
        class HomesSettingsData
        {
            public int HomesLimit { get; set; }
            public Dictionary<string, int> VIPHomesLimits { get; set; }
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyLimits { get; set; }
            public Dictionary<string, int> VIPCooldowns { get; set; }
            public Dictionary<string, int> VIPCountdowns { get; set; }
            public int LocationRadius { get; set; }
            public bool ForceOnTopOfFoundation { get; set; }
            public bool CheckFoundationForOwner { get; set; }
            public bool UseFriends { get; set; }
            public bool UseClans { get; set; }
            public bool UseTeams { get; set; }
            public bool UsableOutOfBuildingBlocked { get; set; }
            public bool UsableIntoBuildingBlocked { get; set; }
            public bool CupOwnerAllowOnBuildingBlocked { get; set; }
            public bool AllowIceberg { get; set; }
            public bool AllowCave { get; set; }
            public bool AllowCraft { get; set; }
            public bool AllowAboveFoundation { get; set; }
            public bool CheckValidOnList { get; set; }
            public int Pay { get; set; }
            public int Bypass { get; set; }
        }
        class TPRData
        {
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyLimits { get; set; }
            public Dictionary<string, int> VIPCooldowns { get; set; }
            public Dictionary<string, int> VIPCountdowns { get; set; }
            public int RequestDuration { get; set; }
            public bool OffsetTPRTarget { get; set; }
            public bool AutoAcceptTPR { get; set; }
            public bool BlockTPAOnCeiling { get; set; }
            public bool UsableOutOfBuildingBlocked { get; set; }
            public bool UsableIntoBuildingBlocked { get; set; }
            public bool CupOwnerAllowOnBuildingBlocked { get; set; }
            public bool AllowCraft { get; set; }
            public int Pay { get; set; }
            public int Bypass { get; set; }
        }
        class TownData
        {
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyLimits { get; set; }
            public Dictionary<string, int> VIPCooldowns { get; set; }
            public Dictionary<string, int> VIPCountdowns { get; set; }
            public string Location { get; set; }
            public bool UsableOutOfBuildingBlocked { get; set; }
            public bool AllowCraft { get; set; }
            public int Pay { get; set; }
            public int Bypass { get; set; }
        }
        class HomeData
        {
            [JsonProperty("l")]
            public Dictionary<string, Vector3> Locations { get; set; } = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("t")]
            public TeleportData Teleports { get; set; } = new TeleportData();
        }
        class TeleportData
        {
            [JsonProperty("a")]
            public int Amount { get; set; }

            [JsonProperty("d")]
            public string Date { get; set; }

            [JsonProperty("t")]
            public int Timestamp { get; set; }
        }
        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    var values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                var o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }
        private class CustomComparerDictionaryCreationConverter<T> : CustomCreationConverter<IDictionary>
        {
            private readonly IEqualityComparer<T> comparer;

            public CustomComparerDictionaryCreationConverter(IEqualityComparer<T> comparer)
            {
                if (comparer == null)
                    throw new ArgumentNullException(nameof(comparer));
                this.comparer = comparer;
            }

            public override bool CanConvert(Type objectType)
            {
                return HasCompatibleInterface(objectType) && HasCompatibleConstructor(objectType);
            }

            private static bool HasCompatibleInterface(Type objectType)
            {
                return objectType.GetInterfaces().Where(i => HasGenericTypeDefinition(i, typeof(IDictionary<,>))).Any(i => typeof(T).IsAssignableFrom(i.GetGenericArguments().First()));
            }

            private static bool HasGenericTypeDefinition(Type objectType, Type typeDefinition)
            {
                return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeDefinition;
            }

            private static bool HasCompatibleConstructor(Type objectType)
            {
                return objectType.GetConstructor(new[] { typeof(IEqualityComparer<T>) }) != null;
            }

            public override IDictionary Create(Type objectType)
            {
                return Activator.CreateInstance(objectType, comparer) as IDictionary;
            }
        }
        #endregion IMPORT

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

#region License (GPL v2)
/*
    Teleportication - NextGen Teleportation Plugin
    Copyright (c) 2020-2025 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; version 2
    of the License only.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
// Reference: System.Data.SQLite
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Teleportication", "RFC1920", "1.5.5")]
    [Description("NextGen Teleportation plugin")]
    internal class Teleportication : RustPlugin
    {
        #region vars
        private SortedDictionary<ulong, Vector3> SavedPoints = new();
        private SortedDictionary<ulong, ulong> TPRRequests = new();
        private SortedDictionary<string, Vector3> monPos = new();
        private SortedDictionary<string, Vector3> monSize = new();
        private SortedDictionary<string, Vector3> cavePos = new();

        private readonly Dictionary<ulong, TPTimer> TeleportTimers = new();
        private readonly Dictionary<string, Dictionary<ulong, TPTimer>> CooldownTimers = new();
        private Dictionary<string, Dictionary<ulong, float>> DailyUsage = new();
        private readonly Dictionary<ulong, TPRTimer> TPRTimers = new();
        private int dateInt;

        //private Coroutine townPositionsC;
        //private List<Vector3> townPositions = new();

        private bool newsave;
        private const string HGUI = "gui.homes";

        private const string permTP_Use = "teleportication.use";
        private const string permTP_TP = "teleportication.tp";
        private const string permTP_OTHERS = "teleportication.tpothers";
        private const string permTP_TPB = "teleportication.tpb";
        private const string permTP_TPR = "teleportication.tpr";
        private const string permTP_Town = "teleportication.town";
        private const string permTP_Bandit = "teleportication.bandit";
        private const string permTP_Outpost = "teleportication.outpost";
        private const string permTP_Tunnel = "teleportication.tunnel";
        private const string permTP_Admin = "teleportication.admin";

        private ConfigData configData;
        private SQLiteConnection sqlConnection;
        public TextInfo TI = CultureInfo.CurrentCulture.TextInfo;
        private string connStr;

        private readonly string logfilename = "log";

        [PluginReference]
        private readonly Plugin Friends, Clans, Economics, ServerRewards, GridAPI, NoEscape, Vanish, ZoneManager, BankSystem;//, CopyPaste, LootProtect;

        private readonly int blockLayer = LayerMask.GetMask("Construction");
        private readonly int groundLayer = LayerMask.GetMask("Terrain", "World");

        public class TPTimer
        {
            public Timer timer;
            public float start;
            public float cooldown;
            public string type;
            public BasePlayer source;
            public string targetName;
            public Vector3 targetLocation;
            public float counter; // Request count while in cooldown to determine bypass go/no-go
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
        private void OnServerInitialized()
        {
            sqlConnection = new SQLiteConnection(connStr);
            sqlConnection.Open();

            LoadData();
            LoadConfigVariables();

            // Setup permissions from VIPSettings
            foreach (KeyValuePair<string, CmdOptions> ttype in configData.Types)
            {
                if (ttype.Value.VIPSettings == null) continue;
                if (ttype.Value.VIPSettings.Count > 0)
                {
                    foreach (KeyValuePair<string, VIPSetting> x in ttype.Value.VIPSettings)
                    {
                        if (!permission.PermissionExists(x.Key, this)) permission.RegisterPermission(x.Key, this);
                    }
                }
            }

            if (configData.Options.WipeOnNewSave && newsave)
            {
                newsave = false;
                // Wipe homes and town, etc.
                CreateOrClearTables(true);
                //AutoSpawnTown();
            }

            FindMonuments();

            if (configData.Options.AddTownMapMarker)
            {
                List<string> target = QuerySingleStringToList("SELECT location FROM rtp_server WHERE name='town'");
                if (target.Count > 0)
                {
                    Vector3 townPos = StringToVector3(target[0]);
                    //Puts($"Town position: {townPos}");
                    NextTick(() => SetTownMapMarker(townPos));
                }
            }
            MidnightDetect(true);
        }

        private void Init()
        {
            // Dummy file, creates the directory for us.
            DynamicConfigFile dataFile = Interface.GetMod().DataFileSystem.GetDatafile(Name + "/teleportication");
            dataFile.Save();
            connStr = $"Data Source={Path.Combine(Interface.GetMod().DataDirectory, Name, "teleportication.db")};";

            CooldownTimers.Add("Home", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Town", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPA", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPB", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TPR", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("TP", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Bandit", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Outpost", new Dictionary<ulong, TPTimer>());
            CooldownTimers.Add("Tunnel", new Dictionary<ulong, TPTimer>());

            DailyUsage.Add("Home", new Dictionary<ulong, float>());
            DailyUsage.Add("Town", new Dictionary<ulong, float>());
            DailyUsage.Add("TPA", new Dictionary<ulong, float>());
            DailyUsage.Add("TPB", new Dictionary<ulong, float>());
            DailyUsage.Add("TPR", new Dictionary<ulong, float>());
            DailyUsage.Add("TP", new Dictionary<ulong, float>());
            DailyUsage.Add("Bandit", new Dictionary<ulong, float>());
            DailyUsage.Add("Outpost", new Dictionary<ulong, float>());
            DailyUsage.Add("Tunnel", new Dictionary<ulong, float>());

            AddCovalenceCommand("home", "CmdHomeTeleport");
            AddCovalenceCommand("homeg", "CmdHomeGUI");
            AddCovalenceCommand("sethome", "CmdSetHome");
            AddCovalenceCommand("town", "CmdTownTeleport");
            AddCovalenceCommand("bandit", "CmdTownTeleport");
            AddCovalenceCommand("outpost", "CmdTownTeleport");
            AddCovalenceCommand("tunnel", "CmdTownTeleport");
            AddCovalenceCommand("tpa", "CmdTpa");
            AddCovalenceCommand("tpb", "CmdTpb");
            AddCovalenceCommand("tpc", "CmdTpc");
            AddCovalenceCommand("tpr", "CmdTpr");
            AddCovalenceCommand("tp", "CmdTp");
            AddCovalenceCommand("tpadmin", "CmdTpAdmin");

            permission.RegisterPermission(permTP_Use, this);
            permission.RegisterPermission(permTP_TPB, this);
            permission.RegisterPermission(permTP_TPR, this);
            permission.RegisterPermission(permTP_TP, this);
            permission.RegisterPermission(permTP_OTHERS, this);
            permission.RegisterPermission(permTP_Town, this);
            permission.RegisterPermission(permTP_Bandit, this);
            permission.RegisterPermission(permTP_Outpost, this);
            permission.RegisterPermission(permTP_Tunnel, this);
            permission.RegisterPermission(permTP_Admin, this);
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                CuiHelper.DestroyUi(player, HGUI);
            }
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
                ["hometoomany"] = "You cannot set any more homes.  Limit is {0}.",
                ["hometooclose"] = "Too close to another home - minimum distance {0}",
                ["homeset"] = "Home {0} has been set.",
                ["homeremoved"] = "Home {0} has been removed.",
                ["homewasremoved"] = "Home has been removed - {0}.",
                ["setblocked"] = "Home cannot be set here - {0}",
                ["blocked"] = "You cannot teleport while blocked!",
                ["blockedinvis"] = "You cannot teleport while invisible!",
                ["invalidhome"] = "Home invalid - {0}",
                ["lastused"] = " Last used: {0} minutes ago",
                ["lastuse"] = "last use",
                ["name"] = "name",
                ["lastday"] = " Not used since server restart",
                ["list"] = "list",
                ["home"] = "Home",
                ["tpb"] = "old location",
                ["debug"] = "Debug set to {0}",
                ["tpr"] = "another player",
                ["town"] = "Town",
                ["outpost"] = "Outpost",
                ["outpostset"] = "Outpost location has been set to {0}",
                ["tunnels"] = "Available Tunnel Entrances:\n{0}",
                ["bandit"] = "Bandit",
                ["banditset"] = "Bandit Town location has been set to {0}",
                ["InCooldownNoticeNoMoney"] = "Currently in cooldown for {0} for another {1} second(s).  Insufficient funds to bypass.",
                ["InCooldownNoticeNoPmt"] = "Currently in cooldown for {0} for another {1} second(s).",
                ["InCooldownNoticePmt"] = "Currently in cooldown for {0} for another {1} second(s).  Run again to pay {2} for bypass.",
                ["CooldownBypassedNotice"] = "Cooldown for {0} bypassed by paying {1}",
                ["limit"] = "You have hit the daily limit for {0}: ({1} of {2})",
                ["reqdenied"] = "Request to teleport to {0} was denied!",
                ["reqaccepted"] = "Request to teleport to {0} was accepted!",
                ["homemissing"] = "No such home...",
                ["crafting"] = "You are not allowed to teleport while crafting.",
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
                ["onboat"] = "You cannot use /{0} on a tugboat.",
                ["oniceberg"] = "You cannot use /{0} on an iceberg.",
                ["intunnel"] = "You cannot use /{0} to/from the tunnel system.",
                ["safezone"] = "You cannot use /{0} from a safe zone.",
                ["remaining"] = "You have {0} {1} teleports remaining for today.",
                ["teleporting"] = "Teleporting to {0} in {1} second(s)...",
                ["sortedby"] = "sorted by {0}",
                ["noprevious"] = "No previous location saved.",
                ["teleportinghome"] = "Teleporting to home {0} in {1} second(s)...",
                ["BackupDone"] = "Teleportication database has been backed up to {0}",
                ["importhelp"] = "/tpadmin import {r/n} {y/1/yes/true}\n\t import RTeleportion or NTeleportation\n\tadd y or 1 or true to actually import\n\totherwise display data only",
                ["tphelp"] = "/tp X,Z OR /tp X,Y,Z -- e.g. /tp 121,-535 will teleport the player to that location on the map.\nIf Y is not specified, player will be moved to ground level.",
                ["tphelpnew"] = "/tp X,Z OR /tp X,Y,Z -- e.g. /tp 121,-535 will teleport the player to that location on the map.\n  If Y is not specified, player will be moved to ground level.\n/tp YOURNAME PLAYERNAME\n  Teleport yourself to another player.\n/tp PLAYERNAME YOURNAME\n  Teleport specified player to your location, if allowed.",
                ["cannottp"] = "Cannot teleport to desired location.",
                ["obstructed"] = "The target location is too close to construction.",
                ["importdone"] = "Homes have been imported from datafile '{0}'",
                ["importing"] = "Importing data for {0}",
                ["tpcancelled"] = "Teleport cancelled!",
                ["tprself"] = "You cannot tpr to yourself.",
                ["playernotfound"] = "Source or target player not found",
                ["cooldown"] = "Cooldown:",
                ["cooldowns"] = "COOLDOWNS:",
                ["playerhomes"] = "PLAYER HOMES:",
                ["remaining"] = "Remaining:",
                ["tprnotify"] = "{0} has requested to be teleported to you.\nType /tpa to accept.",
                ["tpanotify"] = "{0} has accepted your teleport request.  You will be teleported in {1} second(s).",
                ["tprreject"] = "{0} rejected your request.  Or, the request timed out."
            }, this);
        }

        private void OnNewSave() => newsave = true;

        private void LoadData()
        {
            bool found = false;
            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                using (SQLiteCommand r = new("SELECT name FROM sqlite_master WHERE type='table' AND name='rtp_server'", c))
                using (SQLiteDataReader rtbl = r.ExecuteReader())
                {
                    while (rtbl.Read()) { found = true; }
                }
            }
            if (!found) CreateOrClearTables(true);
        }
        #endregion

        #region commands
        [Command("tp")]
        private void CmdTp(IPlayer iplayer, string command, string[] args)
        {
            if (configData.Options.debug) { string debug = string.Join(",", args); Puts($"{debug}"); }
            if (!iplayer.HasPermission(permTP_TP) && !iplayer.HasPermission(permTP_OTHERS) && !iplayer.HasPermission(permTP_Admin))
            {
                Message(iplayer, "notauthorized"); return;
            }

            BasePlayer player = iplayer.Object as BasePlayer;
            if (args.Length == 1)
            {
                // TP to coordinates X,Y,Z or X,0,Z (/tp 100,0,100 OR /tp 100,100)
                // 0 or no Y arg will be translated to the terrain height at the destination
                string[] input = args[0].Split(',');
                if (input.Length > 1)
                {
                    Vector3 pos = default(Vector3);
                    string parsed;
                    if (input.Length == 3)
                    {
                        parsed = input[0] + "," + input[1] + "," + input[2];
                        DoLog($"Parsed arg input from X,Y,Z into coordinates: s:{parsed}/v:{pos}");
                        pos = StringToVector3(parsed);
                    }
                    else if (input.Length == 2)
                    {
                        parsed = input[0] + ",0," + input[1];
                        pos = StringToVector3(parsed);
                        DoLog($"Parsed arg input from X,Y into coordinates: s:{parsed}/v:{pos}");
                        if (TerrainMeta.HeightMap.GetHeight(pos) > pos.y)
                        {
                            // Ensure they are sent above the terrain
                            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                        }
                    }
                    else
                    {
                        Message(iplayer, "tphelpnew");
                        return;
                    }

                    ulong userid = ulong.Parse(iplayer.Id);
                    if (CanTeleport(player, parsed, "TP"))
                    {
                        if (!TeleportTimers.ContainsKey(userid))
                        {
                            AddTimer(player, pos, "TP", "TP");
                            HandleTimer(userid, "TP", true);
                            CreateCooldown(player, "TP");

                            if (!DailyUsage["TP"].ContainsKey(userid)) DailyUsage["TP"].Add(userid, 0);
                            float usage = GetDailyLimit(userid, "TP") - DailyUsage["TP"][userid];
                            if (usage > 0)
                            {
                                Message(iplayer, "remaining", usage.ToString(), "TPB");
                            }
                        }
                        else if (TeleportTimers[userid].cooldown == 0)
                        {
                            Teleport(player, pos, "TP");
                        }
                    }
                }
            }
            else if (args.Length == 2)
            {
                // Player to player tp (/tp src target), sleeping or not
                BasePlayer srcPlayer = FindPlayerByName(args[0], true);
                BasePlayer tgtPlayer = FindPlayerByName(args[1], true);

                if (srcPlayer == null || tgtPlayer == null)
                {
                    Message(iplayer, "playernotfound");
                    return;
                }

                // 'player' set above, i.e. the player running the command
                bool allowed = false;
                if (srcPlayer != player && tgtPlayer != player)
                {
                    if (iplayer.HasPermission(permTP_Admin))
                    {
                        // Allowed to tp 2nd player to 3rd player based on permTP_Admin
                        allowed = true;
                    }
                    else
                    {
                        // No permission to tp 2nd player to 3rd player, i.e. not the calling player
                        Message(iplayer, "notauthorized");
                        return;
                    }
                }
                else if (tgtPlayer == player && iplayer.HasPermission(permTP_OTHERS))
                {
                    // Allowed to tp srcPlayer to tgtPlayer (self) based on permTP_OTHERS
                    allowed = true;
                }
                else if (srcPlayer == player)
                {
                    // Allowed to tp srcPlayer (self) to tgtPlayer based on perm_TP
                    allowed = true;
                }

                if (allowed)
                {
                    if (TeleportTimers.ContainsKey(srcPlayer.userID)) TeleportTimers.Remove(srcPlayer.userID);

                    AddTimer(srcPlayer, tgtPlayer.transform.position, "TP", "TP");
                    HandleTimer(srcPlayer.userID, "TP", true);

                    if (!DailyUsage["TP"].ContainsKey(player.userID)) DailyUsage["TP"].Add(player.userID, 0);
                    float usage = GetDailyLimit(player.userID, "TP") - DailyUsage["TP"][player.userID];
                    if (usage > 0)
                    {
                        Message(iplayer, "remaining", usage.ToString(), "TP");
                    }
                    return;
                }
                Message(iplayer, "notauthorized");
            }
            else
            {
                Message(iplayer, "tphelpnew");
            }
        }

        [Command("tpadmin")]
        private void CmdTpAdmin(IPlayer iplayer, string command, string[] args)
        {
            if (configData.Options.debug) { string debug = string.Join(",", args); Puts($"{debug}"); }

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
                        if (args.Length > 2)
                        {
                            doit = GetBoolValue(args[2]);
                        }

                        if (otpplug != null)
                        {
                            try
                            {
                                // Get user homes from data file
                                DynamicConfigFile tpfile = Interface.GetMod().DataFileSystem.GetFile(otpplug + "Home");
                                tpfile.Settings.NullValueHandling = NullValueHandling.Ignore;
                                tpfile.Settings.Converters = new JsonConverter[] { new UnityVector3Converter(), new CustomComparerDictionaryCreationConverter<string>(StringComparer.OrdinalIgnoreCase) };
                                foreach (KeyValuePair<ulong, HomeData> userHomes in tpfile.ReadObject<Dictionary<ulong, HomeData>>())
                                {
                                    foreach (KeyValuePair<string, Vector3> home in userHomes.Value.Locations)
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
                                DataFileSystem d = new(Interface.GetMod().ConfigDirectory);
                                string[] x = d.GetFiles("", $"{otpplug}.json");
                                OtherConfigData otpcfg = d.GetFile(otpplug).ReadObject<OtherConfigData>();
                                string townloc = otpcfg.Town.Location.Replace("  ", "").Replace(" ", ",");

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
                        using (SQLiteConnection c = new(connStr))
                        {
                            c.Open();
                            using (SQLiteCommand q = new("SELECT name, location FROM rtp_server ORDER BY name", c))
                            using (SQLiteDataReader svr = q.ExecuteReader())
                            {
                                while (svr.Read())
                                {
                                    string nm = !svr.IsDBNull(0) ? svr.GetString(0) : "";
                                    string lc = !svr.IsDBNull(1) ? svr.GetString(1) : "";
                                    loc += "\t" + TI.ToTitleCase(nm) + ": " + lc.TrimEnd() + "\n";
                                }
                            }
                        }
                        Message(iplayer, loc);

                        string flags = $"\tHomeRequireFoundation:\t{configData.Options.HomeRequireFoundation}\n"
                            + $"\tStrictFoundationCheck:\t{configData.Options.StrictFoundationCheck}\n"
                            + $"\tHomeRemoveInvalid:\t{configData.Options.HomeRemoveInvalid}\n"
                            + $"\tHonorBuildingPrivilege:\t{configData.Options.HonorBuildingPrivilege}\n"
                            + $"\tHonorRelationships:\t{configData.Options.HonorRelationships}\n"
                            + $"\tAutoGenBandit:\t{configData.Options.AutoGenBandit}\n"
                            + $"\tAutoGenOutpost:\t{configData.Options.AutoGenOutpost}\n"
                            + $"\tHomeMinimumDistance:\t{configData.Options.HomeMinimumDistance}\n"
                            + $"\tDefaultMonoumentSize:\t{configData.Options.DefaultMonumentSize}\n"
                            + $"\tCaveDistanceSmall:\t{configData.Options.CaveDistanceSmall}\n"
                            + $"\tCaveDistanceMedium:\t{configData.Options.CaveDistanceMedium}\n"
                            + $"\tCaveDistanceLarge:\t{configData.Options.CaveDistanceLarge}\n"
                            + $"\tMinimumTemp:\t{configData.Options.MinimumTemp}\n"
                            + $"\tMaximumTemp:\t{configData.Options.MaximumTemp}\n"
                            + $"\tSetCommand:\t{configData.Options.SetCommand}\n"
                            + $"\tListCommand:\t{configData.Options.ListCommand}\n"
                            + $"\tRemoveCommand:\t{configData.Options.RemoveCommand}";
                        Message(iplayer, "flags");
                        Message(iplayer, flags);

                        break;
                    case "playerinfo":
                    case "pinfo":
                        Message(iplayer, $"{Title} Player Information");
                        Message(iplayer, Lang("cooldowns"));
                        string output = "";
                        foreach (KeyValuePair<string, Dictionary<ulong, TPTimer>> cdt in CooldownTimers)
                        {
                            output += "\t" + cdt.Key + ": \n";
                            foreach (KeyValuePair<ulong, TPTimer> tinfo in cdt.Value)
                            {
                                output += $"\t\t{BasePlayer.FindAwakeOrSleeping(tinfo.Key.ToString()).displayName}, "
                                    + $"{Lang("cooldown")} {tinfo.Value.cooldown}, "
                                    + $"{Lang("remaining")} {Math.Abs(Time.realtimeSinceStartup - tinfo.Value.start - tinfo.Value.cooldown)}\n";
                            }
                        }
                        output += $"\n{Lang("playerhomes")}\n";
                        Dictionary<string, int> pCount = new();
                        using (SQLiteConnection c = new(connStr))
                        {
                            c.Open();
                            using (SQLiteCommand q = new("SELECT userid FROM rtp_player ORDER BY userid", c))
                            using (SQLiteDataReader svr = q.ExecuteReader())
                            {
                                while (svr.Read())
                                {
                                    string playerId = !svr.IsDBNull(0) ? svr.GetString(0) : "";
                                    if (playerId.Length == 0) continue;
                                    if (!pCount.ContainsKey(playerId))
                                    {
                                        pCount.Add(playerId, 1);
                                        continue;
                                    }
                                    pCount[playerId]++;
                                }
                            }
                        }
                        foreach (KeyValuePair<string, int> pHomes in pCount)
                        {
                            output += $"\t{BasePlayer.FindAwakeOrSleeping(pHomes.Key)?.displayName} has {pHomes.Value} homes\n";
                        }
                        Message(iplayer, output);
                        break;
                    case "debug":
                        configData.Options.debug = !configData.Options.debug;
                        Message(iplayer, "debug", configData.Options.debug.ToString());
                        break;
                    case "backup":
                        string backupfile = "teleportication_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".db";
                        if (args.Length > 1)
                        {
                            backupfile = args[1] + ".db";
                        }
                        using (SQLiteConnection c = new(connStr))
                        {
                            c.Open();
                            string file = $"{Interface.GetMod().DataDirectory}{Path.DirectorySeparatorChar}{Name}{Path.DirectorySeparatorChar}{backupfile}";
                            SQLiteConnection.CreateFile(file);
                            SQLiteCommand cmd = c.CreateCommand();
                            cmd.CommandText = $"VACUUM INTO '{file}'";
                            cmd.ExecuteNonQuery();
                            Message(iplayer, "BackupDone", backupfile);
                        }
                        break;
                }
            }
        }

        [Command("sethome")]
        private void CmdSetHome(IPlayer iplayer, string command, string[] args)
        {
            if (args.Length == 1) CmdHomeTeleport(iplayer, "home", new string[] { configData.Options.SetCommand, args[0] });
        }

        [Command("home")]
        private void CmdHomeTeleport(IPlayer iplayer, string command, string[] args)
        {
            if (configData.Options.debug) { string debug = string.Join(",", args); Puts($"{debug}"); }

            if (!iplayer.HasPermission(permTP_Use)) { Message(iplayer, "notauthorized"); return; }
            if (iplayer.Id == "server_console") return;

            BasePlayer player = iplayer.Object as BasePlayer;
            if (args.Length < 1 || (args.Length == 1 && args[0] == configData.Options.ListCommand))
            {
                // List homes
                string available = Lang("homesavail") + "\n";
                bool hashomes = false;
                List<Vector3> allhomes = new();
                string firstHome = string.Empty;
                using (SQLiteConnection c = new(connStr))
                {
                    c.Open();
                    string qh = $"SELECT name, location, lastused FROM rtp_player WHERE userid='{player.userID}'";
                    //Puts(qh);
                    using (SQLiteCommand q = new(qh, c))
                    using (SQLiteDataReader home = q.ExecuteReader())
                    {
                        while (home.Read())
                        {
                            string test = !home.IsDBNull(0) ? home.GetString(0) : "";
                            Vector3 position = StringToVector3(!home.IsDBNull(1) ? home.GetString(1) : "");
                            string pos = PositionToGrid(position);

                            if (test != "")
                            {
                                string timesince = Math.Floor((Time.realtimeSinceStartup / 60) - (Convert.ToSingle(home.GetString(2)) / 60)).ToString();
                                if (int.Parse(timesince) < 0)
                                {
                                    available += test + ": " + position + " [" + pos + "] " + Lang("lastday") + "\n";
                                }
                                else
                                {
                                    available += test + ": " + position + " [" + pos + "] " + Lang("lastused", null, timesince) + "\n";
                                }
                                hashomes = true;
                                allhomes.Add(position);
                                firstHome = test;
                            }
                        }
                    }
                }
                if (hashomes)
                {
                    if (allhomes.Count == 1 && CanTeleport(player, allhomes[0].ToString(), "Home") && configData.Types["Home"].IfOneHomeJustGoThere)
                    {
                        if (!TeleportTimers.ContainsKey(player.userID))
                        {
                            AddTimer(player, allhomes[0], "Home", "Home");
                            HandleTimer(player.userID, "Home", true);
                            CreateCooldown(iplayer.Object as BasePlayer, "Home");

                            if (!DailyUsage["Home"].ContainsKey(player.userID)) DailyUsage["Home"].Add(player.userID, 0);
                            float usage = GetDailyLimit(player.userID, "Home") - DailyUsage["Home"][player.userID];
                            if (usage > 0)
                            {
                                Message(iplayer, "remaining", usage.ToString(), "Home");
                            }

                            Message(iplayer, "teleportinghome", firstHome + "(" + RemoveSpecialCharacters(firstHome) + ")", configData.Types["Home"].CountDown.ToString());
                        }
                        else if (TeleportTimers[player.userID].cooldown == 0)
                        {
                            Teleport(player, allhomes[0], "home");
                        }
                        return;
                    }
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
                BasePlayer target = BasePlayer.Find(args[1]);
                if (target != null && IsFriend(player.userID, target.userID))
                {
                    string available = Lang("homesavailfor", null, RemoveSpecialCharacters(target.displayName)) + "\n";
                    bool hashomes = false;
                    using (SQLiteConnection c = new(connStr))
                    {
                        c.Open();
                        using (SQLiteCommand q = new($"SELECT name, location, lastused FROM rtp_player WHERE userid='{target.userID}'", c))
                        using (SQLiteDataReader home = q.ExecuteReader())
                        {
                            while (home.Read())
                            {
                                string test = !home.IsDBNull(0) ? home.GetString(0) : "";
                                string lastused = !home.IsDBNull(2) ? home.GetString(2) : "";
                                if (test != "")
                                {
                                    string timesince = Math.Floor((Time.realtimeSinceStartup / 60) - (Convert.ToSingle(lastused) / 60)).ToString();
                                    //Puts($"Time since {timesince}");
                                    available += test + ": " + home.GetString(1) + " " + Lang("lastused", null, timesince) + "\n";
                                    hashomes = true;
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
                    bool found = false;
                    using (SQLiteConnection c = new(connStr))
                    {
                        c.Open();
                        string q = $"SELECT name FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'";
                        DoLog(q);
                        using (SQLiteCommand ct = new(q, c))
                        using (SQLiteDataReader pl = ct.ExecuteReader())
                        {
                            while (pl.Read())
                            {
                                if (!pl.IsDBNull(0) && pl.GetString(0) == home) found = true;
                            }
                        }
                    }
                    if (found)
                    {
                        RunUpdateQuery($"UPDATE rtp_player SET location='{player.transform.position}' WHERE userid='{player.userID}' AND name='{home}'");
                    }
                    else
                    {
                        RunUpdateQuery($"INSERT INTO rtp_player VALUES('{player.userID}', '{home}', '{player.transform.position}', '{Time.realtimeSinceStartup}', 0)");
                    }
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
                List<string> found = QuerySingleStringToList($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                if (found.Count > 0)
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
                BasePlayer target = BasePlayer.Find(args[0]);
                if (target != null && IsFriend(player.userID, target.userID))
                {
                    string home = args[1];
                    List<string> homes = QuerySingleStringToList($"SELECT location FROM rtp_player WHERE userid='{target.userID}' AND name='{home}'");

                    if (homes.Count > 0 && CanTeleport(player, homes[0], "Home"))
                    {
                        if (!TeleportTimers.ContainsKey(player.userID))
                        {
                            AddTimer(player, StringToVector3(homes[0]), "Home", "Home");
                            HandleTimer(player.userID, "Home", true);
                            CreateCooldown(iplayer.Object as BasePlayer, "Home");

                            if (!DailyUsage["Home"].ContainsKey(player.userID)) DailyUsage["Home"].Add(player.userID, 0);
                            float usage = GetDailyLimit(player.userID, "Home") - DailyUsage["Home"][player.userID];
                            if (usage > 0)
                            {
                                Message(iplayer, "remaining", usage.ToString(), "Home");
                            }

                            Message(iplayer, "teleportinghome", home + "(" + RemoveSpecialCharacters(target.displayName) + ")", configData.Types["Home"].CountDown.ToString());
                        }
                        else if (TeleportTimers[player.userID].cooldown == 0)
                        {
                            Teleport(player, StringToVector3(homes[0]), "home");
                        }
                    }
                }
            }
            else if (args.Length == 1)
            {
                CuiHelper.DestroyUi(player, HGUI);
                // Use an already set home
                string home = args[0];
                List<string> homes = QuerySingleStringToList($"SELECT location FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                if (homes.Count == 0)
                {
                    Message(iplayer, "homemissing");
                    return;
                }

                if (CanTeleport(player, homes[0], "Home"))
                {
                    if (!TeleportTimers.ContainsKey(player.userID))
                    {
                        AddTimer(player, StringToVector3(homes[0]), "Home", "Home");
                        HandleTimer(player.userID, "Home", true);
                        CreateCooldown(iplayer.Object as BasePlayer, "Home");

                        if (!DailyUsage["Home"].ContainsKey(player.userID)) DailyUsage["Home"].Add(player.userID, 0);
                        float usage = GetDailyLimit(player.userID, "Home") - DailyUsage["Home"][player.userID];
                        if (usage > 0)
                        {
                            Message(iplayer, "remaining", usage.ToString(), "Home");
                        }

                        Message(iplayer, "teleportinghome", home, configData.Types["Home"].CountDown.ToString());
                    }
                    else if (TeleportTimers[player.userID].cooldown == 0)
                    {
                        Teleport(player, StringToVector3(homes[0]), "home");
                    }
                }
            }
        }

        [Command("homeg")]
        private void CmdHomeGUI(IPlayer iplayer, string command, string[] args)
        {
            if (configData.Options.debug) { string debug = string.Join(",", args); Puts($"{debug}"); }

            if (!iplayer.HasPermission(permTP_Use)) { Message(iplayer, "notauthorized"); return; }
            if (iplayer.Id == "server_console") return;

            BasePlayer player = iplayer.Object as BasePlayer;
            string sort = "alpha";

            if (args.Length > 0)
            {
                sort = args[0];
            }
            if (sort == "closeit")
            {
                CuiHelper.DestroyUi(player, HGUI);
                return;
            }
            HomeGUI(player, sort);
        }

        [Command("town")]
        private void CmdTownTeleport(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
            BasePlayer player = iplayer.Object as BasePlayer;
            if (args.Length > 0 && args[0] == configData.Options.SetCommand)
            {
                if (!iplayer.HasPermission(permTP_Admin)) { Message(iplayer, "notauthorized"); return; }
                RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('{command}', '{player.transform.position}')");
                switch (command)
                {
                    case "town":
                        Message(iplayer, "townset", player.transform.position.ToString());
                        Puts(player.transform.position.ToString());
                        if (configData.Options.AddTownMapMarker)
                        {
                            SetTownMapMarker(player.transform.position);
                        }
                        if (configData.Options.TownZoneId?.Length > 0)
                        {
                            string[] zone_args = { "name", "Town", "radius", "150" };
                            object ckzone = ZoneManager?.Call("CheckZoneID", configData.Options.TownZoneId);

                            if (ckzone != null && ckzone is bool && !(bool)ckzone)
                            {
                                configData.Options.TownZoneId = UnityEngine.Random.Range(1, 99999999).ToString();
                                SaveConfig(configData);
                            }
                            if (configData.Options.TownZoneEnterMessage.Length > 0)
                            {
                                List<string> arglist = zone_args.ToList();
                                arglist.Add("enter_message");
                                arglist.Add(configData.Options.TownZoneEnterMessage);
                                zone_args = arglist.ToArray();
                            }
                            if (configData.Options.TownZoneLeaveMessage.Length > 0)
                            {
                                List<string> arglist = zone_args.ToList();
                                arglist.Add("leave_message");
                                arglist.Add(configData.Options.TownZoneLeaveMessage);
                                zone_args = arglist.ToArray();
                            }
                            ZoneManager?.Call("CreateOrUpdateZone", configData.Options.TownZoneId, zone_args, player.transform.position);
                            if (configData.Options.TownZoneFlags.Count > 0)
                            {
                                foreach (string flag in configData.Options.TownZoneFlags)
                                {
                                    ZoneManager?.Call("AddFlag", configData.Options.TownZoneId, flag);
                                }
                            }
                        }
                        Interface.CallHook("OnTownSet", player.transform.position);
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

            switch (command)
            {
                case "tunnel":
                    if (!iplayer.HasPermission(permTP_Tunnel)) { Message(iplayer, "notauthorized"); return; }
                    string dtarget = null;
                    if (args.Length > 0)
                    {
                        dtarget = string.Join(" ", args);
                    }
                    if (dtarget == null)
                    {
                        string res = "";
                        List<string> tunnels = QuerySingleStringToList("SELECT name FROM rtp_server WHERE name LIKE '%Tunnel%' ORDER BY name");
                        if (tunnels.Count > 0)
                        {
                            foreach (string tun in tunnels)
                            {
                                string nm = tun.Replace("Tunnel", "");
                                res += $" {nm}\n";
                            }
                        }
                        Message(iplayer, "tunnels", res);
                        return;
                    }
                    const string dtype = "Tunnel";
                    List<string> tunnel = QuerySingleStringToList($"SELECT location FROM rtp_server WHERE name='Tunnel {dtarget}'");

                    if (tunnel.Count > 0)
                    {
                        if (CanTeleport(player, tunnel[0], dtype))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                AddTimer(player, StringToVector3(tunnel[0]), dtype, Lang("town"));
                                HandleTimer(player.userID, dtype, true);
                                CreateCooldown(iplayer.Object as BasePlayer, "Town");

                                if (!DailyUsage["Town"].ContainsKey(player.userID)) DailyUsage["Town"].Add(player.userID, 0);
                                float usage = GetDailyLimit(player.userID, "Town") - DailyUsage["Town"][player.userID];
                                if (usage > 0)
                                {
                                    Message(iplayer, "remaining", usage.ToString(), "Town");
                                }

                                Message(iplayer, "teleporting", command, configData.Types[dtype].CountDown.ToString());
                            }
                            else if (TeleportTimers[player.userID].cooldown == 0)
                            {
                                Teleport(player, StringToVector3(tunnel[0]), command);
                            }
                        }
                        break;
                    }
                    Message(iplayer, "locationnotset", Lang(command));
                    break;
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
                    List<string> target = QuerySingleStringToList($"SELECT location FROM rtp_server WHERE name='{command}'");
                    string type = TI.ToTitleCase(command);
                    if (target.Count > 0)
                    {
                        if (CanTeleport(player, target[0], type))
                        {
                            if (!TeleportTimers.ContainsKey(player.userID))
                            {
                                AddTimer(player, StringToVector3(target[0]), type, Lang("town"));
                                HandleTimer(player.userID, type, true);
                                CreateCooldown(iplayer.Object as BasePlayer, "Town");

                                if (!DailyUsage["Town"].ContainsKey(player.userID)) DailyUsage["Town"].Add(player.userID, 0);
                                float usage = GetDailyLimit(player.userID, "Town") - DailyUsage["Town"][player.userID];
                                if (usage > 0)
                                {
                                    Message(iplayer, "remaining", usage.ToString(), "Town");
                                }

                                Message(iplayer, "teleporting", command, configData.Types[type].CountDown.ToString());
                            }
                            else if (TeleportTimers[player.userID].cooldown == 0)
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
            BasePlayer player = iplayer.Object as BasePlayer;
            if (SavedPoints.ContainsKey(player.userID))
            {
                Vector3 oldloc = SavedPoints[player.userID];

                if (CanTeleport(player, oldloc.ToString(), "TPB"))
                {
                    if (TeleportTimers.ContainsKey(player.userID)) TeleportTimers.Remove(player.userID);
                    AddTimer(player, oldloc, "TPB", Lang("tpb"));
                    HandleTimer(player.userID, "TPB", true);
                    CreateCooldown(iplayer.Object as BasePlayer, "TPB");

                    if (!DailyUsage["TPB"].ContainsKey(player.userID)) DailyUsage["TPB"].Add(player.userID, 0);
                    float usage = GetDailyLimit(player.userID, "TPB") - DailyUsage["TPB"][player.userID];
                    if (usage > 0)
                    {
                        Message(iplayer, "remaining", usage.ToString(), "TPB");
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
            BasePlayer player = iplayer.Object as BasePlayer;
            HandleTimer(player.userID, "tpc");
            Message(iplayer, "tpcancelled");
        }

        [Command("tpr")]
        private void CmdTpr(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;

            if (configData.Options.debug) { string debug = string.Join(",", args); Puts($"{debug}"); }

            if (!iplayer.HasPermission(permTP_TPR)) { Message(iplayer, "notauthorized"); return; }

            if (args.Length == 1)
            {
                BasePlayer target = FindPlayerByName(args[0]);
                if (target != null)
                {
                    ulong requesterId = ulong.Parse(iplayer.Id);
                    ulong targetId = target.userID;
                    if (requesterId == targetId)
                    {
                        if (configData.Options.debug)
                        {
                            DoLog("Allowing tpr to self in debug mode.");
                        }
                        else
                        {
                            Message(iplayer, "tprself");
                            return;
                        }
                    }
                    if (configData.Types["TPR"].AutoAccept)
                    {
                        if (IsFriend(requesterId, targetId))
                        {
                            DoLog("AutoTPA!");
                            //if (TeleportTimers.ContainsKey(requesterId)) TeleportTimers.Remove(requesterId);
                            AddTimer(iplayer.Object as BasePlayer, target.transform.position, "TPR", iplayer.Name);
                            //TeleportTimers.Add(sourceId, new TPTimer() { type = "TPR", start = Time.realtimeSinceStartup, countdown = configData.Types["TPR"].CountDown, source = (iplayer.Object as BasePlayer), targetName = iplayer.Name, targetLocation = target.transform.position });
                            HandleTimer(requesterId, "TPR", true);
                        }
                    }
                    else
                    {
                        TPRSetup(requesterId, targetId);
                    }
                }
            }
        }

        [Command("tpa")]
        private void CmdTpa(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
            DoLog($"Checking for tpr request for {iplayer.Id}");

            if (TPRRequests.ContainsValue(ulong.Parse(iplayer.Id)))
            {
                ulong requesterId = TPRRequests.FirstOrDefault(x => x.Value == ulong.Parse(iplayer.Id)).Key;
                DoLog($"Found a request from {requesterId}");
                BasePlayer requestpl = FindPlayerById(requesterId);

                if (requestpl != null)
                {
                    DoLog($"Setting timer for {requestpl.displayName} to tp to {iplayer.Name}");
                    if (TeleportTimers.ContainsKey(requesterId)) TeleportTimers.Remove(requesterId);
                    BasePlayer pl = iplayer.Object as BasePlayer;
                    TeleportTimers.Add(requesterId, new TPTimer() { type = "TPR", start = Time.realtimeSinceStartup, cooldown = configData.Types["TPR"].CountDown, source = requestpl, targetName = iplayer.Name, targetLocation = pl.transform.position });
                    HandleTimer(requesterId, "TPR", true);

                    if (!DailyUsage["TPR"].ContainsKey(requestpl.userID)) DailyUsage["TPR"].Add(requestpl.userID, 0);
                    float usage = GetDailyLimit(requestpl.userID, "TPR") - DailyUsage["TPR"][requestpl.userID];
                    if (usage > 0)
                    {
                        Message(iplayer, "remaining", usage.ToString(), "TPR");
                    }

                    Message(requestpl.IPlayer, "tpanotify", iplayer.Name, configData.Types["TPR"].CountDown.ToString());
                    TPRRequests.Remove(requestpl.userID);
                    return;
                }
                TPRRequests.Remove(requestpl.userID);
            }
        }
        #endregion

        #region InboundHooks
        private bool AddServerTp(string name, Vector3 location) => SetServerTp(name, location);

        private bool SetServerTp(string name, Vector3 location, bool force = false)
        {
            List<string> reserved = new() { "bandit", "outpost", "town" };
            if (reserved.Contains(name) && !force) return false;

            List<string> target = QuerySingleStringToList($"SELECT location FROM rtp_server WHERE name='{name}'");
            if (target.Count == 0) return false;

            RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('{name}', '{location}')");
            if (configData.Options.AddTownMapMarker)
            {
                SetTownMapMarker(location);
            }
            return true;
        }

        private bool RemoveServerTp(string name) => UnsetServerTp(name);

        private object GetPlayerTp(BasePlayer player)
        {
            Dictionary<string, Vector3> targets = new();
            string qh = $"SELECT name, location FROM rtp_player WHERE userid='{player.userID}'";
            using SQLiteConnection c = new(connStr);
            c.Open();
            using SQLiteCommand q = new(qh, c);
            using SQLiteDataReader home = q.ExecuteReader();
            while (home.Read())
            {
                string nom = !home.IsDBNull(0) ? home.GetString(0) : "";
                string loc = !home.IsDBNull(1) ? home.GetString(1) : "";
                targets.Add(nom, StringToVector3(loc));
            }
            return targets;
        }

        private bool UnsetServerTp(string name)
        {
            List<string> reserved = new() { "bandit", "outpost", "town" };
            if (reserved.Contains(name)) return false;

            List<string> target = QuerySingleStringToList($"SELECT location FROM rtp_server WHERE name='{name}')");
            if (target.Count == 0) return false;

            RunUpdateQuery($"DELETE FROM rtp_server WHERE name='{name}'");
            return true;
        }

        private object GetAllServerTp()
        {
            Dictionary<string, Vector3> targets = new();
            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                const string qh = "SELECT DISTINCT name, location FROM rtp_server";
                using SQLiteCommand q = new(qh, c);
                using SQLiteDataReader tgts = q.ExecuteReader();
                while (tgts.Read())
                {
                    string nom = !tgts.IsDBNull(0) ? tgts.GetString(0) : "";
                    string loc = !tgts.IsDBNull(1) ? tgts.GetString(1) : "";
                    targets.Add(nom, StringToVector3(loc));
                }
            }
            return targets;
        }

        private object GetServerTp(string name = "")
        {
            if (name.Length > 0)
            {
                List<string> target = QuerySingleStringToList($"SELECT DISTINCT location FROM rtp_server WHERE name='{name}'");
                if (target.Count == 0) return false;

                Vector3 pos = StringToVector3(target[0]);
                if (pos != default && pos != Vector3.zero) return pos;
            }

            Dictionary<string, Vector3> targets = new();

            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                const string qh = "SELECT DISTINCT name, location FROM rtp_server";
                using (SQLiteCommand q = new(qh, c))
                using (SQLiteDataReader tgts = q.ExecuteReader())
                {
                    while (tgts.Read())
                    {
                        string nom = !tgts.IsDBNull(0) ? tgts.GetString(0) : "";
                        string loc = !tgts.IsDBNull(1) ? tgts.GetString(1) : "";
                        targets.Add(name, StringToVector3(loc));
                    }
                }
            }

            if (targets == null || targets.Count == 0) return null;
            return targets;
        }

        private bool ResetServerTp()
        {
            List<string> target = QuerySingleStringToList("SELECT location FROM rtp_server WHERE name NOT IN ('town', 'outpost', 'bandit')");
            if (target.Count == 0) return false;

            RunUpdateQuery("DELETE FROM rtp_server WHERE name NOT IN ('town', 'outpost', 'bandit')");
            return true;
        }
        #endregion

        #region main
        private void TPRSetup(ulong requesterId, ulong targetId)
        {
            //if (TPRRequests.ContainsValue(targetId))
            //{
            //    foreach (KeyValuePair<ulong, ulong> item in TPRRequests.Where(kvp => kvp.Value == targetId).ToList())
            //    {
            //        TPRRequests.Remove(item.Key);
            //    }
            //}
            if (TPRRequests.ContainsKey(requesterId)) TPRRequests.Remove(requesterId);

            BasePlayer source = FindPlayerById(requesterId);
            if (CanTeleport(source, source.transform.position.ToString(), "TPR"))
            {
                TPRRequests.Add(requesterId, targetId);

                if (TPRTimers.ContainsKey(requesterId))
                {
                    TPRTimers[requesterId].timer.Destroy();
                    TPRTimers.Remove(requesterId);
                }
                TPRTimers.Add(requesterId, new TPRTimer() { type = "TPR", start = Time.realtimeSinceStartup, countdown = configData.Types["TPR"].CountDown });
                HandleTimer(requesterId, "TPR", true);
                NextTick(() => TPRNotification());
            }
        }

        private void TPRNotification(bool reject = false)
        {
            DoLog("TPRNotification");
            foreach (KeyValuePair<ulong, ulong> req in TPRRequests)
            {
                if (TPRTimers.ContainsKey(req.Key))
                {
                    DoLog($"Found a TPR timer for {req.Key} teleporting to {req.Value}");
                    BasePlayer src = FindPlayerById(req.Key);
                    BasePlayer tgt = FindPlayerById(req.Value);
                    if (reject && src != null && tgt != null)
                    {
                        Message(src?.IPlayer, "tprreject", tgt?.displayName);
                        TPRTimers[req.Key].timer.Destroy();
                        TPRTimers.Remove(req.Key);
                        return;
                    }
                    Message(tgt?.IPlayer, "tprnotify", src.displayName);
                    //TPRTimers[req.Key].timer.Destroy();
                }
            }
        }

        private bool CanTeleport(BasePlayer player, string location, string type, bool requester = true)
        {
            string reason;
            // OBSTRUCTION
            if (type == "TP" && Obstructed(StringToVector3(location)))
            {
                Message(player.IPlayer, "obstructed");
                return false;
            }

            // LIMIT
            DoLog($"Checking daily usage vs. limit for {player.displayName} for {type}");
            float limit;
            if (AtLimit(player.userID, type, out limit))
            {
                Message(player.IPlayer, "limit", type.ToLower(), DailyUsage[type][player.userID].ToString(), limit.ToString());
                return false;
            }

            // FOUNDATION
            if (configData.Options.HomeRequireFoundation &&
                string.Equals(type, "home", StringComparison.CurrentCultureIgnoreCase))// || string.Equals(type, "tpb", StringComparison.CurrentCultureIgnoreCase))
            {
                if (!HomeCheckFoundation(player, StringToVector3(location), "", out reason))
                {
                    if (string.Equals(type, "home", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (configData.Options.HomeRemoveInvalid)
                        {
                            Message(player.IPlayer, "homewasremoved", reason);
                        }
                        else
                        {
                            Message(player.IPlayer, "invalidhome", reason);
                        }
                    }
                    return false;
                }
            }

            // CRAFTING
            if (configData.Types[type].BlockOnCrafting && player?.inventory?.crafting?.queue?.Count > 0)
            {
                Message(player.IPlayer, "crafting");
                return false;
            }

            // COOLDOWN
            if (CheckCooldown(player, type, out reason))
            {
                DoLog($"Player {player.displayName} is in cooldown for {type}");
                Message(player.IPlayer, reason);
                return false;
            }

            // HOSTILE
            if (configData.Types[type].BlockOnHostile && player.IsHostile())
            {
                float unHostileTime = (float)player.State.unHostileTimestamp;
                float currentTime = (float)Network.TimeEx.currentTimestamp;
                string pt = ((int)Math.Abs(unHostileTime - currentTime) / 60).ToString();
                if ((unHostileTime - currentTime) < 60) pt = "<1";
                Message(player.IPlayer, "onhostile", type, pt);
                return false;
            }

            // PROXIMITY
            string monName = NearMonument(player);
            if (monName != null)
            {
                if (configData.Types[type].BlockOnRig && monName.Contains("Oilrig"))
                {
                    Message(player.IPlayer, "montooclose", type.ToLower(), monName);
                    return false;
                }
                else if (configData.Types[type].BlockOnExcavator && monName.Contains("Excavator"))
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
            if (configData.Types[type].BlockOnCave && cave != null)
            {
                Message(player.IPlayer, "cavetooclose", cave);
                return false;
            }
            if (configData.Types[type].BlockOnSafe && player.InSafeZone())
            {
                Message(player.IPlayer, "safezone", type.ToLower());
                return false;
            }

            CargoShip oncargo = player.GetComponentInParent<CargoShip>();
            if (configData.Types[type].BlockOnCargo && oncargo)
            {
                Message(player.IPlayer, "oncargo", type.ToLower());
                return false;
            }
            HotAirBalloon onballoon = player.GetComponentInParent<HotAirBalloon>();
            if (configData.Types[type].BlockOnBalloon && onballoon)
            {
                Message(player.IPlayer, "onballoon", type.ToLower());
                return false;
            }
            Lift onlift = player.GetComponentInParent<Lift>();
            if (configData.Types[type].BlockOnLift && onlift)
            {
                Message(player.IPlayer, "onlift", type.ToLower());
                return false;
            }

            if (configData.Types[type].BlockInTunnel && InTunnel(player))
            {
                Message(player.IPlayer, "intunnel", type.ToLower());
                return false;
            }
            if (configData.Types[type].BlockOnWater && AboveWater(player))
            {
                Message(player.IPlayer, "onwater", type.ToLower());
                return false;
            }
            //if (configData.Types[type].BlockOnIceberg && AboveIceberg(player))
            //{
            //    Message(player.IPlayer, "oniceberg", type.ToLower());
            //    return false;
            //}

            // CONDITION
            if (configData.Types[type].BlockOnSwimming && player.IsSwimming())
            {
                Message(player.IPlayer, "onswimming", type.ToLower());
                return false;
            }
            if (configData.Types[type].BlockOnHurt && player.IsWounded() && requester)
            {
                Message(player.IPlayer, "onhurt", type.ToLower());
                return false;
            }
            if (configData.Types[type].BlockOnCold && player.metabolism.temperature.value <= configData.Options.MinimumTemp)
            {
                Message(player.IPlayer, "oncold", type.ToLower());
                return false;
            }
            if (configData.Types[type].BlockOnHot && player.metabolism.temperature.value >= configData.Options.MaximumTemp)
            {
                Message(player.IPlayer, "onhot", type.ToLower());
                return false;
            }
            if (configData.Types[type].BlockOnMounted && player.isMounted)
            {
                Message(player.IPlayer, "onmounted", type.ToLower());
                return false;
            }

            // EXTERNAL CHECKS
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
            reason = "";
            bool rtrn = true;

            DoLog($"CanSetHome checking for {player?.UserIDString}");
            List<string> checkhome = QuerySingleStringToList($"SELECT location FROM rtp_player WHERE userid='{player.userID}'");
            if (checkhome.Count > 0)
            {
                DoLog(" checking VIP settings...");
                float homelimit = configData.Types["Home"].HomesLimit;
                string isvip = "";
                // Check all listed VIP permissions, if any, and set the user's limit to that if they have that permission
                // Use the maximum value obtained among all matching vip permissions.
                if (configData.Types["Home"].VIPSettings != null)
                {
                    foreach (KeyValuePair<string, VIPSetting> vip in configData.Types["Home"].VIPSettings)
                    {
                        if (permission.UserHasPermission(player.UserIDString, vip.Key) && vip.Value.VIPHomesLimit > homelimit)
                        {
                            isvip = $" (from {vip.Key} permission)";
                            homelimit = vip.Value.VIPHomesLimit;
                        }
                    }
                }
                DoLog($"Homelimit for {player.displayName}, set to {homelimit}{isvip}.");

                if (homelimit > 0 && checkhome.Count >= homelimit)
                {
                    reason = Lang("hometoomany", null, homelimit.ToString());
                    return false;
                }

                foreach (string home in checkhome)
                {
                    if (Vector3.Distance(player.transform.position, StringToVector3(home)) < configData.Options.HomeMinimumDistance)
                    {
                        reason = Lang("hometooclose", null, configData.Options.HomeMinimumDistance.ToString());
                        return false;
                    }
                }
            }

            if (configData.Options.HomeRequireFoundation && !HomeCheckFoundation(player, position, "", out reason))
            {
                return false;
            }

            string monName = NearMonument(player);
            if (monName != null)
            {
                reason = Lang("montooclose", null, "sethome", monName);
                return false;
            }

            return rtrn;
        }

        private bool HomeCheckFoundation(BasePlayer player, Vector3 position, string home, out string reason)
        {
            reason = null;
            bool rtrn = false;
            DoLog($"Checking for foundation/floor at target {position}");
            RaycastHit hitinfo;
            if (Physics.Raycast(position + new Vector3(0, 0.5f, 0), Vector3.down, out hitinfo, 0.8f, blockLayer))
            {
                BaseEntity entity = hitinfo.GetEntity();
                //DoLog($"Found {entity.ShortPrefabName}");
                if (entity.ShortPrefabName.Equals("foundation") || entity.ShortPrefabName.Equals("floor")
                    || entity.ShortPrefabName.Equals("foundation.triangle") || entity.ShortPrefabName.Equals("floor.triangle")
                    || position.y < entity.WorldSpaceBounds().ToBounds().max.y)
                {
                    DoLog("  Found one.  Checking block perms, etc...");
                    rtrn = true;
                    if (!BlockCheck(entity, player, position, out reason, configData.Options.HonorBuildingPrivilege))
                    {
                        rtrn = false;
                    }
                }
            }
            else
            {
                if (configData.Options.HomeRemoveInvalid)
                {
                    if (string.IsNullOrEmpty(home)) // CanTeleport()
                    {
                        DoLog($"Removing home for {player.displayName} where location is {position}");
                        RunUpdateQuery($"DELETE FROM rtp_player WHERE userid='{player.userID}' AND location='{position}'");
                    }
                    else // sethome
                    {
                        DoLog($"Removing home for {player.displayName} where name is {home}");
                        RunUpdateQuery($"DELETE FROM rtp_player WHERE userid='{player.userID}' AND name='{home}'");
                    }
                }
                reason = Lang("missingfoundation");
                rtrn = false;
            }
            return rtrn;
        }

        private bool BlockCheck(BaseEntity entity, BasePlayer player, Vector3 position, out string reason, bool checktc = false)
        {
            reason = null;
            DoLog($"BlockCheck() called for {entity.ShortPrefabName}");
            if (configData.Options.StrictFoundationCheck)
            {
                Vector3 center = entity.CenterPoint();

                List<BaseEntity> ents = new();
                Vis.Entities(center, 1.5f, ents);
                foreach (BaseEntity wall in ents)
                {
                    if (wall.name.Contains("external.high"))
                    {
                        DoLog($"    Found: {wall.name} @ center {center}, pos {position}");
                        reason = Lang("highwall");
                        return false;
                    }
                }
                DoLog($"  Checking block: {entity.name} @ center {center}, pos: {position}");
                if (entity.PrefabName.Contains("triangle.prefab"))
                {
                    if (Math.Abs(center.x - position.x) < 0.46f && Math.Abs(center.z - position.z) < 0.46f)
                    {
                        DoLog($"    Found: {entity.ShortPrefabName} @ center: {center}, pos: {position}");
                        if (checktc && !CheckCupboardBlock(entity as BuildingBlock, player))
                        {
                            reason = Lang("notowned");
                            return false;
                        }

                        return true;
                    }
                }
                else if (entity.ShortPrefabName.Equals("foundation") || entity.ShortPrefabName.Equals("floor"))
                {
                    if (Math.Abs(center.x - position.x) < 0.7f && Math.Abs(center.z - position.z) < 0.7f)
                    {
                        DoLog($"    Found: {entity.ShortPrefabName} @ center: {center}, pos: {position}");
                        if (checktc && !CheckCupboardBlock(entity as BuildingBlock, player))
                        {
                            reason = Lang("notowned");
                            return false;
                        }

                        return true;
                    }
                }
            }
            else if (checktc)
            {
                if (!CheckCupboardBlock(entity as BuildingBlock, player))
                {
                    DoLog("No strict foundation check, but HonorBuildingPrivilege true - no perms");
                    reason = Lang("notowned");
                    return false;
                }
                return true;
            }

            return true;
        }

        // Check that a building block is owned by/attached to a cupboard and that the user has privileges
        private bool CheckCupboardBlock(BuildingBlock block, BasePlayer player)
        {
            DoLog($"CheckCupboardBlock() called for {block.ShortPrefabName}");
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
                DoLog("Building priv not null, checking authorizedPlayers...");
                foreach (BuildingPrivlidge priv in building.buildingPrivileges)
                {
                    foreach (ulong auth in priv.authorizedPlayers.Select(x => x.userid).ToArray())
                    {
                        // If the player is authed, or is a friend of the authed player, return true if HonorRelationships is enabled.
                        // This should avoid TP to a home location where building priv has been lost (PVP).
                        if (auth == player.userID || (configData.Options.HonorRelationships && IsFriend(player.userID, auth)))
                        {
                            DoLog($"Player {player.userID} has privileges...");
                            return true;
                        }
                    }
                }
                // No matching priv
                DoLog("NO BUILDING PRIV");
                return false;
            }
            DoLog("NO BUILDING AT ALL");
            return true;
        }

        // Check a location to verify that it is not obstructed by construction.
        public bool Obstructed(Vector3 location)
        {
            List<BaseEntity> ents = new();
            Vis.Entities(location, 1, ents, blockLayer);
            foreach (BaseEntity ent in ents)
            {
                return true;
            }
            return false;
        }

        public bool InTunnel(BasePlayer player)
        {
            if (player.transform.position.y < -60f)
            {
                return true;
            }
            List<BaseEntity> ents = new();
            Vis.Entities(player.transform.position, 50, ents);
            foreach (BaseEntity entity in ents)
            {
                if (entity.ShortPrefabName.IndexOf("tunnel", StringComparison.OrdinalIgnoreCase) > 0) return true;
            }

            return false;
        }

        public bool AboveIceberg(BasePlayer player)
        {
            Vector3 pos = player.transform.position;
            DoLog($"Player position: {pos}.  Checking for iceberg...");

            RaycastHit hitinfo;
            if (Physics.Raycast(pos + new Vector3(0, 0.5f, 0), Vector3.down, out hitinfo, 0.8f, groundLayer))
            {
                if (hitinfo.collider.name.Contains("iceberg"))
                {
                    DoLog("Player is above iceberg");
                    return true;
                }
            }
            return false;
        }

        public bool AboveWater(BasePlayer player)
        {
            Vector3 pos = player.transform.position;
            DoLog($"Player position: {pos}.  Checking for water...");

            if ((TerrainMeta.HeightMap.GetHeight(pos) - TerrainMeta.WaterMap.GetHeight(pos)) >= 0)
            {
                DoLog("Player not above water.");
                return false;
            }
            else
            {
                DoLog("Player is above water!");
                return true;
            }
        }

        private string NearMonument(BasePlayer player)
        {
            Vector3 pos = player.transform.position;

            foreach (KeyValuePair<string, Vector3> entry in monPos)
            {
                string monname = entry.Key;
                Vector3 monvector = entry.Value;
                float realDistance = monSize[monname].z;
                monvector.y = pos.y;
                float dist = Vector3.Distance(pos, monvector);

                DoLog($"Checking {monname} dist: {dist}, realDistance: {realDistance}");
                if (dist < realDistance)
                {
                    DoLog($"Player in range of {monname}");
                    return monname;
                }
            }
            return null;
        }

        private string NearCave(BasePlayer player)
        {
            Vector3 pos = player.transform.position;

            foreach (KeyValuePair<string, Vector3> entry in cavePos)
            {
                string cavename = entry.Key;
                float realDistance = 0f;

                if (cavename.Contains("Small"))
                {
                    realDistance = configData.Options.CaveDistanceSmall;
                }
                else if (cavename.Contains("Large"))
                {
                    realDistance = configData.Options.CaveDistanceLarge;
                }
                else if (cavename.Contains("Medium"))
                {
                    realDistance = configData.Options.CaveDistanceMedium;
                }

                Vector3 cavevector = entry.Value;
                cavevector.y = pos.y;
                string cpos = cavevector.ToString();
                float dist = Vector3.Distance(pos, cavevector);

                if (dist < realDistance)
                {
                    DoLog($"NearCave: {cavename} nearby.");
                    return cavename;
                }
                else
                {
                    DoLog("NearCave: Not near this cave.");
                }
            }
            return null;
        }
        #endregion

        #region helpers
        private void SetTownMapMarker(Vector3 position)
        {
            DoLog($"Setting town map marker at {position}");
            foreach (MapMarkerGenericRadius mm in UnityEngine.Object.FindObjectsOfType<MapMarkerGenericRadius>().Where(x => x.name == "town").ToList())
            {
                mm?.Kill();
            }
            MapMarkerGenericRadius marker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", position) as MapMarkerGenericRadius;
            if (marker != null)
            {
                marker.alpha = 0.6f;
                marker.color1 = Color.green;
                marker.color2 = Color.white;
                marker.name = "town";
                marker.appType = ProtoBuf.AppMarkerType.Player;
                marker.radius = 0.2f;
                marker.Spawn();
                marker.SendUpdate();
                marker.SendNetworkUpdate();
            }
        }

        public static string RemoveSpecialCharacters(string str)
        {
            return Regex.Replace(str, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
        }

        private static BasePlayer FindPlayerByName(string name, bool includeSleepers = false)
        {
            if (includeSleepers)
            {
                foreach (BasePlayer current in BasePlayer.allPlayerList)
                {
                    if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    {
                        return current;
                    }
                }
                return null;
            }
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                {
                    return current;
                }
            }
            return null;
        }

        private static BasePlayer FindPlayerById(ulong userid, bool includeSleepers = false)
        {
            if (includeSleepers)
            {
                foreach (BasePlayer current in BasePlayer.allPlayerList)
                {
                    if (current.userID == userid)
                    {
                        return current;
                    }
                }
                return null;
            }
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.userID == userid)
                {
                    return current;
                }
            }
            return null;
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
            if (configData.Options.debug)
            {
                if (configData.Options.logtofile)
                {
                    LogToFile(logfilename, "".PadLeft(indent, ' ') + message, this);
                }
                else
                {
                    Puts(message);
                }
            }
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

            // return as a Vector3
            return new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2])
            );
        }

        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                string[] g = (string[])GridAPI.CallHook("GetGrid", position);
                return string.Concat(g);
            }
            else
            {
                // From GrTeleport for display only
                Vector2 r = new((World.Size / 2) + position.x, (World.Size / 2) + position.z);
                float x = Mathf.Floor(r.x / 146.3f) % 26;
                float z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        public void FindMonuments()
        {
            bool ishapis = ConVar.Server.level.Contains("Hapis");

            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            //foreach (MonumentInfo monument in BaseNetworkable.serverEntities.OfType<MonumentInfo>())
            {
                Puts($"Found monument: {monument?.name}");
                if (monument.name.Contains("power_sub")) continue;
                if (monument.name.Contains("ice_lake")) continue;
                float realWidth = 0f;
                string name = string.Empty;

                if (monument.name == "OilrigAI")
                {
                    name = "Small Oilrig";
                    realWidth = 100f;
                }
                else if (monument.name == "OilrigAI2")
                {
                    name = "Large Oilrig";
                    realWidth = 200f;
                }
                else if (monument.name == "assets/bundled/prefabs/autospawn/monument/medium/radtown_small_3.prefab")
                {
                    name = "Sewer Branch";
                    realWidth = 100;
                }
                else
                {
                    if (ishapis)
                    {
                        foreach (Match e in Regex.Matches(monument.name, @"\w{4,}|\d{1,}"))
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
                //Puts($"Checking monument {name}");
                if (monPos.ContainsKey(name)) continue;
                if (cavePos.ContainsKey(name)) name += RandomString();

                Vector3 extents = monument.Bounds.extents;
                if (realWidth > 0f)
                {
                    extents.z = realWidth;
                }

                if (monument.name.Contains("cave"))
                {
                    //DoLog("  Adding to cave list");
                    cavePos.Add(name, monument.transform.position);
                }
                else if (monument.name.Contains("compound") && configData.Options.AutoGenOutpost)
                {
                    DoLog($"  Adding Outpost target pos: {monument.transform.position}, size: {extents}");
                    Vector3 mt = Vector3.zero;
                    Vector3 bbq = Vector3.zero;
                    List<BaseEntity> ents = new();
                    Vis.Entities(monument.transform.position, 50, ents);
                    foreach (BaseEntity entity in ents)
                    {
                        if (entity.PrefabName.Contains("marketterminal") && mt == Vector3.zero)
                        {
                            mt = entity.transform.position;
                        }
                        else if (entity.PrefabName.Contains("bbq"))
                        {
                            bbq = entity.transform.position;
                        }
                    }
                    if (mt != Vector3.zero && bbq != Vector3.zero)
                    {
                        Vector3 outpost = Vector3.Lerp(mt, bbq, 0.3f) + new Vector3(1f, 0.1f, 1f);
                        RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('outpost', '{outpost}')");
                    }
                }
                else if (monument.name.Contains("bandit") && configData.Options.AutoGenBandit)
                {
                    DoLog($"  Adding BanditTown target pos: {monument.transform.position}, size: {extents}");
                    List<BaseEntity> ents = new();
                    Vis.Entities(monument.transform.position, 50, ents);
                    foreach (BaseEntity entity in ents)
                    {
                        if (entity.PrefabName.Contains("workbench"))
                        {
                            Vector3 bandit = Vector3.Lerp(monument.transform.position, entity.transform.position, 0.45f) + new Vector3(0, 1.5f, 0);
                            RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('bandit', '{bandit}')");
                        }
                    }
                }
                else
                {
                    if (extents.z < 1)
                    {
                        extents.z = configData.Options.DefaultMonumentSize;
                    }
                    monPos.Add(name, monument.transform.position);
                    monSize.Add(name, extents);
                    DoLog($"Adding Monument: {name}, pos: {monument.transform.position}, size: {extents}");
                    if (name.Contains("Entrance Bunker") && configData.Options.AutoGenTunnels)
                    {
                        Vector3 pos = monument.transform.position;
                        pos.y = TerrainMeta.HeightMap.GetHeight(monument.transform.position);
                        string tname = name;
                        tname = tname.Replace("Entrance Bunker ", "Tunnel ");
                        //DoLog($"Adding {tname}, pos: {pos.ToString()}");
                        RunUpdateQuery($"INSERT OR REPLACE INTO rtp_server VALUES('{tname}', '{pos}')");
                    }
                }
            }

            monPos.OrderBy(x => x.Key);
            monSize.OrderBy(x => x.Key);
            cavePos.OrderBy(x => x.Key);
        }

        public string GetClosest(Vector3 startPosition)
        {
            Vector3 bestTarget = new();
            float closestDistanceSqr = Mathf.Infinity;

            foreach (Vector3 potentialTarget in monPos.Values)
            {
                Vector3 direction = potentialTarget - startPosition;

                float dSqrToTarget = direction.sqrMagnitude;

                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget;
                }
            }

            return (from rv in monPos where rv.Value.Equals(bestTarget) select rv.Key).FirstOrDefault();
        }

        private bool RunUpdateQuery(string query)
        {
            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                using (SQLiteCommand cmd = new(query, c))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            return true;
        }

        private List<string> QuerySingleStringToList(string query)
        {
            DoLog($"QuerySingleStringToList:\n  {query}");
            List<string> output = new();
            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                using (SQLiteCommand q = new(query, c))
                using (SQLiteDataReader rtbl = q.ExecuteReader())
                {
                    while (rtbl.Read())
                    {
                        string test = !rtbl.IsDBNull(0) ? rtbl.GetString(0) : "";
                        if (test != "")
                        {
                            output.Add(test);
                        }
                    }
                }
            }
            return output;
        }

        private string RandomString()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            List<char> charList = chars.ToList();

            string random = "";

            for (int i = 0; i <= UnityEngine.Random.Range(5, 10); i++)
            {
                random += charList[UnityEngine.Random.Range(0, charList.Count - 1)];
            }

            return random;
        }

        // playerid = requesting player, ownerid = target or owner of a home
        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (playerid == ownerid) return true;
            if (configData.Options.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if (configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if (configData.Options.useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if (player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    if (playerTeam == null) return false;
                    if (playerTeam.members.Contains(ownerid))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void AddTimer(BasePlayer player, Vector3 targetLoc, string type, string typeName)
        {
            //AddTimer(iplayer.Object as BasePlayer, target.transform.position, "TPR", iplayer.Name);
            DoLog($"Creating a {type} Countdown TPTimer object for {player.UserIDString}.");
            TeleportTimers.Add(
                player.userID,
                new TPTimer()
                {
                    type = type,
                    source = player,
                    targetName = Lang(typeName),
                    targetLocation = targetLoc
                }
            );
        }

        public void HandleTimer(ulong userid, string type, bool start = false)
        {
            if (TeleportTimers.ContainsKey(userid))
            {
                if (start)
                {
                    float countdown = configData.Types[type].CountDown;
                    string isvip = "";
                    if (configData.Types[type].VIPSettings != null)
                    {
                        foreach (KeyValuePair<string, VIPSetting> vip in configData.Types[type].VIPSettings)
                        {
                            if (permission.UserHasPermission(userid.ToString(), vip.Key) && vip.Value.VIPCountDown < countdown)
                            {
                                isvip = $" (from {vip.Key} permission)";
                                countdown = vip.Value.VIPCoolDown + vip.Value.VIPCountDown;
                            }
                        }
                    }

                    DoLog($"Creating a {type} countdown timer for {userid}.  Timer will be set to {countdown} second(s){isvip}.");
                    TeleportTimers[userid].start = Time.realtimeSinceStartup;
                    TeleportTimers[userid].cooldown = countdown;
                    TeleportTimers[userid].timer = timer.Once(TeleportTimers[userid].cooldown, () => Teleport(TeleportTimers[userid].source, TeleportTimers[userid].targetLocation, type));
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
            else if (TPRTimers.ContainsKey(userid))
            {
                if (start)
                {
                    TPRTimers[userid].timer = timer.Once(TPRTimers[userid].countdown, () => TPRNotification(true));
                }
                else
                {
                    TPRTimers[userid].timer.Destroy();
                    TPRTimers.Remove(userid);
                }
            }
        }

        // Send player/userid and type.  Receive cooldown and bypass values.
        // Create TPTimer which will delete itself at the end of the cooldown period.
        public void CreateCooldown(BasePlayer player, string type)
        {
            CreateCooldown(player.userID, type);
        }
        public void CreateCooldown(ulong userid, string type)
        {
            double bypass = configData.Types[type].AllowBypass ? configData.Types[type].BypassAmount : 0;
            float cooldown = configData.Types[type].CoolDown + configData.Types[type].CountDown;

            DeleteCooldown(userid, type);

            string isvip = "";
            if (configData.Types[type].VIPSettings != null)
            {
                foreach (KeyValuePair<string, VIPSetting> vip in configData.Types[type].VIPSettings)
                {
                    if (permission.UserHasPermission(userid.ToString(), vip.Key) && (vip.Value.VIPCoolDown + vip.Value.VIPCountDown) < cooldown)
                    {
                        isvip = $" (from {vip.Key} permission)";
                        cooldown = vip.Value.VIPCoolDown + vip.Value.VIPCountDown;
                        bypass = vip.Value.VIPAllowBypass ? vip.Value.VIPBypassAmount : 0;
                    }
                }
            }

            DoLog($"Creating a {type} cooldown timer for {userid}.  Timer will be set to {cooldown} second(s){isvip} including countdown.");
            BasePlayer source = BasePlayer.Find(userid.ToString());
            CooldownTimers[type].Add(
                userid, new TPTimer()
                {
                    type = type,
                    source = source,
                    start = Time.realtimeSinceStartup,
                    targetName = Lang(type),
                    cooldown = cooldown,
                    timer = timer.Once(cooldown, () => { DoLog($"{type} cooldown timer expired for {source.displayName}"); DeleteCooldown(userid, type); })
                }
            );
        }

        private bool DeleteCooldown(BasePlayer player, string type)
        {
            return DeleteCooldown(player.userID, type);
        }
        private bool DeleteCooldown(ulong userid, string type)
        {
            TPTimer current;
            if (CooldownTimers[type].TryGetValue(userid, out current))
            {
                DoLog($"Destroying {type} cooldown timer for {userid}");
                CooldownTimers[type][userid].timer.Destroy();
                CooldownTimers[type].Remove(userid);
                return true;
            }
            DoLog($"No {type} cooldown timer for {userid} to delete.");
            return false;
        }

        // true - in cooldown, false - not in cooldown
        private bool CheckCooldown(BasePlayer player, string type, out string reason)
        {
            ulong userid = player.userID;
            float cooldown = configData.Types[type].CoolDown + configData.Types[type].CountDown;
            double bypass = configData.Types[type].AllowBypass ? configData.Types[type].BypassAmount : 0;
            reason = "";

            float remaining;

            TPTimer current;
            if (CooldownTimers[type].TryGetValue(player.userID, out current))
            {
                current.counter++;
                string isvip = "";
                if (configData.Types[type].VIPSettings != null)
                {
                    foreach (KeyValuePair<string, VIPSetting> vip in configData.Types[type].VIPSettings)
                    {
                        if (permission.UserHasPermission(userid.ToString(), vip.Key) && (vip.Value.VIPCoolDown + vip.Value.VIPCountDown) < cooldown)
                        {
                            isvip = $" (from {vip.Key} permission)";
                            cooldown = vip.Value.VIPCoolDown + vip.Value.VIPCountDown;
                            bypass = vip.Value.VIPAllowBypass ? vip.Value.VIPBypassAmount : 0;
                        }
                    }
                }

                remaining = (float)Math.Floor((CooldownTimers[type][userid].start + CooldownTimers[type][userid].cooldown) - Time.realtimeSinceStartup);
                DoLog($"{type} cooldown timer for {userid} exists and it set to {cooldown} second(s){isvip} including countdown with {remaining}s remaining.");
                switch (current.counter > 1)
                {
                    case true:
                        // Check was sent a second time, so we want to:
                        // 1. Deduct from the user balance
                        // 2. Clear the timer
                        if (bypass > 0)
                        {
                            HandleMoney(userid, bypass, true);
                            DeleteCooldown(player, type);
                            reason = Lang("CooldownBypassedNotice", null, type, bypass);
                            DoLog($"{type} cooldown timer bypassed for {userid}.");
                            return false;
                        }
                        reason = Lang("InCooldownNoticeNoPmt", null, type, remaining);
                        return false;
                    default:
                        bool hasmoney = HandleMoney(userid, bypass);
                        if (bypass > 0 && hasmoney)
                        {
                            reason = Lang("InCooldownNoticePmt", null, type, current.counter.ToString(), bypass);
                            DoLog($"{type} cooldown timer cannot be bypassed for {userid} by config.");
                            return true;
                        }
                        else if (!hasmoney)
                        {
                            reason = Lang("InCooldownNoticeNoMoney", null, type, remaining);
                            DoLog($"{type} cooldown timer cannot be bypassed for {userid} due to insufficient funds.");
                            return true;
                        }
                        reason = Lang("InCooldownNoticeNoPmt", null, type, remaining);
                        break;
                }
                return true;
            }
            DoLog($"No {type} cooldown timer for {userid} to delete.");
            return false;
        }


        // Check limit for any userid and type based on current activity
        public bool AtLimit(ulong userid, string type, out float limit)
        {
            float current;
            if (!DailyUsage[type].TryGetValue(userid, out current))
            {
                DailyUsage[type].Add(userid, 0);
            }

            limit = GetDailyLimit(userid, type);
            return current >= limit && limit != 0;
        }

        private float GetDailyLimit(ulong userid, string type)
        {
            float limit = configData.Types[type].DailyLimit;
            // Check for player VIP permissions
            if (configData.Types[type].VIPSettings != null)
            {
                foreach (KeyValuePair<string, VIPSetting> perm in configData.Types[type].VIPSettings)
                {
                    if (permission.UserHasPermission(userid.ToString(), perm.Key))
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
            }

            return limit;
        }

        private bool HandleMoney(ulong userID, double bypass, bool withdraw = false, bool deposit = false)
        {
            double balance;
            bool hasmoney = false;

            string userid = userID.ToString();
            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if (configData.Options.useEconomics && Economics)
            {
                balance = (double)Economics?.CallHook("Balance", userid);
                if (balance >= bypass)
                {
                    hasmoney = true;
                    if (withdraw)
                    {
                        return (bool)Economics?.CallHook("Withdraw", userid, bypass);
                    }
                    else if (deposit)
                    {
                        return (bool)Economics?.CallHook("Deposit", userid, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if (configData.Options.useServerRewards && ServerRewards)
            {
                object bal = ServerRewards?.Call("CheckPoints", userid);
                balance = Convert.ToDouble(bal);
                if (balance >= bypass)
                {
                    hasmoney = true;
                    if (withdraw)
                    {
                        return (bool)ServerRewards?.Call("TakePoints", userid, (int)bypass);
                    }
                    else if (deposit)
                    {
                        return (bool)ServerRewards?.Call("AddPoints", userid, (int)bypass);
                    }
                }
            }

            // No money via Economics nor ServerRewards, or plugins not in use.  Try BankSystem.
            if (configData.Options.useBankSystem && BankSystem)
            {
                object bal = BankSystem?.Call("Balance", ulong.Parse(userid));
                balance = Convert.ToDouble(bal);
                if (balance >= bypass)
                {
                    hasmoney = true;
                    if (withdraw)
                    {
                        return (bool)BankSystem?.Call("Withdraw", userid, (int)bypass);
                    }
                    else if (deposit)
                    {
                        bool w = (bool)BankSystem?.Call("Deposit", userid, (int)bypass);
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

        private void MidnightDetect(bool startup = false)
        {
            DateTime dt = TOD_Sky.Instance.Cycle.DateTime;
            if (startup)
            {
                dateInt = Convert.ToInt32(dt.Hour.ToString().PadLeft(2, '0') + dt.Minute.ToString().PadLeft(2, '0') + dt.Second.ToString().PadLeft(2, '0'));
                DoLog($"Startup: Set start time to {dateInt.ToString().PadLeft(6, '0')} for daily limits");
                timer.Once(60f, () => MidnightDetect());
                return;
            }

            // Has game midnight passed since the last run?
            int now = Convert.ToInt32(dt.Hour.ToString().PadLeft(2, '0') + dt.Minute.ToString().PadLeft(2, '0') + dt.Second.ToString().PadLeft(2, '0'));
            if (now > dateInt)
            {
                //DoLog($"MidnightDetect: Still same day.  NOW {now.ToString().PadLeft(6, '0')} > Startup {dateInt.ToString().PadLeft(6, '0')}.");
                timer.Once(60f, () => MidnightDetect());
                return;
            }
            DoLog($"MidnightDetect: Day changed!  NOW {now.ToString().PadLeft(6, '0')} < Startup {dateInt.ToString().PadLeft(6, '0')}.  Clearing the daily limits.");
            dateInt = now;
            DailyUsage = new Dictionary<string, Dictionary<ulong, float>>
            {
                { "Home", new Dictionary<ulong, float>() },
                { "Town", new Dictionary<ulong, float>() },
                { "TPA", new Dictionary<ulong, float>() },
                { "TPB", new Dictionary<ulong, float>() },
                { "TPR", new Dictionary<ulong, float>() },
                { "TP", new Dictionary<ulong, float>() },
                { "Bandit", new Dictionary<ulong, float>() },
                { "Outpost", new Dictionary<ulong, float>() },
                { "Tunnel", new Dictionary<ulong, float>() }
            };
            timer.Once(60f, () => MidnightDetect());
        }

        //        public void TeleportToPlayer(BasePlayer player, BasePlayer target) => Teleport(player, target.transform.position);
        //        public void TeleportToPosition(BasePlayer player, float x, float y, float z) => Teleport(player, new Vector3(x, y, z));

        public void Teleport(BasePlayer player, Vector3 position, string type = "")
        {
            SaveLocation(player);
            HandleTimer(player.userID, type);

            DailyUsage[type][player.userID]++;

            if (player.net?.connection != null) player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);

            player.SetParent(null, true, true);
            player.EnsureDismounted();
            player.Teleport(position);
            player.UpdateNetworkGroup();
            player.StartSleeping();
            player.SendNetworkUpdateImmediate(false);

            if (player.net?.connection != null) player.ClientRPC(RpcTarget.Player("StartLoading", player));
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
        private object ConfigAllRead(string pluginName)
        {
            if (pluginName != Name.ToLower()) return "";
            return configData;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 1, 18))
            {
                using (SQLiteConnection c = new(connStr))
                {
                    c.Open();
                    using (SQLiteCommand ct = new("CREATE TABLE new_player (userid VARCHAR(255), name VARCHAR(255) NOT NULL, location VARCHAR(255), lastused VARCHAR(255), total INTEGER(32))", c))
                    {
                        ct.ExecuteNonQuery();
                    }
                    using (SQLiteCommand ct = new("INSERT INTO new_player SELECT * FROM rtp_player", c))
                    {
                        ct.ExecuteNonQuery();
                    }
                    using (SQLiteCommand ct = new("DROP TABLE IF EXISTS rtp_player", c))
                    {
                        ct.ExecuteNonQuery();
                    }
                    using (SQLiteCommand ct = new("ALTER TABLE new_player RENAME TO rtp_player", c))
                    {
                        ct.ExecuteNonQuery();
                    }
                }
            }

            if (configData.Version < new VersionNumber(1, 2, 8))
            {
                configData.Types["Home"].HomesLimit = 0;
            }

            if (configData.Version < new VersionNumber(1, 4, 5))
            {
                foreach (KeyValuePair<string, CmdOptions> typ in configData.Types)
                {
                    typ.Value.BlockOnCrafting = false;
                }
            }

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new()
            {
                Options = new Options()
                {
                    SetCommand = "set",
                    ListCommand = "list",
                    RemoveCommand = "remove",
                    HomeMinimumDistance = 10f,
                    DefaultMonumentSize = 120f,
                    CaveDistanceSmall = 40f,
                    CaveDistanceMedium = 60f,
                    CaveDistanceLarge = 100f,
                    AutoGenBandit = true,
                    AutoGenOutpost = true,
                    AutoGenTunnels = false,
                    MinimumTemp = 0f,
                    MaximumTemp = 40f,
                    TownZoneEnterMessage = "Welcome to Town!",
                    TownZoneLeaveMessage = "Thanks for stopping by!",
                    TownZoneFlags = new List<string>()
                    {
                        "nodecay",
                        "nohelitargeting"
                    }
                },
                Types = new Dictionary<string, CmdOptions>()
                {
                    ["Home"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        HomesLimit = 10f,
                        BypassAmount = 0f
                    },
                    ["Town"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BypassAmount = 0f
                    },
                    ["Bandit"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BlockOnHostile = true,
                        BypassAmount = 0f
                    },
                    ["Outpost"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BlockOnHostile = true,
                        BypassAmount = 0f
                    },
                    ["TPB"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BypassAmount = 0f
                    },
                    ["TPC"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BypassAmount = 0f
                    },
                    ["TPR"] = new CmdOptions()
                    {
                        CountDown = 5f,
                        CoolDown = 120f,
                        DailyLimit = 30f,
                        BypassAmount = 0f
                    },
                    ["TP"] = new CmdOptions()
                    {
                        CountDown = 2f,
                        CoolDown = 10f,
                        DailyLimit = 30f,
                        BypassAmount = 0f
                    }
                },
                Version = Version
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            public Options Options;
            public Dictionary<string, CmdOptions> Types = new();
            public VersionNumber Version;

            public ConfigData()
            {
                Types.Add("Home", new CmdOptions());
                Types.Add("Town", new CmdOptions());
                Types.Add("Bandit", new CmdOptions());
                Types.Add("Outpost", new CmdOptions());
                Types.Add("Tunnel", new CmdOptions());
                Types.Add("TPB", new CmdOptions());
                Types.Add("TPC", new CmdOptions());
                Types.Add("TPR", new CmdOptions());
                Types.Add("TP", new CmdOptions());
            }
        }

        public class Options
        {
            public bool debug;
            public bool logtofile;
            public bool useClans;
            public bool useFriends;
            public bool useTeams;
            public bool useEconomics;
            public bool useServerRewards;
            public bool useBankSystem;
            public bool useNoEscape;
            public bool useVanish;
            public bool HomeRequireFoundation;
            public bool StrictFoundationCheck;
            public bool HomeRemoveInvalid;
            public bool HonorBuildingPrivilege;
            public bool HonorRelationships;
            public bool WipeOnNewSave;
            public bool AutoGenBandit;
            public bool AutoGenOutpost;
            public bool AutoGenTunnels;
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
            public string TownZoneId;
            public string TownZoneEnterMessage;
            public string TownZoneLeaveMessage;
            public List<string> TownZoneFlags;
            //public string TownCopyPasteString;
            //public ulong TownCopyPasteOwnerID;
        }

        public class CmdOptions : VIPOptions
        {
            public bool IfOneHomeJustGoThere;
            public bool BlockOnCrafting;
            public bool BlockOnHurt;
            public bool BlockOnCold;
            public bool BlockOnHot;
            public bool BlockOnCave;
            public bool BlockOnRig;
            public bool BlockOnMonuments;
            public bool BlockOnHostile;
            public bool BlockOnSafe;
            public bool BlockOnBalloon;
            public bool BlockOnCargo;
            public bool BlockOnExcavator;
            public bool BlockOnLift;
            public bool BlockOnMounted;
            public bool BlockOnSwimming;
            public bool BlockOnWater;
            //public bool BlockOnIceberg;
            public bool BlockForNoEscape;
            public bool BlockIfInvisible;
            public bool BlockInTunnel = true;
            public bool AutoAccept;
            public float DailyLimit;
            public float HomesLimit;
            public float CountDown;
            public float CoolDown;
            public bool AllowBypass;
            public double BypassAmount;
        }

        public class VIPOptions
        {
            public Dictionary<string, VIPSetting> VIPSettings { get; set; }
        }

        public class VIPSetting
        {
            public float VIPDailyLimit;
            public float VIPHomesLimit;
            public float VIPCountDown;
            public float VIPCoolDown;
            public bool VIPAllowBypass;
            public double VIPBypassAmount;
        }
        #endregion

        #region UI
        private void HomeGUI(BasePlayer player, string orderby = "alpha")
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, HGUI);

            CuiElementContainer container = UI.Container(HGUI, UI.Color("222222", 0.9f), "0.2 0.2", "0.8 0.8", true, "Overlay");
            string append;
            string label;
            switch (orderby)
            {
                case "last":
                    UI.Button(ref container, HGUI, UI.Color("#4055d8", 1f), Lang("alpha"), 12, "0.82 0.93", "0.91 0.99", "homeg alpha");
                    append = " ORDER BY name";
                    label = Lang("homesavail") + " " + Lang("sortedby", null, Lang("lastuse"));
                    break;
                default:
                    UI.Button(ref container, HGUI, UI.Color("#4055d8", 1f), Lang("last"), 12, "0.82 0.93", "0.91 0.99", "homeg last");
                    append = " ORDER BY lastused";
                    label = Lang("homesavail") + " " + Lang("sortedby", null, Lang("name"));
                    break;
            }

            UI.Label(ref container, HGUI, UI.Color("#ffffff", 1f), label, 14, "0.1 0.93", "0.8 0.99");
            UI.Button(ref container, HGUI, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.93", "0.99 0.99", "homeg closeit");

            int row = 0;
            int col = 0;
            float[] posb = new float[4];

            using (SQLiteConnection c = new(connStr))
            {
                c.Open();
                string qh = $"SELECT name, location, lastused FROM rtp_player WHERE userid={player.userID}{append}";
                using (SQLiteCommand q = new(qh, c))
                using (SQLiteDataReader home = q.ExecuteReader())
                {
                    while (home.Read())
                    {
                        if (row > 10)
                        {
                            row = 0;
                            col++;
                        }

                        string hname = !home.IsDBNull(0) ? home.GetString(0) : "";
                        Vector3 position = StringToVector3(!home.IsDBNull(1) ? home.GetString(1) : "");
                        string pos = PositionToGrid(position);

                        posb = GetButtonPositionZ(row, col);
                        UI.Button(ref container, HGUI, UI.Color("#d85540", 1f), $"{hname} ({pos})", 10, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"home {hname}");//, UI.Color("#ffffff", 1));
                        row++;
                    }
                }
            }

            CuiHelper.AddUi(player, container);
        }

        private int RowNumber(int max, int count) => Mathf.FloorToInt(count / max);

        private float[] GetButtonPosition(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.096f * columnNumber);
            float offsetY = (0.80f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + 0.196f, offsetY + 0.03f };
        }
        //private float[] GetButtonPositionP(int rowNumber, int columnNumber)
        //{
        //    float offsetX = 0.05f + (0.186f * columnNumber);
        //    float offsetY = (0.85f - (rowNumber * 0.074f));

        //    return new float[] { offsetX, offsetY, offsetX + 0.256f, offsetY + 0.03f };
        //}
        private float[] GetButtonPositionP(int rowNumber, int columnNumber, float colspan = 1f)
        {
            float offsetX = 0.05f + (0.126f * columnNumber);
            float offsetY = (0.87f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + (0.226f * colspan), offsetY + 0.03f };
        }

        private float[] GetButtonPositionS(int rowNumber, int columnNumber, float colspan = 1f)
        {
            float offsetX = 0.05f + (0.116f * columnNumber);
            float offsetY = (0.87f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + (0.206f * colspan), offsetY + 0.03f };
        }

        private float[] GetButtonPositionZ(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.156f * columnNumber);
            float offsetY = (0.77f - (rowNumber * 0.052f));

            return new float[] { offsetX, offsetY, offsetX + 0.296f, offsetY + 0.03f };
        }

        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = {AnchorMin = min, AnchorMax = max},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
            }

            public static void Panel(ref CuiElementContainer container, string panel, string color, string min, string max, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    CursorEnabled = cursor
                },
                panel);
            }

            public static void Label(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                },
                panel);
            }

            public static void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }

            public static void Input(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = align,
                            CharsLimit = 30,
                            Color = color,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text,
                            NeedsKeyboard = true
                        },
                        new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max },
                        new CuiNeedsCursorComponent()
                    }
                });
            }

            public static void Icon(ref CuiElementContainer container, string panel, string color, string imageurl, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent
                        {
                            Url = imageurl,
                            Sprite = "assets/content/textures/generic/fulltransparent.tga",
                            Color = color
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = min,
                            AnchorMax = max
                        }
                    }
                });
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.Substring(1);
                }
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion

        #region IMPORT
        /// <summary>
        ///  Classes for import of data from N/RTeleportation
        /// </summary>
        private class OtherConfigData
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

        private class SettingsData
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
            public bool InterruptAboveWater { get; set; }
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

        private class GameVersionData
        {
            public int Network { get; set; }
            public int Save { get; set; }
            public string Level { get; set; }
            public string LevelURL { get; set; }
            public int WorldSize { get; set; }
            public int Seed { get; set; }
        }

        private class AdminSettingsData
        {
            public bool AnnounceTeleportToTarget { get; set; }
            public bool UseableByAdmins { get; set; }
            public bool UseableByModerators { get; set; }
            public int LocationRadius { get; set; }
            public int TeleportNearDefaultDistance { get; set; }
        }

        private class HomesSettingsData
        {
            public int HomesLimit { get; set; }
            public Dictionary<string, int> VIPHomesLimits { get; set; }
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyUsage { get; set; }
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

        private class TPRData
        {
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyUsage { get; set; }
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

        private class TownData
        {
            public int Cooldown { get; set; }
            public int Countdown { get; set; }
            public int DailyLimit { get; set; }
            public Dictionary<string, int> VIPDailyUsage { get; set; }
            public Dictionary<string, int> VIPCooldowns { get; set; }
            public Dictionary<string, int> VIPCountdowns { get; set; }
            public string Location { get; set; }
            public bool UsableOutOfBuildingBlocked { get; set; }
            public bool AllowCraft { get; set; }
            public int Pay { get; set; }
            public int Bypass { get; set; }
        }

        private class HomeData
        {
            [JsonProperty("l")]
            public Dictionary<string, Vector3> Locations { get; set; } = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("t")]
            public TeleportData Teleports { get; set; } = new TeleportData();
        }

        private class TeleportData
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
                Vector3 vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                JObject o = JObject.Load(reader);
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
                return objectType.GetInterfaces().Any(i => HasGenericTypeDefinition(i, typeof(IDictionary<,>)) && typeof(T).IsAssignableFrom(i.GetGenericArguments().FirstOrDefault()));
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
                SQLiteCommand cd = new("DROP TABLE IF EXISTS rtp_server", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("CREATE TABLE rtp_server (name VARCHAR(255) NOT NULL UNIQUE, location VARCHAR(255))", sqlConnection);
                cd.ExecuteNonQuery();

                cd = new SQLiteCommand("DROP TABLE IF EXISTS rtp_player", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("CREATE TABLE rtp_player (userid VARCHAR(255), name VARCHAR(255) NOT NULL, location VARCHAR(255), lastused VARCHAR(255), total INTEGER(32))", sqlConnection);
                cd.ExecuteNonQuery();
            }
            else
            {
                SQLiteCommand cd = new("DELETE FROM rtp_server", sqlConnection);
                cd.ExecuteNonQuery();
                cd = new SQLiteCommand("DELETE FROM rtp_player", sqlConnection);
                cd.ExecuteNonQuery();
            }
        }
        #endregion
    }
}

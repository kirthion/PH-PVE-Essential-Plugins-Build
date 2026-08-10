using Rocket.API;
using Rocket.API.Collections;
using Rocket.API.Extensions;
using Rocket.Core;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using ShimmysAdminTools.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ShimmysAdminTools.Models
{
    public partial class PluginConfig
    {
        public int PH_BackCooldownSeconds = 3600;
        public bool PH_BackSaveOnDeath = true;
        public bool PH_BackSaveBeforeHome = true;
        public bool PH_BackSaveBeforeWarp = true;
        public bool PH_BackSaveBeforeTpa = true;
        public int PH_TpaRequestTimeoutSeconds = 60;
        public bool PH_AdminControlEnabled = true;
        public bool PH_EagleEyeAnnouncementEnabled = true;
        public string PH_EagleEyeAnnouncementPermission = "phpve.eagleeye.announce";
        public string PH_EagleEyeAnnouncementColor = "yellow";
        public string PH_MessageColor = "cyan";
        public bool PH_CuffEnabled = true;
    }
}

namespace ShimmysAdminTools
{
    internal sealed class PHBackState
    {
        public bool HasLocation;
        public Vector3 Position;
        public byte Angle;
        public DateTime CooldownUntilUtc;
    }

    internal sealed class PHTpaRequest
    {
        public ulong RequesterId;
        public DateTime ExpiresUtc;
    }

    public static class PHCore
    {
        private static bool initialized;
        private static AdminToolsPlugin plugin;
        private static readonly Dictionary<ulong, PHBackState> BackStates = new Dictionary<ulong, PHBackState>();
        private static readonly Dictionary<ulong, PHTpaRequest> TpaRequests = new Dictionary<ulong, PHTpaRequest>();
        private static readonly Dictionary<ulong, ulong> ReplyTargets = new Dictionary<ulong, ulong>();
        private static string backStatePath;

        public static PluginConfig Config => AdminToolsPlugin.Config;

        public static void Initialize(AdminToolsPlugin instance)
        {
            if (initialized) return;
            initialized = true;
            plugin = instance;
            backStatePath = Path.Combine(plugin.Directory, "PHPVE.backstate.txt");
            LoadBackStates();

            U.Events.OnPlayerConnected += OnPlayerConnected;
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            ChatManager.onCheckPermissions += OnChatCheckPermissions;
            Player.OnAnyPlayerAdminUsageChanged += OnAnyPlayerAdminUsageChanged;
            BarricadeManager.onTransformRequested += OnBarricadeTransformRequested;
            StructureManager.onTransformRequested += OnStructureTransformRequested;

            foreach (SteamPlayer sp in Provider.clients)
                ApplyAdminPermissions(UnturnedPlayer.FromSteamPlayer(sp));
        }

        public static void Shutdown()
        {
            if (!initialized) return;
            initialized = false;
            U.Events.OnPlayerConnected -= OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            ChatManager.onCheckPermissions -= OnChatCheckPermissions;
            Player.OnAnyPlayerAdminUsageChanged -= OnAnyPlayerAdminUsageChanged;
            BarricadeManager.onTransformRequested -= OnBarricadeTransformRequested;
            StructureManager.onTransformRequested -= OnStructureTransformRequested;
            SaveBackStates();
            TpaRequests.Clear();
            ReplyTargets.Clear();
        }

        public static string T(string key, params object[] args)
        {
            try { return plugin.Translate(key, args); }
            catch { return key; }
        }

        public static Color MessageColor()
        {
            try { return UnturnedChat.GetColorFromName(Config.PH_MessageColor, Color.cyan); }
            catch { return Color.cyan; }
        }

        public static void Say(UnturnedPlayer player, string message, Color? color = null)
        {
            if (player == null) return;
            UnturnedChat.Say(player, message, color ?? MessageColor());
        }

        public static void Broadcast(string message, Color color)
        {
            // Deliberately deliver to each connected client instead of relying on a legacy global helper.
            foreach (SteamPlayer target in Provider.clients.ToArray())
                ChatManager.serverSendMessage(message, color, null, target, EChatMode.SAY, null, true);
        }

        public static bool HasAny(IRocketPlayer caller, params string[] permissions)
        {
            if (caller == null) return false;
            if (caller.HasPermission("*")) return true;
            foreach (string permission in permissions)
                if (!string.IsNullOrWhiteSpace(permission) && caller.HasPermission(permission)) return true;
            return false;
        }

        public static bool HasAny(UnturnedPlayer caller, params string[] permissions)
        {
            return HasAny((IRocketPlayer)caller, permissions);
        }

        public static bool Require(UnturnedPlayer caller, params string[] permissions)
        {
            if (HasAny(caller, permissions)) return true;
            Say(caller, T("PH_NoPermission"), Color.red);
            return false;
        }

        private static void OnPlayerConnected(UnturnedPlayer player)
        {
            ApplyAdminPermissions(player);
        }

        private static void OnPlayerDisconnected(UnturnedPlayer player)
        {
            if (player != null && Config.PH_AdminControlEnabled)
            {
                player.Player.look.sendFreecamAllowed(false);
                player.Player.look.sendWorkzoneAllowed(false);
                player.Player.look.sendSpecStatsAllowed(false);
            }
            if (player != null)
            {
                TpaRequests.Remove(player.CSteamID.m_SteamID);
                ReplyTargets.Remove(player.CSteamID.m_SteamID);
            }
        }

        private static void ApplyAdminPermissions(UnturnedPlayer player)
        {
            if (player == null || !Config.PH_AdminControlEnabled) return;
            player.Player.look.sendFreecamAllowed(HasAny(player, "admin.freecam", "phpve.admin.freecam"));
            player.Player.look.sendWorkzoneAllowed(HasAny(player, "admin.editor", "phpve.admin.editor"));
            player.Player.look.sendSpecStatsAllowed(HasAny(player, "admin.spectate", "phpve.admin.spectate"));
        }

        private static void OnAnyPlayerAdminUsageChanged(Player rawPlayer, EPlayerAdminUsageFlags oldFlags, EPlayerAdminUsageFlags newFlags)
        {
            if (!Config.PH_AdminControlEnabled) return;
            UnturnedPlayer player = UnturnedPlayer.FromPlayer(rawPlayer);
            if (player == null) return;

            int oldValue = (int)oldFlags;
            int newValue = (int)newFlags;
            bool spectatorActivated = (oldValue & 4) == 0 && (newValue & 4) != 0;
            if (!spectatorActivated || !Config.PH_EagleEyeAnnouncementEnabled) return;
            if (!HasAny(player, Config.PH_EagleEyeAnnouncementPermission)) return;

            Color color;
            try { color = UnturnedChat.GetColorFromName(Config.PH_EagleEyeAnnouncementColor, Color.yellow); }
            catch { color = Color.yellow; }
            Broadcast(T("PH_EagleEye", player.DisplayName), color);
        }

        private static void OnBarricadeTransformRequested(CSteamID instigator, byte x, byte y, ushort plant, uint instanceID,
            ref Vector3 point, ref byte angle_x, ref byte angle_y, ref byte angle_z, ref bool shouldAllow)
        {
            if (!Config.PH_AdminControlEnabled || HasEditorOtherObjects(instigator)) return;
            if (!BarricadeManager.tryGetRegion(x, y, plant, out BarricadeRegion region)) return;
            foreach (BarricadeDrop drop in region.drops)
            {
                if (drop.instanceID != instanceID) continue;
                if (drop.GetServersideData().owner == instigator.m_SteamID) return;
                shouldAllow = false;
                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(instigator);
                Say(player, T("PH_EditorOwnBarricades"), Color.red);
                return;
            }
        }

        private static void OnStructureTransformRequested(CSteamID instigator, byte x, byte y, uint instanceID,
            ref Vector3 point, ref byte angle_x, ref byte angle_y, ref byte angle_z, ref bool shouldAllow)
        {
            if (!Config.PH_AdminControlEnabled || HasEditorOtherObjects(instigator)) return;
            foreach (StructureRegion region in StructureManager.regions)
            {
                foreach (StructureDrop drop in region.drops)
                {
                    if (drop.instanceID != instanceID) continue;
                    if (drop.GetServersideData().owner == instigator.m_SteamID) return;
                    shouldAllow = false;
                    UnturnedPlayer player = UnturnedPlayer.FromCSteamID(instigator);
                    Say(player, T("PH_EditorOwnStructures"), Color.red);
                    return;
                }
            }
        }

        private static bool HasEditorOtherObjects(CSteamID playerId)
        {
            UnturnedPlayer player = UnturnedPlayer.FromCSteamID(playerId);
            return player != null && HasAny(player, "admin.editor.otherobjects", "phpve.admin.editor.otherobjects");
        }

        private static void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            if (Config.PH_BackSaveOnDeath) SaveBackLocation(player);
        }

        private static void OnChatCheckPermissions(SteamPlayer steamPlayer, string text, ref bool shouldExecuteCommand, ref bool shouldList)
        {
            if (!shouldExecuteCommand || steamPlayer == null || string.IsNullOrWhiteSpace(text)) return;
            string value = text.Trim();
            if (value.StartsWith("/")) value = value.Substring(1);
            string name = value.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (name == null) return;
            name = name.ToLowerInvariant();
            UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steamPlayer);
            if (player == null) return;
            if (name == "home" && Config.PH_BackSaveBeforeHome) SaveBackLocation(player);
            if (name == "warp" && Config.PH_BackSaveBeforeWarp) SaveBackLocation(player);
        }

        public static void SaveBackLocation(UnturnedPlayer player)
        {
            if (player == null) return;
            ulong id = player.CSteamID.m_SteamID;
            PHBackState state;
            if (!BackStates.TryGetValue(id, out state))
            {
                state = new PHBackState();
                BackStates[id] = state;
            }
            state.HasLocation = true;
            state.Position = player.Position;
            state.Angle = MeasurementTool.angleToByte(player.Rotation);
            SaveBackStates();
        }

        public static void ExecuteBack(UnturnedPlayer player)
        {
            if (!Require(player, "phpve.command.back", "essentials.command.back")) return;
            ulong id = player.CSteamID.m_SteamID;
            PHBackState state;
            if (!BackStates.TryGetValue(id, out state) || !state.HasLocation)
            {
                Say(player, T("PH_Back_NoLocation"), Color.red);
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (state.CooldownUntilUtc > now)
            {
                TimeSpan left = state.CooldownUntilUtc - now;
                Say(player, T("PH_Back_Cooldown", FormatDuration(left)), Color.red);
                return;
            }

            Vector3 oldPosition = player.Position;
            byte oldAngle = MeasurementTool.angleToByte(player.Rotation);
            Vector3 destination = state.Position;
            byte destinationAngle = state.Angle;
            player.Teleport(destination, destinationAngle);

            // Swap the stored location so /back behaves as a true previous-location system,
            // while the one-hour cooldown prevents repeated bouncing.
            state.Position = oldPosition;
            state.Angle = oldAngle;
            state.HasLocation = true;
            state.CooldownUntilUtc = now.AddSeconds(Math.Max(0, Config.PH_BackCooldownSeconds));
            SaveBackStates();
            Say(player, T("PH_Back_Returned"));
        }

        private static string FormatDuration(TimeSpan time)
        {
            if (time.TotalHours >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)time.TotalHours, time.Minutes);
            if (time.TotalMinutes >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", (int)time.TotalMinutes, time.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}s", Math.Max(1, (int)Math.Ceiling(time.TotalSeconds)));
        }

        public static void SendTpa(UnturnedPlayer requester, string targetText)
        {
            if (!Require(requester, "phpve.tpa.send")) return;
            UnturnedPlayer target = UnturnedPlayer.FromName(targetText);
            if (target == null || target == requester)
            {
                Say(requester, T("PH_Tpa_PlayerNotFound"), Color.red);
                return;
            }
            TpaRequests[target.CSteamID.m_SteamID] = new PHTpaRequest
            {
                RequesterId = requester.CSteamID.m_SteamID,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(10, Config.PH_TpaRequestTimeoutSeconds))
            };
            Say(requester, T("PH_Tpa_Sent", target.DisplayName));
            Say(target, T("PH_Tpa_Received", requester.DisplayName));
        }

        public static void RespondTpa(UnturnedPlayer target, bool accept)
        {
            if (!Require(target, "phpve.tpa.respond")) return;
            PHTpaRequest request;
            ulong targetId = target.CSteamID.m_SteamID;
            if (!TpaRequests.TryGetValue(targetId, out request))
            {
                Say(target, T("PH_Tpa_NoPending"), Color.red);
                return;
            }
            TpaRequests.Remove(targetId);
            if (request.ExpiresUtc < DateTime.UtcNow)
            {
                Say(target, T("PH_Tpa_Expired"), Color.red);
                return;
            }
            UnturnedPlayer requester = UnturnedPlayer.FromCSteamID(new CSteamID(request.RequesterId));
            if (requester == null)
            {
                Say(target, T("PH_Tpa_PlayerNotFound"), Color.red);
                return;
            }
            if (!accept)
            {
                Say(target, T("PH_Tpa_Denied_Target", requester.DisplayName));
                Say(requester, T("PH_Tpa_Denied_Requester", target.DisplayName), Color.red);
                return;
            }
            if (Config.PH_BackSaveBeforeTpa) SaveBackLocation(requester);
            requester.Teleport(target);
            Say(target, T("PH_Tpa_Accepted_Target", requester.DisplayName));
            Say(requester, T("PH_Tpa_Accepted_Requester", target.DisplayName));
        }

        public static void Tell(UnturnedPlayer sender, string targetText, string message)
        {
            if (!Require(sender, "phpve.command.tell", "essentials.command.tell")) return;
            UnturnedPlayer target = UnturnedPlayer.FromName(targetText);
            if (target == null || target == sender)
            {
                Say(sender, T("PH_Tell_PlayerNotFound"), Color.red);
                return;
            }
            ReplyTargets[sender.CSteamID.m_SteamID] = target.CSteamID.m_SteamID;
            ReplyTargets[target.CSteamID.m_SteamID] = sender.CSteamID.m_SteamID;
            Say(sender, T("PH_Tell_To", target.DisplayName, message), Color.magenta);
            Say(target, T("PH_Tell_From", sender.DisplayName, message), Color.magenta);
        }

        public static void Reply(UnturnedPlayer sender, string message)
        {
            if (!Require(sender, "phpve.command.reply", "essentials.command.reply")) return;
            ulong otherId;
            if (!ReplyTargets.TryGetValue(sender.CSteamID.m_SteamID, out otherId))
            {
                Say(sender, T("PH_Reply_NoTarget"), Color.red);
                return;
            }
            UnturnedPlayer target = UnturnedPlayer.FromCSteamID(new CSteamID(otherId));
            if (target == null)
            {
                Say(sender, T("PH_Reply_NoTarget"), Color.red);
                return;
            }
            Tell(sender, target.DisplayName, message);
        }

        public static void Freeze(UnturnedPlayer caller, string targetText, bool freeze)
        {
            string legacy = freeze ? "essentials.command.freeze" : "essentials.command.unfreeze";
            string canonical = freeze ? "phpve.command.freeze" : "phpve.command.unfreeze";
            if (!Require(caller, canonical, legacy)) return;
            UnturnedPlayer target = UnturnedPlayer.FromName(targetText);
            if (target == null)
            {
                Say(caller, T("PH_PlayerNotFound"), Color.red);
                return;
            }
            PHFrozen existing = target.Player.gameObject.GetComponent<PHFrozen>();
            if (freeze)
            {
                if (existing == null)
                {
                    existing = target.Player.gameObject.AddComponent<PHFrozen>();
                    existing.Initialize(target);
                }
                Say(caller, T("PH_Freeze_Frozen", target.DisplayName));
                Say(target, T("PH_Freeze_YouFrozen"), Color.red);
            }
            else
            {
                if (existing != null) UnityEngine.Object.Destroy(existing);
                Say(caller, T("PH_Freeze_Unfrozen", target.DisplayName));
                Say(target, T("PH_Freeze_YouUnfrozen"));
            }
        }

        public static void Repair(UnturnedPlayer player)
        {
            if (!Require(player, "phpve.command.repair", "essentials.command.repair")) return;
            for (byte page = 0; page < PlayerInventory.PAGES; page++)
            {
                Items items = player.Player.inventory.items[page];
                if (items == null) continue;
                foreach (ItemJar jar in items.items.ToArray())
                {
                    if (jar.item.quality != 100)
                        player.Player.inventory.sendUpdateQuality(page, jar.x, jar.y, 100);
                }
            }
            Say(player, T("PH_Repair_Done"));
        }

        public static void Sudo(UnturnedPlayer caller, string targetText, string commandText)
        {
            if (!Require(caller, "phpve.command.sudo", "essentials.command.sudo")) return;
            UnturnedPlayer target = UnturnedPlayer.FromName(targetText);
            if (target == null)
            {
                Say(caller, T("PH_PlayerNotFound"), Color.red);
                return;
            }
            bool result = R.Commands.Execute(target, commandText.TrimStart('/'));
            Say(caller, result ? T("PH_Sudo_Done", target.DisplayName) : T("PH_Sudo_Failed", target.DisplayName), result ? MessageColor() : Color.red);
        }

        public static void Cuff(UnturnedPlayer caller, string targetText, bool cuff)
        {
            if (!Config.PH_CuffEnabled) return;
            string perm = cuff ? "phpve.command.cuff" : "phpve.command.uncuff";
            if (!Require(caller, perm)) return;
            UnturnedPlayer target = UnturnedPlayer.FromName(targetText);
            if (target == null)
            {
                Say(caller, T("PH_PlayerNotFound"), Color.red);
                return;
            }
            if (cuff)
            {
                target.Player.equipment.dequip();
                target.Player.animator.sendGesture((EPlayerGesture)11, true);
                Say(caller, T("PH_Cuff_Done", target.DisplayName));
                Say(target, T("PH_Cuff_Target"));
            }
            else
            {
                target.Player.animator.sendGesture((EPlayerGesture)12, true);
                Say(caller, T("PH_Uncuff_Done", target.DisplayName));
                Say(target, T("PH_Uncuff_Target"));
            }
        }

        private static void LoadBackStates()
        {
            BackStates.Clear();
            if (string.IsNullOrEmpty(backStatePath) || !File.Exists(backStatePath)) return;
            foreach (string raw in File.ReadAllLines(backStatePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split('|');
                if (p.Length != 7) continue;
                ulong id;
                long ticks;
                float x, y, z;
                byte angle;
                int has;
                if (!ulong.TryParse(p[0], out id) || !long.TryParse(p[1], out ticks) ||
                    !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                    !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                    !byte.TryParse(p[5], out angle) || !int.TryParse(p[6], out has)) continue;
                BackStates[id] = new PHBackState
                {
                    CooldownUntilUtc = new DateTime(Math.Max(DateTime.MinValue.Ticks, Math.Min(DateTime.MaxValue.Ticks, ticks)), DateTimeKind.Utc),
                    Position = new Vector3(x, y, z),
                    Angle = angle,
                    HasLocation = has != 0
                };
            }
        }

        private static void SaveBackStates()
        {
            try
            {
                if (string.IsNullOrEmpty(backStatePath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(backStatePath));
                string[] lines = BackStates.Select(kvp => string.Join("|",
                    kvp.Key.ToString(CultureInfo.InvariantCulture),
                    kvp.Value.CooldownUntilUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                    kvp.Value.Position.x.ToString("R", CultureInfo.InvariantCulture),
                    kvp.Value.Position.y.ToString("R", CultureInfo.InvariantCulture),
                    kvp.Value.Position.z.ToString("R", CultureInfo.InvariantCulture),
                    kvp.Value.Angle.ToString(CultureInfo.InvariantCulture),
                    kvp.Value.HasLocation ? "1" : "0")).ToArray();
                File.WriteAllLines(backStatePath, lines);
            }
            catch { }
        }
    }

    public sealed class PHFrozen : MonoBehaviour
    {
        private UnturnedPlayer target;
        private Vector3 frozenPosition;
        private byte frozenAngle;

        public void Initialize(UnturnedPlayer player)
        {
            target = player;
            frozenPosition = player.Position;
            frozenAngle = MeasurementTool.angleToByte(player.Rotation);
        }

        private void FixedUpdate()
        {
            if (target == null || target.Player == null) return;
            if ((target.Position - frozenPosition).sqrMagnitude > 0.01f)
                target.Teleport(frozenPosition, frozenAngle);
        }
    }
}

namespace PHPVE.Commands
{
    internal abstract class PHCommandBase : IRocketCommand
    {
        public abstract string Name { get; }
        public abstract string Help { get; }
        public abstract string Syntax { get; }
        public virtual AllowedCaller AllowedCaller => AllowedCaller.Player;
        public virtual List<string> Aliases => new List<string>();
        // Permissions are checked per subcommand so /tpa can separate send from respond.
        public virtual List<string> Permissions => new List<string>();
        public abstract void Execute(IRocketPlayer caller, string[] command);
        protected UnturnedPlayer Player(IRocketPlayer caller) => caller as UnturnedPlayer;
        protected void Usage(UnturnedPlayer player) => ShimmysAdminTools.PHCore.Say(player, "/" + Name + (string.IsNullOrWhiteSpace(Syntax) ? "" : " " + Syntax), Color.red);
    }

    public sealed class CommandBack : PHCommandBase
    {
        public override string Name => "back";
        public override string Help => "Returns to your previous saved location.";
        public override string Syntax => "";
        public override void Execute(IRocketPlayer caller, string[] command)
        {
            var p = Player(caller); if (p == null) return; ShimmysAdminTools.PHCore.ExecuteBack(p);
        }
    }

    public sealed class CommandTpa : PHCommandBase
    {
        public override string Name => "tpa";
        public override string Help => "Teleport request system.";
        public override string Syntax => "<player | accept | deny>";
        public override void Execute(IRocketPlayer caller, string[] command)
        {
            var p = Player(caller); if (p == null) return;
            if (command.Length != 1) { Usage(p); return; }
            string arg = command[0];
            if (arg.Equals("accept", StringComparison.OrdinalIgnoreCase)) ShimmysAdminTools.PHCore.RespondTpa(p, true);
            else if (arg.Equals("deny", StringComparison.OrdinalIgnoreCase)) ShimmysAdminTools.PHCore.RespondTpa(p, false);
            else ShimmysAdminTools.PHCore.SendTpa(p, arg);
        }
    }

    public sealed class CommandTpAccept : PHCommandBase
    {
        public override string Name => "tpaccept";
        public override string Help => "Accept a pending teleport request.";
        public override string Syntax => "";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p != null) ShimmysAdminTools.PHCore.RespondTpa(p, true); }
    }

    public sealed class CommandTpDeny : PHCommandBase
    {
        public override string Name => "tpdeny";
        public override string Help => "Deny a pending teleport request.";
        public override string Syntax => "";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p != null) ShimmysAdminTools.PHCore.RespondTpa(p, false); }
    }

    public sealed class CommandTell : PHCommandBase
    {
        public override string Name => "tell";
        public override string Help => "Send a private message.";
        public override string Syntax => "<player> <message>";
        public override List<string> Aliases => new List<string> { "msg" };
        public override void Execute(IRocketPlayer caller, string[] command)
        {
            var p = Player(caller); if (p == null) return;
            if (command.Length < 2) { Usage(p); return; }
            ShimmysAdminTools.PHCore.Tell(p, command[0], string.Join(" ", command.Skip(1)));
        }
    }

    public sealed class CommandReply : PHCommandBase
    {
        public override string Name => "reply";
        public override string Help => "Reply to your most recent private-message partner.";
        public override string Syntax => "<message>";
        public override List<string> Aliases => new List<string> { "r" };
        public override void Execute(IRocketPlayer caller, string[] command)
        {
            var p = Player(caller); if (p == null) return;
            if (command.Length < 1) { Usage(p); return; }
            ShimmysAdminTools.PHCore.Reply(p, string.Join(" ", command));
        }
    }

    public sealed class CommandFreeze : PHCommandBase
    {
        public override string Name => "freeze";
        public override string Help => "Freeze a player in place.";
        public override string Syntax => "<player>";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p == null) return; if (command.Length != 1) { Usage(p); return; } ShimmysAdminTools.PHCore.Freeze(p, command[0], true); }
    }

    public sealed class CommandUnfreeze : PHCommandBase
    {
        public override string Name => "unfreeze";
        public override string Help => "Unfreeze a player.";
        public override string Syntax => "<player>";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p == null) return; if (command.Length != 1) { Usage(p); return; } ShimmysAdminTools.PHCore.Freeze(p, command[0], false); }
    }

    public sealed class CommandRepair : PHCommandBase
    {
        public override string Name => "repair";
        public override string Help => "Repair all items in your inventory.";
        public override string Syntax => "";
        public override List<string> Aliases => new List<string> { "fix" };
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p != null) ShimmysAdminTools.PHCore.Repair(p); }
    }

    public sealed class CommandSudo : PHCommandBase
    {
        public override string Name => "sudo";
        public override string Help => "Execute a Rocket command as another player using that player's permissions.";
        public override string Syntax => "<player> <command>";
        public override void Execute(IRocketPlayer caller, string[] command)
        {
            var p = Player(caller); if (p == null) return;
            if (command.Length < 2) { Usage(p); return; }
            ShimmysAdminTools.PHCore.Sudo(p, command[0], string.Join(" ", command.Skip(1)));
        }
    }

    public sealed class CommandCuff : PHCommandBase
    {
        public override string Name => "cuff";
        public override string Help => "Cuff a player.";
        public override string Syntax => "<player>";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p == null) return; if (command.Length != 1) { Usage(p); return; } ShimmysAdminTools.PHCore.Cuff(p, command[0], true); }
    }

    public sealed class CommandUncuff : PHCommandBase
    {
        public override string Name => "uncuff";
        public override string Help => "Uncuff a player.";
        public override string Syntax => "<player>";
        public override void Execute(IRocketPlayer caller, string[] command) { var p = Player(caller); if (p == null) return; if (command.Length != 1) { Usage(p); return; } ShimmysAdminTools.PHCore.Cuff(p, command[0], false); }
    }
}

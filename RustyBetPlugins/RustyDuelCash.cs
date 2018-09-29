using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using __red.RustyDuelCash.Exceptions;
using Oxide.Core;
using Oxide.Core.Plugins;
using Newtonsoft.Json.Linq;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using System.Globalization;
using ProtoBuf;

namespace __red.RustyDuelCash.Exceptions
{
    public class ArenaSpawnPointsException : Exception
    {
        public ArenaSpawnPointsException(string message) : base(message) { }
    }

    public class BalanceException : Exception
    {
        public BalanceException(string message) : base(message) { }
    }

    public class DuelCoreException : Exception
    {
        public DuelCoreException(string message) : base(message) { }
    }
}

namespace Oxide.Plugins
{
    [Info("RustyDuelCash", "__red", "0.0.1")]
    class RustyDuelCash : RustPlugin
    {
        #region Fields
        private HashSet<DuelPlayer>                m_OnlinePlayers = new HashSet<DuelPlayer>();
        private List<Arena>                        m_ActiveArenas  = new List<Arena>();
        private List<DuelRequest>                  m_Bets          = new List<DuelRequest>();

        public RustyDuelCashConfig m_Config;
        #endregion

        #region Custom Classes
        public enum BalanceAction
        {
            Subtract,
            Add,
            Multiple,
            Split
        }
        public class DeserterInfo
        {
            public bool           IsDeserter { get; set; }
            public string         Reason     { get; set; }
            public string         From       { get; set; }
            public TimerExtension Expire     { get; set; }

            public void Instaintiate(string reason, string from, PluginTimers timer, int secs, Action callback, Action initmsg)
            {
                IsDeserter = true;
                Reason = reason;
                From = from;
                Expire = new TimerExtension();
                Expire.Instantiate(timer, secs, callback, initmsg);
            }
            public void Destroy()
            {
                Reason = string.Empty;
                From = string.Empty;
                Expire.Destroy();
                IsDeserter = false;
            }
        }

        public enum Container
        {
            Belt,
            Main,
            Wear
        }
        public class DuelPlayer
        {
            public const string INCORRECT_ID   = "-1";
            public const string INCORRECT_NAME = "Unknown";

            public string       Id          { get; set; }
            public string       Name        { get; set; }
            public int          Balance     { get; set; }
            public int          TotalWins   { get; set; }
            public int          TotalDies   { get; set; }
            public int          CurrentWins { get; set; }
            public int          CurrentDies { get; set; }
            public int          Rating      { get; set; }
            public int          Victories   { get; set; }
            public int          Loses       { get; set; }
            public DeserterInfo Deserter    { get; set; }
            public Vector3      OldPoint    { get; set; }
            public Team         CurrentTeam { get; set; }

            private Dictionary<Container, ItemContainer>   OldItems    { get; set; }

            public DuelPlayer() : this(INCORRECT_ID, INCORRECT_NAME, -1, -1, -1) { }
            public DuelPlayer(string id, string name, int bal, int wins, int dies)
            {
                Id = id;
                Name = name;
                Balance = bal;
                TotalWins = wins;
                TotalDies = dies;

                Deserter = new DeserterInfo();
            }
            
            public int CalculateBalance(BalanceAction action, int predicate)
            {
                if(action == BalanceAction.Add)
                {
                    Balance += predicate;

                    return Balance;
                }
                else if(action == BalanceAction.Multiple)
                {
                    Balance *= predicate;

                    return Balance;
                }
                else if(action == BalanceAction.Split)
                {
                    if(predicate == 0) throw new BalanceException("Balance cannot a split in ZERO");
                    else
                    {
                        Balance /= predicate;

                        return Balance;
                    }
                }
                else
                {
                    if (Balance < predicate) throw new BalanceException("Balance cannot be less then increase");
                    else
                    {
                        Balance -= predicate;

                        return Balance;
                    }
                }
            }
            public void CalculateRating()
            {
                if(Victories != 0 && Loses != 0)
                {
                    int result = (Victories * TotalWins) - (Loses * TotalDies);
                    if(result <= 0)
                    {
                        Rating = 0;
                    }
                    else
                    {
                        Rating = result;
                    }
                }
                else if(Victories != 0 && Loses == 0)
                {
                    int result = (Victories * TotalWins) - TotalDies;
                    if (result <= 0)
                    {
                        Rating = 0;
                    }
                    else
                    {
                        Rating = result;
                    }
                }
                else if(Victories == 0 && Loses != 0)
                {
                    int result = TotalWins - (TotalDies + Loses);
                    if (result <= 0)
                    {
                        Rating = 0;
                    }
                    else
                    {
                        Rating = result;
                    }
                }
                else
                {
                    int result = TotalWins - TotalDies;
                    if (result <= 0)
                    {
                        Rating = 0;
                    }
                    else
                    {
                        Rating = result;
                    }
                }
            }
            public void OnAccepted()
            {
                OldPoint = FindPlayer(Id).transform.position;
            }
            public void OnWin()
            {
                TotalWins++;
                CurrentWins++;
            }
            public void OnDie()
            {
                TotalDies++;
                CurrentDies++;
            }
            public void OnVictory(int cash)
            {
                ResetCurrents();

                if (cash != 0)
                {
                    Victories++;
                    CalculateBalance(BalanceAction.Add, cash);
                    CalculateRating();
                }
            }
            public void OnLose(int cash)
            {
                ResetCurrents();

                if (cash != 0)
                {
                    Loses++;
                    CalculateRating();
                }
            }

            public void ResetCurrents()
            {
                CurrentDies = 0;
                CurrentWins = 0;
                CurrentTeam = Team.None;
            }
            public void Destroy()
            {
                OldPoint = Vector3.zero;
                OldItems = null;
            }
            public void ForceDestroy(int cash)
            {
                ResetCurrents();

                CalculateBalance(BalanceAction.Add, cash);
            }
            public void SetDeserter(string reason, string from, PluginTimers timer, int secs, Action init)
            {
                if (Deserter == null) Deserter = new DeserterInfo();
                Deserter.Instaintiate(reason, from, timer, secs, Deserter.Destroy, init);
            }
            public void SetItems(Dictionary<Container, ItemContainer> items)
            {
                OldItems = items;
            }
            public ItemContainer GetContainer(Container type)
            {
                if(OldItems.ContainsKey(type))
                {
                    return OldItems[type];
                }
                else
                {
                    return null;
                }
            }
            public void DestroyItems()
            {
                OldItems = null;
            }

            //public static bool operator ==(DuelPlayer p1, DuelPlayer p2)
            //{
            //    if (p1.Id == p2.Id) return true;
            //    else return false;
            //}
            //public static bool operator !=(DuelPlayer p1, DuelPlayer p2)
            //{
            //    if (p1.Id != p2.Id) return true;
            //    else return false;
            //}
            public override int GetHashCode()
            {
                return base.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                return base.Equals(obj);
            }
            public override string ToString()
            {
                return $"{Id}:{Name}:{Balance}:{Victories}:{Loses}";
            }
        }
        public class DuelRequest
        {
            public DuelPlayer Initiator { get; set; }
            public DuelPlayer Responser { get; set; }
            public int        Bet       { get; set; }
            public int        Weapon    { get; set; }
            public int        Ammo      { get; set; }
            public string     Arena     { get; set; }

            public DuelRequest(DuelPlayer init, int bet, int weapon, int ammo, string arena = "Случайно")
            {
                Initiator = init;
                Bet       = bet;
                Arena     = arena;
                Weapon    = weapon;
                Ammo      = ammo;
            }

            public void OnResponse(DuelPlayer resp)
            {
                Responser = resp;

                Initiator.CalculateBalance(BalanceAction.Subtract, Bet);
                Responser.CalculateBalance(BalanceAction.Subtract, Bet);
            }
        }
        public class Arena
        {
            public int              Id            { get; set; }
            public string           CurrentArena  { get; set; }
            public ArenaSpawnPoints Spawns        { get; set; }
            public ArenaPoints      Corners       { get; set; }
            public DuelPlayer       RedPlayer     { get; set; }
            public DuelPlayer       BluePlayer    { get; set; }
            public TimerExtension   CurrentTimer  { get; set; }
            public int              Cash          { get; set; }
            public int              CurrentWeapon { get; set; }
            public int              CurrentAmmo   { get; set; }

            private PluginTimers  m_tInstance  { get; set; }

            public Arena(int id, ArenaPoints corners, DuelPlayer red, DuelPlayer blue, PluginTimers timer, int everyCash)
            {
                Id           = id;
                Corners      = corners;
                RedPlayer    = red;
                BluePlayer   = blue;
                m_tInstance  = timer;

                Cash         = everyCash * 2;

                CurrentTimer = new TimerExtension();
            }

            public void Boot(List<Vector3> blue, List<Vector3> red)
            {
                Spawns = new ArenaSpawnPoints(red, blue);
            }
            public void InitializeWeapon(int weapon, int ammo)
            {
                CurrentWeapon = weapon;
                CurrentAmmo   = ammo;
            }
            public void Start(int secs, Action init, Action callback)
            {
                CurrentTimer.Instantiate(m_tInstance, secs, callback, init);
            }
            public void Kick(DuelPlayer player)
            {
                TeleportHelper.TeleportToPoint(FindPlayer(player.Id), player.OldPoint);
            }

            public void ForceDestroy()
            {
                RedPlayer.ForceDestroy(Cash / 2);
                BluePlayer.ForceDestroy(Cash / 2);

                Kick(RedPlayer);
                Kick(BluePlayer);
            }
        }

        //TODO: Future for <cfg>
        public class ArenaInfo
        {
            public string   Name            { get; set; }
            public string   CenterPosition  { get; set; }
            public string[] RedSpawnPoints  { get; set; }
            public string[] BlueSpawnPoints { get; set; }

            public ArenaInfo() : this("", "", null, null) { }
            public ArenaInfo(string name, string center, string[] red, string[] blue)
            {
                Name            = name;
                CenterPosition  = center;
                RedSpawnPoints  = red;
                BlueSpawnPoints = blue;
            }
        }

        public class ArenaPoints
        {
            public Vector3 CenterDot   { get; set; }

            public ArenaPoints(Vector3 dot)
            {
                CenterDot = dot;
            }
        }
        public enum Team
        {
            None,
            Red,
            Blue
        }
        public class ArenaSpawnPoints
        {
            public List<Vector3> BluePoints { get; set; }
            public List<Vector3> RedPoints  { get; set; }

            public ArenaSpawnPoints(List<Vector3> red, List<Vector3> blue)
            {
                BluePoints = new List<Vector3>();
                RedPoints = new List<Vector3>();

                foreach (Vector3 point in blue)
                {
                    AddSpawnPoint(Team.Blue, point);
                }

                foreach(Vector3 point in red)
                {
                    AddSpawnPoint(Team.Red, point);
                }
            }
            public void AddSpawnPoint(Team team, Vector3 dot)
            {
                if(team == Team.Blue)
                {
                    BluePoints.Add(dot);
                }
                else if(team == Team.Red)
                {
                    RedPoints.Add(dot);
                }
                else
                {
                    throw new ArenaSpawnPointsException($"Incorrect team({team.ToString()}) for adding spawn point");
                }
            }
            public Vector3 GetRandom(Team team)
            {
                if(team == Team.Blue)
                {
                    return BluePoints.GetRandom();
                }
                else if(team == Team.Red)
                {
                    return RedPoints.GetRandom();
                }
                else
                {
                    return Vector3.up;
                }
            }
        }
        public class TimerExtension
        {
            public Timer Object    { get; set; }
            public int   Remaining { get; set; }
            public bool  IsEnabled { get; set; }

            public TimerExtension()
            {
                Remaining = 0;
                IsEnabled = false;
            }

            public void Instantiate(PluginTimers @object, int secs, Action callBackAction, Action init)
            {
                init();
                Object = @object.Repeat(1, secs, () =>
                {
                    secs--;
                    Remaining = secs;
                    if (secs == 0)
                    {
                        IsEnabled = false;
                        callBackAction();
                    }
                    else IsEnabled = true;
                });
            }

            public void Destroy()
            {
                if (Object != null)
                {
                    Object.Destroy();
                    Object = null;
                }

                IsEnabled = false;
            }
        }
        public static class TeleportHelper
        {
            public static void TeleportToPlayer(BasePlayer player, BasePlayer target)
            {
                player.Teleport(target);
            }
            public static void TeleportToPoint(BasePlayer player, Vector3 point)
            {
                player.Teleport(point);
            }
        }
        #endregion

        #region Configuration
        public class RustyDuelCashConfig
        {
            [JsonProperty("Префикс плагина в чате")]
            public string ChatPrefix { get; set; }

            [JsonProperty("Включить префикс плагина в чате?")]
            public bool EnableChatPrefix { get; set; }

            [JsonProperty("Радиус, при выходе из которого игрока автоматически выбрасывает из арены")]
            public int ArenaCheckRadius { get; set; }

            [JsonProperty("Координаты арен для использования:")]
            public Dictionary<string, string> ArenasCoordinates { get; set; }

            [JsonProperty("Координаты респавна команды красных для конкретных арен:")]
            public Dictionary<string, string[]> RedSpawnCoordinates { get; set; }

            [JsonProperty("Координаты респавна команды синих для конкретных арен:")]
            public Dictionary<string, string[]> BlueSpawnCoordinates { get; set; }

            [JsonProperty("Динамическая система привилегий. Позволяет установить доступ к аренам")]
            public Dictionary<string, List<string>> ArenasPermissions { get; set; }

            [JsonProperty("Список доступного оружия для участия на арене")]
            public List<string> AllowedWeaponList { get; set; }

            [JsonProperty("Время на тренировку")]
            public int TrainSeconds { get; set; }

            [JsonProperty("Время на битву")]
            public int MatchSeconds { get; set; }

            [JsonProperty("Вешать клеймо дизертира, если покинул арену до ее окончания")]
            public bool EnableDeserterSystem { get; set; }

            [JsonProperty("Сколько будет длиться клеймо дизертира? (в секундах)")]
            public int DeserterDebuffTime { get; set; }

            [JsonProperty("Время ожидания перед телепортом на арену (в секундах)")]
            public int TimeToTeleportOnArena { get; set; }

            public static RustyDuelCashConfig Prototype()
            {
                return new RustyDuelCashConfig()
                {
                    ChatPrefix             = "[DUELS]: ",
                    EnableChatPrefix       = true,
                    TrainSeconds           = 60,
                    MatchSeconds           = 600,
                    EnableDeserterSystem   = true,
                    DeserterDebuffTime     = 600,
                    TimeToTeleportOnArena  = 10,
                    ArenaCheckRadius       = 75,
                    ArenasCoordinates      = new Dictionary<string, string>()
                    {
                        ["Снежные Пики"]    = "(-124.3, 93.7, -144.4)",
                    },
                    RedSpawnCoordinates = new Dictionary<string, string[]>()
                    {
                        ["Снежные Пики"] = new string[]
                        {
                            "(-180.2, 100.0, -80.5)",
                            "(-197.8, 100.0, -72.3)"
                        }
                    },
                    BlueSpawnCoordinates = new Dictionary<string, string[]>()
                    {
                        ["Снежные Пики"] = new string[]
                        {
                            "(-37.1, 100.0, -242.9)",
                            "(-35.9, 100.1, -249.6)"
                        }
                    },
                    ArenasPermissions      = new Dictionary<string, List<string>>()
                    {
                        ["rustyduelcash.silver"] = new List<string>()
                        {
                            "Снежные Пики",
                        },
                        ["rustyduelcash.gold"] = new List<string>()
                        {
                            "Снежные Пики",
                        },
                        ["rustyduelcash.vip"] = new List<string>()
                        {
                            "Снежные Пики",
                        },
                        ["rustyduelcash.admin"] = new List<string>()
                        {
                            "Снежные Пики",
                        }
                    },
                    AllowedWeaponList      = new List<string>()
                    {
                        "ak.rifle",
                        "smg.thompson"
                    },
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.Converters.Add(Converter);
            m_Config = Config.ReadObject<RustyDuelCashConfig>();

            RegisterPermissions();
        }
        protected override void LoadDefaultConfig()
        {
            m_Config = RustyDuelCashConfig.Prototype();

            PrintWarning("Creating default config for this plugin");

            RegisterPermissions();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(m_Config);
        }
        #endregion

        #region Date Storage
        public void LoadAllPlayers()
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                LoadPlayer(player);
            }
        }
        public void LoadPlayer(BasePlayer player)
        {
            DynamicConfigFile plDataFile = Interface.Oxide.DataFileSystem.GetFile($"{Title}\\{player.UserIDString}");
            plDataFile.Settings.Converters.Add(Converter);

            DuelPlayer target = plDataFile.ReadObject<DuelPlayer>();
            if(target.Id == DuelPlayer.INCORRECT_ID || target.Name == DuelPlayer.INCORRECT_NAME)
            {
                target = null;
                target = new DuelPlayer(player.UserIDString, player.displayName, 0, 0, 0);
            }

            if(m_OnlinePlayers.Any((x) => x.Id == target.Id))
            {
                PrintError($"Duplicate items: '{target.Id}' in online players hashset. Old data deleted");
                m_OnlinePlayers.Remove(target);
            }

            m_OnlinePlayers.Add(target);
        }
        public void SaveAllPlayers()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                SavePlayer(player, true);
            }
        }
        public void SavePlayer(BasePlayer player, bool all = false)
        {
            if (m_OnlinePlayers.Any((x) => x.Id == player.UserIDString))
            {
                m_OnlinePlayers.Where((x) => x.Id == player.UserIDString).First().Destroy();

                Interface.Oxide.DataFileSystem.WriteObject($"{Title}\\{player.UserIDString}", m_OnlinePlayers.Where((x) => x.Id == player.UserIDString).First());

                if (!all) m_OnlinePlayers.Remove(m_OnlinePlayers.Where((x) => x.Id == player.UserIDString).First());
            }
            else
            {
                PrintError("Trying to save a not exists player duel data. Check the code");
            }
        }
        #endregion

        #region Localize
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                ["arena_ready_to_teleport"]            = "Вы будете телепортированы на арену через {0} секунд",
                ["arena_not_found"]                    = "Указанная Вами арена не найдена или не доступна",
                ["arena_stop_you_losed"]               = "Вы проиграли игроку '{0}'. Скоро Вы будете возвращены домой",
                ["arena_stop_you_win"]                 = "Вы выиграли игрока '{0}'. Скоро вы будете возвращены домой",
                ["arena_stop_global_message_normal"]   = "Арена #{0} -> Победил: {1} : Проиграл: {2}",
                ["arena_stop_standoff"]                = "Вы сыграли в ничью. Ваша ставка возвращена Вам",
                ["arena_stop_global_message_standoff"] = "Арена #{0} -> Бойцы сыграли в ничью",
                ["error_not_weapon_in_hands"]          = "Для участия у Вас должен быть очищен инвентарь и выбрано оружие в руках",
                ["info_request_created"]               = "Запрос на дуэль принят. Ожидайте пока кто-нибудь подтвердит",
                ["error_incorrect_rdc_create"]         = "Непонятная команда. Используется она так: /rdc.create 50 RadiationKey, кстати, узнать списки доступных арен можно введя /rdc.arenas",
                ["error_already_requested"]            = "Вы уже создавали запрос на дуэль, отмените его командой: /rdc.cancel или дождитесь ответа на Ваш запрос",
                ["error_incorrect_bet"]                = "Некорректная ставка, используйте числовые значения",
                ["error_arena_buzy"]                   = "Арена, на которую вы пытаетесь попасть - занята. Попробуйте другую или позже",
                ["error_you_dnt_exists_in_database"]   = "Упс! Мы не нашли Вас в базе данных, скорее всего Разраюотчик уже решает эту проблему",
                ["error_bet_disallow_zero"]            = "А Вы себе как представляете ставку ниже нуля ?",
                ["error_arena_dnt_exists"]             = "Введенная Вами арена не существует, список доступных арен: /rdc.arenas",
            }, this);
        }
        #endregion

        #region Initialize
        public void RegisterPermissions()
        {
            foreach(var perm in m_Config.ArenasPermissions)
            {
                if(permission.PermissionExists(perm.Key))
                {
                    continue;
                }
                else
                {
                    permission.RegisterPermission(perm.Key, this);
                    string message = $"Registered new permission: '{perm.Key}'. Allowed arenas: \n";
                    for(int i = 0; i < perm.Value.Count; i++)
                    {
                        message += $"{i}. {perm.Value[i]} \n";
                    }

                    PrintWarning(message);
                }
            }
        }
        #endregion

        #region Core
        private string GetArenaFullName(string part)
        {
            if(IsArenaExists(part))
            {
                return GetAllArenas().Where((x) => x.Contains(part)).First();
            }
            else
            {
                return String.Empty;
            }
        }
        private List<string> GetAllArenas()
        {
            List<string> all = new List<string>();
            foreach(var arena in m_Config.ArenasCoordinates)
            {
                if (!all.Contains(arena.Key)) all.Add(arena.Key);
                else continue;
            }

            return all;
        }
        private bool IsArenaExists(string arena)
        {
            return GetAllArenas().Any((x) => x.Contains(arena));
        }
        private bool IsArenaIsBuzy(string arena)
        {
            return m_ActiveArenas.Any((x) => x.CurrentArena == arena);
        }
        private bool IsArenaMember(BasePlayer player)
        {
            return (m_ActiveArenas.Any((x) => x.RedPlayer.Id == player.UserIDString) || m_ActiveArenas.Any((x) => x.BluePlayer.Id == player.UserIDString));
        }
        private bool IsDeserter(BasePlayer player)
        {
            return GetCommonDuelData(player).Deserter.IsDeserter;
        }
        private bool IsAlreadyRequested(BasePlayer player)
        {
            return m_Bets.Any((x) => x.Initiator.Id == player.UserIDString);
        }
        private bool IsAllowedWeapon(string prefab)
        {
            return m_Config.AllowedWeaponList.Any((x) => x.Contains(prefab));
        }
        private T FindInitiatorByName<T>(string name) where T : class
        {
            if(m_Bets.Any((x) => x.Initiator.Name.Contains(name)))
            {
                if(typeof(T) == typeof(DuelRequest))
                {
                    return (T)(object)m_Bets.Where((x) => x.Initiator.Name.Contains(name)).First();
                }
                else if(typeof(T) == typeof(DuelPlayer))
                {
                    return (T)(object)m_Bets.Where((x) => x.Initiator.Name.Contains(name)).First().Initiator;
                }
                else
                {
                    throw new DuelCoreException($"Incorrect type '{typeof(T)}' for find. Must be a DuelRequest and DuelPlayer");
                }
            }
            else
            {
                return null;
            }
        }
        private void OnSpawn(BasePlayer player)
        {
            TeleportHelper.TeleportToPoint(player, GetArenaStorage(player).Spawns.GetRandom(GetCommonDuelData(player).CurrentTeam));
        }
        private DuelPlayer GetCommonDuelData(BasePlayer player)
        {
            if (!m_OnlinePlayers.Any((x) => x.Id == player.UserIDString))
            {
                throw new DuelCoreException($"Player: '{player.UserIDString}' not found in online players. Check your code or load function");
            }

            DuelPlayer obj = m_OnlinePlayers.Where((x) => x.Id == player.UserIDString)?.First();
            if (obj == null) throw new DuelCoreException($"Incorrect error with getting duel player object for '{player.UserIDString}'");
            else
            {
                return obj;
            }
        }
        private Arena GetArenaStorage(BasePlayer player)
        {
            if(m_ActiveArenas.Any((x) => x.RedPlayer.Id == player.UserIDString))
            {
                Arena arena = m_ActiveArenas.Where((x) => x.RedPlayer.Id == player.UserIDString).FirstOrDefault();
                if(arena == null)
                {
                    throw new DuelCoreException($"Incorrect data for '{player.UserIDString}([Red] player)' arena member");
                }
                else
                {
                    return arena;
                }
            }
            else if(m_ActiveArenas.Any((x) => x.BluePlayer.Id == player.UserIDString))
            {
                Arena arena = m_ActiveArenas.Where((x) => x.BluePlayer.Id == player.UserIDString).FirstOrDefault();
                if (arena == null)
                {
                    throw new DuelCoreException($"Incorrect data for '{player.UserIDString}([Blue] player)' arena member");
                }
                else
                {
                    return arena;
                }
            }
            else
            {
                return null;
            }
        }
        private void PreparePlayerForArena(BasePlayer player)
        {
            ClearInventory(player);
        }
        private string GetChoisedWeapon(BasePlayer player)
        {
            string weapon = player.GetActiveItem().name;
            if(IsAllowedWeapon(weapon))
            {
                return weapon;
            }
            else
            {
                return String.Empty;
            }
        }
        private void ClearInventory(BasePlayer player)
        {
            SaveAllContainers(player);
        }
        private void SaveAllContainers(BasePlayer player)
        {
            Dictionary<Container, ItemContainer> items = new Dictionary<Container, ItemContainer>();
            DuelPlayer data = GetCommonDuelData(player);

            if (player.inventory.containerBelt.itemList.Count > 0)
            {
                items.Add(Container.Belt, player.inventory.containerBelt);
                player.inventory.containerBelt.Clear();
            }

            if (player.inventory.containerMain.itemList.Count > 0)
            {
                items.Add(Container.Main, player.inventory.containerMain);
                player.inventory.containerMain.Clear();
            }

            if (player.inventory.containerWear.itemList.Count > 0)
            {
                items.Add(Container.Wear, player.inventory.containerWear);
                player.inventory.containerWear.Clear();
            }

            data.SetItems(items);
        }
        private void RestoreAllContainers(BasePlayer player)
        {
            ItemContainer[] containers = new ItemContainer[3];
            DuelPlayer data = GetCommonDuelData(player);
            Array containerValues = Enum.GetValues(typeof(Container));

            for (int i = 0; i < containerValues.Length; i++)
            {
                if(data.GetContainer((Container)containerValues.GetValue(i)) != null)
                {
                    if ((Container)containerValues.GetValue(i) == Container.Belt)
                    {
                        foreach (var item in data.GetContainer((Container)containerValues.GetValue(i)).itemList)
                        {
                            item.MoveToContainer(player.inventory.containerBelt);
                        }
                    }
                    else if ((Container)containerValues.GetValue(i) == Container.Main)
                    {
                        foreach (var item in data.GetContainer((Container)containerValues.GetValue(i)).itemList)
                        {
                            item.MoveToContainer(player.inventory.containerMain);
                        }
                    }
                    else if ((Container)containerValues.GetValue(i) == Container.Wear)
                    {
                        foreach (var item in data.GetContainer((Container)containerValues.GetValue(i)).itemList)
                        {
                            item.MoveToContainer(player.inventory.containerWear);
                        }
                    }
                    else continue;
                }
            }
        }
        private bool IsSameIP(string username, string targetname)
        {
            BasePlayer user = FindPlayer(username);
            BasePlayer target = FindPlayer(targetname);
            if (user == null || target == null) return false;
            if (user.Connection.ipaddress == target.Connection.ipaddress) return true;
            else return false;
        }
        private int[] GetWeaponData(Item active)
        {
            if(active == null)
            {
                return null;
            }

            if(IsAllowedWeapon(active.info.shortname))
            {
                BaseProjectile projectile = active.GetHeldEntity() as BaseProjectile;
                if (projectile == null)
                {
                    throw new DuelCoreException($"Can't getting a component 'BaseProjectile' in item: '{active.info.shortname}'");
                }
                int[] data = new int[2];
                data[0] = active.info.itemid;

                if(projectile.primaryMagazine.ammoType == null)
                {
                    return null;
                }
                else
                {
                    data[1] = projectile.primaryMagazine.ammoType.itemid;

                    return data;
                }
            }
            else
            {
                return null;
            }
        }
        private void GiveChoisedWeapon(BasePlayer player, int weapon, int ammo, int amount = 120)
        {
            if (weapon == 0 || ammo == 0)
            {
                SendReply(player, weapon.ToString());
                SendReply(player, ammo.ToString());

                return;
            }

            Item weaponItem = ItemManager.CreateByItemID(weapon);
            Item ammoItem = ItemManager.CreateByItemID(ammo, amount);

            if (weaponItem == null || ammoItem == null)
            {
                SendReply(player, weapon.ToString());
                SendReply(player, ammo.ToString());

                return;
            }
            else
            {
                player.GiveItem(weaponItem);
                player.GiveItem(ammoItem);
            }
        }
        #endregion

        #region Hooks
        void Loaded()
        {
            LoadAllPlayers();
        }
        void Unload()
        {
            SaveAllPlayers();
        }
        void OnPlayerInit(BasePlayer player)
        {
            LoadPlayer(player);
        }
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            SavePlayer(player);
        }
        #endregion

        #region UI
        public class UI
        {
            public static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool useCursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = {Color = color},
                            RectTransform = {AnchorMin = aMin, AnchorMax = aMax},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            public static void LoadImage(ref CuiElementContainer container, string panel, string url, string aMin, string aMax)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    FadeOut = 0.15f,
                    Components =
                    {
                        new CuiRawImageComponent { Url = url, FadeIn = 0.3f },
                        new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax }
                    }
                });
            }

            public static void CreateInput(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, bool password, int charLimit, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent { Text = text, FontSize = size, Align = align, Color = color, Command = command, IsPassword = password, CharsLimit = charLimit},
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax }
                    }
                });
            }

            public static void CreatePanel(ref CuiElementContainer container, string panel, string color, string aMin, string aMax, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                    CursorEnabled = cursor
                },
                panel);
            }

            public static void CreateText(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax }
                },
                panel);
            }

            public static void CreateButton(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 1.0f },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.TrimStart('#');
                }

                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }

        public void ShowDuelsList(BasePlayer player)
        {
            //CuiElementContainer container = UI.CreateElementContainer("RustyDuelCashDuelList", "0.0006764603 0.0006764603 0.0006764603 0.7144409", "0.2480469 0.2916667", "0.75 0.7343751", true);

            ///*
            // * HEADER START
            // */
            //UI.CreatePanel(ref container, "RustyDuelCashDuelList", "0.8131743 0.8131743 0.8131743 0.5686275", "0.003891058 0.8235293", "0.9941635 0.9941176");
            //UI.CreateText(ref container, "RustyDuelCashDuelList", "0.3176471 0.1215686 0.572549 1", "Список дуэлей: ", 34, "0.02357566 0.08620673", "0.4204322 0.7758625");
            //UI.CreatePanel(ref container, "RustyDuelCashDuelList", "0.3176471 0.1176471 0.5686275 0.5568628", "0.003891051 0.7617646", "0.9961089 0.8205882", true);
            //UI.CreateText(ref container, "RustyDuelCashDuelList", "1 1 1 1", "Игрок: ", 14, "0.005893901 -0.05000114", "0.2514735 1");
            //UI.CreateText(ref container, "RustyDuelCashDuelList", "1 1 1 1", "Ставка: ", 14, "0.2538948 0.1028655", "0.4341847 0.9000016");
            //UI.CreateText(ref container, "RustyDuelCashDuelList", "1 1 1 1", "Арена: ", 14, "0.4424998 0.1028675", "0.7387035 0.9000036");
            //if(m_Bets.Count > 0)
            //{
            //    DuelRequest request = m_Bets.First();

            //    UI.CreatePanel(ref container, $"RustyDuelCashDuelList", "0.7019002 0.7019002 0.7019002 0.09661709", "0.003891051 0.6823528", "0.9941635 0.7617647", true);
            //    UI.CreateText(ref container, $"RustyDuelCashDuelList", "1 1 1 1", request.Initiator.Name, 14, "0.005928867 1.196993", "0.2490118 2.036714");
            //    UI.CreateText(ref container, $"RustyDuelCashDuelList", "1 1 1 1", request.Bet.ToString(), 14, "0.3632812 0.5703125", "0.4765625 0.5976563");
            //    UI.CreateText(ref container, $"RustyDuelCashDuelList", "1 1 1 1", request.Arena, 14, "0.4716797 0.5716145", "0.6142578 0.5989583");
            //    UI.CreateButton(ref container, $"RustyDuelCashDuelList", "0.01737355 0.5522067 0.007965274 0.4842647", "Принять", 14, "0.771642 0.0370349", "0.9999883 0.9259235", "rdc.accept");
            //}

            //CuiHelper.AddUi(player, container);

            var json = "[{\"name\":\"GeneralPanel\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0.0006764603 0.0006764603 0.0006764603 0.7144409\"},{\"type\":\"NeedsCursor\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.2480469 0.2916667\",\"anchormax\":\"0.75 0.7343751\",\"offsetmax\":\"0 0\"}]},{\"name\":\"Header\",\"parent\":\"GeneralPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0.8131743 0.8131743 0.8131743 0.5686275\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.003891058 0.8235293\",\"anchormax\":\"0.9941635 0.9941176\",\"offsetmax\":\"0 0\"}]},{\"name\":\"HeaderText\",\"parent\":\"Header\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"Список дуэлей: \",\"fontSize\":34,\"align\":\"MiddleCenter\",\"color\":\"0.3176471 0.1215686 0.572549 1\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.02357566 0.08620673\",\"anchormax\":\"0.4577603 0.8793101\",\"offsetmax\":\"0 0\"}]},{\"name\":\"SeparatorLine\",\"parent\":\"GeneralPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0.3176471 0.1176471 0.5686275 0.5568628\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.003891051 0.7617646\",\"anchormax\":\"0.9961089 0.8205882\",\"offsetmax\":\"0 0\"}]},{\"name\":\"SeparatorName\",\"parent\":\"SeparatorLine\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"игрок\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.005893901 -0.05000114\",\"anchormax\":\"0.2514735 1\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"SeparatorBet\",\"parent\":\"SeparatorLine\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"ставка\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.2538948 0.1028655\",\"anchormax\":\"0.4341847 0.9000016\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"SeparatorArena\",\"parent\":\"SeparatorLine\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"арена\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.4424998 0.1028675\",\"anchormax\":\"0.7387035 0.9000036\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataPanel\",\"parent\":\"GeneralPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0.7019002 0.7019002 0.7019002 0.09661709\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.003891051 0.6823528\",\"anchormax\":\"0.9941635 0.7617647\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataBet\",\"parent\":\"DuelDataPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"50\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.3632812 0.5703125\",\"anchormax\":\"0.4765625 0.5976563\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataArena\",\"parent\":\"DuelDataPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"Радиационный ключ\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.4716797 0.5716145\",\"anchormax\":\"0.6142578 0.5989583\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataEnjoyButton\",\"parent\":\"DuelDataPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Button\",\"color\":\"0.01737355 0.5522067 0.007965274 0.4842647\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.771642 0.0370349\",\"anchormax\":\"0.9999883 0.9259235\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataEnjoyText\",\"parent\":\"DuelDataEnjoyButton\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"принять\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.640625 0.5716145\",\"anchormax\":\"0.7382813 0.5976563\",\"offsetmax\":\"0 0\"}]},{\"name\":\"DuelDataPlayer\",\"parent\":\"DuelDataPanel\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"__red\",\"fontSize\":12,\"align\":\"MiddleCenter\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.005928867 1.196993\",\"anchormax\":\"0.2490118 2.036714\",\"offsetmin\":\"0 0\",\"offsetmax\":\"0 0\"}]},{\"name\":\"Footer\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0.654902 0.654902 0.654902 0.5604309\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.2490234 0.2929688\",\"anchormax\":\"0.7490234 0.3242188\",\"offsetmax\":\"0 0\"}]},{\"name\":\"BalanceText\",\"parent\":\"Footer\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"Bаланс: 147\",\"fontSize\":16,\"align\":\"MiddleRight\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.8007813 0.04166698\",\"anchormax\":\"0.9902344 0.916667\",\"offsetmax\":\"0 0\"}]},{\"name\":\"RatingText\",\"parent\":\"Footer\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"Рейтинг: 1276\",\"fontSize\":16,\"align\":\"MiddleLeft\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.2519531 0.04166603\",\"anchormax\":\"0.4472656 0.875\",\"offsetmax\":\"0 0\"}]},{\"name\":\"VictoriesText\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"UnityEngine.UI.Text\",\"text\":\"Статистика: 14/2\",\"fontSize\":16,\"align\":\"MiddleLeft\"},{\"type\":\"RectTransform\",\"anchormin\":\"0.2519531 0.2942708\",\"anchormax\":\"0.3720703 0.3203125\",\"offsetmax\":\"0 0\"}]},{\"name\":\"CuiElement\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"RectTransform\",\"anchormin\":\"0.09765625 0.1302083\",\"anchormax\":\"0.1953125 0.2604167\",\"offsetmax\":\"0 0\"}]},{\"name\":\"CuiElement\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"RectTransform\",\"anchormin\":\"0.09765625 0.1302083\",\"anchormax\":\"0.1953125 0.2604167\",\"offsetmax\":\"0 0\"}]},{\"name\":\"CuiElement\",\"parent\":\"Overlay\",\"components\":[{\"type\":\"RectTransform\",\"anchormin\":\"0.125 0.125\",\"anchormax\":\"0.2314453 0.1875\",\"offsetmax\":\"0 0\"}]}]";
            CuiHelper.AddUi(player, json);
        }
        public void DestroyDuelList(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "RustyDuelCashDuelList");
        }
        #endregion

        #region Commands
        [ChatCommand("rdc.admin.position")]
        private void CmdChatAdminPosition(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!player.IsAdmin) return;

            SendReply(player, $"Позиция: {player.transform.position}");
        }

        [ChatCommand("rdc.create")]
        private void CmdChatCreateDuel(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args.Length < 2)
            {
                SendReply(player, GetMessage("error_incorrect_rdc_create", this));

                return;
            }

            if(IsAlreadyRequested(player))
            {
                SendReply(player, GetMessage("error_already_requested", this));

                return;
            }
            if(!IsArenaExists(args[1]))
            {
                SendReply(player, GetMessage("error_arena_dnt_exists", this));

                return;
            }
            if(IsArenaIsBuzy(args[1]))
            {
                SendReply(player, GetMessage("error_arena_buzy", this));

                return;
            }
            int bet = -1;
            if(!Int32.TryParse(args[0], out bet))
            {
                SendReply(player, GetMessage("error_incorrect_bet", this));

                return;
            }
            if(bet < 0)
            {
                SendReply(player, GetMessage("error_bet_disallow_zero", this));

                return;
            }

            CreateDuelRequest(player, bet, GetArenaFullName(args[1]));
        }
        [ChatCommand("rdc.list")]
        private void CmdChatShowList(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (m_Bets.Count <= 0)
            {
                SendReply(player, "Доступных дуэлей нет");
            }
            else
            {
                SendReply(player, "Доступные дуэли: ");
                foreach (var bet in m_Bets)
                {
                    SendReply(player, $"Игрок: {bet.Initiator.Name}, Ставка: {bet.Bet}, Арена: {bet.Arena}");
                }

                SendReply(player, "Для того чтобы принять дуэль введите /rdc.accept никигрока");
            }
        }
        [ChatCommand("rdc.accept")]
        private void CmdChatAcceptDuel(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args.Length < 1)
            {
                SendReply(player, "Не знаю такую команду. Нужно указывать имя инициатора дуэлей");

                return;
            }
            DuelRequest request = FindInitiatorByName<DuelRequest>(args[0]);
            if (request == null)
            {
                SendReply(player, "Дуэль с таким инициатором не найден. Укажите корректное имя");

                return;
            }

            //if (request.Initiator.Id == player.UserIDString)
            //{
            //    SendReply(player, "Нельзя принимать свои же дуэли");

            //    return;
            //}
            //if (IsSameIP(request.Initiator.Name, player.displayName))
            //{
            //    SendReply(player, $"Запрещено принимать дуэль с одного и тогоже компьютера");

            //    return;
            //}

            try
            {
                request.OnResponse(GetCommonDuelData(player));
            }
            catch(BalanceException)
            {
                SendReply(player, "У вас не хватает средств на балансе для совершения этого действия");
            }
            finally
            {
                request.Initiator.OnAccepted();
                request.Responser.OnAccepted();

                PreparePlayerForArena(FindPlayer(request.Initiator.Id));
                PreparePlayerForArena(FindPlayer(request.Responser.Id));

                InitializeArena(request);
            }
        }

        [ChatCommand("rdc.arenas")]
        private void CmdChatArenasList(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            List<string> availible = new List<string>();
            foreach(string arena in GetAllArenas())
            {
                if (!IsArenaIsBuzy(arena)) availible.Add($"Арена: {arena}. Статус: <color=#00d219>доступна</color>"); 
                else availible.Add($"Арена: {arena}. Статус: <color=#d20015>не доступна</color>"); ;
            }

            SendReply(player, "Список доступных арен: ");
            foreach (string arena in availible)
            {
                SendReply(player, arena);
            }
        }

        [ChatCommand("rdc.cancel")]
        private void CmdChatArenasCancel(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsAlreadyRequested(player)) return;

            CancelDuelRequest(player);
        }
        #endregion

        #region Duel Instruments
        private void CreateDuelRequest(BasePlayer player, int bet, string arena = "Случайно")
        {
            DuelPlayer data = GetCommonDuelData(player);
            if(data == null)
            {
                SendReply(player, GetMessage("error_you_dnt_exists_in_database", this));

                return;
            }
            if(data.Balance < bet)
            {
                SendReply(player, $"Ваш баланс: {data.Balance}, а ставка: {bet}. Как вы думаете вы сможете создать такую ставку ?");

                return;
            }
            Item item = player.GetActiveItem() ?? null;
            if(item == null)
            {
                SendReply(player, "Для запуска арены Вам требуется держать выбранное оружие в руке");

                return;
            }

            int[] weapon = GetWeaponData(item);
            if(weapon == null)
            {
                SendReply(player, "Для запуска арены Вам требуется держать выбранное оружие в руке");

                return;
            }

            DuelRequest request = new DuelRequest(data, bet, weapon[0], weapon[1], arena);
            m_Bets.Add(request);

            SendReply(player, GetMessage("info_request_created", this));
        }
        private void CancelDuelRequest(BasePlayer player)
        {
            DuelRequest request = FindInitiatorByName<DuelRequest>(player.displayName);
            if(m_Bets.Contains(request))
            {
                m_Bets.Remove(request);

                SendReply(player, $"Ваш запрос на дуэль удален из списка");
            }
        }
        #endregion

        #region Arena Instruments
        private List<Vector3> GetSpawnPoints(Team team, string arena)
        {
            List<Vector3> points = new List<Vector3>();
            string[] spawns;

            if(team == Team.Blue)
            {
                if(m_Config.BlueSpawnCoordinates.ContainsKey(arena))
                {
                    spawns = m_Config.BlueSpawnCoordinates.Where((x) => x.Key == arena).First().Value;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                if (m_Config.RedSpawnCoordinates.ContainsKey(arena))
                {
                    spawns = m_Config.RedSpawnCoordinates.Where((x) => x.Key == arena).First().Value;
                }
                else
                {
                    return null;
                }
            }

            foreach (string spawn in spawns)
            {
                points.Add(spawn.ToVector3());
            }

            return points;
        }
        public void InitializeArena(DuelRequest request)
        {
            if (m_Config.ArenasCoordinates.Any((x) => x.Key == request.Arena))
            {
                var arenaInfo = m_Config.ArenasCoordinates.Where((x) => x.Key == request.Arena).First();

                request.Initiator.CurrentTeam = Team.Red;
                request.Responser.CurrentTeam = Team.Blue;

                Arena arena = new Arena(GenerateArenaId(), new ArenaPoints(arenaInfo.Value.ToVector3()), request.Initiator, request.Responser, timer, request.Bet);
                arena.Boot(GetSpawnPoints(Team.Blue, request.Arena), GetSpawnPoints(Team.Red, request.Arena));

                arena.InitializeWeapon(request.Weapon, request.Ammo);
                m_ActiveArenas.Add(arena);


                SendReply(FindPlayer(request.Initiator.Id), string.Format(GetMessage("arena_ready_to_teleport", this), m_Config.TimeToTeleportOnArena));
                SendReply(FindPlayer(request.Responser.Id), string.Format(GetMessage("arena_ready_to_teleport", this), m_Config.TimeToTeleportOnArena));

                timer.Once(m_Config.TimeToTeleportOnArena, () =>
                {
                    TeleportHelper.TeleportToPoint(FindPlayer(request.Initiator.Id), arena.Spawns.GetRandom(request.Initiator.CurrentTeam));
                    TeleportHelper.TeleportToPoint(FindPlayer(request.Responser.Id), arena.Spawns.GetRandom(request.Responser.CurrentTeam));

                    GiveChoisedWeapon(FindPlayer(request.Initiator.Id), request.Weapon, request.Ammo);
                    GiveChoisedWeapon(FindPlayer(request.Responser.Id), request.Weapon, request.Ammo);

                    CancelDuelRequest(FindPlayer(request.Initiator.Id));
                });
            }
            else
            {
                SendReply(FindPlayer(request.Initiator.Id), GetMessage("arena_not_found", this));
                SendReply(FindPlayer(request.Responser.Id), GetMessage("arena_not_found", this));
            }
        }
        public void StopArena(DuelPlayer redOrBluePlayer)
        {
            if(m_ActiveArenas.Any((x) => x.RedPlayer == redOrBluePlayer))
            {
                Arena currentArena = m_ActiveArenas.Where((x) => x.RedPlayer == redOrBluePlayer).First();
                if (currentArena.RedPlayer.CurrentWins > currentArena.BluePlayer.CurrentWins)
                {
                    currentArena.RedPlayer.OnVictory(currentArena.Cash);
                    currentArena.BluePlayer.OnLose(currentArena.Cash);

                    SendReply(FindPlayer(currentArena.RedPlayer.Id), string.Format(GetMessage("arena_stop_you_win", this), currentArena.BluePlayer.Name));
                    SendReply(FindPlayer(currentArena.BluePlayer.Id), string.Format(GetMessage("arena_stop_you_losed", this), currentArena.RedPlayer.Name));

                    SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message_normal", this), currentArena.Id, currentArena.RedPlayer.Name, currentArena.BluePlayer.Name));
                }
                else if (currentArena.RedPlayer.CurrentWins < currentArena.BluePlayer.CurrentWins)
                {
                    currentArena.RedPlayer.OnLose(currentArena.Cash);
                    currentArena.BluePlayer.OnVictory(currentArena.Cash);

                    SendReply(FindPlayer(currentArena.BluePlayer.Id), string.Format(GetMessage("arena_stop_you_win", this), currentArena.RedPlayer.Name));
                    SendReply(FindPlayer(currentArena.RedPlayer.Id), string.Format(GetMessage("arena_stop_you_losed", this), currentArena.BluePlayer.Name));

                    SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message", this), currentArena.Id, currentArena.BluePlayer.Name, currentArena.RedPlayer.Name));
                }
                else
                {
                    currentArena.RedPlayer.ForceDestroy(currentArena.Cash / 2);
                    currentArena.BluePlayer.ForceDestroy(currentArena.Cash / 2);

                    SendReply(FindPlayer(currentArena.RedPlayer.Id), GetMessage("arena_stop_standoff", this));
                    SendReply(FindPlayer(currentArena.RedPlayer.Id), GetMessage("arena_stop_standoff", this));

                    SendGlobalMessage(GetMessage("arena_stop_global_message_standoff", this));
                }

                currentArena.Kick(currentArena.RedPlayer);
                currentArena.Kick(currentArena.BluePlayer);
            }
        }
        private int GenerateArenaId()
        {
            int result = UnityEngine.Random.Range(0, 65535);
            if(m_ActiveArenas.Any((x) => x.Id == result))
            {
                return GenerateArenaId();
            }
            else
            {
                return result;
            }
        }
        #endregion

        #region Instruments
        private static BasePlayer FindPlayer(string nameOrUserId)
        {
            nameOrUserId = nameOrUserId.ToLower();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.displayName.ToLower().Contains(nameOrUserId) || player.UserIDString == nameOrUserId)
                    return player;
            }
            foreach (var player in BasePlayer.sleepingPlayerList)
            {
                if (player.displayName.ToLower().Contains(nameOrUserId) || player.UserIDString == nameOrUserId)
                    return player;
            }
            return default(BasePlayer);
        }
        private string GetMessage(string key, Plugin caller)
        {
            if (m_Config.EnableChatPrefix)
            {
                string prefix = $"<color=#ffa500ff>{m_Config.ChatPrefix}</color>";

                prefix += lang.GetMessage(key, caller);

                return prefix;
            }
            else
            {
                return lang.GetMessage(key, caller);
            }
        }
        private void SendGlobalMessage(string message)
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                SendReply(player, message);
            }
        }
        #endregion

        #region Converter
        static UnityVector3Converter Converter = new UnityVector3Converter();
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
        #endregion
    }
}
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
    [Info("RustyDuelCash (prealpha version)", "__red", "0.22.1687")]
    class RustyDuelCash : RustPlugin
    {
        #region Fields
        private static HashSet<DuelPlayer>                m_OnlinePlayers = new HashSet<DuelPlayer>();
        private static List<Arena>                        m_ActiveArenas  = new List<Arena>();
        private static List<DuelRequest>                  m_Bets          = new List<DuelRequest>();

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

            private List<Item>   OldItems    { get; set; }

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
                CurrentTeam = Team.None;

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
                CurrentTeam = Team.None;

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
            public void AddItem(Item item)
            {
                if (OldItems == null) OldItems = new List<Item>();
                if (OldItems.Any((x) => x.info.itemid == item.info.itemid)) return;

                OldItems.Add(item);
            }
            public List<Item> GetItems()
            {
                return OldItems;
            }
            public void DestroyItems()
            {
                OldItems = null;
            }
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
            public void StartTraining(int secs, Action init, Action callback)
            {
                Start(secs, init, callback);
            }
            public void StartBattle(int secs, Action init, Action final)
            {
                Start(secs, init, final);
            }
            private void Start(int secs, Action init, Action callback)
            {
                CurrentTimer.Instantiate(m_tInstance, secs, callback, init);
            }
            public void DestroyTimers()
            {
                CurrentTimer.Destroy();
            }
            public void Kick(DuelPlayer player)
            {
                TeleportHelper.TeleportToPoint(FindPlayer(player.Id), player.OldPoint);
            }
            public void RespawnMembers()
            {
                TeleportHelper.TeleportToPoint(FindPlayer(RedPlayer.Id), Spawns.GetRandom(Team.Red));
                TeleportHelper.TeleportToPoint(FindPlayer(BluePlayer.Id), Spawns.GetRandom(Team.Blue));
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
                    MatchSeconds           = 180,
                    EnableDeserterSystem   = true,
                    DeserterDebuffTime     = 600,
                    TimeToTeleportOnArena  = 10,
                    ArenaCheckRadius       = 75,
                    ArenasCoordinates      = new Dictionary<string, string>()
                    {
                        ["Снежные Пики"]    = "(-124.3, 93.7, -144.4)",
                        ["Мясорубка"]       = "(-397.3, 101.1, 418.3)",
                        ["Буря в Пустыне"]  = "(-327.1, 100.0, 106.9)",
                        ["Антенны"]         = "(-82.5, 100.2, -393.1)",
                    },
                    RedSpawnCoordinates = new Dictionary<string, string[]>()
                    {
                        ["Снежные Пики"] = new string[]
                        {
                            "(-180.2, 100.0, -80.5)",
                            "(-197.8, 100.0, -72.3)",
                            "(-193.4, 100.0, -83.5)",
                            "(-216.7, 101.4, -132.0)",
                            "(-213.4, 101.1, -185.9)"
                        },
                        ["Мясорубка"] = new string[]
                        {
                            "(-328.9, 100.0, 423.1)",
                            "(-331.5, 100.3, 424.9)",
                            "(-340.4, 100.1, 423.7)",
                            "(-363.8, 100.4, 420.4)",
                            "(-378.6, 101.0, 464.4)"
                        },
                        ["Буря в Пустыне"] = new string[]
                        {
                            "(-421.2, 102.0, 172.8)",
                            "(-444.9, 101.4, 169.3)",
                            "(-462.0, 103.0, 167.6)",
                            "(-451.8, 102.3, 207.2)",
                            "(-479.5, 101.9, 198.7)"
                        },
                        ["Антенны"] = new string[]
                        {
                            "(22.5, 100.6, -396.6)",
                            "(31.2, 100.8, -427.1)",
                            "(52.0, 100.8, -467.0)",
                            "(68.0, 100.0, -404.3)",
                            "(70.6, 102.5, -359.3)"
                        },
                    },
                    BlueSpawnCoordinates = new Dictionary<string, string[]>()
                    {
                        ["Снежные Пики"] = new string[]
                        {
                            "(-37.1, 100.0, -242.9)",
                            "(-35.9, 100.1, -249.6)",
                            "(-12.7, 100.6, -247.2)",
                            "(-36.1, 101.0, -167.6)",
                            "(-148.0, 101.4, -244.1)"
                        },
                        ["Мясорубка"] = new string[]
                        {
                            "(-462.6, 100.2, 417.7)",
                            "(-458.4, 100.2, 426.1)",
                            "(-460.1, 100.0, 418.9)",
                            "(-445.3, 100.2, 415.7)",
                            "(-428.1, 100.6, 421.4)"
                        },
                        ["Буря в Пустыне"] = new string[]
                        {
                            "(-462.6, 100.2, 417.7)",
                            "(-458.4, 100.2, 426.1)",
                            "(-460.1, 100.0, 418.9)",
                            "(-445.3, 100.2, 415.7)",
                            "(-428.1, 100.6, 421.4)"
                        },
                        ["Антенны"] = new string[]
                        {
                            "(-166.4, 100.4, -477.8)",
                            "(-196.6, 100.5, -315.8)",
                            "(-175.2, 100.5, -314.8)",
                            "(-196.1, 99.4, -399.3)",
                            "(-176.3, 100.9, -446.8)"
                        },
                    },
                    ArenasPermissions      = new Dictionary<string, List<string>>()
                    {
                        ["rustyduelcash.silver"] = new List<string>()
                        {
                            "Снежные Пики",
                            "Буря в Пустыне"
                        },
                        ["rustyduelcash.gold"] = new List<string>()
                        {
                            "Снежные Пики",
                            "Мясорубка",
                            "Буря в Пустыне"
                        },
                        ["rustyduelcash.vip"] = new List<string>()
                        {
                            "Снежные Пики",
                            "Мясорубка",
                            "Буря в Пустыне",
                            "Антенны",
                        },
                        ["rustyduelcash.admin"] = new List<string>()
                        {
                            "Снежные Пики",
                            "Мясорубка",
                            "Буря в Пустыне",
                            "Антенны",
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
                PrintWarning($"Duplicate items: '{target.Id}' in online players hashset. Old data deleted");
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

            m_OnlinePlayers.Clear();
        }
        public void SavePlayer(BasePlayer player, bool all = false)
        {
            DuelPlayer saveable = GetRepository<DuelPlayer>(player.UserIDString);
            if(saveable != null)
            {
                saveable.Destroy();
                Interface.Oxide.DataFileSystem.WriteObject($"{Title}\\{player.UserIDString}", saveable);

                if (!all)
                {
                    if(m_OnlinePlayers.Contains(saveable))
                    {
                        m_OnlinePlayers.Remove(saveable);
                    }
                    else
                    {
                        PrintWarning($"Don't founded: {saveable.Id}");
                    }
                }
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
                ["arena_stop_global_message_normal"]   = "Арена #{0}({1}) -> Победил: {2} : Проиграл: {3}",
                ["arena_stop_standoff"]                = "Вы сыграли в ничью. Ваша ставка возвращена Вам",
                ["arena_stop_global_message_standoff"] = "Арена #{0}({1}) -> Бойцы сыграли в ничью",
                ["error_not_weapon_in_hands"]          = "Для участия у Вас должен быть очищен инвентарь и выбрано оружие в руках",
                ["info_request_created"]               = "Запрос на дуэль принят. Ожидайте пока кто-нибудь подтвердит",
                ["error_incorrect_rdc_create"]         = "Непонятная команда. Используется она так: /rdc.create 50 RadiationKey, кстати, узнать списки доступных арен можно введя /rdc.arenas",
                ["error_already_requested"]            = "Вы уже создавали запрос на дуэль, отмените его командой: /rdc.cancel или дождитесь ответа на Ваш запрос",
                ["error_incorrect_bet"]                = "Некорректная ставка, используйте числовые значения",
                ["error_arena_buzy"]                   = "Арена, на которую вы пытаетесь попасть - занята. Попробуйте другую или позже",
                ["error_you_dnt_exists_in_database"]   = "Упс! Мы не нашли Вас в базе данных, скорее всего Разраюотчик уже решает эту проблему",
                ["error_bet_disallow_zero"]            = "А Вы себе как представляете ставку ниже нуля ?",
                ["error_arena_dnt_exists"]             = "Введенная Вами арена не существует, список доступных арен: /rdc.arenas",
                ["info_duel_list_empty"]               = "Доступных дуэлей нет",
                ["info_duel_list_title"]               = "Доступные дуэли: ",
                ["info_duel_list_item"]                = "Игрок: {0}, Ставка: {1}, Арена: {2}",
                ["info_duel_list_footer"]              = "Для того, чтобы принять дуэль введите: /rdc.accept [ник игрока] (можно сокращенно)",
                ["error_command_rdcaccept_length"]     = "Неправильное использование команды. Используйте /rdc.accept ялюблюадмина",
                ["error_initiator_not_found"]          = "Дуэль игрока '{0}' не найден. Проверьте введенные данные",
                ["error_accepting_self"]               = "Нельзя принимать свои дуэли",
                ["error_accepting_selfip"]             = "Найдено совпадение IP-адресов. Ваша команда отклонена",
                ["error_accepting_min_balance"]        = "Не хватает BetCoins для создания ставки",
                ["info_arenas_list_item"]              = "Арена: {0}. Статус: {1}",
                ["info_arenas_list_title"]             = "Список доступных арен: ",
                ["error_weapon_in_hand_dnt_exists"]    = "Для запуска арены возьмите оружие в руку",
                ["info_duel_request_removed"]          = "Ваш запрос дуэли на арене: '{0}' удален из списка доступных дуэлей",
                ["info_duel_training_started"]         = "Тренировка началась. Время тренировки: '{0}'",
                ["info_duel_match_start"]              = "Бой начался. Длительнось боя: '{0}'",
                ["info_duel_stop_force"]               = "Игрок: '{0}' покинул арену раньше установленного времени. Выигрыш Ваш, хоть и выиграли Вы нечестно :(",
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
                return GetAllArenas().Where((x) => x.ToLower().Contains(part.ToLower())).First();
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
            return GetAllArenas().Any((x) => x.ToLower().Contains(arena.ToLower()));
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
            return GetRepository<DuelPlayer>(player.UserIDString).Deserter.IsDeserter;
        }
        private bool IsAlreadyRequested(BasePlayer player)
        {
            return GetRepository<DuelRequest>(player.UserIDString) != null ? true : false;
        }
        private bool IsAllowedWeapon(string prefab)
        {
            return m_Config.AllowedWeaponList.Any((x) => x.Contains(prefab));
        }
        private static T GetRepository<T>(string playerId) where T : class
        {
            if(typeof(T) == typeof(DuelPlayer))
            {
                if(m_OnlinePlayers.Any((x) => x.Id == playerId))
                {
                    return (T)(object)m_OnlinePlayers.Where((x) => x.Id == playerId).First();
                }
                else
                {
                    return (T)(object)null;
                }
            }
            else if(typeof(T) == typeof(DuelRequest))
            {
                if (m_Bets.Any((x) => x.Initiator.Id == playerId))
                {
                    return (T)(object)m_Bets.Where((x) => x.Initiator.Id == playerId).First();
                }
                else if(m_Bets.Any((x) => x.Responser.Id == playerId))
                {
                    return (T)(object)m_Bets.Where((x) => x.Responser.Id == playerId).First();
                }
                else
                {
                    return (T)(object)null;
                }
            }
            else if(typeof(T) == typeof(Arena))
            {
                if (m_ActiveArenas.Any((x) => x.RedPlayer.Id == playerId))
                {
                    return (T)(object)m_ActiveArenas.Where((x) => x.RedPlayer.Id == playerId).First();
                }
                else if(m_ActiveArenas.Any((x) => x.BluePlayer.Id == playerId))
                {
                    return (T)(object)m_ActiveArenas.Where((x) => x.BluePlayer.Id == playerId).First();
                }
                else
                {
                    return (T)(object)null;
                }
            }
            else
            {
                return (T)(object)null;
            }
        }
        private void ClearInventory(BasePlayer player, bool save)
        {
            if(save)
                SaveAllContainers(player);

            DeleteAllItems(player);
        }
        private void DeleteAllItems(BasePlayer player)
        {
            player.inventory.containerBelt.Clear();
            player.inventory.containerMain.Clear();
            player.inventory.containerWear.Clear();
        }
        private void SaveAllContainers(BasePlayer player)
        {
            DuelPlayer data = GetRepository<DuelPlayer>(player.UserIDString);
            data.DestroyItems();

            if (player.inventory.AllItems().Count() > 0)
            {
                foreach(var item in player.inventory.AllItems())
                {
                    data.AddItem(ItemManager.CreateByItemID(item.info.itemid, item.amount)); // <- Самый жесткий в мире костыль :D
                }
            }
        }
        private void RestoreAllContainers(BasePlayer player)
        {
            List<Item> items = GetRepository<DuelPlayer>(player.UserIDString).GetItems();

            if(items != null && items.Count > 0)
            {
                foreach(var item in items)
                {
                    player.GiveItem(item);
                }
            }

            GetRepository<DuelPlayer>(player.UserIDString).DestroyItems();
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
            if (player == null) return;
            if (!IsArenaMember(player))
            {
                SavePlayer(player);
            }
            else
            {
                DuelPlayer playerChild = GetRepository<DuelPlayer>(player.UserIDString);
                if (playerChild == null) return;

                if(playerChild.CurrentTeam == Team.Red)
                {
                    StopArena(playerChild, Team.Blue);
                }
                else
                {
                    StopArena(playerChild, Team.Red);
                }

                SavePlayer(player);
            }
        }
        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer victimParent = entity?.ToPlayer() ?? null;
            if (victimParent == null) return;
            if (!IsArenaMember(victimParent)) return;

            DuelPlayer victim = GetRepository<DuelPlayer>(victimParent.UserIDString);
            if (victim == null) return;

            victim.OnDie();
            ClearInventory(victimParent, false);

            BasePlayer attackerParent = info?.InitiatorPlayer ?? null;
            if (attackerParent != null)
            {
                if (IsArenaMember(attackerParent))
                {
                    DuelPlayer attacker = GetRepository<DuelPlayer>(attackerParent.UserIDString);
                    attacker.OnWin();

                    ClearInventory(attackerParent, false);
                }
            }

            /*
             * TODO:
             * -> Refresh Counters GUI
             */
        }
        void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            if (!IsArenaMember(player))
            {
                /*
                 * TODO:
                 * -> player.Teleport(m_Config.LobbyCoordinates.GetRandom());
                 */

                return;
            }

            Arena arena = GetRepository<Arena>(player.UserIDString);
            if (arena == null) return;

            arena.RespawnMembers();

            ClearInventory(FindPlayer(arena.RedPlayer.Id), false);
            ClearInventory(FindPlayer(arena.BluePlayer.Id), false);

            GiveChoisedWeapon(FindPlayer(arena.RedPlayer.Id), arena.CurrentWeapon, arena.CurrentAmmo);
            GiveChoisedWeapon(FindPlayer(arena.BluePlayer.Id), arena.CurrentWeapon, arena.CurrentAmmo);
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
                SendReply(player, GetMessage("info_duel_list_empty", this));
            }
            else
            {
                SendReply(player, GetMessage("info_duel_list_title", this));
                foreach (var bet in m_Bets)
                {
                    SendReply(player, string.Format(GetMessage("info_duel_list_item", this), bet.Initiator.Name, bet.Bet, bet.Arena));
                }

                SendReply(player, GetMessage("info_duel_list_footer", this));
            }
        }
        [ChatCommand("rdc.accept")]
        private void CmdChatAcceptDuel(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args.Length < 1)
            {
                SendReply(player, GetMessage("error_command_rdcaccept_length", this));

                return;
            }
            string initiatorId = FindPlayer(args[0])?.UserIDString ?? string.Empty;

            DuelRequest request = GetRepository<DuelRequest>(initiatorId);
            if (request == null)
            {
                SendReply(player, string.Format(GetMessage("error_initiator_not_found", this), args[0]));

                return;
            }
            if (request.Initiator.Id == player.UserIDString)
            {
                SendReply(player, GetMessage("error_accepting_self", this));

                return;
            }
            if (IsSameIP(request.Initiator.Name, player.displayName))
            {
                SendReply(player, GetMessage("error_accepting_selfip", this));

                return;
            }

            try
            {
                request.OnResponse(GetRepository<DuelPlayer>(player.UserIDString));
            }
            catch(BalanceException)
            {
                SendReply(player, GetMessage("error_accepting_min_balance", this));
            }
            finally
            {
                request.Initiator.OnAccepted();
                request.Responser.OnAccepted();

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
                if (!IsArenaIsBuzy(arena)) availible.Add(string.Format(GetMessage("info_arenas_list_item", this), arena, "<color=#00d219>доступна</color>"));
                else availible.Add(string.Format(GetMessage("info_arenas_list_item", this), arena, "<color=#d20015>не доступна</color>"));
            }

            SendReply(player, GetMessage("info_arenas_list_title", this));
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
        [ChatCommand("arena.leave")]
        private void CmdChatArenaLeave(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!IsArenaMember(player)) return;

            Arena arena = GetRepository<Arena>(player.UserIDString);
            if(arena.RedPlayer.Id == player.UserIDString)
            {
                StopArena(GetRepository<DuelPlayer>(player.UserIDString), Team.Blue);
            }
            else
            {
                StopArena(GetRepository<DuelPlayer>(player.UserIDString), Team.Red);
            }
        }
        #endregion

        #region Duel Instruments
        private void CreateDuelRequest(BasePlayer player, int bet, string arena = "Случайно")
        {
            DuelPlayer data = GetRepository<DuelPlayer>(player.UserIDString);
            if (data == null)
            {
                SendReply(player, GetMessage("error_you_dnt_exists_in_database", this));

                return;
            }
            if(data.Balance < bet)
            {
                SendReply(player, GetMessage("error_accepting_min_balance", this));

                return;
            }
            Item item = player.GetActiveItem() ?? null;
            if(item == null)
            {
                SendReply(player, GetMessage("error_weapon_in_hand_dnt_exists", this));

                return;
            }

            int[] weapon = GetWeaponData(item);
            if(weapon == null)
            {
                SendReply(player, GetMessage("error_weapon_in_hand_dnt_exists", this));

                return;
            }
            if(arena == "Случайно")
            {
                arena = m_Config.ArenasCoordinates.Keys.ToList().GetRandom();
                while(IsArenaIsBuzy(arena))
                {
                    arena = m_Config.ArenasCoordinates.Keys.ToList().GetRandom();
                }
            }

            DuelRequest request = new DuelRequest(data, bet, weapon[0], weapon[1], arena);
            m_Bets.Add(request);

            SendReply(player, GetMessage("info_request_created", this));
        }
        private void CancelDuelRequest(BasePlayer player)
        {
            DuelRequest request = GetRepository<DuelRequest>(player.UserIDString);
            if(m_Bets.Contains(request))
            {
                m_Bets.Remove(request);

                SendReply(player, string.Format(GetMessage("info_duel_request_removed", this), request.Arena));
            }
        }
        #endregion

        #region Player Instruments
        private void HealPlayer(BasePlayer player)
        {
            player.Heal(100f);
        }
        private string ToNormalTimeString(int seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            string time = string.Empty;
            if(span.Days > 0)
            {
                time += $"{span.Days}д. ";
            }
            if(span.Days > 0)
            {
                time += $"{span.Hours}ч. ";
            }
            if(span.Minutes > 0)
            {
                time += $"{span.Minutes}м. ";
            }
            if(span.Seconds > 0)
            {
                time += $"{span.Seconds}с.";
            }

            return time;
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

                ClearInventory(FindPlayer(request.Initiator.Id), true);
                ClearInventory(FindPlayer(request.Responser.Id), true);

                SendReply(FindPlayer(request.Initiator.Id), string.Format(GetMessage("arena_ready_to_teleport", this), m_Config.TimeToTeleportOnArena));
                SendReply(FindPlayer(request.Responser.Id), string.Format(GetMessage("arena_ready_to_teleport", this), m_Config.TimeToTeleportOnArena));

                timer.Once(m_Config.TimeToTeleportOnArena, () =>
                {
                    TeleportHelper.TeleportToPoint(FindPlayer(request.Initiator.Id), arena.Spawns.GetRandom(request.Initiator.CurrentTeam));
                    TeleportHelper.TeleportToPoint(FindPlayer(request.Responser.Id), arena.Spawns.GetRandom(request.Responser.CurrentTeam));

                    GiveChoisedWeapon(FindPlayer(request.Initiator.Id), request.Weapon, request.Ammo);
                    GiveChoisedWeapon(FindPlayer(request.Responser.Id), request.Weapon, request.Ammo);

                    HealPlayer(FindPlayer(request.Initiator.Id));
                    HealPlayer(FindPlayer(request.Responser.Id));

                    CancelDuelRequest(FindPlayer(request.Initiator.Id));

                    if(m_ActiveArenas.Contains(arena))
                    {

                        Arena current = m_ActiveArenas.Where((x) => x.Id == arena.Id).First();
                        current.StartTraining(m_Config.TrainSeconds, () =>
                        {
                            SendReply(FindPlayer(current.RedPlayer.Id), string.Format(GetMessage("info_duel_training_started", this), ToNormalTimeString(m_Config.TrainSeconds)));
                            SendReply(FindPlayer(current.BluePlayer.Id), string.Format(GetMessage("info_duel_training_started", this), ToNormalTimeString(m_Config.TrainSeconds)));
                        }, () =>
                        {
                            current.DestroyTimers();
                            current.RespawnMembers();

                            HealPlayer(FindPlayer(current.BluePlayer.Id));
                            HealPlayer(FindPlayer(current.RedPlayer.Id));

                            ClearInventory(FindPlayer(current.BluePlayer.Id), false);
                            ClearInventory(FindPlayer(current.RedPlayer.Id), false);

                            current.StartBattle(m_Config.MatchSeconds, () =>
                            {
                                GiveChoisedWeapon(FindPlayer(current.BluePlayer.Id), current.CurrentWeapon, current.CurrentAmmo);
                                GiveChoisedWeapon(FindPlayer(current.RedPlayer.Id), current.CurrentWeapon, current.CurrentAmmo);

                                SendReply(FindPlayer(current.RedPlayer.Id), string.Format(GetMessage("info_duel_match_start", this), ToNormalTimeString(m_Config.MatchSeconds)));
                                SendReply(FindPlayer(current.BluePlayer.Id), string.Format(GetMessage("info_duel_match_start", this), ToNormalTimeString(m_Config.MatchSeconds)));
                            }, () =>
                            {
                                StopArena(current.RedPlayer);
                            });
                        });
                    }
                });
            }
            else
            {
                SendReply(FindPlayer(request.Initiator.Id), GetMessage("arena_not_found", this));
                SendReply(FindPlayer(request.Responser.Id), GetMessage("arena_not_found", this));
            }
        }
        public void StopArena(DuelPlayer redOrBluePlayer, Team winner = Team.None)
        {
            if(m_ActiveArenas.Any((x) => x.RedPlayer == redOrBluePlayer))
            {
                Arena current = GetRepository<Arena>(redOrBluePlayer.Id);

                if (winner != Team.None)
                {
                    if (winner == Team.Red)
                    {
                        current.RedPlayer.OnVictory(current.Cash);
                        current.BluePlayer.OnLose(current.Cash);

                        SendReply(FindPlayer(current.RedPlayer.Id), string.Format(GetMessage("info_duel_stop_force", this), current.BluePlayer.Name));
                        SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message_normal", this), current.Id, current.CurrentArena, current.RedPlayer.Name, current.BluePlayer.Name));
                    }
                    else if (winner == Team.Blue)
                    {
                        current.BluePlayer.OnVictory(current.Cash);
                        current.RedPlayer.OnLose(current.Cash);

                        SendReply(FindPlayer(current.BluePlayer.Id), string.Format(GetMessage("info_duel_stop_force", this), current.RedPlayer.Name));
                        SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message_normal", this), current.Id, current.CurrentArena, current.BluePlayer.Name, current.RedPlayer.Name));
                    }
                    else
                    {
                        throw new DuelCoreException($"Incorrect winner type: {winner}, breaking manual stopping the arena");
                    }

                    current.Kick(current.RedPlayer);
                    current.Kick(current.BluePlayer);
                }
                else
                {
                    if (current.RedPlayer.CurrentWins > current.BluePlayer.CurrentWins)
                    {
                        current.RedPlayer.OnVictory(current.Cash);
                        current.BluePlayer.OnLose(current.Cash);

                        SendReply(FindPlayer(current.RedPlayer.Id), string.Format(GetMessage("arena_stop_you_win", this), current.BluePlayer.Name));
                        SendReply(FindPlayer(current.BluePlayer.Id), string.Format(GetMessage("arena_stop_you_losed", this), current.RedPlayer.Name));

                        SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message_normal", this), current.Id, current.RedPlayer.Name, current.BluePlayer.Name));
                    }
                    else if (current.RedPlayer.CurrentWins < current.BluePlayer.CurrentWins)
                    {
                        current.RedPlayer.OnLose(current.Cash);
                        current.BluePlayer.OnVictory(current.Cash);

                        SendReply(FindPlayer(current.BluePlayer.Id), string.Format(GetMessage("arena_stop_you_win", this), current.RedPlayer.Name));
                        SendReply(FindPlayer(current.RedPlayer.Id), string.Format(GetMessage("arena_stop_you_losed", this), current.BluePlayer.Name));

                        SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message", this), current.Id, current.CurrentArena, current.BluePlayer.Name, current.RedPlayer.Name));
                    }
                    else
                    {
                        current.RedPlayer.ForceDestroy(current.Cash / 2);
                        current.BluePlayer.ForceDestroy(current.Cash / 2);

                        SendReply(FindPlayer(current.RedPlayer.Id), GetMessage("arena_stop_standoff", this));
                        SendReply(FindPlayer(current.RedPlayer.Id), GetMessage("arena_stop_standoff", this));

                        SendGlobalMessage(string.Format(GetMessage("arena_stop_global_message_standoff", this), current.Id, current.CurrentArena));
                    }

                    current.Kick(current.RedPlayer);
                    current.Kick(current.BluePlayer);
                }

                ClearInventory(FindPlayer(current.RedPlayer.Id), false);
                ClearInventory(FindPlayer(current.BluePlayer.Id), false);

                RestoreAllContainers(FindPlayer(current.RedPlayer.Id));
                RestoreAllContainers(FindPlayer(current.BluePlayer.Id));

                if(m_ActiveArenas.Contains(current))
                {
                    m_ActiveArenas.Remove(current);
                }
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
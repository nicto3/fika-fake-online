using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FikaServer.Models.Enums;
using FikaServer.Models.Fika.Presence;
using FikaServer.Services;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;

namespace Par4d0xyBot
{
    /// <summary>
    /// ВАЖНО: это чисто декоративный слой. То, что тут показано в списке
    /// "онлайн игроков" (в меню / на барахолке / в схроне / "в рейде"),
    /// НЕ связано с тем, взаправду ли она в этот момент физически заспавнена
    /// как бот в чьём-то реальном рейде (за это отвечает Par4d0xyBotGenerator).
    /// Так проще и надёжнее — не пытаемся синхронизировать выдуманный статус
    /// с реальными событиями рейда, которые мы пока не умеем ловить точно.
    /// </summary>
    public class Par4d0xySimulatedRaidService : IDisposable
    {
        private readonly PresenceService _presence;
        private readonly Random _rng = new();
        private readonly object _lock = new();
        private string _sessionId = "";
        private Timer? _timer;
        private DateTime _lastLoggedAt = DateTime.MinValue;

        // TODO: когда раздекомпилим FikaRaidPresence — сюда добавить
        // реальные Map/StartedAt поля в RaidInformation. Пока просто
        // переключаем Activity на IN_RAID без деталей, чтобы не гадать
        // с полями вслепую и не рисковать сборкой.
        // Настоящие внутренние ключи карт (подтверждено декомпилом
        // ExtensionMethods.ToFikaLocation в FikaServer) — раньше тут были
        // человекочитаемые названия "на глаз", теперь реальные movok-ключи.
        private static readonly (string Key, string DisplayName)[] Locations =
        {
            ("bigmap", "Customs"),
            ("factory4_day", "Factory (day)"),
            ("factory4_night", "Factory (night)"),
            ("interchange", "Interchange"),
            ("laboratory", "Laboratory"),
            ("lighthouse", "Lighthouse"),
            ("rezervbase", "Reserve"),
            ("sandbox", "Ground Zero"),
            ("shoreline", "Shoreline"),
            ("tarkovstreets", "Streets of Tarkov"),
            ("woods", "Woods"),
        };

        private static readonly EFikaPlayerPresences[] IdleActivities =
        {
            EFikaPlayerPresences.IN_MENU,
            EFikaPlayerPresences.IN_HIDEOUT,
            EFikaPlayerPresences.IN_FLEA
        };

        public Par4d0xySimulatedRaidService(PresenceService presence)
        {
            _presence = presence;
        }

        public void Start(string sessionId)
        {
            _sessionId = sessionId;
            ScheduleNextIdleTick();
        }

        // Не строгий цикл по кругу (Menu->Hideout->Flea->Menu...), а
        // случайный выбор + случайная длительность — чтобы не выглядело
        // "аймерно"/механически. ~30% шанс "уйти в рейд" на каждом тике.
        private void ScheduleNextIdleTick()
        {
            var activity = IdleActivities[_rng.Next(IdleActivities.Length)];
            Push(activity, null);

            var goToRaid = _rng.Next(100) < 30;
            var delay = goToRaid
                ? TimeSpan.FromSeconds(_rng.Next(20, 90))
                : TimeSpan.FromSeconds(_rng.Next(45, 240));

            _timer = new Timer(_ =>
            {
                if (goToRaid) EnterSimulatedRaid();
                else ScheduleNextIdleTick();
            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        private void EnterSimulatedRaid()
        {
            var location = Locations[_rng.Next(Locations.Length)];
            var raidSeconds = _rng.Next(300, 1501); // 5–25 минут
            var skirmishCount = Math.Max(1, raidSeconds / 300); // стычка раз в ~5 минут, по ТЗ

            var raidInfo = new FikaRaidPresence
            {
                Location = location.Key,
                Side = default, // VERIFY: EFikaSide.Usec/Bear — оба варианта не совпали,
                                 // глянь автодополнением EFikaSide. и пришли реальные имена
                Time = default, // VERIFY: EFikaTime.Day не совпал, аналогично
                Started = true,
                MatchId = new MongoId()
            };

            Push(EFikaPlayerPresences.IN_RAID, raidInfo);
            LogThrottled($"'в рейде': {location.DisplayName}, ~{raidSeconds / 60}мин, стычек ожидается: {skirmishCount}");

            RunNextSkirmish(raidSeconds, skirmishesLeft: skirmishCount, elapsedSeconds: 0, kills: 0);
        }

        // Раз в ~5 минут — стычка: 30% шанс погибнуть в ней (по ТЗ). Победа —
        // +1 килл, сидит в рейде дальше до следующей стычки. Если все стычки
        // пройдены живой — досиживает остаток времени рейда молча и в конце
        // засчитывается как "выжила" с полным временем.
        private void RunNextSkirmish(int totalRaidSeconds, int skirmishesLeft, int elapsedSeconds, int kills)
        {
            if (skirmishesLeft <= 0)
            {
                var remaining = Math.Max(1, totalRaidSeconds - elapsedSeconds);
                _timer = new Timer(_ => FinishRaid(survived: true, kills, totalRaidSeconds), null,
                    TimeSpan.FromSeconds(remaining), Timeout.InfiniteTimeSpan);
                return;
            }

            const int secondsBetweenSkirmishes = 300; // 5 минут, по ТЗ
            _timer = new Timer(_ =>
            {
                var newElapsed = elapsedSeconds + secondsBetweenSkirmishes;
                var died = _rng.Next(100) < 30; // 30% шанс погибнуть в стычке, по ТЗ

                if (died)
                {
                    FinishRaid(survived: false, kills, newElapsed);
                }
                else
                {
                    RunNextSkirmish(totalRaidSeconds, skirmishesLeft - 1, newElapsed, kills + 1);
                }
            }, null, TimeSpan.FromSeconds(secondsBetweenSkirmishes), Timeout.InfiniteTimeSpan);
        }

        private void FinishRaid(bool survived, int kills, int timeInRaidSeconds)
        {
            ApplyRaidOutcome(survived);
            RecordStats(survived, kills, timeInRaidSeconds);

            var cooldownMinutes = _rng.Next(5, 11); // 5–10 минут в схроне после рейда
            Push(EFikaPlayerPresences.IN_HIDEOUT, null);
            LogThrottled($"в схроне ~{cooldownMinutes}мин после рейда ({(survived ? "выжила" : "погибла")})");

            _timer = new Timer(_ => ScheduleNextIdleTick(), null,
                TimeSpan.FromMinutes(cooldownMinutes), Timeout.InfiniteTimeSpan);
        }

        // // VERIFY: точная форма OverallCounters/CounterKeyValue не декомпилена
        // напрямую — беру по общеизвестной схеме профиля Tarkov (список
        // объектов Key(string[])/Value(double), Key вроде ["Kills"] или
        // ["ExitStatus","Survived"]). Если тип не совпадёт — компилятор
        // сразу покажет где именно, C# тут не даст молча записать не туда.
        private void RecordStats(bool survived, int kills, int timeInRaidSeconds)
        {
            try
            {
                var saveServer = ServiceLocator.ServiceProvider.GetService<SaveServer>();
                var profile = saveServer?.GetProfile(_sessionId);
                var pmcData = (BotBase?)profile?.CharacterData?.PmcData;
                var eft = pmcData?.Stats?.Eft;
                if (eft == null) return;

                eft.TotalInGameTime = (eft.TotalInGameTime ?? 0) + timeInRaidSeconds;

                IncrementCounter(eft, new HashSet<string> { "Sessions" }, 1); // VERIFY: похоже именно это двигает "Рейдов" в профиле, но не декомпилено напрямую
                IncrementCounter(eft, new HashSet<string> { "Kills" }, kills);
                IncrementCounter(eft, survived
                    ? new HashSet<string> { "ExitStatus", "Survived" }
                    : new HashSet<string> { "ExitStatus", "Killed" }, 1);
                if (!survived)
                {
                    IncrementCounter(eft, new HashSet<string> { "Deaths" }, 1);
                }

                saveServer!.SaveProfileAsync(_sessionId).GetAwaiter().GetResult();
                Console.WriteLine($"[par4d0xy] Статистика записана: убийств +{kills}, " +
                                   $"{(survived ? "выжила" : "погибла")}, время +{timeInRaidSeconds / 60}мин");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[par4d0xy] Не удалось записать статистику: {ex.Message}");
            }
        }

        private static void IncrementCounter(EftStats eft, HashSet<string> key, double amount)
        {
            var items = eft.OverallCounters?.Items;
            if (items == null) return;

            var existing = items.FirstOrDefault(c => c.Key != null && c.Key.SetEquals(key));
            if (existing != null)
            {
                existing.Value = (existing.Value ?? 0) + amount;
            }
            else
            {
                items.Add(new CounterKeyValue { Key = key, Value = amount });
            }
        }

        // Слоты, которые снимаются при "гибели" — оружие и основной обвес.
        // Карманы/жетон/каску-подсумки не трогаем, чтобы не выглядело совсем
        // абсурдно (человек хотя бы в носках, а не буквально без ничего).
        private static readonly HashSet<string> StrippedOnDeathSlots = new()
        {
            "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
            "ArmorVest", "TacticalVest", "Headwear", "Backpack",
            "FaceCover", "Earpiece", "Eyewear"
        };

        private void ApplyRaidOutcome(bool survived)
        {
            if (survived) return; // выжила — гир как есть, ничего не трогаем

            try
            {
                var saveServer = ServiceLocator.ServiceProvider.GetService<SaveServer>();
                var profile = saveServer?.GetProfile(_sessionId);
                var pmcData = (BotBase?)profile?.CharacterData?.PmcData;
                if (pmcData?.Inventory?.Equipment == null) return;

                var equipmentId = (string)pmcData.Inventory.Equipment.Value;
                var toStrip = pmcData.Inventory.Items
                    .Where(i => i.ParentId == equipmentId && i.SlotId != null && StrippedOnDeathSlots.Contains(i.SlotId))
                    .ToList();

                foreach (var item in toStrip)
                {
                    RemoveSubtreeById((string)item.Id, pmcData.Inventory.Items);
                    pmcData.Inventory.Items.Remove(item);
                }

                // Тот самый метод из LocationLifecycleService.EndLocalRaid —
                // реально пишет изменения в user/profiles/{id}.json на диск.
                saveServer!.SaveProfileAsync(_sessionId).GetAwaiter().GetResult();

                Console.WriteLine($"[par4d0xy] Погибла (симуляция) — снято предметов: {toStrip.Count}, сохранено в профиль.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[par4d0xy] Не удалось применить исход рейда: {ex.Message}");
            }
        }

        private static void RemoveSubtreeById(string itemId, List<Item> items)
        {
            var children = items.Where(i => i.ParentId == itemId).ToList();
            foreach (var child in children)
            {
                RemoveSubtreeById((string)child.Id, items);
                items.Remove(child);
            }
        }

        private void Push(EFikaPlayerPresences activity, FikaRaidPresence? raidInfo)
        {
            lock (_lock)
            {
                _presence.UpdatePlayerPresence(_sessionId, new FikaSetPresence
                {
                    Activity = activity,
                    RaidInformation = raidInfo
                });
            }
        }

        // Раз в 5 минут максимум — чтобы не заспамить консоль сервера.
        private void LogThrottled(string message)
        {
            if (DateTime.UtcNow - _lastLoggedAt < TimeSpan.FromMinutes(5)) return;
            _lastLoggedAt = DateTime.UtcNow;
            Console.WriteLine($"[par4d0xy] {message}");
        }

        public void Dispose() => _timer?.Dispose();
    }
}

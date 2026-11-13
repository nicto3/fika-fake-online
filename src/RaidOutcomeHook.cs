using System;

namespace Par4d0xyBot
{
    /// <summary>
    /// Определяет: пережил par4d0xy рейд или нет, и обновляет тайник.
    ///
    /// // VERIFY: событие конца рейда в server-csharp — раньше (TS-версия) это было
    /// что-то вроде IMatchController / EndOfflineRaidCallbacks / SaveLoadRouter,
    /// в C#-ревизии ищи через IDE: "RaidEnd", "EndLocalRaid", "MatchController"
    /// внутри SPTarkov.Server.Core. Нужен именно серверный колбэк конца рейда,
    /// который получает список ботов и их состояние (живы/мертвы), а не клиентский.
    ///
    /// Поскольку хостишь ты — этот колбэк гарантированно отработает на твоём
    /// процессе сервера, независимо от того, кто из друзей нанёс килл.
    /// </summary>
    // [Injectable]
    public class RaidOutcomeHook
    {
        private readonly LoadoutStore _store;

        public RaidOutcomeHook(LoadoutStore store)
        {
            _store = store;
        }

        /// <summary>
        /// botIsAlive — итог по нашему боту на конец рейда (true = дожил/ушёл, false = убит).
        /// currentGear — фактическое снаряжение бота на момент конца рейда
        /// (с учётом того, что он подобрал/потерял по пути — если такое отслеживание
        /// вообще нужно; можно и просто сохранять исходный гир без изменений).
        /// </summary>
        public void OnRaidEnded(bool botIsAlive, EquipmentSet? currentGear)
        {
            var state = _store.Load();

            if (botIsAlive)
            {
                _store.ApplySurvival(state, currentGear ?? state.Equipment);
                Console.WriteLine("[par4d0xy] Выжил. Гир сохранён на следующий рейд.");
            }
            else
            {
                _store.ApplyDeath(state);
                Console.WriteLine("[par4d0xy] Погиб. В следующий рейд идёт налегке.");
            }

            _store.Save(state);
        }
    }
}

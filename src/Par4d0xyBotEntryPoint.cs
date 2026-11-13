using System;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Reflection.Patching;
using FikaServer.Services;

namespace Par4d0xyBot
{
    /// <summary>
    /// Реальная точка входа мода. Без класса, реализующего IOnLoad, ничего из
    /// нашего кода никогда не запустится — DI-контейнер просто не будет знать,
    /// когда его создавать. Паттерн подтверждён по нескольким живым модам
    /// (WTT-CommonLib, MoreBotsAPI) под этот же C#-сервер.
    /// </summary>
    [Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
    public class Par4d0xyBotEntryPoint : IOnLoad
    {
        private readonly PresenceService _fikaPresenceService;
        private Par4d0xySimulatedRaidService? _simulatedRaidService;

        public Par4d0xyBotEntryPoint(PresenceService fikaPresenceService)
        {
            _fikaPresenceService = fikaPresenceService;
        }

        public Task OnLoad()
        {
            // Тот же паттерн, что PatchManager у ABPS: создаём патч и включаем.
            ((AbstractPatch)new Par4d0xyBotGeneratorPatch()).Enable();

            // Не блокируем сам OnLoad — SaveServer на этом этапе ещё может не
            // успеть загрузить профили с диска, поэтому регистрируем в фоне
            // с повтором, пока не получится.
            _ = RegisterWithRetryAsync("6a788d036ca4e914d8f449cd");
            return Task.CompletedTask;
        }

        private async Task RegisterWithRetryAsync(string sessionId)
        {
            while (true)
            {
                try
                {
                    // НАЙДЕНО ЧЕРЕЗ ДЕКОМПИЛЯЦИЮ FikaServer.dll: UpdatePlayerPresence
                    // тихо ничего не делает, если сессии нет в словаре _onlinePlayers.
                    // AddPlayerPresence — вот что реально кладёт её туда первый раз.
                    _fikaPresenceService.AddPlayerPresence(sessionId);

                    _simulatedRaidService = new Par4d0xySimulatedRaidService(_fikaPresenceService);
                    _simulatedRaidService.Start(sessionId);

                    Console.WriteLine("[par4d0xy] Зарегистрирована в presence успешно, симуляция запущена");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[par4d0xy] Профиль ещё не загружен ({ex.Message}), повтор через 2 сек");
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }
    }
}

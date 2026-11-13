# Par4d0xyBot — мод для SPT 4.0.13

Идея: постоянный NPC-бот с ником `par4d0xy`, снаряга которого хранится
в собственном "тайнике" (JSON-файл мода), а не в обычном профиле игрока.

- Выжил в рейде → гир на конце рейда сохраняется, в следующий рейд он идёт с ним.
- Погиб → тайник обнуляется до `nakedFallback`, следующий рейд — налегке.
- Работает как чистый **серверный мод** (не нужен BepInEx) — жизнь/смерть
  бота решает твой SPT-сервер, который крутится у тебя на ПК как хоста.
- Совместимость с ABPS/APBS: наш хук должен применяться **последним**,
  после того как они отработали спавн/шмот — тогда конфликтов не будет,
  мы просто переписываем финальный инвентарь одного конкретного бота.

## Что уже реально готово и рабочее

- `LoadoutStore.cs` — чтение/запись тайника, обработка "умер/выжил".
  Не зависит от SPT SDK, можно юнит-тестить отдельно.
- `StatusCycleService.cs` — фоновый цикл статуса (Online → Stash → Flea →
  InRaid), пишет `data/status.json`. Рабочий уже сейчас.
- `data/par4d0xy_loadout.json` — стартовый тайник, отредактируй под себя.

## Что нужно доделать руками (или следующим шагом со мной)

Помечено `// VERIFY` в коде:

1. **ForcedSpawnHook.cs** — найти в `SPTarkov.Server.Core` актуальный
   сервис генерации волны ботов (искать через IDE: `IBotGenerator`,
   `BotGenerationService`). Нужно, чтобы наш код мог:
   - гарантировать присутствие par4d0xy в волне,
   - сработать ПОСЛЕ APBS/ABPS и перезаписать его инвентарь.
2. **RaidOutcomeHook.cs** — найти актуальный серверный колбэк конца рейда
   (искать: `RaidEnd`, `EndLocalRaid`, `MatchController`) который отдаёт
   список ботов и их состояние живой/мёртвый.
3. **[Injectable] регистрация** — сверить с любым мод-примером из
   `sp-tarkov/server-mod-examples`, там актуальный паттерн DI для 4.0.x.
4. Версии NuGet-пакетов в `.csproj` — поставить те, что реально стоят
   в твоей папке `SPT/user/mods` / referenced assemblies.

## Почему я не дописал эти 4 пункта сам

Сервер SPT переехал на C# совсем недавно, и точные имена классов/методов
в версии 4.0.13 я не могу гарантировать не глядя в твою реальную сборку —
рискую подсунуть нерабочий код с уверенным видом. Быстрее всего: открой
`SPTarkov.Server.Core.dll` в IDE (Rider/VS — "Go to Definition" или
Object Browser), найди эти два хук-поинта, скинь мне сигнatures — я
доделаю оставшиеся 4 файла точно под них за один проход.

## Fika-статус в списке друзей — теперь по-настоящему

Нашёл реальный API (Fika-Server-CSharp, актуальная версия под 4.0.13):

- `FikaServer/Callbacks/PresenceCallbacks.cs` — обрабатывает
  `/fika/presence/get|set|setget`, внутри дёргает
  `PresenceService.UpdatePlayerPresence(sessionID, FikaSetPresence)`.
- `FikaServer/Services/PresenceService.cs` — сам сервис, `[Injectable]`,
  то есть **можно попросить его у DI-контейнера прямо в нашем моде**
  конструктором — это `Par4d0xyPresenceHook.cs`.
- `FikaSetPresence` = `{ Activity: EFikaPlayerPresences, RaidInformation: FikaRaidPresence? }`.

Это значит: никакого фейкового файла и никакого клиентского патча не
нужно — мы просто вызываем ТОТ ЖЕ метод, который вызывается когда
настоящий игрок обновляет статус. Для Fika это будет неотличимо от
настоящего "игрок в сети".

### Что доделать руками (2 пункта, быстро):

1. **Имена значений `EFikaPlayerPresences`** — я поставил Online/
   Hideout/Marketplace/InRaid по смыслу по аналогии, но точные названия
   в enum'е могут отличаться. Открой
   `FikaServer.Models.Enums.EFikaPlayerPresences` в IDE
   (Go To Definition от поля `Activity`) и поправь `IdleCycle` в
   `Par4d0xyPresenceHook.cs`.
2. **Ссылка на `FikaServer.dll`** в `.csproj` — она лежит в папке, куда
   у тебя установлен Fika-Server-CSharp (`SPT/user/mods/fika-server/...`
   или как она у тебя называется), добавь `<Reference>` с `HintPath` на
   неё.
3. **`par4d0xySessionId`** — это `_id` реального профиля par4d0xy.
   Пока просто скопируй руками из `SPT/user/profiles/*.json` после
   создания профиля через лаунчер. Потом можно автоматизировать поиск
   по нику через `SaveServer`.

Источники (открыты, публичный код, не архивная TS-версия — она
устарела и заброшена 11 окт. 2025):
- https://github.com/project-fika/Fika-Server-CSharp
- https://github.com/project-fika/fika-server-csharp/blob/72d38506/FikaServer/Callbacks/PresenceCallbacks.cs
- https://github.com/project-fika/fika-server-csharp/blob/72d38506/FikaServer/Models/Fika/Presence/FikaSetPresence.cs

## Следующий большой кусок — настоящий профиль-"тайник"

Отдельно ещё нужно: после `RaidOutcomeHook.OnRaidEnded` писать не
только в наш `LoadoutStore`, а ещё и прямо в JSON настоящего профиля
par4d0xy (`SaveServer` / `SPT/user/profiles/{id}.json`, секция
`characters.pmc.Inventory.items`), чтобы друг, зайдя на аккаунт через
лаунчер, реально видел бардак в стеше. Это следующий шаг — предметно
сделаю, когда будет ссылка на профиль/структуру инвентаря из твоего
дампа (или я сам гляну актуальную схему `PmcData.Inventory` в
`SPTarkov.Server.Core.Models`).

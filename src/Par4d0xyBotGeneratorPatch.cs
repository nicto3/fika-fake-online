using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Servers;

namespace Par4d0xyBot
{
    /// <summary>
    /// Harmony-патч (паттерн ABPS) на BotGenerator.GenerateBot.
    ///
    /// ВАЖНОЕ ИЗМЕНЕНИЕ: раньше гир брался из отдельного JSON (LoadoutStore),
    /// который так и остался пустым. Теперь гир копируется НАПРЯМУЮ из её
    /// настоящего профиля (SptProfile.CharacterData.PmcData.Inventory) — то
    /// есть буквально то, что надето на персонаже, когда заходишь на её
    /// аккаунт через лаунчер и одеваешь её руками через экран персонажа.
    /// Копируется с вложенными предметами (магазин/патроны/обвес), а не
    /// только верхний слой — иначе оружие могло достаться без патронов.
    /// </summary>
    public class Par4d0xyBotGeneratorPatch : AbstractPatch
    {
        private const string Par4d0xySessionId = "6a788d036ca4e914d8f449cd";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGenerator), "GenerateBot", null, null);
        }

        // Окно в 3 минуты вместо разового флага — чтобы claim сбрасывался
        // между рейдами (пауза в меню/лаунчере), а не только 1 раз за весь
        // процесс сервера.
        private static DateTime _lastClaimedAt = DateTime.MinValue;
        private static readonly object ClaimLock = new();

        [PatchPostfix]
        public static void Postfix(BotBase __result, BotGenerationDetails botGenerationDetails)
        {
            if (__result == null) return;
            if (!botGenerationDetails.IsPmc || botGenerationDetails.IsPlayerScav) return;

            lock (ClaimLock)
            {
                if (DateTime.UtcNow - _lastClaimedAt < TimeSpan.FromMinutes(3)) return;
                _lastClaimedAt = DateTime.UtcNow;
            }

            ApplyPar4d0xyIdentity(__result);
        }

        private static void ApplyPar4d0xyIdentity(BotBase bot)
        {
            bot.Info.Nickname = "par4d0xy";
            bot.Info.LowerNickname = "par4d0xy";

            var pmcData = GetHerRealPmcData();
            if (pmcData == null)
            {
                Console.WriteLine("[par4d0xy] Не удалось прочитать её реальный профиль — осталась без подмены гира.");
                return;
            }

            if (pmcData.Info?.Level != null)
            {
                bot.Info.Level = pmcData.Info.Level;
            }

            // Сторона у настоящего аккаунта фиксирована раз и навсегда —
            // подтягиваем её, вместо случайной BEAR/USEC каждый рейд.
            if (!string.IsNullOrEmpty(pmcData.Info?.Side))
            {
                bot.Info.Side = pmcData.Info.Side;
            }

            // Внешность (лицо/тело/руки/ноги) — та самая, что выбрана при
            // создании персонажа, а не случайная модель от ABPS/ваниль.
            if (pmcData.Customization != null && bot.Customization != null)
            {
                bot.Customization.Head = pmcData.Customization.Head;
                bot.Customization.Body = pmcData.Customization.Body;
                bot.Customization.Feet = pmcData.Customization.Feet;
                bot.Customization.Hands = pmcData.Customization.Hands;
            }

            ApplyRealGearFromProfile(bot, pmcData);

            Console.WriteLine("[par4d0xy] Заспавнена в рейде с реальным гиром из её профиля (Harmony postfix).");
        }

        private static BotBase? GetHerRealPmcData()
        {
            var saveServer = ServiceLocator.ServiceProvider.GetService<SaveServer>();
            var profile = saveServer?.GetProfile(Par4d0xySessionId);
            return (BotBase?)profile?.CharacterData?.PmcData;
        }

        private static void ApplyRealGearFromProfile(BotBase bot, BotBase herPmcData)
        {
            if (herPmcData.Inventory?.Equipment == null || bot.Inventory?.Equipment == null) return;

            var herEquipmentId = (string)herPmcData.Inventory.Equipment.Value;
            var botEquipmentId = (string)bot.Inventory.Equipment.Value;
            var herItems = herPmcData.Inventory.Items;

            // Всё, что надето ПРЯМО в корень экипировки — оружие, броня,
            // разгрузка, шлем, рюкзак и т.д. Именно то, что видно "на теле".
            var directlyEquipped = herItems.Where(i => i.ParentId == herEquipmentId).ToList();

            var copiedSlots = 0;
            foreach (var rootItem in directlyEquipped)
            {
                var slotId = rootItem.SlotId;
                if (string.IsNullOrEmpty(slotId)) continue;

                RemoveSubtree(bot.Inventory.Items, slotId, botEquipmentId);
                CopySubtree(rootItem, botEquipmentId, slotId, herItems, bot.Inventory.Items);
                copiedSlots++;
            }

            Console.WriteLine($"[par4d0xy] Скопировано слотов гира: {copiedSlots}");
        }

        private static void RemoveSubtree(List<Item> items, string slotId, string parentId)
        {
            var root = items.FirstOrDefault(i => i.SlotId == slotId && i.ParentId == parentId);
            if (root == null) return;
            RemoveSubtreeById((string)root.Id, items);
            items.Remove(root);
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

        // Рекурсивно клонирует предмет и всё что в нём/на нём вложено
        // (магазин -> патроны, оружие -> прицел/цевье/магазин и т.д.),
        // выдавая всем копиям новые Id, но сохраняя структуру связей.
        private static void CopySubtree(Item sourceRoot, string newParentId, string? overrideSlotId,
            List<Item> sourceItems, List<Item> destItems)
        {
            var newId = new MongoId();
            destItems.Add(new Item
            {
                Id = newId,
                Template = sourceRoot.Template,
                ParentId = newParentId,
                SlotId = overrideSlotId ?? sourceRoot.SlotId,
                Upd = sourceRoot.Upd // сохраняем прочность/патроны в магазине/etc как есть
            });

            var children = sourceItems.Where(i => i.ParentId == (string)sourceRoot.Id).ToList();
            foreach (var child in children)
            {
                CopySubtree(child, (string)newId, null, sourceItems, destItems);
            }
        }

        public Par4d0xyBotGeneratorPatch() : base(null)
        {
        }
    }
}

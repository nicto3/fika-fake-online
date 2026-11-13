using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;

namespace Par4d0xyBot
{
    // Снаряжение одного слота/группы. Здесь просто список tpl-id вещей.
    // При интеграции с реальным SPT-инвентарём ты замапишь это на Item[]
    // (id/tpl/parentId/slotId), но само хранилище (чтение/запись файла) уже готово.
    public class LoadoutState
    {
        [JsonPropertyName("nickname")]
        public string Nickname { get; set; } = "par4d0xy";

        [JsonPropertyName("alive")]
        public bool Alive { get; set; } = true;

        [JsonPropertyName("lastOutcome")]
        public string LastOutcome { get; set; } = "none"; // none | survived | died

        [JsonPropertyName("raidsPlayed")]
        public int RaidsPlayed { get; set; }

        [JsonPropertyName("raidsSurvived")]
        public int RaidsSurvived { get; set; }

        [JsonPropertyName("equipment")]
        public EquipmentSet Equipment { get; set; } = new();

        [JsonPropertyName("nakedFallback")]
        public EquipmentSet NakedFallback { get; set; } = new();
    }

    public class EquipmentSet
    {
        [JsonPropertyName("Weapon")] public string? Weapon { get; set; }
        [JsonPropertyName("ArmorVest")] public string? ArmorVest { get; set; }
        [JsonPropertyName("TacticalVest")] public string? TacticalVest { get; set; }
        [JsonPropertyName("Headwear")] public string? Headwear { get; set; }
        [JsonPropertyName("Backpack")] public string? Backpack { get; set; }
        [JsonPropertyName("Items")] public System.Collections.Generic.List<string> Items { get; set; } = new();
    }

    /// <summary>
    /// Читает/пишет data/par4d0xy_loadout.json.
    /// Помечен [Injectable] чтобы DI мог сам создавать этот класс и
    /// подставлять его другим сервисам (Par4d0xyBotGenerator и т.д.) —
    /// без атрибута DI выдаёт "Unable to resolve service" при старте.
    /// Путь к data/ вычисляем от расположения самой сборки (Par4d0xyBot.dll
    /// лежит в user/mods/par4d0xy-bot/ вместе с data/ — DI не может
    /// подставить голую string, поэтому конструктор больше её не просит).
    /// </summary>
    [Injectable]
    public class LoadoutStore
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public LoadoutStore()
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                               ?? throw new InvalidOperationException("Не удалось определить папку сборки Par4d0xyBot.dll");
            _path = Path.Combine(assemblyDir, "data", "par4d0xy_loadout.json");
        }

        public LoadoutState Load()
        {
            if (!File.Exists(_path))
                throw new FileNotFoundException($"par4d0xy_loadout.json не найден: {_path}");

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<LoadoutState>(json)
                   ?? throw new InvalidDataException("Не удалось распарсить par4d0xy_loadout.json");
        }

        public void Save(LoadoutState state)
        {
            var json = JsonSerializer.Serialize(state, JsonOpts);
            File.WriteAllText(_path, json);
        }

        /// Вызывается когда сервер решил, что par4d0xy погиб в рейде.
        public void ApplyDeath(LoadoutState state)
        {
            state.Alive = true; // жив в мета-смысле (профиль остаётся), просто без вещей
            state.LastOutcome = "died";
            state.RaidsPlayed++;
            state.Equipment = new EquipmentSet
            {
                Weapon = state.NakedFallback.Weapon,
                ArmorVest = state.NakedFallback.ArmorVest,
                TacticalVest = state.NakedFallback.TacticalVest,
                Headwear = state.NakedFallback.Headwear,
                Backpack = state.NakedFallback.Backpack,
                Items = new System.Collections.Generic.List<string>(state.NakedFallback.Items)
            };
        }

        /// Вызывается когда par4d0xy пережил рейд (дожил до конца / вышел живым).
        /// currentGear — то, что было на нём по факту на момент конца рейда (с учётом лута).
        public void ApplySurvival(LoadoutState state, EquipmentSet currentGear)
        {
            state.LastOutcome = "survived";
            state.RaidsPlayed++;
            state.RaidsSurvived++;
            state.Equipment = currentGear;
        }
    }
}

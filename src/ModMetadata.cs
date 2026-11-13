using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;

namespace Par4d0xyBot
{
    /// <summary>
    /// Обязательный класс метаданных. Из твоего же SPTarkov_Server_Core.xml
    /// (doc-комментарий на AbstractModMetadata):
    /// "This record is required to be overridden by all mods. All properties
    /// must be overridden. For properties, that you don't need, just assign null."
    /// Раньше я переопределил только 6 из 11 — вот полный список.
    /// </summary>
    public record ModMetadata : AbstractModMetadata
    {
        public override string ModGuid { get; init; } = "com.par4d0xy.raidbot";

        // Только буквы и цифры (без пробелов/точек) — так требуют гайдлайны сервера.
        public override string Name { get; init; } = "Par4d0xyBot";

        public override string Author { get; init; } = "par4d0xy";

        public override List<string>? Contributors { get; init; } = null;

        public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");

        // "~4.0.13" = патчи 4.0.x от 4.0.13 и выше, но не 4.1.0.
        public override Range SptVersion { get; init; } = new("~4.0.13");

        public override List<string>? Incompatibilities { get; init; } = null;

        public override Dictionary<string, Range>? ModDependencies { get; init; } = null;

        public override string? Url { get; init; } = null;

        public override bool? IsBundleMod { get; init; } = null;

        public override string License { get; init; } = "MIT";
    }
}

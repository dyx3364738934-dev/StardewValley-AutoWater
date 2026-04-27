using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace AutoWater
{
    /// <summary>
    /// Mod配置类
    /// </summary>
    public class ModConfig
    {
        public bool EnableAutoWater { get; set; } = true;
        public bool ShowNotification { get; set; } = true;
        public bool OnlyWaterCrops { get; set; } = true;
        public bool WaterGreenhouse { get; set; } = true;
        public bool WaterGingerIsland { get; set; } = true;
        public bool WaterGardenPots { get; set; } = true;
    }

    /// <summary>
    /// 自动浇水 Mod
    /// </summary>
    public class ModEntry : Mod
    {
        private ModConfig config = null!;

        private static readonly HashSet<string> GingerIslandLocations = new()
        {
            "IslandWest", "IslandWestCave1", "IslandNorth", "IslandNorthCave1",
            "IslandEast", "IslandSouth", "IslandSouthEast", "IslandSouthEastCave",
            "IslandGreenhouse", "IslandFarmHouse"
        };

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!config.EnableAutoWater || !Context.IsWorldReady)
                return;

            int total = 0;

            // 普通位置（农场、温室等）
            foreach (var location in GetActiveLocations())
                total += WaterLocation(location);

            // 姜岛
            if (config.WaterGingerIsland)
                total += WaterGingerIsland();

            // 播报
            if (total > 0 && config.ShowNotification)
                Game1.addHUDMessage(new HUDMessage($"自动浇灌了 {total} 格作物", HUDMessage.newQuest_type) { noIcon = true, timeLeft = 3000f });
        }

        private IEnumerable<GameLocation> GetActiveLocations()
            => Context.IsMultiplayer ? Helper.Multiplayer.GetActiveLocations() : Game1.locations;

        private int WaterGingerIsland()
        {
            int total = 0;
            foreach (var name in GingerIslandLocations)
            {
                var location = Game1.getLocationFromName(name);
                if (location != null && ShouldWaterLocation(location))
                    total += WaterTerrain(location);
            }
            return total;
        }

        private int WaterLocation(GameLocation location)
            => ShouldWaterLocation(location) ? WaterTerrain(location) : 0;

        private int WaterTerrain(GameLocation location)
        {
            int count = 0;

            // 耕地
            if (location.terrainFeatures != null)
            {
                foreach (var pair in location.terrainFeatures.Pairs)
                {
                    if (pair.Value is HoeDirt dirt)
                    {
                        if (config.OnlyWaterCrops && dirt.crop == null)
                            continue;
                        if (dirt.state.Value != HoeDirt.watered)
                        {
                            dirt.state.Value = HoeDirt.watered;
                            count++;
                        }
                    }
                }
            }

            // 花园盆栽
            if (config.WaterGardenPots && location.objects != null)
            {
                foreach (var pair in location.objects.Pairs)
                {
                    if (pair.Value is IndoorPot pot && pot.hoeDirt.Value?.crop != null && pot.hoeDirt.Value.state.Value != HoeDirt.watered)
                    {
                        pot.hoeDirt.Value.state.Value = HoeDirt.watered;
                        count++;
                    }
                }
            }

            return count;
        }

        private bool ShouldWaterLocation(GameLocation location)
        {
            string name = location.Name ?? "";

            // 姜岛
            if (GingerIslandLocations.Contains(name))
                return name == "IslandGreenhouse" ? config.WaterGingerIsland && config.WaterGreenhouse : config.WaterGingerIsland;

            // 雨天户外
            if (Game1.isRaining && location.IsOutdoors && !location.IsGreenhouse)
                return false;

            // 温室
            if (location.IsGreenhouse)
                return config.WaterGreenhouse;

            // 农场或户外
            return location.IsFarm || location.IsOutdoors;
        }
    }
}
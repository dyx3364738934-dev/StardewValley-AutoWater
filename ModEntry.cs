using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

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
        /// <summary>水壶吸水时触发全自动浇水</summary>
        public bool EnableWaterCanRefillTrigger { get; set; } = true;
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

        // 水壶水量追踪（用于吸水触发）
        //   - -1   = 未初始化（玩家未持有水壶）
        //   - 0..N = 已追踪
        private int lastWaterLevel = -1;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;

            Monitor.Log($"AutoWater 已加载 (吸水触发={(config.EnableWaterCanRefillTrigger ? "启用" : "禁用")})", LogLevel.Info);
        }

        // ===== 每日触发 =====
        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!config.EnableAutoWater || !Context.IsWorldReady)
                return;

            WaterAllLocations();
        }

        // ===== 吸水触发 =====
        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !config.EnableAutoWater || !config.EnableWaterCanRefillTrigger)
            {
                lastWaterLevel = -1;
                return;
            }

            var player = Game1.player;
            if (player?.CurrentTool is not WateringCan can)
            {
                lastWaterLevel = -1;
                return;
            }

            int currentLevel = can.WaterLeft;

            if (lastWaterLevel < 0)
            {
                // 第一次检测到水壶：初始化追踪值，跳过本次检测
                lastWaterLevel = currentLevel;
                return;
            }

            // 水量增加 → 吸水/回水了
            if (currentLevel > lastWaterLevel)
            {
                int watered = WaterAllLocations("吸水时");
                Monitor.Log($"吸水触发浇水: {watered} 格 (水量 {lastWaterLevel}→{currentLevel})", LogLevel.Trace);
            }

            lastWaterLevel = currentLevel;
        }

        // ===== 统一浇水逻辑（返回浇灌格数） =====
        private int WaterAllLocations(string prefix = "")
        {
            int total = 0;

            foreach (var location in GetActiveLocations())
                total += WaterLocation(location);

            if (config.WaterGingerIsland)
                total += WaterGingerIsland();

            if (total > 0)
            {
                string msg = string.IsNullOrEmpty(prefix)
                    ? $"自动浇灌了 {total} 格作物"
                    : $"{prefix}自动浇灌了 {total} 格作物";

                if (config.ShowNotification)
                {
                    var hudMsg = new HUDMessage(msg) { timeLeft = 3000f };
                    // 显示金水壶图标
                    hudMsg.messageSubject = ItemRegistry.Create("(T)GoldWateringCan");
                    Game1.addHUDMessage(hudMsg);
                }

                Monitor.Log(msg, LogLevel.Trace);
            }

            return total;
        }

        // ===== 浇地核心方法 =====
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
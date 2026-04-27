using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace LinkedChests
{
    public interface ILinkedChestsApi
    {
        void TriggerSort(Chest chest);
    }

    public class LinkedChestsApiImpl : ILinkedChestsApi
    {
        public void TriggerSort(Chest chest)
        {
            if (!ModEntry.Instance.Config.EnableLinkedSort)
                return;

            var linked = ChestLinker.FindLinkedChests(chest, Game1.player.currentLocation);
            if (linked.Count > 1)
                ChestLinker.DoLinkedSort(linked);
        }
    }

    public class ModConfig
    {
        public bool EnableLinkedSort { get; set; } = true;
        public bool ShowNotification { get; set; } = true;
    }

    public class ModEntry : Mod
    {
        internal static ModEntry Instance { get; private set; } = null!;
        internal ModConfig Config { get; private set; } = null!;
        private Harmony? harmony;

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Config = helper.ReadConfig<ModConfig>();

            harmony = new Harmony("Koko.LinkedChests");

            // 手动打补丁（避免 PatchAll 因参数类型不匹配而静默失败）
            HarmonyPatches.Apply(harmony);

            Monitor.Log("Linked Chests 已加载！相邻箱子一键联排功能已激活。", LogLevel.Info);
        }

        public override object? GetApi()
        {
            return new LinkedChestsApiImpl();
        }
    }
}

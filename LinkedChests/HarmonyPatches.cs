using System;
using System.Linq;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace LinkedChests
{
    /// <summary>
    /// Harmony 补丁：在 ItemGrabMenu.receiveLeftClick 之后拦截整理按钮，
    /// 对链接箱子组执行跨箱子智能重分配（Postfix 方式，不干扰原版逻辑）
    /// </summary>
    internal static class HarmonyPatches
    {
        public static bool IsPatched { get; private set; }

        public static void Apply(Harmony harmony)
        {
            var targetType = typeof(ItemGrabMenu);

            // 尝试多个参数签名（优先3参数版，兜底2参数版）
            var paramSets = new[]
            {
                new Type[] { typeof(int), typeof(int), typeof(bool) },
                new Type[] { typeof(int), typeof(int) },
            };

            foreach (var paramSet in paramSets)
            {
                try
                {
                    var method = AccessTools.Method(targetType, "receiveLeftClick", paramSet);
                    if (method != null)
                    {
                        var postfix = new HarmonyMethod(
                            typeof(HarmonyPatches),
                            nameof(Postfix_ReceiveLeftClick));

                        harmony.Patch(method, postfix: postfix);

                        ModEntry.Instance.Monitor.Log(
                            $"[Harmony] 补丁已应用(Postfix): ItemGrabMenu.receiveLeftClick({string.Join(", ", paramSet.Select(p => p.Name))})",
                            StardewModdingAPI.LogLevel.Info);
                        IsPatched = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log(
                        $"[Harmony] 尝试补丁失败 ({paramSet.Length}参数): {ex.Message}",
                        StardewModdingAPI.LogLevel.Debug);
                }
            }

            // 兜底：不指定参数类型
            try
            {
                var method = AccessTools.Method(targetType, "receiveLeftClick");
                if (method != null)
                {
                    var postfix = new HarmonyMethod(
                        typeof(HarmonyPatches),
                        nameof(Postfix_ReceiveLeftClick));
                    harmony.Patch(method, postfix: postfix);

                    ModEntry.Instance.Monitor.Log(
                        "[Harmony] 补丁已应用(Postfix): ItemGrabMenu.receiveLeftClick (无参数约束)",
                        StardewModdingAPI.LogLevel.Info);
                    IsPatched = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"[Harmony] 通用补丁失败: {ex.Message}",
                    StardewModdingAPI.LogLevel.Debug);
            }

            ModEntry.Instance.Monitor.Log(
                "[Harmony] ⚠️ 未能找到 receiveLeftClick 方法！整理按钮联排功能不可用。",
                StardewModdingAPI.LogLevel.Warn);
        }

        /// <summary>
        /// Postfix：原版整理执行完毕后，叠加联排整理
        /// 不返回任何值，不会干扰原版逻辑
        /// </summary>
        private static void Postfix_ReceiveLeftClick(
            object __instance, int x, int y)
        {
            // ── 检测：是不是整理按钮点击？ ──
            var organizeButton = AccessTools.Field(typeof(ItemGrabMenu), "organizeButton")
                ?.GetValue(__instance) as ClickableTextureComponent;

            if (organizeButton == null || !organizeButton.containsPoint(x, y))
                return; // 不是整理按钮点击

            // ── 检测：是不是箱子界面？ ──
            var context = AccessTools.Field(typeof(ItemGrabMenu), "context")
                ?.GetValue(__instance);

            if (context is not Chest chest)
                return; // 不是箱子（出货箱、商店等）

            // ── 功能开关 ──
            if (!ModEntry.Instance.Config.EnableLinkedSort)
                return;

            // ── 查找链接组 ──
            var linkedChests = ChestLinker.FindLinkedChests(chest, Game1.player.currentLocation);

            if (linkedChests.Count <= 1)
                return; // 没有邻居，不需要联排

            // ── 执行链接整理 ──
            try
            {
                ChestLinker.DoLinkedSort(linkedChests);

                if (ModEntry.Instance.Config.ShowNotification)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        $"整理了 {linkedChests.Count} 个链接箱子", HUDMessage.newQuest_type)
                    {
                        noIcon = true,
                        timeLeft = 2000f
                    });
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"链接整理出错: {ex}", StardewModdingAPI.LogLevel.Error);
            }
        }
    }
}

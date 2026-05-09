using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace LinkedChests
{
    /// <summary>
    /// Harmony 补丁：整理按钮联排 + 工作台全场景覆盖
    /// </summary>
    internal static class HarmonyPatches
    {
        public static bool IsPatched { get; private set; }

        // 运行时反射缓存的 CraftingPage 字段信息
        private static FieldInfo? _craftingListField;

        public static void Apply(Harmony harmony)
        {
            // ════════════════════════════════════════════
            // 补丁 1：整理按钮 → 跨箱子联排整理
            // ════════════════════════════════════════════
            ApplyOrganizeButtonPatch(harmony);

            // ════════════════════════════════════════════
            // 补丁 2：工作台 → 当前场景箱子全覆盖
            // ════════════════════════════════════════════
            ApplyWorkbenchPatch(harmony);

            // 运行时发现 CraftingPage 中的箱子列表字段
            DiscoverCraftingField();
        }

        private static void DiscoverCraftingField()
        {
            try
            {
                var type = typeof(CraftingPage);
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!f.FieldType.IsGenericType) continue;
                    var gen = f.FieldType.GetGenericTypeDefinition();
                    if (gen != typeof(List<>)) continue;
                    var elem = f.FieldType.GetGenericArguments()[0];
                    // SDV 1.6 中 _materialContainers 类型为 List<IInventory>
                    if (elem == typeof(IInventory) || elem == typeof(Chest))
                    {
                        _craftingListField = f;
                        ModEntry.Instance.Monitor.Log(
                            $"[Harmony] 发现 CraftingPage 箱子列表字段: {f.Name} ({elem.Name})",
                            StardewModdingAPI.LogLevel.Info);
                        return;
                    }
                }
                ModEntry.Instance.Monitor.Log(
                    "[Harmony] ⚠️ 未发现 CraftingPage 箱子列表字段，工作台功能不可用",
                    StardewModdingAPI.LogLevel.Warn);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"[Harmony] 发现 CraftingPage 字段失败: {ex.Message}",
                    StardewModdingAPI.LogLevel.Debug);
            }
        }

        // ────────────────────────────────────────────────
        //  补丁 1：整理按钮联排
        // ────────────────────────────────────────────────

        private static void ApplyOrganizeButtonPatch(Harmony harmony)
        {
            var targetType = typeof(ItemGrabMenu);
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
                        harmony.Patch(method,
                            postfix: new HarmonyMethod(typeof(HarmonyPatches),
                                nameof(Postfix_OrganizeButton)));
                        ModEntry.Instance.Monitor.Log(
                            $"[Harmony] 整理补丁已应用: ItemGrabMenu.receiveLeftClick({string.Join(", ", paramSet.Select(p => p.Name))})",
                            StardewModdingAPI.LogLevel.Info);
                        IsPatched = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log(
                        $"[Harmony] 整理补丁尝试失败 ({paramSet.Length}参数): {ex.Message}",
                        StardewModdingAPI.LogLevel.Debug);
                }
            }

            // 兜底
            try
            {
                var method = AccessTools.Method(targetType, "receiveLeftClick");
                if (method != null)
                {
                    harmony.Patch(method,
                        postfix: new HarmonyMethod(typeof(HarmonyPatches),
                            nameof(Postfix_OrganizeButton)));
                    ModEntry.Instance.Monitor.Log(
                        "[Harmony] 整理补丁已应用: ItemGrabMenu.receiveLeftClick (无参数约束)",
                        StardewModdingAPI.LogLevel.Info);
                    IsPatched = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"[Harmony] 整理补丁通用失败: {ex.Message}",
                    StardewModdingAPI.LogLevel.Debug);
            }

            ModEntry.Instance.Monitor.Log(
                "[Harmony] ⚠️ 未能找到 receiveLeftClick，整理联排功能不可用",
                StardewModdingAPI.LogLevel.Warn);
        }

        private static void Postfix_OrganizeButton(object __instance, int x, int y)
        {
            var organizeButton = AccessTools.Field(typeof(ItemGrabMenu), "organizeButton")
                ?.GetValue(__instance) as ClickableTextureComponent;
            if (organizeButton == null || !organizeButton.containsPoint(x, y))
                return;

            var context = AccessTools.Field(typeof(ItemGrabMenu), "context")
                ?.GetValue(__instance);
            if (context is not Chest chest)
                return;

            if (!ModEntry.Instance.Config.EnableLinkedSort)
                return;

            var location = Game1.player?.currentLocation;
            if (location == null)
                return;

            try
            {
                // 使用 tile-aware 版本，一次遍历搞定
                var linkedWithTiles = ChestLinker.FindLinkedChestsWithTiles(chest, location);

                if (linkedWithTiles.Count <= 1)
                    return;

                // 统计物品总数（在排序前统计，因为排序会清空再填充）
                int totalItemStacks = linkedWithTiles.Sum(t => t.chest.Items.Count(i => i != null));

                ChestLinker.DoLinkedSort(linkedWithTiles);

                if (ModEntry.Instance.Config.ShowNotification)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        $"整理了 {linkedWithTiles.Count} 个链接箱子，共 {totalItemStacks} 件物品", HUDMessage.newQuest_type)
                    { noIcon = true, timeLeft = 2000f });
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"链接整理出错: {ex}", StardewModdingAPI.LogLevel.Error);
            }
        }

        // ────────────────────────────────────────────────
        //  补丁 2：工作台当前场景全覆盖（getContainerContents postfix）
        // ────────────────────────────────────────────────

        private static void ApplyWorkbenchPatch(Harmony harmony)
        {
            var targetType = typeof(CraftingPage);

            // SDV 1.6 中 CraftingPage 只有一个获取容器内容的方法：
            //   IList<Item> getContainerContents()
            // 直接在其上做 postfix，替换返回值为全场景箱子物品，最可靠
            try
            {
                var method = AccessTools.Method(targetType, "getContainerContents");
                if (method != null)
                {
                    harmony.Patch(method,
                        postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Postfix_getContainerContents)));
                    ModEntry.Instance.Monitor.Log(
                        "[Harmony] 工作台补丁已应用: CraftingPage.getContainerContents()",
                        StardewModdingAPI.LogLevel.Info);
                    return;
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"[Harmony] 工作台 getContainerContents 补丁失败: {ex.Message}", StardewModdingAPI.LogLevel.Debug);
            }

            ModEntry.Instance.Monitor.Log(
                "[Harmony] ⚠️ 未能找到 getContainerContents，工作台功能不可用",
                StardewModdingAPI.LogLevel.Warn);
        }

        /// <summary>
        /// Postfix：拦截 getContainerContents() 的返回值，替换为全场景箱子物品
        /// getContainerContents 每次调用时原版会重建列表，我们直接替换结果避免物品重复
        /// </summary>
        private static void Postfix_getContainerContents(ref IList<Item> __result)
        {
            if (!ModEntry.Instance.Config.EnableWorkbenchRangeBoost)
                return;

            try
            {
                var location = Game1.player?.currentLocation;
                if (location == null)
                    return;

                // 重建物品列表：遍历当前场景所有箱子
                var allItems = new List<Item>();

                foreach (var pair in location.objects.Pairs)
                {
                    if (pair.Value is Chest chest)
                    {
                        for (int i = 0; i < chest.Items.Count; i++)
                        {
                            var item = chest.Items[i];
                            if (item != null)
                                allItems.Add(item);
                        }
                    }
                }

                // 替换返回值（原版相邻箱子也在场景中，不会丢失）
                __result = allItems;
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"[工作台] getContainerContents 注入失败: {ex.Message}",
                    StardewModdingAPI.LogLevel.Error);
            }
        }
    }
}

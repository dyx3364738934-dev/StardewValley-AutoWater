using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace QuickStack
{
    /// <summary>
    /// Linked Chests API 接口（软依赖）
    /// </summary>
    public interface ILinkedChestsApi
    {
        void TriggerSort(Chest chest);
    }

    public class ModConfig
    {
        public SButton StackKey { get; set; } = SButton.Tab;
        public int Range { get; set; } = 5;
        public bool StackToWholeRoom { get; set; } = true;
        public bool ExcludeTools { get; set; } = true;
        public bool ExcludeWeapons { get; set; } = true;
        public bool ShowNotification { get; set; } = true;
        public int FlightDuration { get; set; } = 400;
        public bool EnableAnimation { get; set; } = true;
        public bool AutoSortLinkedChests { get; set; } = true;

        /// <summary>启用收藏锁定功能</summary>
        public bool EnableFavoriteLock { get; set; } = true;

        /// <summary>收藏/取消收藏按键</summary>
        public SButton FavoriteKey { get; set; } = SButton.MouseMiddle;
    }

    public class FlyingItem
    {
        public Item Item { get; set; } = null!;
        public Vector2 StartPos { get; set; }
        public Vector2 TargetPos { get; set; }
        public Vector2 CurrentPos { get; set; }
        public float Alpha { get; set; } = 0f;        // 当前透明度
        public float Progress { get; set; } = 0f;      // 总进度 0..1
        public double StartTimeMs { get; set; }
        public GameLocation Location { get; set; } = null!;

        /// <summary>0→1 渐显阶段在总时长中的占比</summary>
        public float FadeRatio { get; set; } = 1f / 3f;  // 0.5s / 1.5s = 1/3

        /// <summary>总时长（毫秒）</summary>
        public double TotalDurationMs { get; set; } = 1500;
    }

    public class ModEntry : Mod
    {
        /// <summary>modData 键名：标记物品为收藏（锁定不被堆叠）</summary>
        private const string FavoriteKey = "Koko.QuickStack/Favorite";

        /// <summary>收藏星星在 mouseCursors 中的源矩形</summary>
        private static readonly Rectangle StarSourceRect = new(338, 400, 8, 8);

        private ModConfig config = null!;
        private List<FlyingItem> flyingItems = new();
        private ILinkedChestsApi? linkedChestsApi;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;

            helper.Events.GameLoop.GameLaunched += (_, _) =>
            {
                linkedChestsApi = helper.ModRegistry.GetApi<ILinkedChestsApi>("Koko.LinkedChests");
                if (linkedChestsApi != null)
                    Monitor.Log("检测到 Linked Chests，一键堆叠后将自动触发联排整理。", LogLevel.Info);
            };
        }

        // ═══════════════════════════════════════════════════════════
        // 输入处理：堆叠按键 + 收藏切换
        // ═══════════════════════════════════════════════════════════

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // ── 快捷堆叠（需要玩家自由状态，不能有菜单遮挡）──
            if (e.Button == config.StackKey && Context.IsPlayerFree)
            {
                DoQuickStack();
                return;
            }

            // ── 收藏切换（中键，需要菜单打开）──
            if (config.EnableFavoriteLock && e.Button == config.FavoriteKey)
            {
                ToggleFavorite(e.Cursor.ScreenPixels);
            }
        }

        /// <summary>
        /// 在鼠标位置处切换物品的收藏状态
        /// </summary>
        private void ToggleFavorite(Vector2 mousePos)
        {
            var inv = GetPlayerInventoryMenu();
            if (inv == null) return;

            // 遍历所有栏位，找到鼠标悬停的那个
            for (int i = 0; i < inv.inventory.Count; i++)
            {
                var slot = inv.inventory[i];
                if (!slot.containsPoint((int)mousePos.X, (int)mousePos.Y))
                    continue;

                if (i >= inv.actualInventory.Count) break;
                var item = inv.actualInventory[i];
                if (item == null) break;

                // 切换收藏标记
                if (item.modData.ContainsKey(FavoriteKey))
                {
                    item.modData.Remove(FavoriteKey);
                    Game1.playSound("cancel");
                }
                else
                {
                    item.modData[FavoriteKey] = "true";
                    Game1.playSound("coin");
                }
                return;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 渲染：在收藏物品上绘制紫色星星
        // ═══════════════════════════════════════════════════════════

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (!config.EnableFavoriteLock) return;

            var inv = GetPlayerInventoryMenu();
            if (inv == null) return;

            SpriteBatch spriteBatch = e.SpriteBatch;

            for (int i = 0; i < inv.inventory.Count && i < inv.actualInventory.Count; i++)
            {
                var item = inv.actualInventory[i];
                if (item == null || !IsFavorite(item))
                    continue;

                var slot = inv.inventory[i];
                // 星星画在物品栏左上角，放大2倍，不透明
                float starScale = 2f;
                var starPos = new Vector2(slot.bounds.X + 2, slot.bounds.Y + 2);
                spriteBatch.Draw(
                    Game1.mouseCursors,
                    starPos,
                    StarSourceRect,
                    Color.MediumPurple,
                    0f, Vector2.Zero,
                    starScale, SpriteEffects.None, 1f);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 辅助：获取当前菜单中的玩家物品栏
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 从当前活跃菜单中提取玩家物品栏组件
        /// </summary>
        private static InventoryMenu? GetPlayerInventoryMenu()
        {
            var menu = Game1.activeClickableMenu;

            // 直接打开的背包选项卡
            if (menu is InventoryPage invPage)
                return invPage.inventory;

            // 箱子/商店等 ItemGrabMenu 中的玩家物品栏
            if (menu is ItemGrabMenu grabMenu)
                return grabMenu.inventory;

            // ESC 菜单中的背包选项卡
            if (menu is GameMenu gameMenu &&
                gameMenu.currentTab < gameMenu.pages.Count &&
                gameMenu.pages[gameMenu.currentTab] is InventoryPage tabInv)
                return tabInv.inventory;

            return null;
        }

        // ═══════════════════════════════════════════════════════════
        // 收藏状态读写
        // ═══════════════════════════════════════════════════════════

        private static bool IsFavorite(Item item)
            => item.modData.ContainsKey(FavoriteKey);

        // ═══════════════════════════════════════════════════════════
        // 下方为原有逻辑（飞行、堆叠等），只改了 ShouldExclude
        // ═══════════════════════════════════════════════════════════

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (flyingItems.Count == 0) return;

            double now = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;

            for (int i = flyingItems.Count - 1; i >= 0; i--)
            {
                FlyingItem fi = flyingItems[i];
                if (fi.Location != Game1.player.currentLocation)
                {
                    flyingItems.RemoveAt(i);
                    continue;
                }

                double elapsed = now - fi.StartTimeMs;
                fi.Progress = (float)Math.Min(1.0, elapsed / fi.TotalDurationMs);

                // 阶段1：渐显，位置不动（0 → FadeRatio）
                if (fi.Progress < fi.FadeRatio)
                {
                    fi.Alpha = fi.Progress / fi.FadeRatio;         // 0 → 1
                    fi.CurrentPos = fi.StartPos;                   // 停在玩家身上
                }
                // 阶段2：飞行，全不透明（FadeRatio → 1）
                else
                {
                    fi.Alpha = 1f;
                    float flyT = (fi.Progress - fi.FadeRatio) / (1f - fi.FadeRatio); // 0 → 1
                    float easedFlyT = EaseOutCubic(flyT);
                    fi.CurrentPos = Vector2.Lerp(fi.StartPos, fi.TargetPos, easedFlyT);
                }

                if (fi.Progress >= 1f)
                    flyingItems.RemoveAt(i);
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (flyingItems.Count == 0) return;

            SpriteBatch spriteBatch = e.SpriteBatch;

            foreach (FlyingItem fi in flyingItems)
            {
                if (fi.Location != Game1.player.currentLocation) continue;

                Vector2 drawPos = fi.CurrentPos - new Vector2(Game1.viewport.X, Game1.viewport.Y);
                fi.Item.drawInMenu(spriteBatch, drawPos, 1f, fi.Alpha, 0f,
                    StackDrawType.Draw, Color.White * fi.Alpha, false);
            }
        }

        private static float EaseOutCubic(float t)
            => 1f - (float)Math.Pow(1f - t, 3f);

        private void DoQuickStack()
        {
            Farmer player = Game1.player;
            GameLocation location = player.currentLocation;

            Dictionary<Chest, Vector2> chestData = GetNearbyChestsWithPositions(location, player);
            if (chestData.Count == 0)
            {
                if (config.ShowNotification) ShowHUDMessage("附近没有箱子");
                return;
            }

            int totalStacked = 0;
            int itemsStacked = 0;
            Vector2 playerPos = player.Position;
            var affectedChests = new HashSet<Chest>();
            bool soundPlayed = false;

            for (int i = 0; i < player.Items.Count; i++)
            {
                Item? backpackItem = player.Items[i];
                if (backpackItem == null) continue;
                if (ShouldExclude(backpackItem)) continue;

                foreach (var chestEntry in chestData)
                {
                    Chest chest = chestEntry.Key;
                    Vector2 chestPos = chestEntry.Value;

                    Item? matchingItem = FindMatchingItem(chest, backpackItem);
                    if (matchingItem == null) continue;

                    int canStack = matchingItem.maximumStackSize() - matchingItem.Stack;
                    if (canStack <= 0) continue;

                    int actualStack = Math.Min(canStack, backpackItem.Stack);
                    if (actualStack <= 0) continue;

                    matchingItem.Stack += actualStack;
                    backpackItem.Stack -= actualStack;
                    totalStacked += actualStack;
                    affectedChests.Add(chest);

                    // 首次堆叠时播放箱子打开音效
                    if (!soundPlayed)
                    {
                        Game1.playSound("openChest");
                        soundPlayed = true;
                    }

                    if (config.EnableAnimation)
                        AddFlyingAnimation(backpackItem, playerPos, chestPos, location);

                    if (backpackItem.Stack <= 0)
                    {
                        player.Items[i] = null;
                        itemsStacked++;
                        break;
                    }
                }
            }

            // 联动 LinkedChests
            if (totalStacked > 0 && linkedChestsApi != null && config.AutoSortLinkedChests)
            {
                foreach (var chest in affectedChests)
                    linkedChestsApi.TriggerSort(chest);
            }

            if (config.ShowNotification)
            {
                if (totalStacked > 0)
                    ShowHUDMessage($"堆叠了 {itemsStacked} 种物品，共 {totalStacked} 个");
                else
                    ShowHUDMessage("没有可堆叠的物品");
            }
        }

        private void AddFlyingAnimation(Item item, Vector2 startPos, Vector2 chestPos, GameLocation location)
        {
            // 终点：箱子正上方半个格子处（箱子顶部居中）
            var targetPos = new Vector2(chestPos.X + 32, chestPos.Y - 32);

            flyingItems.Add(new FlyingItem
            {
                Item = item,
                StartPos = startPos + new Vector2(32, 0),
                TargetPos = targetPos,
                CurrentPos = startPos + new Vector2(32, 0),
                StartTimeMs = Game1.currentGameTime.TotalGameTime.TotalMilliseconds,
                Location = location
            });
        }

        private Dictionary<Chest, Vector2> GetNearbyChestsWithPositions(GameLocation location, Farmer player)
        {
            Dictionary<Chest, Vector2> result = new();

            if (config.StackToWholeRoom)
            {
                if (location.objects != null)
                {
                    foreach (var pair in location.objects.Pairs)
                    {
                        if (pair.Value is Chest chest && chest.playerChest.Value)
                            result[chest] = pair.Key * 64;
                    }
                }
            }
            else
            {
                Vector2 playerPos = player.Position;
                int rangePixels = config.Range * 64;

                if (location.objects != null)
                {
                    foreach (var pair in location.objects.Pairs)
                    {
                        if (pair.Value is Chest chest && chest.playerChest.Value)
                        {
                            Vector2 chestPos = pair.Key * 64;
                            if (Vector2.Distance(playerPos, chestPos) <= rangePixels)
                                result[chest] = chestPos;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 检查物品是否应该被排除（工具、武器、收藏物品）
        /// </summary>
        private bool ShouldExclude(Item item)
        {
            if (config.ExcludeWeapons && item.Category == -99) return true;
            if (config.ExcludeTools && item.Category == -98) return true;
            if (config.EnableFavoriteLock && IsFavorite(item)) return true;
            return false;
        }

        private static Item? FindMatchingItem(Chest chest, Item target)
        {
            foreach (Item? item in chest.Items)
            {
                if (item == null) continue;
                if (item.QualifiedItemId != target.QualifiedItemId) continue;

                if (item is StardewValley.Object obj && target is StardewValley.Object targetObj)
                    if (obj.Quality != targetObj.Quality) continue;

                return item;
            }
            return null;
        }

        private static void ShowHUDMessage(string message)
        {
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type)
            {
                noIcon = true,
                timeLeft = 2000f
            });
        }
    }
}

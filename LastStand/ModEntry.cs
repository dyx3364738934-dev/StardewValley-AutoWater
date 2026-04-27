using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace LastStand
{
    public class ModConfig
    {
        public float StaminaDrainPerSecond { get; set; } = 10f;
        public float RecoveryDuration { get; set; } = 10f;
        public float CooldownDuration { get; set; } = 30f;
        public float OverlayFadeInDuration { get; set; } = 2f;
        public float OverlayAlphaMax { get; set; } = 0.6f;
        public float OverlayAlphaRecovery { get; set; } = 0.3f;
        public bool EnableOverlay { get; set; } = true;
    }

    public class ModEntry : Mod
    {
        private ModConfig config;
        private Texture2D pixelTexture;
        
        private bool isDesperate;
        private bool isRecovering;
        private bool isCooldown;
        private float recoveryTimer;
        private float cooldownTimer;
        private float overlayFadeTimer;
        private bool usedLastStandToday;  // 今天是否已用过搏命

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;

            Monitor.Log("Last Stand 已加载！", LogLevel.Info);
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // 新的一天，重置搏命次数
            usedLastStandToday = false;
            isDesperate = false;
            isRecovering = false;
            isCooldown = false;
            Monitor.Log("🌅 新的一天，搏命次数已重置", LogLevel.Info);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            pixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            pixelTexture.SetData(new[] { Color.White });
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.player == null) return;

            var player = Game1.player;

            // 冷却期：完全不干预
            if (isCooldown) return;

            // ===== 搏命期间：一直锁血（关键修复！） =====
            if (isDesperate && player.health < 1)
            {
                player.health = 1;
            }

            // ===== 触发搏命（只有今天没用过才能触发） =====
            if (player.health <= 1 && !usedLastStandToday && !isDesperate && !isRecovering)
            {
                player.health = 1;
                EnterDesperateState();
            }

            // ===== 搏命期间：按 tick 掉体力 =====
            if (isDesperate)
            {
                // 每秒掉 StaminaDrainPerSecond 点，每帧掉 StaminaDrainPerSecond/60 点
                float drainPerTick = config.StaminaDrainPerSecond / 60f;
                player.stamina = Math.Max(0, player.stamina - drainPerTick);

                if (player.stamina <= 0f)
                {
                    ForceFaint();
                }
            }

            // 血量恢复 → 进入恢复期
            if (isDesperate && player.health > 1)
            {
                EnterRecoveryState();
            }

            // 恢复期结束后解除减速
            if (!isDesperate && !isRecovering && player.temporarySpeedBuff < 0)
            {
                player.temporarySpeedBuff = 0;
            }
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.player == null) return;

            // 恢复期倒计时
            if (isRecovering)
            {
                recoveryTimer -= 1f;
                Game1.player.temporarySpeedBuff = -2;  // 减速
                
                if (recoveryTimer <= 0f)
                    ExitRecoveryState();
            }

            // 冷却期倒计时
            if (isCooldown)
            {
                cooldownTimer -= 1f;
                if (cooldownTimer <= 0f)
                {
                    isCooldown = false;
                    Monitor.Log("✅ 冷却期结束，搏命系统重新激活", LogLevel.Info);
                }
            }

            // 体力低警告
            if (isDesperate && Game1.player.stamina <= 20f && Game1.player.stamina > 0f)
            {
                Monitor.Log("⚠️ 体力即将耗尽！快吃东西！", LogLevel.Warn);
            }
        }

        private void EnterDesperateState()
        {
            isDesperate = true;
            usedLastStandToday = true;  // 标记今天已使用
            overlayFadeTimer = 0f;
            Game1.playSound("bigSelect");
            
            // 💬 播报：某某某触发了肾上腺素
            ShowHUDNotification($"{Game1.player.Name} 触发了肾上腺素！");
            
            Monitor.Log("💀 【搏命】血量见底！今日唯一一次搏命机会！每秒流失" + config.StaminaDrainPerSecond + "体力！", LogLevel.Warn);
        }

        private void EnterRecoveryState()
        {
            isDesperate = false;
            isRecovering = true;
            recoveryTimer = config.RecoveryDuration;
            Game1.playSound("coin");
            
            ShowHUDNotification("搏命解除，身体虚弱中...");
            
            Monitor.Log("🫁 【恢复】搏命解除！移动减速" + config.RecoveryDuration + "秒", LogLevel.Info);
        }

        private void ExitRecoveryState()
        {
            isRecovering = false;
            Game1.player.temporarySpeedBuff = 0;
            
            ShowHUDNotification("状态完全恢复！");
            
            Monitor.Log("✅ 状态恢复！", LogLevel.Info);
        }

        private void ForceFaint()
        {
            isDesperate = false;
            isRecovering = false;
            isCooldown = true;
            cooldownTimer = config.CooldownDuration;
            
            ShowHUDNotification($"{Game1.player.Name} 体力耗尽，被抬走了...");
            
            Monitor.Log("💀 体力耗尽昏迷！" + config.CooldownDuration + "秒冷却期", LogLevel.Error);
            Game1.playSound("death");
        }

        /// <summary>屏幕上方显示 HUD 通知（最显眼的播报方式）</summary>
        private void ShowHUDNotification(string message)
        {
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type)
            {
                noIcon = true,
                timeLeft = 3000f  // 显示 3 秒
            });

            // 多人模式下同时发送聊天框消息
            if (Context.IsMultiplayer && Game1.chatBox != null)
            {
                Game1.chatBox.addMessage(message, Color.OrangeRed);
            }
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!config.EnableOverlay || !Context.IsWorldReady || pixelTexture == null) return;

            float alpha = 0f;
            
            if (isDesperate)
            {
                overlayFadeTimer += (float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                float progress = Math.Min(1f, overlayFadeTimer / config.OverlayFadeInDuration);
                alpha = config.OverlayAlphaMax * EaseInQuad(progress);
            }
            else if (isRecovering)
            {
                float progress = recoveryTimer / config.RecoveryDuration;
                alpha = config.OverlayAlphaRecovery * progress;
            }

            if (alpha > 0.001f)
            {
                e.SpriteBatch.Draw(pixelTexture, 
                    new Rectangle(0, 0, Game1.viewport.Width, Game1.viewport.Height), 
                    Color.Black * alpha);
            }
        }
        
        private float EaseInQuad(float t)
        {
            return t * t;
        }
    }
}
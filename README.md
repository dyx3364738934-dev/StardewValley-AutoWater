# Auto Water

星露谷物语 SMAPI Mod —— 每天清晨自动浇灌全农场所有作物，无需手动浇水。

## 功能

- **🌅 每日自动浇水**：每天起床后自动浇灌农场所有已耕地块（支持作物、温室、姜岛、花园盆栽）
- **💧 吸水触发浇水**：在水边给水壶吸水时自动触发全农场浇水
- **🔔 金水壶图标通知**：触发浇水时左下角 HUD 显示金水壶图标 + 浇灌格数
- **🌧️ 雨天智能跳过**：雨天自动跳过户外浇水，不浪费水量
- **🏝️ 姜岛支持**：可选覆盖姜岛农场区域
- **🪴 花园盆栽**：可选浇灌室内花园盆栽

## 安装

1. 安装 [SMAPI](https://smapi.io/) (v4.0.0+)
2. 下载最新 [Release](https://github.com/dyx3364738934-dev/StardewValley-AutoWater/releases)
3. 解压到 `Stardew Valley/Mods/AutoWater/`
4. 启动游戏

## 配置

运行一次游戏后，在 `Mods/AutoWater/config.json` 中修改：

```json
{
  "EnableAutoWater": true,
  "ShowNotification": true,
  "OnlyWaterCrops": true,
  "WaterGreenhouse": true,
  "WaterGingerIsland": true,
  "WaterGardenPots": true,
  "EnableWaterCanRefillTrigger": true
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `EnableAutoWater` | 启用每日自动浇水 | `true` |
| `ShowNotification` | 浇水时显示 HUD 通知 | `true` |
| `OnlyWaterCrops` | 仅浇灌有种子的地块 | `true` |
| `WaterGreenhouse` | 浇灌温室 | `true` |
| `WaterGingerIsland` | 浇灌姜岛 | `true` |
| `WaterGardenPots` | 浇灌花园盆栽 | `true` |
| `EnableWaterCanRefillTrigger` | 吸水时触发浇水 | `true` |

## 兼容性

- Stardew Valley 1.6+
- SMAPI 4.0.0+
- 兼容多人模式（仅浇灌主机农场）

## 版本历史

- **v1.1.0** — 浇水通知添加金水壶图标
- **v1.0.0** — 初始版本：每日自动浇水 + 吸水触发

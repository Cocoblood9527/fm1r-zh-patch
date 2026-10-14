# FRONT MISSION 1st Remake 繁体中文汉化补丁

针对《前线任务 初代 重制版》官方中文版机翻质量的玩家自制汉化补丁。
**不修改、不重新分发任何游戏原始文件**，全部文本在运行时注入。

适用版本：v.3.0.1（Unity 2021.3.6f1，Windows x64 Mono 版）

---

## 这个补丁做了什么

官方自带的繁体中文是机器翻译，语序和用词都很生硬。本补丁用人工修订过的译文
在运行时替换游戏读取的 8198 条文本，并解决随之而来的字形缺失问题。

游戏自带的中文字体是一张**静态子集图集**，只有约 3200 个字形——刚好覆盖机翻原文
用过的字。任何新字都会掉到 `NotoSansSC` 这个动态字体上，而它的图集在发行版里不可写，
于是显示成 □。补丁在运行时用 Noto Sans CJK TC 新建一个动态字体资产，插到所有字体资产
的 fallback 链首位，因此译文里可以使用任何常用汉字，且不影响原本就能显示的字。

| | 官方 | 打补丁后 |
| --- | --- | --- |
| 菜单文案 | 配置 / 制作人员名单 | 設置 / 工作人員列表 |
| 字形覆盖 | ~3200（超出显示 □） | 完整 CJK |
| 游戏原始文件 | — | 未被改动 |

---

## 安装

### Windows

1. 完全退出游戏。
2. 下载 [最新 Release](../../releases) 里的 `FM1R-ZH-Patch.zip`。
3. 把压缩包内容**解压到游戏根目录**（与 `FRONT MISSION 1st Remake.exe` 同级）。
   解压后该目录应出现 `BepInEx\`、`winhttp.dll`、`doorstop_config.ini`。
4. 启动游戏，语言选「繁體中文」。

首次启动会慢几秒——补丁在启动时把需要的汉字现场光栅化成字形，之后会明显变快。

### Steam Deck / Proton

除上面的文件外，需要在 Steam 的启动设置里加上：

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

否则 Unity 会加载 Proton 自带的 `winhttp`，BepInEx 的注入入口不会被触发。

### 卸载

删除游戏根目录下的 `BepInEx\`、`winhttp.dll`、`doorstop_config.ini`，游戏即回到原版。

---

## 自己改文本

编辑 `BepInEx/plugins/FM1RZhPatch/zh.csv`（UTF-8，两列 `key,zh`），不需要重新编译任何东西。
详细注意事项见 [`docs/edit-text.txt`](docs/edit-text.txt)。

---

## 从源码构建

需要 .NET SDK 和游戏安装目录（编译时要引用其中的 `UnityEngine` 程序集，这些 DLL 不入库）：

```bash
GAME_DIR="/path/to/FRONT MISSION 1st Remake" ./build.sh
```

脚本会自行下载 BepInEx 5.4.23.5 与 Noto Sans CJK TC（带 SHA-256 校验），
产出 `dist/FM1R-ZH-Patch.zip`。

---

## 目录结构

```
src/FM1RZhPatch/     文本注入插件：改写 Walker.LocalizedMessages 的中文表
src/FM1RGlyphFix/    字形注入插件：运行时挂入动态 CJK 字体
data/zh.csv          8198 条繁体中文文本
data/chars.txt       启动时预热的字形列表
docs/                面向玩家的安装与改文本说明
build.sh             编译 + 打包
```

### 实现要点

- 游戏的文本在 `Walker.LocalizationData.messages` 里，每种语言一个
  `Walker.LocalizedMessages`，含 `m_Keys: string[]` 与 `m_Values: MessageInfo[]`。
  `MessageInfo` 是引用类型且与 `m_LocalisationDict` 共享，所以改写 `m_Value` 即可生效。
- `Walker.Localization` 会把解析结果抄进约 200 个 **static 字段**，因此注入必须挂在
  `Localization.Initialize` / `SetLanguage` 的**前缀**上，而不是等表初始化完再补。
- 插件必须编译为 **net472**。这个游戏是 .NET Framework 兼容级别的 Mono 构建，
  `Managed/` 里没有 `netstandard.dll`，netstandard 程序集会被 Unity 拒绝加载
  （`The script 'X' could not be instantiated!`）。
- 部分字体资产（菜单用的 `TC_LangName`、`LiberationSans SDF`）要等对应界面才加载，
  所以字形注入带一个 2 秒轮询持续补挂。

---

## 许可与声明

- `src/` 下的插件代码以 MIT 许可发布，见 [`LICENSE`](LICENSE)。
- `THIRD-PARTY/NotoSansCJK-OFL.txt`：随包分发的 Noto Sans CJK TC 字体采用
  SIL Open Font License 1.1，由 Google Noto 项目提供。
- `data/zh.csv` 是玩家对游戏剧本的演绎性翻译，**不包含也不替代 Square Enix 的原始文本**，
  仅供同好交流使用，不得用于任何商业用途。若权利方要求下架，会立即删除。
- 本补丁与 Square Enix、Forever Entertainment 无任何关联。

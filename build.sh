#!/usr/bin/env bash
# 编译两个插件并组装成可直接解压到游戏目录的分发包。
#
#   GAME_DIR="/path/to/FRONT MISSION 1st Remake" ./build.sh
#
# 需要 .NET SDK（dotnet）和 curl。BepInEx 与 Noto 字体在构建时下载，不入库。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
VENDOR="$ROOT/vendor"
BEPINEX_VER="5.4.23.5"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VER}/BepInEx_win_x64_${BEPINEX_VER}.zip"
FONT_URL="https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/TraditionalChinese/NotoSansCJKtc-Regular.otf"
FONT_SHA256="dce08bd4fd91aa8aa76ed8fea4b694c2dfb8550f67871e326843212ddbeb88b4"
OUT="$ROOT/dist"
WORK="$OUT/FM1R-ZH-Patch"

: "${GAME_DIR:?请设置 GAME_DIR 指向游戏安装目录（编译要引用其中的 UnityEngine 程序集）}"
MANAGED="$GAME_DIR/FRONT MISSION 1st Remake_Data/Managed"
[ -d "$MANAGED" ] || { echo "找不到 $MANAGED"; exit 1; }
command -v dotnet >/dev/null || { echo "需要 .NET SDK（dotnet）"; exit 1; }

mkdir -p "$VENDOR"
if [ ! -f "$VENDOR/BepInEx/BepInEx/core/BepInEx.dll" ]; then
  echo "==> 下载 BepInEx $BEPINEX_VER"
  curl -fL --retry 3 -o "$VENDOR/bepinex.zip" "$BEPINEX_URL"
  /usr/bin/unzip -oq "$VENDOR/bepinex.zip" -d "$VENDOR/BepInEx"
  rm -f "$VENDOR/bepinex.zip"
fi

if [ ! -d "$VENDOR/Managed" ]; then
  echo "==> 从游戏目录取引用程序集（只复制编译需要的四个）"
  mkdir -p "$VENDOR/Managed"
  for d in UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.TextRenderingModule.dll; do
    cp "$MANAGED/$d" "$VENDOR/Managed/"
  done
fi

FONT="$VENDOR/NotoSansCJKtc-Regular.otf"
if [ ! -f "$FONT" ]; then
  echo "==> 下载 Noto Sans CJK TC"
  curl -fL --retry 3 -o "$FONT" "$FONT_URL"
fi
echo "$FONT_SHA256  $FONT" | shasum -a 256 -c - || { echo "字体校验失败"; exit 1; }

echo "==> 编译"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
for p in FM1RZhPatch FM1RGlyphFix; do
  (cd "$ROOT/src/$p" && dotnet build -c Release -v quiet --nologo \
    -p:BepInExDir="$VENDOR/BepInEx/BepInEx" -p:GameManaged="$VENDOR/Managed")
done

echo "==> 组装 $WORK"
rm -rf "$OUT"; mkdir -p "$WORK/BepInEx/plugins/FM1RZhPatch"
cp -R "$VENDOR/BepInEx/BepInEx/core" "$WORK/BepInEx/"
cp "$VENDOR/BepInEx/winhttp.dll" "$VENDOR/BepInEx/doorstop_config.ini" \
   "$VENDOR/BepInEx/.doorstop_version" "$VENDOR/BepInEx/changelog.txt" "$WORK/"
cp "$ROOT/src/FM1RZhPatch/bin/Release/FM1RZhPatch.dll"     "$WORK/BepInEx/plugins/"
cp "$ROOT/src/FM1RGlyphFix/bin/Release/FM1RGlyphFix.dll"   "$WORK/BepInEx/plugins/"
cp "$ROOT/data/zh.csv"    "$WORK/BepInEx/plugins/FM1RZhPatch/"
cp "$ROOT/data/chars.txt" "$WORK/BepInEx/plugins/"
cp "$FONT" "$WORK/BepInEx/plugins/"
cp "$ROOT/docs/"*.txt "$WORK/"
cp "$ROOT/THIRD-PARTY/NotoSansCJK-OFL.txt" "$WORK/LICENSE-Noto.txt"

( cd "$OUT" && rm -f FM1R-ZH-Patch.zip && zip -qrX FM1R-ZH-Patch.zip FM1R-ZH-Patch )
echo
echo "产物：$OUT/FM1R-ZH-Patch.zip"
du -sh "$OUT/FM1R-ZH-Patch.zip"

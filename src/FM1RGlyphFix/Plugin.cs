using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace FM1RGlyphFix
{
    [BepInPlugin("com.fm1r.glyphfix", "FM1R Dynamic Glyph Fallback", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string TMP = "Unity.TextMeshPro";
        private Type _faType;
        private object _tc, _sc;
        private Font _source;
        private object _dyn;

        private void Awake() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(12f);
            _faType = FindType("TMPro.TMP_FontAsset");
            if (_faType == null) { Log("找不到 TMP_FontAsset"); yield break; }

            var assets = Resources.FindObjectsOfTypeAll(_faType);
            foreach (var a in assets)
                Log($"FONT {Name(a)} mode={Get(a, "m_AtlasPopulationMode")} " +
                    $"chars={Count(Get(a, "m_CharacterTable"))} fb=[{Fallbacks(a)}]");

            _tc = assets.FirstOrDefault(a => Name(a) == "NotoSansTC-Regular SDF");
            _sc = assets.FirstOrDefault(a => Name(a) == "NotoSansSC-Regular SDF");
            if (_tc == null) { Log("没有 NotoSansTC-Regular SDF"); yield break; }

            _source = FindSourceFont();
            if (_source == null) { Log("找不到可作为字形来源的 Font 对象"); yield break; }
            Log($"字形来源 Font：{_source.name}");

            var dyn = _dyn = CreateAsset(_source);
            if (dyn == null) { Log("CreateFontAsset 失败"); yield break; }
            StartCoroutine(KeepWiring());
            Log($"已创建动态字体资产 {Name(dyn)} mode={Get(dyn, "m_AtlasPopulationMode")}");

            int wired = 0;
            foreach (var a in assets)
            {
                if (ReferenceEquals(a, dyn)) continue;
                if (AddFallback(a, dyn)) wired++;
            }
            Log($"已挂到 {wired} 个字体资产的 fallback 链首位");
            WireGlobalFallback(dyn);
            ((UnityEngine.Object)dyn).name = "FM1R Dynamic CJK";
            DumpMetrics("TC ", _tc);
            DumpMetrics("DYN", dyn);
            Verify(dyn);
            Log("=== 字形修复完成 ===");
        }

        private void DumpMetrics(string tag, object a) =>
            Log($"{tag}: pointSize={Get(a, "m_PointSize")} scale={Get(a, "m_Scale")} " +
                $"atlas={Get(a, "m_AtlasWidth")}x{Get(a, "m_AtlasHeight")} pad={Get(a, "m_AtlasPadding")} " +
                $"renderMode={Get(a, "m_AtlasRenderMode")} face={Get(a, "m_FaceInfo")}");

        // 把实际会显示成口口的字逐个交给 TMP 光栅化，看能不能真的画出来
        private void Verify(object dyn)
        {
            var mi = _faType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "TryAddCharacterInternal");
            if (mi == null) { Log("没有 TryAddCharacterInternal，无法自动验证"); return; }
            var ps = mi.GetParameters();
            Log($"验证签名：{mi.Name}({string.Join(", ", ps.Select(x => x.ParameterType.Name))})");

            var dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var path = System.IO.Path.Combine(dir, "chars.txt");
            // 这个文件只是把首次光栅化提前到启动时，缺了也不影响正确性
            if (!System.IO.File.Exists(path)) { Log("没有 chars.txt，跳过预热"); return; }
            var file = System.IO.File.ReadAllText(path).Trim();
            int ok = 0; var failed = new StringBuilder();
            foreach (var ch in file.Distinct())
            {
                object[] args = new object[ps.Length];
                args[0] = Convert.ChangeType(ch, ps[0].ParameterType);
                for (int i = 1; i < args.Length; i++) args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                bool r;
                try { r = (bool)mi.Invoke(dyn, args); }
                catch (Exception e) { r = false; failed.Append(ch).Append('(').Append(e.GetBaseException().GetType().Name).Append(')'); }
                if (r) ok++; else failed.Append(ch);
            }
            Log($"光栅化成功 {ok}/{file.Distinct().Count()} 个原本会显示成口口的字；失败：{(failed.Length == 0 ? "无" : failed.ToString())}");
            Log($"验证后 dyn 字符表大小={Count(Get(dyn, "m_CharacterTable"))}");
        }

        private Font FindSourceFont()
        {
            // 优先用随补丁分发的 NotoSansTC，字形风格才和游戏原有的繁中字体一致
            var dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.ttf")
                         .Concat(System.IO.Directory.GetFiles(dir, "*.otf")))
            {
                try
                {
                    var f = new Font(file);   // Unity 用文件路径载入 TTF/OTF
                    Log($"使用外部分发字体 {f.name}");
                    return f;
                }
                catch (Exception e) { Log($"  外部字体 {file} 载入失败：{e.GetBaseException().Message}"); }
            }
            Log("未找到外部字体，改用游戏内置的字体源（简体风格）");
            var sc = Get(_sc, "m_SourceFontFile") as Font;
            if (sc != null) return sc;
            return Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name.Contains("NotoSans"));
        }

        private object CreateAsset(Font font)
        {
            var overloads = _faType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "CreateFontAsset" && m.GetParameters().Length > 0 &&
                            m.GetParameters()[0].ParameterType == typeof(Font))
                .OrderBy(m => m.GetParameters().Length).ToList();
            foreach (var m in overloads)
                Log("可用重载：" + string.Join(", ", m.GetParameters().Select(Sig)));
            if (overloads.Count == 0) return null;

            var tcFace = Get(_tc, "m_FaceInfo");
            int tcSample = AsInt(Get(tcFace, "pointSize"), 36);
            int tcPad = AsInt(Get(_tc, "m_AtlasPadding"), 9);
            object tcMode = EnumByName(GetTypeField(_tc, "m_AtlasRenderMode"),
                                       Get(_tc, "m_AtlasRenderMode")?.ToString(), "SDFAA");

            // 带模板资产的重载能一次继承 TC 的全部度量，优先用
            var best = overloads.FirstOrDefault(m => m.GetParameters()
                         .Any(p => p.ParameterType == _faType)) ?? overloads.Last();
            var ps = best.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var t = ps[i].ParameterType;
                if (i == 0) args[i] = font;
                else if (t == _faType) args[i] = _tc;
                else if (t == typeof(int)) args[i] = IntDefault(ps[i].Name, tcSample, tcPad);
                else if (t.IsEnum) args[i] = tcMode != null && t == tcMode.GetType() ? tcMode
                                       : EnumByName(t, null, "Dynamic") ?? Activator.CreateInstance(t);
                else if (t == typeof(bool)) args[i] = true;
                else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else args[i] = null;
            }
            Log("选用重载：" + best.Name + "(" + string.Join(", ", ps.Select(Sig)) + ")");
            try { return best.Invoke(null, args); }
            catch (Exception e) { Log("  调用失败：" + e.GetBaseException().Message); return null; }
        }

        private static string Sig(ParameterInfo p) => p.ParameterType.Name + " " + p.Name;
        private static int AsInt(object v, int dflt) => v == null ? dflt : Convert.ToInt32(v);
        private static object EnumByName(Type t, string have, string want)
        {
            if (t == null) return null;
            var names = Enum.GetNames(t);
            if (have != null && Array.IndexOf(names, have) >= 0) return Enum.Parse(t, have);
            return Array.IndexOf(names, want) >= 0 ? Enum.Parse(t, want) : null;
        }
        private static Type GetTypeField(object o, string name) =>
            o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;

        private static int IntDefault(string name, int pointSize, int padding)
        {
            switch (name)
            {
                case "samplingPointSize":
                case "pointSize": return pointSize;
                case "atlasWidth":
                case "atlasHeight": return 4096;
                case "atlasSize": return 4096;
                case "padding":
                case "atlasPadding": return padding;
                default: return 0;
            }
        }

        private static object DefaultValue(Type enumType)
        {
            var names = Enum.GetNames(enumType);
            // Dynamic=1, 渲染模式取 Normal
            foreach (var want in new[] { "Dynamic", "SDFAA", "Linear", "Fast", "Normal" })
            {
                var hit = Array.FindIndex(names, n => n == want);
                if (hit >= 0) return Enum.ToObject(enumType, hit);
            }
            return Activator.CreateInstance(enumType);
        }

        private static int IntDefault(string name)
        {
            switch (name)
            {
                case "samplingPointSize":
                case "pointSize": return 36;
                case "atlasWidth":
                case "atlasHeight":
                case "atlasSize": return 2048;
                case "padding": return 8;
                default: return 0;
            }
        }

        // TMP_Settings 的全局回退链对所有字体资产生效，包括之后才加载的那些
        private void WireGlobalFallback(object dyn)
        {
            var settingsType = FindType("TMPro.TMP_Settings");
            var settings = settingsType == null ? null :
                Resources.FindObjectsOfTypeAll(settingsType).FirstOrDefault();
            if (settings == null) { Log("找不到 TMP_Settings，只做了逐资产挂载"); return; }
            foreach (var name in new[] { "m_fallbackFontAssetTable", "m_GlobalFallbackFontAssetTable" })
            {
                var list = Get(settings, name) as System.Collections.IList;
                if (list == null) continue;
                if (!list.Contains(dyn)) list.Insert(0, dyn);
                Log($"已写入全局回退链 {name}（{list.Count} 项）");
            }
            Log("全局回退字体：" + string.Join(",", ((Get(settings, "m_fallbackFontAssetTable") as System.Collections.IEnumerable)
                ?? new object[0]).Cast<object>().Select(Name)));
        }

        // 有些字体资产（例如菜单用的 TC_LangName）要等对应界面打开才加载，
        // 所以常驻补挂，确保任何时刻新出现的资产都拿到动态回退。
        private IEnumerator KeepWiring()
        {
            var wait = new WaitForSeconds(2f);
            var seen = new HashSet<string>();
            while (true)
            {
                yield return wait;
                if (_dyn == null) continue;
                foreach (var a in Resources.FindObjectsOfTypeAll(_faType))
                {
                    if (ReferenceEquals(a, _dyn)) continue;
                    var list = Get(a, "m_FallbackFontAssetTable") as System.Collections.IList;
                    if (list == null || list.Contains(_dyn)) continue;
                    list.Insert(0, _dyn);
                    if (seen.Add(Name(a))) Log($"为新加载的字体资产补挂动态回退：{Name(a)}");
                }
            }
        }

        private bool AddFallback(object asset, object dyn)
        {
            var list = Get(asset, "m_FallbackFontAssetTable") as System.Collections.IList;
            if (list == null || list.Contains(dyn)) return false;
            list.Insert(0, dyn);
            return true;
        }

        private static Type FindType(string full) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetType(full); } catch { return null; } })
                .FirstOrDefault(t => t != null);

        private static object Get(object o, string field)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(o);
                var p = t.GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead) { try { return p.GetValue(o); } catch { return null; } }
            }
            return null;
        }

        private static string Name(object o) => ((UnityEngine.Object)o).name;
        private static string Count(object list) => (list as System.Collections.ICollection)?.Count.ToString() ?? "-";

        private string Fallbacks(object asset)
        {
            var list = Get(asset, "m_FallbackFontAssetTable") as System.Collections.IEnumerable;
            return list == null ? "" : string.Join(",", list.Cast<object>()
                .Where(x => x != null).Select(Name));
        }

        private void Log(string s) => Logger.LogInfo(s);
    }
}

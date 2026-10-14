using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FM1RZhPatch
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "com.fm1r.zhpatch";
        internal const string PluginName = "FM1R Chinese Translation";
        internal const string PluginVersion = "1.0.0";

        private static Dictionary<string, string> _table;
        private static Type _messagesType;
        private static object _chinese;
        private static FieldInfo _keys, _values, _language, _dict, _value;
        private static ManualLogSource _log;

        private void Awake()
        {
            _log = Logger;
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var csv = Path.Combine(dir, "FM1RZhPatch", "zh.csv");
            if (!File.Exists(csv)) csv = Path.Combine(dir, "zh.csv");
            if (!File.Exists(csv))
            {
                Logger.LogError($"找不到文本表，放这里即可：{csv}");
                return;
            }
            _table = Csv.Read(csv);
            Logger.LogInfo($"{PluginName} {PluginVersion}：读入 {_table.Count} 条文本");

            _messagesType = Type.GetType("Walker.LocalizedMessages, Assembly-CSharp");
            var langType = Type.GetType("Walker.Localization+GameLanguage, Assembly-CSharp");
            var infoType = Type.GetType("Walker.LocalizedMessages+MessageInfo, Assembly-CSharp");
            if (_messagesType == null || langType == null || infoType == null)
            {
                Logger.LogError("找不到 Walker.LocalizedMessages / GameLanguage / MessageInfo，游戏版本可能已变化");
                return;
            }
            _chinese = Enum.Parse(langType, "Chinese");
            _keys = AccessTools.Field(_messagesType, "m_Keys");
            _values = AccessTools.Field(_messagesType, "m_Values");
            _language = AccessTools.Field(_messagesType, "m_Language");
            _dict = AccessTools.Field(_messagesType, "m_LocalisationDict");
            _value = AccessTools.Field(infoType, "m_Value");

            var harmony = new Harmony(PluginGuid);
            harmony.Patch(
                AccessTools.Method(_messagesType, "Initialize"),
                postfix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(AfterInitialize),
                    BindingFlags.Static | BindingFlags.NonPublic)));

            // Localization 会把表里的文字抄进一批 static 字段，抄之前先把表换掉
            var locType = Type.GetType("Walker.Localization, Assembly-CSharp");
            foreach (var name in new[] { "Initialize", "SetLanguage" })
            {
                var m = AccessTools.Method(locType, name);
                if (m == null) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ApplyAll),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            }

            ApplyAll();
        }

        private static void ApplyAll()
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(_messagesType)) Apply(o);
        }

        private static void AfterInitialize(object __instance) => Apply(__instance);

        private static void Apply(object messages)
        {
            if (messages == null || !Equals(_language.GetValue(messages), _chinese)) return;
            var keys = _keys.GetValue(messages) as string[];
            var values = _values.GetValue(messages) as Array;
            if (keys == null || values == null) return;

            int n = Math.Min(keys.Length, values.Length), changed = 0, missing = 0;
            for (int i = 0; i < n; i++)
            {
                var key = keys[i];
                if (key == null) continue;
                if (!_table.TryGetValue(key, out var text)) { missing++; continue; }
                var info = values.GetValue(i);
                if (info == null || (string)_value.GetValue(info) == text) continue;
                _value.SetValue(info, text);
                changed++;
            }
            SyncCache(messages, keys, values, n);
            _log.LogInfo($"已套用：改写 {changed} 条，文本表中缺 {missing} 条");
        }

        // m_LocalisationDict 只在当前语言下构建；确保它指向我们改过的那批 MessageInfo
        private static void SyncCache(object messages, string[] keys, Array values, int n)
        {
            if (!(_dict.GetValue(messages) is IDictionary cache)) return;
            int relinked = 0;
            for (int i = 0; i < n; i++)
            {
                var key = keys[i];
                if (key == null || !_table.ContainsKey(key)) continue;
                var info = values.GetValue(i);
                if (info == null || (cache.Contains(key) && ReferenceEquals(cache[key], info))) continue;
                cache[key] = info;
                relinked++;
            }
            if (relinked > 0) _log.LogInfo($"重建字典条目 {relinked} 个");
        }
    }

    internal static class Csv
    {
        public static Dictionary<string, string> Read(string path)
        {
            var rows = new Dictionary<string, string>(9000);
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            var fields = new List<string>(2);
            var sb = new StringBuilder();
            bool quoted = false;

            void EndField() { fields.Add(sb.ToString()); sb.Clear(); }
            void EndRow()
            {
                EndField();
                if (fields.Count >= 2 && fields[0] != "key") rows[fields[0]] = fields[1];
                fields.Clear();
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"') { sb.Append(c); continue; }
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') EndField();
                else if (c == '\n') EndRow();
                else if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
                else sb.Append(c);
            }
            if (sb.Length > 0 || fields.Count > 0) EndRow();
            return rows;
        }
    }
}

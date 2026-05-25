using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.PrefabAtlasSpriteRemap
{
    /// <summary>
    /// Prefab 散图 Sprite → 图集 Multiple 子 Sprite 批量替换工具窗口。
    /// </summary>
    public sealed class PrefabAtlasSpriteRemapWindow : EditorWindow
    {
        private const string MenuPath = "Tools/UI/Prefab 散图引用替换为图集子 Sprite...";
        private const string RevertMenuPath = "Tools/UI/Prefab 撤销图集子 Sprite 为散图引用...";
        private const string ValidateMapMenuPath = "Tools/UI/验证图集映射与子 Sprite 一致性";
        private const string ScanSelectedMenuPath = "Assets/UI/替换选中 Prefab 散图为图集子 Sprite";
        private const string RevertSelectedMenuPath = "Assets/UI/撤销选中 Prefab 图集子 Sprite 为散图";
        private static string DefaultSearchRoot => PrefabAtlasSpriteRemapTool.DefaultPrefabSearchRoot;

        [SerializeField] private string searchRoot = DefaultSearchRoot;
        [SerializeField] private bool includeAllAssets;
        [SerializeField] private int maxLogLines = 200;

        private Vector2 scroll;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private PrefabAtlasSpriteRemapTool.RemapReport lastReport;
        private PrefabAtlasSpriteRemapTool.RevertReport lastRevertReport;
        private bool showRevertLog;

        /// <summary>
        /// 打开工具窗口。
        /// </summary>
        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<PrefabAtlasSpriteRemapWindow>(true, "Prefab 图集 Sprite 替换", true);
            window.minSize = new Vector2(560f, 420f);
        }

        /// <summary>
        /// 打开撤销替换工具窗口。
        /// </summary>
        [MenuItem(RevertMenuPath)]
        private static void OpenRevert()
        {
            var window = GetWindow<PrefabAtlasSpriteRemapWindow>(true, "Prefab 图集 Sprite 替换", true);
            window.minSize = new Vector2(560f, 420f);
            window.showRevertLog = true;
        }

        /// <summary>
        /// 校验 SpriteAtlasPathMap 中每条映射是否能在图集 png 内找到同名 Multiple 子 Sprite。
        /// </summary>
        [MenuItem(ValidateMapMenuPath, false, 2209)]
        private static void ValidateSpriteAtlasMapMenu()
        {
            if (!PrefabAtlasSpriteRemapTool.ValidateSpriteAtlasMap(out PrefabAtlasSpriteRemapTool.MapValidationReport report))
            {
                return;
            }

            string summary = BuildMapValidationSummary(report);
            EditorUtility.DisplayDialog("图集映射校验", summary, "确定");
            Debug.Log("[PrefabAtlasSpriteRemap] " + summary.Replace("\n", " | "));
            if (report.MissingEntries.Count > 0)
            {
                int max = Mathf.Min(report.MissingEntries.Count, 100);
                var sb = new StringBuilder();
                for (int i = 0; i < max; i++)
                {
                    sb.AppendLine("  MISSING: " + report.MissingEntries[i]);
                }

                if (report.MissingEntries.Count > max)
                {
                    sb.AppendLine($"  ... 另有 {report.MissingEntries.Count - max} 条");
                }

                Debug.LogWarning(sb.ToString());
            }
        }

        /// <summary>
        /// 对 Project 中选中的 Prefab 执行替换。
        /// </summary>
        [MenuItem(ScanSelectedMenuPath, false, 2210)]
        private static void ReplaceSelectedPrefabs()
        {
            List<string> paths = CollectSelectedPrefabPaths();
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Prefab 图集 Sprite 替换", "请先选中至少一个 Prefab。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Prefab 图集 Sprite 替换",
                    $"将对 {paths.Count} 个 Prefab 写回图集子 Sprite 引用，是否继续？",
                    "执行",
                    "取消"))
            {
                return;
            }

            if (!PrefabAtlasSpriteRemapTool.ProcessPrefabs(paths, true, out PrefabAtlasSpriteRemapTool.RemapReport report))
            {
                return;
            }

            EditorUtility.DisplayDialog("Prefab 图集 Sprite 替换", BuildSummary(report), "确定");
            Debug.Log(BuildDetailLog(report, 80));
        }

        /// <summary>
        /// 校验是否选中了 Prefab。
        /// </summary>
        [MenuItem(ScanSelectedMenuPath, true)]
        private static bool ValidateReplaceSelectedPrefabs()
        {
            return CollectSelectedPrefabPaths().Count > 0;
        }

        /// <summary>
        /// 对选中的 Prefab 撤销图集子 Sprite，恢复为散图引用。
        /// </summary>
        [MenuItem(RevertSelectedMenuPath, false, 2211)]
        private static void RevertSelectedPrefabs()
        {
            List<string> paths = CollectSelectedPrefabPaths();
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Prefab 撤销图集 Sprite", "请先选中至少一个 Prefab。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Prefab 撤销图集 Sprite",
                    $"将把 {paths.Count} 个 Prefab 中的图集子 Sprite 恢复为 Sprite/ 散图引用，是否继续？",
                    "执行",
                    "取消"))
            {
                return;
            }

            if (!PrefabAtlasSpriteRemapTool.RevertPrefabsToScatteredSprites(paths, true, out PrefabAtlasSpriteRemapTool.RevertReport report))
            {
                return;
            }

            EditorUtility.DisplayDialog("Prefab 撤销图集 Sprite", BuildRevertSummary(report), "确定");
            Debug.Log(BuildRevertDetailLog(report, 80));
        }

        /// <summary>
        /// 校验是否选中了 Prefab。
        /// </summary>
        [MenuItem(RevertSelectedMenuPath, true)]
        private static bool ValidateRevertSelectedPrefabs()
        {
            return CollectSelectedPrefabPaths().Count > 0;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Prefab Sprite 引用：散图 ⇄ 图集子图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "【替换】仅处理 Sprite/ 散图 → 同风格 Atlas 子 Sprite。\n"
                + "【撤销】仅处理 Atlas/ 图集子 Sprite → Sprite/ 同名散图（优先映射表 atlas 目录，再搜同风格子目录）。\n"
                + "已是散图或找不到散图文件时不修改。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            includeAllAssets = EditorGUILayout.ToggleLeft("搜索整个 Assets（否则仅搜索下方目录）", includeAllAssets);
            using (new EditorGUI.DisabledScope(includeAllAssets))
            {
                searchRoot = EditorGUILayout.TextField("Prefab 搜索目录", searchRoot);
            }

            maxLogLines = EditorGUILayout.IntSlider("日志最多显示条数", maxLogLines, 20, 2000);
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("校验映射表", GUILayout.Height(28f)))
                {
                    RunValidateMap();
                }

                if (GUILayout.Button("扫描预览（不写盘）", GUILayout.Height(28f)))
                {
                    RunScan(applyChanges: false);
                }

                if (GUILayout.Button("扫描并替换（写盘）", GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog(
                            "确认替换",
                            "将修改 Prefab 中的 Sprite 引用并保存，建议先执行「扫描预览」。是否继续？",
                            "执行",
                            "取消"))
                    {
                        showRevertLog = false;
                        RunScan(applyChanges: true);
                    }
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("撤销预览（不写盘）", GUILayout.Height(28f)))
                {
                    RunRevert(applyChanges: false);
                }

                if (GUILayout.Button("撤销并写回散图（写盘）", GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog(
                            "确认撤销",
                            "将把图集子 Sprite 引用改回 Sprite/ 散图并保存，是否继续？",
                            "执行",
                            "取消"))
                    {
                        RunRevert(applyChanges: true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            if (!showRevertLog && lastReport != null && lastReport.Records.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("替换明细（节选）", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                int showCount = Mathf.Min(lastReport.Records.Count, maxLogLines);
                for (int i = 0; i < showCount; i++)
                {
                    PrefabAtlasSpriteRemapTool.ReplaceRecord record = lastReport.Records[i];
                    EditorGUILayout.LabelField(
                        $"{record.PrefabPath}\n  {record.PropertyPath}: {record.SpriteName}\n  {record.OldAssetPath} → {record.NewAtlasPath}",
                        EditorStyles.wordWrappedLabel);
                }

                if (lastReport.Records.Count > showCount)
                {
                    EditorGUILayout.LabelField($"... 另有 {lastReport.Records.Count - showCount} 条，详见 Console 日志");
                }

                EditorGUILayout.EndScrollView();
            }

            if (showRevertLog && lastRevertReport != null && lastRevertReport.Records.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("撤销明细（节选）", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                int showCount = Mathf.Min(lastRevertReport.Records.Count, maxLogLines);
                for (int i = 0; i < showCount; i++)
                {
                    PrefabAtlasSpriteRemapTool.RevertRecord record = lastRevertReport.Records[i];
                    EditorGUILayout.LabelField(
                        $"{record.PrefabPath}\n  {record.PropertyPath}: {record.SpriteName}\n  {record.OldAtlasPath} → {record.NewScatteredPath}",
                        EditorStyles.wordWrappedLabel);
                }

                if (lastRevertReport.Records.Count > showCount)
                {
                    EditorGUILayout.LabelField($"... 另有 {lastRevertReport.Records.Count - showCount} 条，详见 Console 日志");
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void RunValidateMap()
        {
            if (!PrefabAtlasSpriteRemapTool.ValidateSpriteAtlasMap(out PrefabAtlasSpriteRemapTool.MapValidationReport report))
            {
                statusMessage = "映射表校验失败，请查看 Console。";
                statusType = MessageType.Error;
                return;
            }

            statusMessage = BuildMapValidationSummary(report);
            statusType = report.MissingSubSpriteCount == 0 ? MessageType.Info : MessageType.Warning;
            if (report.MissingSubSpriteCount > 0)
            {
                Debug.LogWarning(BuildMapValidationDetailLog(report, maxLogLines));
            }
            else
            {
                Debug.Log(BuildMapValidationDetailLog(report, maxLogLines));
            }
        }

        private void RunRevert(bool applyChanges)
        {
            string root = includeAllAssets ? "Assets" : searchRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = "Assets";
            }

            List<string> prefabPaths = PrefabAtlasSpriteRemapTool.CollectPrefabPaths(root);
            if (prefabPaths.Count == 0)
            {
                statusMessage = $"未在 {root} 下找到 Prefab。";
                statusType = MessageType.Warning;
                lastRevertReport = null;
                return;
            }

            if (!PrefabAtlasSpriteRemapTool.RevertPrefabsToScatteredSprites(prefabPaths, applyChanges, out lastRevertReport))
            {
                statusMessage = "撤销失败，请查看 Console。";
                statusType = MessageType.Error;
                return;
            }

            showRevertLog = true;
            lastReport = null;
            statusMessage = BuildRevertSummary(lastRevertReport) + (applyChanges ? "\n已写回 Prefab。" : "\n（预览模式，未写盘）");
            statusType = MessageType.Info;
            Debug.Log(BuildRevertDetailLog(lastRevertReport, maxLogLines));
        }

        private void RunScan(bool applyChanges)
        {
            string root = includeAllAssets ? "Assets" : searchRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = "Assets";
            }

            List<string> prefabPaths = PrefabAtlasSpriteRemapTool.CollectPrefabPaths(root);
            if (prefabPaths.Count == 0)
            {
                statusMessage = $"未在 {root} 下找到 Prefab。";
                statusType = MessageType.Warning;
                lastReport = null;
                return;
            }

            if (!PrefabAtlasSpriteRemapTool.ProcessPrefabs(prefabPaths, applyChanges, out lastReport))
            {
                statusMessage = "处理失败，请查看 Console。";
                statusType = MessageType.Error;
                return;
            }

            showRevertLog = false;
            lastRevertReport = null;
            statusMessage = BuildSummary(lastReport) + (applyChanges ? "\n已写回 Prefab。" : "\n（预览模式，未写盘）");
            statusType = MessageType.Info;
            Debug.Log(BuildDetailLog(lastReport, maxLogLines));
        }

        private static List<string> CollectSelectedPrefabPaths()
        {
            var paths = new List<string>();
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(selection[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static string BuildMapValidationSummary(PrefabAtlasSpriteRemapTool.MapValidationReport report)
        {
            return "映射条目总数: " + report.TotalMappingEntries
                + "\n可解析到同名子 Sprite: " + report.ResolvedSubSpriteCount
                + "\n缺失子 Sprite: " + report.MissingSubSpriteCount
                + (report.MissingSubSpriteCount == 0
                    ? "\n\n与 Prefab 替换逻辑一致（LoadAllAssetsAtPath + 子图名 = 映射表 key = 散图 sprite.name）。"
                    : "\n\n存在缺失时 Prefab 替换也会失败，请先重新生成图集或修正映射表。");
        }

        private static string BuildMapValidationDetailLog(
            PrefabAtlasSpriteRemapTool.MapValidationReport report,
            int maxLines)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[PrefabAtlasSpriteRemap] " + BuildMapValidationSummary(report).Replace("\n", " | "));
            int count = Mathf.Min(report.MissingEntries.Count, maxLines);
            for (int i = 0; i < count; i++)
            {
                sb.AppendLine("  MISSING: " + report.MissingEntries[i]);
            }

            return sb.ToString();
        }

        private static string BuildSummary(PrefabAtlasSpriteRemapTool.RemapReport report)
        {
            return "扫描 Prefab: " + report.PrefabCount
                + "\n将修改/已修改 Prefab: " + report.ModifiedPrefabCount
                + "\n替换引用数: " + report.ReplacedReferenceCount
                + "\n跳过（非散图）: " + report.SkippedNotScattered
                + "\n跳过（映射表无此 Sprite）: " + report.SkippedNoMapping
                + "\n跳过（无同风格映射）: " + report.SkippedNoSameStyleMapping
                + "\n跳过（同风格图集无同名子图）: " + report.SkippedNoSubSprite;
        }

        private static string BuildRevertSummary(PrefabAtlasSpriteRemapTool.RevertReport report)
        {
            return "扫描 Prefab: " + report.PrefabCount
                + "\n将修改/已修改 Prefab: " + report.ModifiedPrefabCount
                + "\n撤销引用数: " + report.RevertedReferenceCount
                + "\n跳过（已是散图）: " + report.SkippedAlreadyScattered
                + "\n跳过（非图集子 Sprite）: " + report.SkippedNotAtlas
                + "\n跳过（找不到散图）: " + report.SkippedNoScattered;
        }

        private static string BuildRevertDetailLog(PrefabAtlasSpriteRemapTool.RevertReport report, int maxLines)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[PrefabAtlasSpriteRemap] REVERT " + BuildRevertSummary(report).Replace("\n", " | "));
            int count = Mathf.Min(report.Records.Count, maxLines);
            for (int i = 0; i < count; i++)
            {
                PrefabAtlasSpriteRemapTool.RevertRecord r = report.Records[i];
                sb.AppendLine($"  {r.PrefabPath} :: {r.PropertyPath} :: {r.SpriteName} :: {r.OldAtlasPath} => {r.NewScatteredPath}");
            }

            if (report.Records.Count > count)
            {
                sb.AppendLine($"  ... truncated {report.Records.Count - count} lines");
            }

            return sb.ToString();
        }

        private static string BuildDetailLog(PrefabAtlasSpriteRemapTool.RemapReport report, int maxLines)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[PrefabAtlasSpriteRemap] " + BuildSummary(report).Replace("\n", " | "));
            int count = Mathf.Min(report.Records.Count, maxLines);
            for (int i = 0; i < count; i++)
            {
                PrefabAtlasSpriteRemapTool.ReplaceRecord r = report.Records[i];
                sb.AppendLine($"  {r.PrefabPath} :: {r.PropertyPath} :: {r.SpriteName} :: {r.OldAssetPath} => {r.NewAtlasPath}");
            }

            if (report.Records.Count > count)
            {
                sb.AppendLine($"  ... truncated {report.Records.Count - count} lines");
            }

            return sb.ToString();
        }
    }
}

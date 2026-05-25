using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.ScatteredSpriteBorderToAtlas
{
    /// <summary>
    /// 散图九宫格同步到图集 Multiple 子 Sprite 工具窗口。
    /// </summary>
    public sealed class ScatteredSpriteBorderToAtlasWindow : EditorWindow
    {
        private const string MenuPath = "Tools/UI/散图九宫格同步到图集子 Sprite...";
        private const string SyncSelectedAtlasMenuPath = "Assets/UI/同步散图九宫格到选中图集";
        private const string SyncSelectedSpriteFolderMenuPath = "Assets/UI/同步散图九宫格到对应图集";

        [SerializeField] private bool onlyIfAtlasMissingBorder = true;
        [SerializeField] private int maxLogLines = 200;

        private Vector2 scroll;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private ScatteredSpriteBorderToAtlasTool.ApplyReport lastReport;

        /// <summary>
        /// 打开工具窗口。
        /// </summary>
        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<ScatteredSpriteBorderToAtlasWindow>(true, "散图九宫格同步图集", true);
            window.minSize = new Vector2(560f, 400f);
        }

        /// <summary>
        /// 对选中的图集贴图执行同步。
        /// </summary>
        [MenuItem(SyncSelectedAtlasMenuPath, false, 2211)]
        private static void SyncSelectedAtlasesMenu()
        {
            var atlasPaths = CollectSelectedAtlasTexturePaths();
            if (atlasPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("散图九宫格同步图集", "请先选中至少一张图集 png。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "散图九宫格同步图集",
                    $"将把散图九宫格写入 {atlasPaths.Count} 张图集的 Multiple 子 Sprite，是否继续？",
                    "执行",
                    "取消"))
            {
                return;
            }

            if (!ScatteredSpriteBorderToAtlasTool.ApplyForAtlasTextures(
                    atlasPaths,
                    true,
                    true,
                    out ScatteredSpriteBorderToAtlasTool.ApplyReport report))
            {
                return;
            }

            EditorUtility.DisplayDialog("散图九宫格同步图集", BuildSummary(report), "确定");
            Debug.Log("[ScatteredSpriteBorderToAtlas] " + BuildSummary(report).Replace("\n", " | "));
        }

        /// <summary>
        /// 校验选中图集菜单是否可用。
        /// </summary>
        /// <returns>选中图集时返回 true。</returns>
        [MenuItem(SyncSelectedAtlasMenuPath, true)]
        private static bool ValidateSyncSelectedAtlasesMenu()
        {
            return CollectSelectedAtlasTexturePaths().Count > 0;
        }

        /// <summary>
        /// 对选中的散图目录执行同步。
        /// </summary>
        [MenuItem(SyncSelectedSpriteFolderMenuPath, false, 2212)]
        private static void SyncSelectedSpriteFoldersMenu()
        {
            var folderPaths = CollectSelectedScatteredFolderPaths();
            if (folderPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("散图九宫格同步图集", "请先选中 Sprite 风格下的散图目录。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "散图九宫格同步图集",
                    $"将把 {folderPaths.Count} 个散图目录的九宫格同步到对应图集，是否继续？",
                    "执行",
                    "取消"))
            {
                return;
            }

            if (!ScatteredSpriteBorderToAtlasTool.ApplyForScatteredFolders(
                    folderPaths,
                    true,
                    true,
                    out ScatteredSpriteBorderToAtlasTool.ApplyReport report))
            {
                return;
            }

            EditorUtility.DisplayDialog("散图九宫格同步图集", BuildSummary(report), "确定");
            Debug.Log("[ScatteredSpriteBorderToAtlas] " + BuildSummary(report).Replace("\n", " | "));
        }

        /// <summary>
        /// 校验选中散图目录菜单是否可用。
        /// </summary>
        /// <returns>选中有效散图目录时返回 true。</returns>
        [MenuItem(SyncSelectedSpriteFolderMenuPath, true)]
        private static bool ValidateSyncSelectedSpriteFoldersMenu()
        {
            return CollectSelectedScatteredFolderPaths().Count > 0;
        }

        /// <summary>
        /// 绘制编辑器窗口。
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("散图九宫格 → 图集 Multiple 子 Sprite", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "从 Assets/GameRes/UI/Sprite 散图读取 TextureImporter 九宫格，"
                + "按打图集时的透明裁剪规则换算后写入 SpriteAtlasPathMap 对应图集子 Sprite。\n"
                + "散图路径：Sprite/{风格}/{图集目录名}/{sprite}.png",
                MessageType.Info);

            onlyIfAtlasMissingBorder = EditorGUILayout.ToggleLeft(
                "仅当图集子 Sprite 尚无九宫格时写入（推荐）",
                onlyIfAtlasMissingBorder);
            maxLogLines = EditorGUILayout.IntField("日志最多显示条数", Mathf.Max(10, maxLogLines));

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览（不写回）", GUILayout.Height(28f)))
                {
                    RunApply(false);
                }

                if (GUILayout.Button("执行同步", GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog(
                            "散图九宫格同步图集",
                            "将按映射表全量同步九宫格到图集子 Sprite，是否继续？",
                            "执行",
                            "取消"))
                    {
                        RunApply(true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            if (lastReport == null)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("上次结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(BuildSummary(lastReport), EditorStyles.wordWrappedLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            var showCount = Mathf.Min(lastReport.Records.Count, maxLogLines);
            for (var i = 0; i < showCount; i++)
            {
                var record = lastReport.Records[i];
                EditorGUILayout.LabelField(
                    $"{record.SpriteName} ({record.StyleName})\n"
                    + $"  散图: {record.ScatteredPath}\n"
                    + $"  图集: {record.AtlasPath}\n"
                    + $"  border: {FormatBorder(record.OldBorder)} → {FormatBorder(record.NewBorder)}",
                    EditorStyles.wordWrappedMiniLabel);
            }

            if (lastReport.Records.Count > showCount)
            {
                EditorGUILayout.LabelField($"... 另有 {lastReport.Records.Count - showCount} 条", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 执行全量映射表同步。
        /// </summary>
        /// <param name="applyChanges">是否写回资源。</param>
        private void RunApply(bool applyChanges)
        {
            if (!ScatteredSpriteBorderToAtlasTool.ApplyFromSpriteAtlasMap(
                    applyChanges,
                    onlyIfAtlasMissingBorder,
                    out lastReport))
            {
                statusMessage = "同步失败，请查看 Console。";
                statusType = MessageType.Error;
                return;
            }

            statusMessage = BuildSummary(lastReport);
            statusType = MessageType.Info;
            Repaint();

            var log = BuildDetailLog(lastReport, maxLogLines);
            if (applyChanges)
            {
                Debug.Log(log);
            }
            else
            {
                Debug.Log("[预览] " + log);
            }
        }

        /// <summary>
        /// 收集选中的图集贴图路径。
        /// </summary>
        /// <returns>图集 Asset 路径列表。</returns>
        private static List<string> CollectSelectedAtlasTexturePaths()
        {
            var paths = new List<string>();
            var used = new HashSet<string>();
            var selected = Selection.GetFiltered<Object>(SelectionMode.Assets);
            for (var i = 0; i < selected.Length; i++)
            {
                var assetPath = AssetDatabase.GetAssetPath(selected[i]);
                if (string.IsNullOrEmpty(assetPath) || used.Contains(assetPath))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { assetPath });
                    for (var j = 0; j < guids.Length; j++)
                    {
                        var texPath = AssetDatabase.GUIDToAssetPath(guids[j]);
                        if (texPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                            && AssetImporter.GetAtPath(texPath) is TextureImporter importer
                            && importer.spriteImportMode == SpriteImportMode.Multiple
                            && used.Add(texPath))
                        {
                            paths.Add(texPath);
                        }
                    }

                    continue;
                }

                if (assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                    && AssetImporter.GetAtPath(assetPath) is TextureImporter ti
                    && ti.spriteImportMode == SpriteImportMode.Multiple
                    && used.Add(assetPath))
                {
                    paths.Add(assetPath);
                }
            }

            return paths;
        }

        /// <summary>
        /// 收集选中的散图目录路径。
        /// </summary>
        /// <returns>散图目录 Asset 路径列表。</returns>
        private static List<string> CollectSelectedScatteredFolderPaths()
        {
            var paths = new List<string>();
            var used = new HashSet<string>();
            var selected = Selection.GetFiltered<Object>(SelectionMode.Assets);
            for (var i = 0; i < selected.Length; i++)
            {
                var assetPath = AssetDatabase.GetAssetPath(selected[i]);
                if (string.IsNullOrEmpty(assetPath) || !AssetDatabase.IsValidFolder(assetPath) || used.Contains(assetPath))
                {
                    continue;
                }

                if (assetPath.StartsWith(ScatteredSpriteBorderToAtlasTool.SpriteRoot, System.StringComparison.OrdinalIgnoreCase)
                    && used.Add(assetPath))
                {
                    paths.Add(assetPath);
                }
            }

            return paths;
        }

        /// <summary>
        /// 构建汇总文本。
        /// </summary>
        /// <param name="report">同步报告。</param>
        /// <returns>汇总字符串。</returns>
        private static string BuildSummary(ScatteredSpriteBorderToAtlasTool.ApplyReport report)
        {
            return "映射条目: " + report.MappingEntryCount
                + "\n涉及图集: " + report.AtlasTextureCount
                + "\n已修改图集: " + report.ModifiedAtlasCount
                + "\n已写入子 Sprite: " + report.AppliedCount
                + "\n跳过（无散图）: " + report.SkippedNoScattered
                + "\n跳过（散图无九宫格）: " + report.SkippedNoBorderOnScattered
                + "\n跳过（图集无同名子图）: " + report.SkippedNoAtlasSubSprite
                + "\n跳过（图集已有九宫格）: " + report.SkippedAlreadyHasBorder
                + "\n跳过（未变化）: " + report.SkippedUnchanged;
        }

        /// <summary>
        /// 构建详细日志。
        /// </summary>
        /// <param name="report">同步报告。</param>
        /// <param name="maxLines">最多行数。</param>
        /// <returns>日志文本。</returns>
        private static string BuildDetailLog(ScatteredSpriteBorderToAtlasTool.ApplyReport report, int maxLines)
        {
            var sb = new StringBuilder();
            sb.Append("[ScatteredSpriteBorderToAtlas] ").Append(BuildSummary(report).Replace("\n", " | "));
            var count = Mathf.Min(report.Records.Count, maxLines);
            for (var i = 0; i < count; i++)
            {
                var r = report.Records[i];
                sb.Append("\n  ").Append(r.SpriteName)
                    .Append(" :: ").Append(r.StyleName)
                    .Append(" :: ").Append(r.ScatteredPath)
                    .Append(" => ").Append(r.AtlasPath)
                    .Append(" :: ").Append(FormatBorder(r.OldBorder))
                    .Append(" -> ").Append(FormatBorder(r.NewBorder));
            }

            if (report.Records.Count > count)
            {
                sb.Append("\n  ... 另有 ").Append(report.Records.Count - count).Append(" 条");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 格式化 border 为可读字符串。
        /// </summary>
        /// <param name="border">Unity border。</param>
        /// <returns>格式化文本。</returns>
        private static string FormatBorder(Vector4 border)
        {
            return $"({border.x:0.##},{border.y:0.##},{border.z:0.##},{border.w:0.##})";
        }
    }
}

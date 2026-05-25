using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.TexturePackerClone
{
    /// <summary>
    /// 纯 C# TexturePacker 兼容导出工具入口。
    /// </summary>
    public sealed class TpCloneAtlasBuilderWindow : EditorWindow
    {
        private const string WindowMenuPath = "Tools/TexturePackerClone/生成 UI 图集...";
        private const string GenerateSelectedMenuPath = "Assets/TexturePackerClone/生成选中 Sprite 目录图集";
        private const string CompareSelectedMenuPath = "Assets/TexturePackerClone/对照比较选中 Sprite 目录";
        private const string SetSelectedAstc6X6MenuPath = "Assets/TexturePackerClone/设置选中图集 ASTC 6x6";
        private const string UiSpriteRootFolder = "Assets/GameRes/UI/Sprite";
        private const string UiAtlasRootFolder = "Assets/GameRes/UI/Atlas";
        private const string SpriteAtlasMapPath = UiAtlasRootFolder + "/SpriteAtlasPathMap.json";
        private const float DefaultPixelsPerUnit = 100f;

        [SerializeField] private string spriteFolderPath = "Assets/GameRes/UI/Sprite/WarmStyle/Button";
        [SerializeField] private string outputFolderPath = "Assets/GameRes/UI/Atlas/WarmStyle";
        [SerializeField] private float pixelsPerUnit = DefaultPixelsPerUnit;
        [SerializeField] private bool remapPrefabsAfterBuild = true;

        private Vector2 scroll;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private readonly List<string> logs = new List<string>();

        /// <summary>
        /// 打开 clone 图集生成窗口。
        /// </summary>
        [MenuItem(WindowMenuPath)]
        private static void Open()
        {
            var window = GetWindow<TpCloneAtlasBuilderWindow>(true, "TexturePackerClone", true);
            window.minSize = new Vector2(560f, 360f);
            window.remapPrefabsAfterBuild = EditorPrefs.GetBool(TpCloneAtlasPostBuild.RemapPrefabsPrefKey, true);
        }

        /// <summary>
        /// 启用窗口时同步 EditorPrefs。
        /// </summary>
        private void OnEnable()
        {
            remapPrefabsAfterBuild = EditorPrefs.GetBool(TpCloneAtlasPostBuild.RemapPrefabsPrefKey, true);
        }

        /// <summary>
        /// 从 Project 面板选中的 Sprite 目录生成 UI 图集。
        /// </summary>
        [MenuItem(GenerateSelectedMenuPath, false, 2201)]
        private static void GenerateSelectedSpriteFolderAtlas()
        {
            try
            {
                var result = GenerateUiAtlasFromSpriteFolder(GetSelectedAssetFolder(), DefaultPixelsPerUnit, false);
                var atlasAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.AtlasPath);
                if (atlasAsset != null)
                {
                    Selection.activeObject = atlasAsset;
                    EditorGUIUtility.PingObject(atlasAsset);
                }

                EditorUtility.DisplayDialog(
                    "TexturePackerClone UI 图集生成",
                    $"生成完成: {result.AtlasPath}\nSprite 数量: {result.SpriteCount}",
                    "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("TexturePackerClone UI 图集生成", $"生成失败: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 校验 Project 面板当前选择是否可以生成 UI 图集。
        /// </summary>
        /// <returns>当前选择有效时返回 true。</returns>
        [MenuItem(GenerateSelectedMenuPath, true)]
        private static bool ValidateGenerateSelectedSpriteFolderAtlas()
        {
            return TryGetSelectedAssetFolder(out var folderPath) &&
                TryResolveUiAtlasPaths(folderPath, out _, out _, out _);
        }

        /// <summary>
        /// 对照 TexturePacker CLI 输出和 clone 输出。
        /// </summary>
        [MenuItem(CompareSelectedMenuPath, false, 2202)]
        private static void CompareSelectedSpriteFolderAtlas()
        {
            try
            {
                var report = TpCloneComparison.CompareWithTexturePacker(GetSelectedAssetFolder());
                Debug.Log(report.ToLogString());
                EditorUtility.DisplayDialog(
                    "TexturePackerClone 对照比较",
                    report.ToDialogString(),
                    "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("TexturePackerClone 对照比较", $"比较失败: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 校验 Project 面板当前选择是否可以执行对照比较。
        /// </summary>
        /// <returns>当前选择有效时返回 true。</returns>
        [MenuItem(CompareSelectedMenuPath, true)]
        private static bool ValidateCompareSelectedSpriteFolderAtlas()
        {
            return ValidateGenerateSelectedSpriteFolderAtlas();
        }

        /// <summary>
        /// 将 Project 面板选中的图集或目录批量设置为 ASTC 6x6。
        /// </summary>
        [MenuItem(SetSelectedAstc6X6MenuPath, false, 2203)]
        private static void SetSelectedAtlasAstc6X6()
        {
            try
            {
                var changedCount = TpCloneUnityImporter.ApplyAstc6X6ToSelectedTextures();
                EditorUtility.DisplayDialog(
                    "TexturePackerClone 图集压缩",
                    $"已设置 ASTC 6x6: {changedCount} 张贴图",
                    "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("TexturePackerClone 图集压缩", $"设置失败: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 校验 Project 面板当前选择是否包含可设置的贴图。
        /// </summary>
        [MenuItem(SetSelectedAstc6X6MenuPath, true)]
        private static bool ValidateSetSelectedAtlasAstc6X6()
        {
            return TpCloneUnityImporter.HasSelectedTextureAssets();
        }

        /// <summary>
        /// 绘制编辑器窗口。
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("输入", EditorStyles.boldLabel);
            spriteFolderPath = EditorGUILayout.TextField("Sprite 目录", spriteFolderPath);
            EditorGUILayout.BeginHorizontal();
            outputFolderPath = EditorGUILayout.TextField("输出目录", outputFolderPath);
            if (GUILayout.Button("浏览...", GUILayout.Width(70f)))
            {
                BrowseOutputFolder();
            }
            EditorGUILayout.EndHorizontal();
            pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
            var newRemapPrefabs = EditorGUILayout.ToggleLeft(
                "打图集后自动将 Prefab 散图引用替换为图集子 Sprite",
                remapPrefabsAfterBuild);
            if (newRemapPrefabs != remapPrefabsAfterBuild)
            {
                remapPrefabsAfterBuild = newRemapPrefabs;
                EditorPrefs.SetBool(TpCloneAtlasPostBuild.RemapPrefabsPrefKey, remapPrefabsAfterBuild);
            }

            EditorGUILayout.HelpBox(
                $"输出目录可以直接填写 Assets 下路径，也可以点击“浏览...”选择已有目录。\n" +
                $"窗口构建会把 PNG/JSON 输出到指定目录并导入为 Multiple Sprite。\n" +
                $"勾选上方选项时，会扫描引用本次散图目录的 UI Prefab 并写回图集子 Sprite。\n" +
                $"右键菜单仍按 {UiSpriteRootFolder}/{{Style}}/{{AtlasName}} -> {UiAtlasRootFolder}/{{Style}} 推导输出目录。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成 UI 图集", GUILayout.Height(30f)))
            {
                RunBuild();
            }

            if (GUILayout.Button("使用当前选择", GUILayout.Width(120f), GUILayout.Height(30f)))
            {
                UseSelection();
            }

            if (GUILayout.Button("选中输出目录", GUILayout.Width(120f), GUILayout.Height(30f)))
            {
                SelectOutputFolder();
            }
            EditorGUILayout.EndHorizontal();

            /*
            if (GUILayout.Button("对照 TexturePacker 输出", GUILayout.Height(26f)))
            {
                RunComparison();
            }
            */
            
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (var i = 0; i < logs.Count; i++)
            {
                EditorGUILayout.LabelField(logs[i], EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 执行窗口里的构建流程。
        /// </summary>
        private void RunBuild()
        {
            try
            {
                logs.Clear();
                var result = GenerateAtlasFromSpriteFolder(spriteFolderPath, outputFolderPath, pixelsPerUnit, false);
                logs.Add($"{result.AtlasName}: {result.SpriteCount} sprites -> {result.AtlasPath}");
                statusMessage = $"生成完成: {result.AtlasPath}";
                statusType = MessageType.Info;
            }
            catch (Exception e)
            {
                statusMessage = $"生成失败: {e.Message}";
                statusType = MessageType.Error;
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 执行窗口里的对照比较流程。
        /// </summary>
        private void RunComparison()
        {
            try
            {
                logs.Clear();
                var report = TpCloneComparison.CompareWithTexturePacker(spriteFolderPath);
                logs.Add(report.ToLogString());
                statusMessage = report.IsMatch ? "对照通过，坐标和像素完全一致。" : "对照完成，存在差异，详见日志。";
                statusType = report.IsMatch ? MessageType.Info : MessageType.Warning;
            }
            catch (Exception e)
            {
                statusMessage = $"比较失败: {e.Message}";
                statusType = MessageType.Error;
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 使用当前 Project 面板选中的目录。
        /// </summary>
        private void UseSelection()
        {
            try
            {
                spriteFolderPath = GetSelectedAssetFolder();
                if (TryResolveUiAtlasPaths(spriteFolderPath, out var resolvedOutputFolderPath, out _, out _))
                {
                    outputFolderPath = resolvedOutputFolderPath;
                }

                statusMessage = $"已选择: {spriteFolderPath}";
                statusType = MessageType.Info;
            }
            catch (Exception e)
            {
                statusMessage = e.Message;
                statusType = MessageType.Warning;
            }
        }

        /// <summary>
        /// 使用当前 Project 面板选中的目录作为输出目录。
        /// </summary>
        private void SelectOutputFolder()
        {
            try
            {
                outputFolderPath = GetSelectedAssetFolder();
                statusMessage = $"已设置输出目录: {outputFolderPath}";
                statusType = MessageType.Info;
            }
            catch (Exception e)
            {
                statusMessage = e.Message;
                statusType = MessageType.Warning;
            }
        }

        /// <summary>
        /// 打开目录选择器设置输出目录。
        /// </summary>
        private void BrowseOutputFolder()
        {
            var startFolder = ResolveFolderPanelStartPath(outputFolderPath);
            var selectedFolder = EditorUtility.OpenFolderPanel("选择 TexturePackerClone 输出目录", startFolder, string.Empty);
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            if (!TryConvertToAssetPath(selectedFolder, out var selectedAssetPath))
            {
                statusMessage = "输出目录必须位于当前 Unity 项目的 Assets 目录下。";
                statusType = MessageType.Warning;
                return;
            }

            outputFolderPath = selectedAssetPath;
            statusMessage = $"已设置输出目录: {outputFolderPath}";
            statusType = MessageType.Info;
        }

        /// <summary>
        /// 从 UI Sprite 目录生成对应风格目录下的 Multiple Sprite 图集。
        /// </summary>
        /// <param name="sourceFolderPath">UI Sprite 目录路径。</param>
        /// <param name="ppu">生成 Sprite 的 Pixels Per Unit。</param>
        /// <param name="showDialog">是否弹出完成提示。</param>
        /// <returns>构建结果。</returns>
        public static BuildResult GenerateUiAtlasFromSpriteFolder(string sourceFolderPath, float ppu, bool showDialog)
        {
            try
            {
                ValidateAssetFolder(sourceFolderPath, nameof(sourceFolderPath));
                if (ppu <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(ppu), "Pixels Per Unit 必须大于 0。");
                }

                if (!TryResolveUiAtlasPaths(sourceFolderPath, out var outputFolderPath, out var atlasName, out var error))
                {
                    throw new ArgumentException(error, nameof(sourceFolderPath));
                }

                var result = GenerateAtlasFromSpriteFolder(sourceFolderPath, outputFolderPath, atlasName, ppu);
                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "TexturePackerClone UI 图集生成",
                        $"生成完成: {result.AtlasPath}\nSprite 数量: {result.SpriteCount}",
                        "OK");
                }

                return result;
            }
            catch (Exception e)
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("TexturePackerClone UI 图集生成", $"生成失败: {e.Message}", "OK");
                }

                Debug.LogException(e);
                throw;
            }
        }

        /// <summary>
        /// 从 Sprite 目录生成 Multiple Sprite 图集到指定输出目录。
        /// </summary>
        /// <param name="sourceFolderPath">Sprite 目录路径。</param>
        /// <param name="outputFolderPath">图集输出目录路径。</param>
        /// <param name="ppu">生成 Sprite 的 Pixels Per Unit。</param>
        /// <param name="showDialog">是否弹出完成提示。</param>
        /// <returns>构建结果。</returns>
        public static BuildResult GenerateAtlasFromSpriteFolder(
            string sourceFolderPath,
            string outputFolderPath,
            float ppu,
            bool showDialog)
        {
            try
            {
                ValidateAssetFolder(sourceFolderPath, nameof(sourceFolderPath));
                ValidateAssetFolderPath(outputFolderPath, nameof(outputFolderPath));
                if (ppu <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(ppu), "Pixels Per Unit 必须大于 0。");
                }

                var atlasName = Path.GetFileName(TpCloneAssetUtility.NormalizeAssetPath(sourceFolderPath).TrimEnd('/'));
                if (string.IsNullOrEmpty(atlasName))
                {
                    throw new ArgumentException($"无法从 Sprite 目录推导图集名: {sourceFolderPath}", nameof(sourceFolderPath));
                }

                var result = GenerateAtlasFromSpriteFolder(sourceFolderPath, outputFolderPath, atlasName, ppu);
                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "TexturePackerClone 图集生成",
                        $"生成完成: {result.AtlasPath}\nSprite 数量: {result.SpriteCount}",
                        "OK");
                }

                return result;
            }
            catch (Exception e)
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("TexturePackerClone 图集生成", $"生成失败: {e.Message}", "OK");
                }

                Debug.LogException(e);
                throw;
            }
        }

        /// <summary>
        /// 执行 clone 图集导出和 Unity 导入。
        /// </summary>
        /// <param name="sourceFolderPath">Sprite 目录路径。</param>
        /// <param name="outputFolderPath">图集输出目录路径。</param>
        /// <param name="atlasName">图集名。</param>
        /// <param name="ppu">生成 Sprite 的 Pixels Per Unit。</param>
        /// <returns>构建结果。</returns>
        private static BuildResult GenerateAtlasFromSpriteFolder(
            string sourceFolderPath,
            string outputFolderPath,
            string atlasName,
            float ppu)
        {
            var normalizedOutputFolderPath = TpCloneAssetUtility.NormalizeAssetPath(outputFolderPath).TrimEnd('/');
            TpCloneAssetUtility.EnsureAssetFolder(normalizedOutputFolderPath);
            var jsonPath = $"{normalizedOutputFolderPath}/{atlasName}.json";
            var imagePath = $"{normalizedOutputFolderPath}/{atlasName}.png";
            var exportResult = TpCloneExporter.ExportToAssetPaths(sourceFolderPath, jsonPath, imagePath);
            AssetDatabase.Refresh();
            TpCloneUnityImporter.ImportSpriteSheet(imagePath, jsonPath, ppu);
            TpCloneSpriteAtlasMapWriter.Append(SpriteAtlasMapPath, imagePath, atlasName, exportResult.SpriteNames);
            AssetDatabase.DeleteAsset(jsonPath);
            AssetDatabase.Refresh();

            TryExtractStyleFromSourceFolder(sourceFolderPath, out var styleName);
            var buildResult = new BuildResult(
                atlasName,
                exportResult.SpriteCount,
                imagePath,
                sourceFolderPath,
                styleName ?? string.Empty);
            TpCloneAtlasPostBuild.RaiseCompleted(new TpCloneAtlasPostBuild.Context(
                sourceFolderPath,
                imagePath,
                atlasName,
                styleName ?? string.Empty,
                exportResult.SpriteCount));
            return buildResult;
        }

        /// <summary>
        /// 从 UI Sprite 源目录解析风格名。
        /// </summary>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <param name="styleName">风格名。</param>
        /// <returns>解析成功时返回 true。</returns>
        private static bool TryExtractStyleFromSourceFolder(string sourceFolderPath, out string styleName)
        {
            styleName = null;
            var normalized = TpCloneAssetUtility.NormalizeAssetPath(sourceFolderPath).TrimEnd('/');
            var spriteRootPrefix = $"{UiSpriteRootFolder}/";
            if (!normalized.StartsWith(spriteRootPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var relativePath = normalized.Substring(spriteRootPrefix.Length);
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            styleName = parts[0];
            return !string.IsNullOrEmpty(styleName);
        }

        /// <summary>
        /// 获取当前 Project 面板选中的 Asset 目录。
        /// </summary>
        /// <returns>选中的 Asset 目录路径。</returns>
        private static string GetSelectedAssetFolder()
        {
            if (TryGetSelectedAssetFolder(out var folderPath))
            {
                return folderPath;
            }

            throw new ArgumentException("请在 Project 面板选中一个 Assets 下的目录。");
        }

        /// <summary>
        /// 尝试获取当前 Project 面板选中的 Asset 目录。
        /// </summary>
        /// <param name="folderPath">选中的 Asset 目录路径。</param>
        /// <returns>选择有效时返回 true。</returns>
        private static bool TryGetSelectedAssetFolder(out string folderPath)
        {
            folderPath = string.Empty;
            if (Selection.activeObject == null)
            {
                return false;
            }

            var selectedPath = TpCloneAssetUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(Selection.activeObject));
            if (string.IsNullOrEmpty(selectedPath) || !AssetDatabase.IsValidFolder(selectedPath))
            {
                return false;
            }

            folderPath = selectedPath;
            return true;
        }

        /// <summary>
        /// 根据 UI Sprite 目录推导对应风格的图集输出目录和图集名。
        /// </summary>
        /// <param name="sourceFolderPath">UI Sprite 目录路径。</param>
        /// <param name="outputFolderPath">图集输出目录。</param>
        /// <param name="atlasName">图集名。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>路径可推导时返回 true。</returns>
        private static bool TryResolveUiAtlasPaths(
            string sourceFolderPath,
            out string outputFolderPath,
            out string atlasName,
            out string error)
        {
            outputFolderPath = string.Empty;
            atlasName = string.Empty;
            error = string.Empty;

            var normalized = TpCloneAssetUtility.NormalizeAssetPath(sourceFolderPath);
            var spriteRootPrefix = $"{UiSpriteRootFolder}/";
            if (!normalized.StartsWith(spriteRootPrefix, StringComparison.Ordinal))
            {
                error = $"请选择 {UiSpriteRootFolder}/{{Style}}/{{AtlasName}} 下的 Sprite 目录。当前目录: {sourceFolderPath}";
                return false;
            }

            var relativePath = normalized.Substring(spriteRootPrefix.Length);
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = $"请选择风格目录下的具体图集 Sprite 目录，例如 {UiSpriteRootFolder}/DefaultStyle/Button。";
                return false;
            }

            outputFolderPath = $"{UiAtlasRootFolder}/{parts[0]}";
            atlasName = parts[parts.Length - 1];
            return true;
        }

        /// <summary>
        /// 验证输入目录必须已经存在。
        /// </summary>
        /// <param name="folderPath">Asset 目录路径。</param>
        /// <param name="paramName">参数名。</param>
        private static void ValidateAssetFolder(string folderPath, string paramName)
        {
            if (string.IsNullOrEmpty(folderPath) ||
                !folderPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !AssetDatabase.IsValidFolder(folderPath))
            {
                throw new ArgumentException($"目录不存在或不是 Assets 下目录: {folderPath}", paramName);
            }
        }

        /// <summary>
        /// 验证输出目录路径必须位于 Assets 下，目录可不存在。
        /// </summary>
        /// <param name="folderPath">Asset 目录路径。</param>
        /// <param name="paramName">参数名。</param>
        private static void ValidateAssetFolderPath(string folderPath, string paramName)
        {
            var normalized = TpCloneAssetUtility.NormalizeAssetPath(folderPath).TrimEnd('/');
            if (string.IsNullOrEmpty(normalized) ||
                normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"输出目录必须位于 Assets 下: {folderPath}", paramName);
            }
        }

        /// <summary>
        /// 获取目录选择器的起始磁盘路径。
        /// </summary>
        /// <param name="assetPath">Asset 目录路径。</param>
        /// <returns>磁盘目录路径。</returns>
        private static string ResolveFolderPanelStartPath(string assetPath)
        {
            var normalized = TpCloneAssetUtility.NormalizeAssetPath(assetPath).TrimEnd('/');
            if (!string.IsNullOrEmpty(normalized) &&
                (normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                return TpCloneAssetUtility.ToAbsolutePath(normalized);
            }

            return Application.dataPath;
        }

        /// <summary>
        /// 将当前项目 Assets 下的磁盘路径转换为 Asset 路径。
        /// </summary>
        /// <param name="absolutePath">磁盘路径。</param>
        /// <param name="assetPath">转换后的 Asset 路径。</param>
        /// <returns>路径位于 Assets 下时返回 true。</returns>
        private static bool TryConvertToAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets";
                return true;
            }

            var rootPrefix = assetsRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetPath = "Assets/" + fullPath.Substring(rootPrefix.Length).Replace('\\', '/');
            return true;
        }
    }

    /// <summary>
    /// clone 图集导出管线。
    /// </summary>
    internal static class TpCloneExporter
    {
        private const int DefaultMaxSize = 4096;
        private const byte AlphaThreshold = 1;
        private const int TrimMargin = 1;

        /// <summary>
        /// 导出到 Unity Asset 路径。
        /// </summary>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <param name="jsonAssetPath">输出 JSON Asset 路径。</param>
        /// <param name="imageAssetPath">输出 PNG Asset 路径。</param>
        /// <returns>导出结果。</returns>
        public static ExportResult ExportToAssetPaths(string sourceFolderPath, string jsonAssetPath, string imageAssetPath)
        {
            return Export(
                sourceFolderPath,
                TpCloneAssetUtility.ToAbsolutePath(jsonAssetPath),
                TpCloneAssetUtility.ToAbsolutePath(imageAssetPath),
                Path.GetFileName(imageAssetPath));
        }

        /// <summary>
        /// 导出到磁盘绝对路径。
        /// </summary>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <param name="jsonAbsolutePath">输出 JSON 绝对路径。</param>
        /// <param name="imageAbsolutePath">输出 PNG 绝对路径。</param>
        /// <returns>导出结果。</returns>
        public static ExportResult ExportToAbsolutePaths(
            string sourceFolderPath,
            string jsonAbsolutePath,
            string imageAbsolutePath)
        {
            return Export(sourceFolderPath, jsonAbsolutePath, imageAbsolutePath, Path.GetFileName(imageAbsolutePath));
        }

        /// <summary>
        /// 执行完整导出流程。
        /// </summary>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <param name="jsonAbsolutePath">输出 JSON 绝对路径。</param>
        /// <param name="imageAbsolutePath">输出 PNG 绝对路径。</param>
        /// <param name="imageName">写入 JSON meta.image 的图片名。</param>
        /// <returns>导出结果。</returns>
        private static ExportResult Export(
            string sourceFolderPath,
            string jsonAbsolutePath,
            string imageAbsolutePath,
            string imageName)
        {
            var sprites = TpCloneImageScanner.Scan(sourceFolderPath, AlphaThreshold, TrimMargin);
            if (sprites.Count == 0)
            {
                throw new InvalidDataException($"目录下没有可打包图片: {sourceFolderPath}");
            }

            var atlas = TpCloneMaxRectsPacker.Pack(sprites, DefaultMaxSize);
            TpCloneAtlasComposer.WritePng(atlas, imageAbsolutePath);
            TpCloneJsonWriter.WriteJson(atlas, imageName, jsonAbsolutePath);
            return new ExportResult(
                atlas.Frames.Count,
                jsonAbsolutePath,
                imageAbsolutePath,
                CreateSpriteNames(atlas));
        }

        /// <summary>
        /// 生成 Unity Multiple Sprite 子资源名。
        /// </summary>
        /// <param name="atlas">图集数据。</param>
        /// <returns>子资源名列表。</returns>
        private static List<string> CreateSpriteNames(TpCloneAtlas atlas)
        {
            var spriteNames = new List<string>(atlas.Frames.Count);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < atlas.Frames.Count; i++)
            {
                var rawName = Path.GetFileNameWithoutExtension(atlas.Frames[i].Sprite.FileName);
                spriteNames.Add(TpCloneAssetUtility.MakeUniqueAssetName(rawName, usedNames));
            }

            return spriteNames;
        }

        /// <summary>
        /// clone 导出结果。
        /// </summary>
        public readonly struct ExportResult
        {
            /// <summary>Sprite 数量。</summary>
            public int SpriteCount { get; }

            /// <summary>JSON 绝对路径。</summary>
            public string JsonPath { get; }

            /// <summary>PNG 绝对路径。</summary>
            public string ImagePath { get; }

            /// <summary>Sprite 子资源名列表。</summary>
            public List<string> SpriteNames { get; }

            /// <summary>
            /// 创建导出结果。
            /// </summary>
            /// <param name="spriteCount">Sprite 数量。</param>
            /// <param name="jsonPath">JSON 绝对路径。</param>
            /// <param name="imagePath">PNG 绝对路径。</param>
            /// <param name="spriteNames">Sprite 子资源名列表。</param>
            public ExportResult(int spriteCount, string jsonPath, string imagePath, List<string> spriteNames)
            {
                SpriteCount = spriteCount;
                JsonPath = jsonPath;
                ImagePath = imagePath;
                SpriteNames = spriteNames;
            }
        }
    }

    /// <summary>
    /// Sprite 名称到 Multiple Sprite 图集路径的映射表写入器。
    /// </summary>
    internal static class TpCloneSpriteAtlasMapWriter
    {
        private const string VersionPropertyName = "version";
        private const string SpritesPropertyName = "sprites";
        private const string StylePropertyName = "style";
        private const string AtlasPropertyName = "atlas";
        private const string PathPropertyName = "path";
        private const int CurrentVersion = 1;

        /// <summary>
        /// 追加 Sprite 名称到图集路径的映射关系。
        /// </summary>
        /// <param name="mapAssetPath">映射表 Asset 路径。</param>
        /// <param name="atlasAssetPath">图集 Asset 路径。</param>
        /// <param name="atlasName">图集名。</param>
        /// <param name="spriteNames">Sprite 子资源名列表。</param>
        public static void Append(
            string mapAssetPath,
            string atlasAssetPath,
            string atlasName,
            IReadOnlyList<string> spriteNames)
        {
            if (spriteNames == null || spriteNames.Count == 0)
            {
                return;
            }

            var normalizedMapPath = TpCloneAssetUtility.NormalizeAssetPath(mapAssetPath);
            var normalizedAtlasPath = TpCloneAssetUtility.NormalizeAssetPath(atlasAssetPath);
            var root = LoadOrCreate(normalizedMapPath);
            var sprites = GetOrCreateSprites(root);
            var styleName = ResolveStyleName(normalizedAtlasPath);

            for (var i = 0; i < spriteNames.Count; i++)
            {
                AppendSpriteMapping(sprites, spriteNames[i], styleName, atlasName, normalizedAtlasPath);
            }

            Write(normalizedMapPath, root);
        }

        /// <summary>
        /// 读取已有映射表，文件不存在时创建空表。
        /// </summary>
        /// <param name="mapAssetPath">映射表 Asset 路径。</param>
        /// <returns>映射表 JSON 根节点。</returns>
        private static JObject LoadOrCreate(string mapAssetPath)
        {
            var absolutePath = TpCloneAssetUtility.ToAbsolutePath(mapAssetPath);
            if (!File.Exists(absolutePath))
            {
                return CreateEmptyRoot();
            }

            var content = File.ReadAllText(absolutePath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return CreateEmptyRoot();
            }

            var root = JObject.Parse(content);
            root[VersionPropertyName] = CurrentVersion;
            return root;
        }

        /// <summary>
        /// 创建空映射表根节点。
        /// </summary>
        /// <returns>映射表 JSON 根节点。</returns>
        private static JObject CreateEmptyRoot()
        {
            return new JObject
            {
                [VersionPropertyName] = CurrentVersion,
                [SpritesPropertyName] = new JObject()
            };
        }

        /// <summary>
        /// 获取或创建 sprites 节点。
        /// </summary>
        /// <param name="root">映射表 JSON 根节点。</param>
        /// <returns>sprites 节点。</returns>
        private static JObject GetOrCreateSprites(JObject root)
        {
            if (root[SpritesPropertyName] is JObject sprites)
            {
                return sprites;
            }

            sprites = new JObject();
            root[SpritesPropertyName] = sprites;
            return sprites;
        }

        /// <summary>
        /// 追加单个 Sprite 的图集路径映射。
        /// </summary>
        /// <param name="sprites">sprites 节点。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="styleName">风格名。</param>
        /// <param name="atlasName">图集名。</param>
        /// <param name="atlasAssetPath">图集 Asset 路径。</param>
        private static void AppendSpriteMapping(
            JObject sprites,
            string spriteName,
            string styleName,
            string atlasName,
            string atlasAssetPath)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return;
            }

            if (!(sprites[spriteName] is JArray mappings))
            {
                mappings = new JArray();
                sprites[spriteName] = mappings;
            }

            if (ContainsMapping(mappings, styleName, atlasAssetPath))
            {
                return;
            }

            mappings.Add(new JObject
            {
                [StylePropertyName] = styleName,
                [AtlasPropertyName] = atlasName,
                [PathPropertyName] = atlasAssetPath
            });
        }

        /// <summary>
        /// 判断映射是否已经存在。
        /// </summary>
        /// <param name="mappings">Sprite 对应的映射列表。</param>
        /// <param name="styleName">风格名。</param>
        /// <param name="atlasAssetPath">图集 Asset 路径。</param>
        /// <returns>相同风格和图集路径已存在时返回 true。</returns>
        private static bool ContainsMapping(JArray mappings, string styleName, string atlasAssetPath)
        {
            for (var i = 0; i < mappings.Count; i++)
            {
                if (!(mappings[i] is JObject mapping))
                {
                    continue;
                }

                var existingStyle = mapping[StylePropertyName]?.Value<string>() ?? string.Empty;
                var existingPath = mapping[PathPropertyName]?.Value<string>() ?? string.Empty;
                if (string.Equals(existingStyle, styleName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existingPath, atlasAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从图集路径解析风格目录名。
        /// </summary>
        /// <param name="atlasAssetPath">图集 Asset 路径。</param>
        /// <returns>风格目录名。</returns>
        private static string ResolveStyleName(string atlasAssetPath)
        {
            const string atlasRootPrefix = "Assets/GameRes/UI/Atlas/";
            if (!atlasAssetPath.StartsWith(atlasRootPrefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var relativePath = atlasAssetPath.Substring(atlasRootPrefix.Length);
            var separatorIndex = relativePath.IndexOf('/');
            return separatorIndex > 0 ? relativePath.Substring(0, separatorIndex) : string.Empty;
        }

        /// <summary>
        /// 写出映射表文件。
        /// </summary>
        /// <param name="mapAssetPath">映射表 Asset 路径。</param>
        /// <param name="root">映射表 JSON 根节点。</param>
        private static void Write(string mapAssetPath, JObject root)
        {
            var directoryPath = Path.GetDirectoryName(mapAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directoryPath))
            {
                TpCloneAssetUtility.EnsureAssetFolder(directoryPath);
            }

            File.WriteAllText(TpCloneAssetUtility.ToAbsolutePath(mapAssetPath), root.ToString());
        }
    }

    /// <summary>
    /// Sprite 九宫格边距读取与裁剪后换算。
    /// </summary>
    internal static class TpCloneSpriteBorderUtility
    {
        /// <summary>
        /// 从源贴图 TextureImporter 读取九宫格边距。
        /// </summary>
        /// <param name="assetPath">源贴图 Asset 路径。</param>
        /// <returns>Unity border：(left, bottom, right, top)。</returns>
        public static Vector4 ReadSourceSpriteBorder(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return Vector4.zero;
            }

            if (importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                var spriteName = Path.GetFileNameWithoutExtension(assetPath);
#pragma warning disable 618
                var sheet = importer.spritesheet;
#pragma warning restore 618
                for (var i = 0; i < sheet.Length; i++)
                {
                    if (string.Equals(sheet[i].name, spriteName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sheet[i].border;
                    }
                }

                return Vector4.zero;
            }

            return importer.spriteBorder;
        }

        /// <summary>
        /// 按透明裁剪区域将源图 border 换算到裁剪后 Sprite 坐标系。
        /// </summary>
        /// <param name="border">源图 border。</param>
        /// <param name="trimRect">裁剪矩形（左下角坐标）。</param>
        /// <param name="sourceWidth">源图宽度。</param>
        /// <param name="sourceHeight">源图高度。</param>
        /// <returns>裁剪后的 border。</returns>
        public static Vector4 AdjustBorderForTrim(
            Vector4 border,
            TpIntRect trimRect,
            int sourceWidth,
            int sourceHeight)
        {
            if (!HasBorder(border))
            {
                return Vector4.zero;
            }

            var left = Mathf.Max(0f, border.x - trimRect.x);
            var bottom = Mathf.Max(0f, border.y - trimRect.y);
            var right = Mathf.Max(0f, border.z - (sourceWidth - trimRect.Right));
            var top = Mathf.Max(0f, border.w - (sourceHeight - trimRect.Bottom));
            return new Vector4(left, bottom, right, top);
        }

        /// <summary>
        /// 判断 border 是否包含有效九宫格边距。
        /// </summary>
        /// <param name="border">Unity border。</param>
        /// <returns>任一边距大于 0 时返回 true。</returns>
        public static bool HasBorder(Vector4 border)
        {
            return border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f;
        }
    }

    /// <summary>
    /// 输入图片扫描和透明裁剪。
    /// </summary>
    internal static class TpCloneImageScanner
    {
        /// <summary>
        /// 扫描目录中的 Texture2D 并裁剪透明边。
        /// </summary>
        /// <param name="folderPath">源 Sprite 目录。</param>
        /// <param name="alphaThreshold">透明裁剪阈值。</param>
        /// <param name="trimMargin">裁剪区域外扩像素。</param>
        /// <returns>裁剪后的 Sprite 数据。</returns>
        public static List<TpCloneSprite> Scan(string folderPath, byte alphaThreshold, int trimMargin)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            var paths = new List<string>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsSupportedImagePath(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            var sprites = new List<TpCloneSprite>(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                sprites.Add(ReadAndTrim(paths[i], alphaThreshold, trimMargin));
            }

            return sprites;
        }

        /// <summary>
        /// 读取单张图片并裁剪透明边。
        /// </summary>
        /// <param name="assetPath">图片 Asset 路径。</param>
        /// <param name="alphaThreshold">透明裁剪阈值。</param>
        /// <param name="trimMargin">裁剪区域外扩像素。</param>
        /// <returns>裁剪后的 Sprite 数据。</returns>
        private static TpCloneSprite ReadAndTrim(string assetPath, byte alphaThreshold, int trimMargin)
        {
            var readableTexture = LoadImageFile(assetPath);
            try
            {
                var pixels = readableTexture.GetPixels32();
                var width = readableTexture.width;
                var height = readableTexture.height;
                var trim = FindTrimRect(pixels, width, height, alphaThreshold, trimMargin);
                var trimmedPixels = CopyTrimmedPixels(pixels, width, trim);
                var sourceBorder = TpCloneSpriteBorderUtility.ReadSourceSpriteBorder(assetPath);
                var trimmedBorder = TpCloneSpriteBorderUtility.AdjustBorderForTrim(sourceBorder, trim, width, height);
                return new TpCloneSprite(
                    assetPath,
                    Path.GetFileName(assetPath),
                    width,
                    height,
                    trim,
                    trimmedPixels,
                    trimmedBorder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readableTexture);
            }
        }

        /// <summary>
        /// 从磁盘图片字节读取纹理，特殊格式失败时回退 Unity 导入纹理。
        /// </summary>
        /// <param name="assetPath">图片 Asset 路径。</param>
        /// <returns>可读纹理。</returns>
        private static Texture2D LoadImageFile(string assetPath)
        {
            var extension = Path.GetExtension(assetPath);
            if (!string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(File.ReadAllBytes(TpCloneAssetUtility.ToAbsolutePath(assetPath))))
                {
                    return texture;
                }

                UnityEngine.Object.DestroyImmediate(texture);
            }

            var sourceAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (sourceAsset == null)
            {
                throw new InvalidDataException($"读取图片失败: {assetPath}");
            }

            return MakeReadableCopy(sourceAsset);
        }

        /// <summary>
        /// 创建可读纹理副本。
        /// </summary>
        /// <param name="source">源纹理。</param>
        /// <returns>可读纹理。</returns>
        private static Texture2D MakeReadableCopy(Texture2D source)
        {
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        /// <summary>
        /// 查找透明裁剪区域。
        /// </summary>
        /// <param name="pixels">源像素，左下角坐标顺序。</param>
        /// <param name="width">源宽度。</param>
        /// <param name="height">源高度。</param>
        /// <param name="alphaThreshold">透明裁剪阈值。</param>
        /// <param name="trimMargin">裁剪区域外扩像素。</param>
        /// <returns>裁剪矩形，使用左下角坐标。</returns>
        private static TpIntRect FindTrimRect(
            Color32[] pixels,
            int width,
            int height,
            byte alphaThreshold,
            int trimMargin)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a < alphaThreshold)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return new TpIntRect(0, 0, 1, 1);
            }

            minX = Mathf.Max(0, minX - trimMargin);
            minY = Mathf.Max(0, minY - trimMargin);
            maxX = Mathf.Min(width - 1, maxX + trimMargin);
            maxY = Mathf.Min(height - 1, maxY + trimMargin);
            return new TpIntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// 复制裁剪区域像素。
        /// </summary>
        /// <param name="sourcePixels">源像素。</param>
        /// <param name="sourceWidth">源宽度。</param>
        /// <param name="trimRect">裁剪区域。</param>
        /// <returns>裁剪后的像素。</returns>
        private static Color32[] CopyTrimmedPixels(Color32[] sourcePixels, int sourceWidth, TpIntRect trimRect)
        {
            var pixels = new Color32[trimRect.w * trimRect.h];
            for (var y = 0; y < trimRect.h; y++)
            {
                var sourceOffset = (trimRect.y + y) * sourceWidth + trimRect.x;
                var targetOffset = y * trimRect.w;
                Array.Copy(sourcePixels, sourceOffset, pixels, targetOffset, trimRect.w);
            }

            return pixels;
        }

        /// <summary>
        /// 判断资源路径是否是 TexturePacker 支持的常见图片格式。
        /// </summary>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>支持打包时返回 true。</returns>
        private static bool IsSupportedImagePath(string assetPath)
        {
            var extension = Path.GetExtension(assetPath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// MaxRects 不旋转排版实现。
    /// </summary>
    internal static class TpCloneMaxRectsPacker
    {
        private const int BorderPadding = 1;
        private const int ShapePadding = 2;

        /// <summary>
        /// 将裁剪后的 Sprite 排入图集。
        /// </summary>
        /// <param name="sprites">裁剪后的 Sprite 数据。</param>
        /// <param name="maxSize">最大图集尺寸。</param>
        /// <returns>图集数据。</returns>
        public static TpCloneAtlas Pack(List<TpCloneSprite> sprites, int maxSize)
        {
            var sortedSprites = new List<TpCloneSprite>(sprites);
            sortedSprites.Sort(CompareSpritesForPacking);

            var minWidth = BorderPadding * 2 + 1;
            var minHeight = 1;
            for (var i = 0; i < sortedSprites.Count; i++)
            {
                minWidth = Mathf.Max(minWidth, sortedSprites[i].TrimRect.w + BorderPadding * 2);
                minHeight = Mathf.Max(minHeight, sortedSprites[i].TrimRect.h + BorderPadding * 2);
            }

            TpCloneAtlas bestAtlas = null;
            var bestArea = int.MaxValue;
            for (var width = minWidth; width <= maxSize; width++)
            {
                if (bestAtlas != null && width * minHeight > bestArea)
                {
                    break;
                }

                if (TryPack(sortedSprites, width, maxSize, out var packedFrames))
                {
                    var atlas = CropAtlas(packedFrames);
                    var area = atlas.Width * atlas.Height;
                    if (area < bestArea || area == bestArea && atlas.Width < bestAtlas.Width)
                    {
                        bestAtlas = atlas;
                        bestArea = area;
                    }
                }
            }

            if (bestAtlas != null)
            {
                return bestAtlas;
            }

            throw new InvalidOperationException($"无法在 {maxSize}x{maxSize} 内完成排版。");
        }

        /// <summary>
        /// 尝试在指定尺寸中完成排版。
        /// </summary>
        /// <param name="sprites">待排版 Sprite。</param>
        /// <param name="width">图集宽度。</param>
        /// <param name="height">图集高度。</param>
        /// <param name="packedFrames">排版结果。</param>
        /// <returns>成功排入全部 Sprite 时返回 true。</returns>
        private static bool TryPack(
            List<TpCloneSprite> sprites,
            int width,
            int height,
            out List<TpCloneFrame> packedFrames)
        {
            packedFrames = new List<TpCloneFrame>(sprites.Count);
            var freeRects = new List<TpIntRect>
            {
                new TpIntRect(BorderPadding, BorderPadding, width - BorderPadding * 2, height - BorderPadding * 2)
            };

            for (var i = 0; i < sprites.Count; i++)
            {
                var sprite = sprites[i];
                var packWidth = sprite.TrimRect.w + ShapePadding;
                var packHeight = sprite.TrimRect.h + ShapePadding;
                if (!TryFindPosition(freeRects, packWidth, packHeight, out var usedRect))
                {
                    return false;
                }

                SplitFreeRects(freeRects, usedRect);
                PruneFreeRects(freeRects);
                packedFrames.Add(new TpCloneFrame(sprite, new TpIntRect(
                    usedRect.x,
                    usedRect.y,
                    sprite.TrimRect.w,
                    sprite.TrimRect.h)));
            }

            packedFrames.Sort(CompareFramesBySourcePath);
            return true;
        }

        /// <summary>
        /// 为矩形选择 Best Short Side Fit 位置。
        /// </summary>
        /// <param name="freeRects">空闲矩形列表。</param>
        /// <param name="width">矩形宽度。</param>
        /// <param name="height">矩形高度。</param>
        /// <param name="usedRect">选中的放置矩形。</param>
        /// <returns>找到位置时返回 true。</returns>
        private static bool TryFindPosition(
            List<TpIntRect> freeRects,
            int width,
            int height,
            out TpIntRect usedRect)
        {
            var bestShortSide = int.MaxValue;
            var bestLongSide = int.MaxValue;
            usedRect = default;

            for (var i = 0; i < freeRects.Count; i++)
            {
                var free = freeRects[i];
                if (width > free.w || height > free.h)
                {
                    continue;
                }

                var leftoverX = free.w - width;
                var leftoverY = free.h - height;
                var shortSide = Mathf.Min(leftoverX, leftoverY);
                var longSide = Mathf.Max(leftoverX, leftoverY);
                if (shortSide < bestShortSide ||
                    shortSide == bestShortSide && longSide < bestLongSide ||
                    shortSide == bestShortSide && longSide == bestLongSide && free.y < usedRect.y ||
                    shortSide == bestShortSide && longSide == bestLongSide && free.y == usedRect.y && free.x < usedRect.x)
                {
                    usedRect = new TpIntRect(free.x, free.y, width, height);
                    bestShortSide = shortSide;
                    bestLongSide = longSide;
                }
            }

            return bestShortSide != int.MaxValue;
        }

        /// <summary>
        /// 按已使用矩形切分空闲矩形。
        /// </summary>
        /// <param name="freeRects">空闲矩形列表。</param>
        /// <param name="usedRect">已使用矩形。</param>
        private static void SplitFreeRects(List<TpIntRect> freeRects, TpIntRect usedRect)
        {
            for (var i = freeRects.Count - 1; i >= 0; i--)
            {
                var free = freeRects[i];
                if (!Intersects(free, usedRect))
                {
                    continue;
                }

                freeRects.RemoveAt(i);
                if (usedRect.x < free.Right && usedRect.Right > free.x)
                {
                    if (usedRect.y > free.y && usedRect.y < free.Bottom)
                    {
                        freeRects.Add(new TpIntRect(free.x, free.y, free.w, usedRect.y - free.y));
                    }

                    if (usedRect.Bottom < free.Bottom)
                    {
                        freeRects.Add(new TpIntRect(free.x, usedRect.Bottom, free.w, free.Bottom - usedRect.Bottom));
                    }
                }

                if (usedRect.y < free.Bottom && usedRect.Bottom > free.y)
                {
                    if (usedRect.x > free.x && usedRect.x < free.Right)
                    {
                        freeRects.Add(new TpIntRect(free.x, free.y, usedRect.x - free.x, free.h));
                    }

                    if (usedRect.Right < free.Right)
                    {
                        freeRects.Add(new TpIntRect(usedRect.Right, free.y, free.Right - usedRect.Right, free.h));
                    }
                }
            }
        }

        /// <summary>
        /// 移除被其他空闲矩形完全包含的矩形。
        /// </summary>
        /// <param name="freeRects">空闲矩形列表。</param>
        private static void PruneFreeRects(List<TpIntRect> freeRects)
        {
            for (var i = 0; i < freeRects.Count; i++)
            {
                for (var j = i + 1; j < freeRects.Count; j++)
                {
                    if (Contains(freeRects[i], freeRects[j]))
                    {
                        freeRects.RemoveAt(j);
                        j--;
                    }
                    else if (Contains(freeRects[j], freeRects[i]))
                    {
                        freeRects.RemoveAt(i);
                        i--;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 裁剪图集尺寸到已使用区域并保留 TexturePacker 默认边距。
        /// </summary>
        /// <param name="frames">排版帧列表。</param>
        /// <returns>图集数据。</returns>
        private static TpCloneAtlas CropAtlas(List<TpCloneFrame> frames)
        {
            var width = 1;
            var height = 1;
            for (var i = 0; i < frames.Count; i++)
            {
                width = Mathf.Max(width, frames[i].FrameRect.Right);
                height = Mathf.Max(height, frames[i].FrameRect.Bottom);
            }

            width += BorderPadding;
            height += BorderPadding;
            return new TpCloneAtlas(width, height, frames);
        }

        /// <summary>
        /// 比较两个矩形是否相交。
        /// </summary>
        /// <param name="a">矩形 A。</param>
        /// <param name="b">矩形 B。</param>
        /// <returns>相交时返回 true。</returns>
        private static bool Intersects(TpIntRect a, TpIntRect b)
        {
            return a.x < b.Right && a.Right > b.x && a.y < b.Bottom && a.Bottom > b.y;
        }

        /// <summary>
        /// 判断外层矩形是否包含内层矩形。
        /// </summary>
        /// <param name="outer">外层矩形。</param>
        /// <param name="inner">内层矩形。</param>
        /// <returns>完全包含时返回 true。</returns>
        private static bool Contains(TpIntRect outer, TpIntRect inner)
        {
            return inner.x >= outer.x &&
                inner.y >= outer.y &&
                inner.Right <= outer.Right &&
                inner.Bottom <= outer.Bottom;
        }

        /// <summary>
        /// 按面积和尺寸对 Sprite 排版顺序排序。
        /// </summary>
        /// <param name="a">Sprite A。</param>
        /// <param name="b">Sprite B。</param>
        /// <returns>排序结果。</returns>
        private static int CompareSpritesForPacking(TpCloneSprite a, TpCloneSprite b)
        {
            var areaCompare = (b.TrimRect.w * b.TrimRect.h).CompareTo(a.TrimRect.w * a.TrimRect.h);
            if (areaCompare != 0) return areaCompare;

            var sideCompare = Mathf.Max(b.TrimRect.w, b.TrimRect.h).CompareTo(Mathf.Max(a.TrimRect.w, a.TrimRect.h));
            if (sideCompare != 0) return sideCompare;

            return string.Compare(a.AssetPath, b.AssetPath, StringComparison.Ordinal);
        }

        /// <summary>
        /// 按源路径恢复 JSON 输出顺序。
        /// </summary>
        /// <param name="a">帧 A。</param>
        /// <param name="b">帧 B。</param>
        /// <returns>排序结果。</returns>
        private static int CompareFramesBySourcePath(TpCloneFrame a, TpCloneFrame b)
        {
            return string.Compare(a.Sprite.AssetPath, b.Sprite.AssetPath, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 图集 PNG 合成器。
    /// </summary>
    internal static class TpCloneAtlasComposer
    {
        /// <summary>
        /// 写出合成后的 PNG。
        /// </summary>
        /// <param name="atlas">图集数据。</param>
        /// <param name="imageAbsolutePath">输出 PNG 绝对路径。</param>
        public static void WritePng(TpCloneAtlas atlas, string imageAbsolutePath)
        {
            var directory = Path.GetDirectoryName(imageAbsolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var pixels = new Color32[atlas.Width * atlas.Height];
            for (var i = 0; i < atlas.Frames.Count; i++)
            {
                CopyFramePixels(atlas, atlas.Frames[i], pixels);
            }

            var texture = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(imageAbsolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 将单帧像素复制到图集像素数组。
        /// </summary>
        /// <param name="atlas">图集数据。</param>
        /// <param name="frame">帧数据。</param>
        /// <param name="atlasPixels">图集像素数组。</param>
        private static void CopyFramePixels(TpCloneAtlas atlas, TpCloneFrame frame, Color32[] atlasPixels)
        {
            var sourcePixels = frame.Sprite.TrimmedPixels;
            var sourceWidth = frame.Sprite.TrimRect.w;
            var sourceHeight = frame.Sprite.TrimRect.h;
            var targetBottom = atlas.Height - frame.FrameRect.y - frame.FrameRect.h;
            for (var y = 0; y < sourceHeight; y++)
            {
                var sourceOffset = y * sourceWidth;
                var targetOffset = (targetBottom + y) * atlas.Width + frame.FrameRect.x;
                Array.Copy(sourcePixels, sourceOffset, atlasPixels, targetOffset, sourceWidth);
            }
        }
    }

    /// <summary>
    /// TexturePacker JSON Array 写出器。
    /// </summary>
    internal static class TpCloneJsonWriter
    {
        /// <summary>
        /// 写出 TexturePacker json-array 格式。
        /// </summary>
        /// <param name="atlas">图集数据。</param>
        /// <param name="imageName">图集图片文件名。</param>
        /// <param name="jsonAbsolutePath">输出 JSON 绝对路径。</param>
        public static void WriteJson(TpCloneAtlas atlas, string imageName, string jsonAbsolutePath)
        {
            var directory = Path.GetDirectoryName(jsonAbsolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var frames = new JArray();
            for (var i = 0; i < atlas.Frames.Count; i++)
            {
                frames.Add(CreateFrame(atlas.Frames[i]));
            }

            var root = new JObject
            {
                ["frames"] = frames,
                ["meta"] = new JObject
                {
                    ["app"] = "TexturePackerClone",
                    ["version"] = "1.0",
                    ["image"] = imageName,
                    ["format"] = "RGBA8888",
                    ["size"] = CreateSize(atlas.Width, atlas.Height),
                    ["scale"] = "1"
                }
            };

            File.WriteAllText(jsonAbsolutePath, root.ToString());
        }

        /// <summary>
        /// 创建单帧 JSON。
        /// </summary>
        /// <param name="frame">帧数据。</param>
        /// <returns>单帧 JSON。</returns>
        private static JObject CreateFrame(TpCloneFrame frame)
        {
            var sprite = frame.Sprite;
            var sourceYFromTop = sprite.SourceHeight - sprite.TrimRect.y - sprite.TrimRect.h;
            var frameJson = new JObject
            {
                ["filename"] = sprite.FileName,
                ["frame"] = CreateRect(frame.FrameRect.x, frame.FrameRect.y, frame.FrameRect.w, frame.FrameRect.h),
                ["rotated"] = false,
                ["trimmed"] = sprite.TrimRect.w != sprite.SourceWidth || sprite.TrimRect.h != sprite.SourceHeight,
                ["spriteSourceSize"] = CreateRect(sprite.TrimRect.x, sourceYFromTop, sprite.TrimRect.w, sprite.TrimRect.h),
                ["sourceSize"] = CreateSize(sprite.SourceWidth, sprite.SourceHeight),
                ["pivot"] = new JObject
                {
                    ["x"] = 0.5f,
                    ["y"] = 0.5f
                }
            };

            if (TpCloneSpriteBorderUtility.HasBorder(sprite.Border))
            {
                frameJson["border"] = CreateBorder(sprite.Border);
            }

            return frameJson;
        }

        /// <summary>
        /// 创建九宫格边距 JSON。
        /// </summary>
        /// <param name="border">Unity border：(left, bottom, right, top)。</param>
        /// <returns>边距 JSON。</returns>
        private static JObject CreateBorder(Vector4 border)
        {
            return new JObject
            {
                ["left"] = border.x,
                ["bottom"] = border.y,
                ["right"] = border.z,
                ["top"] = border.w
            };
        }

        /// <summary>
        /// 创建矩形 JSON。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <param name="w">宽度。</param>
        /// <param name="h">高度。</param>
        /// <returns>矩形 JSON。</returns>
        private static JObject CreateRect(int x, int y, int w, int h)
        {
            return new JObject
            {
                ["x"] = x,
                ["y"] = y,
                ["w"] = w,
                ["h"] = h
            };
        }

        /// <summary>
        /// 创建尺寸 JSON。
        /// </summary>
        /// <param name="w">宽度。</param>
        /// <param name="h">高度。</param>
        /// <returns>尺寸 JSON。</returns>
        private static JObject CreateSize(int w, int h)
        {
            return new JObject
            {
                ["w"] = w,
                ["h"] = h
            };
        }
    }

    /// <summary>
    /// 将 clone JSON/PNG 导入为 Unity Multiple Sprite。
    /// </summary>
    internal static class TpCloneUnityImporter
    {
        private const int UnityTextureImporterMaxSize = 4096;
        private static readonly string[] Astc6X6PlatformNames = { "Android", "iPhone" };

        /// <summary>
        /// 按 TexturePacker JSON 数据导入 Multiple Sprite。
        /// </summary>
        /// <param name="imageAssetPath">图集图片 Asset 路径。</param>
        /// <param name="jsonAssetPath">图集 JSON Asset 路径。</param>
        /// <param name="ppu">生成 Sprite 的 Pixels Per Unit。</param>
        public static void ImportSpriteSheet(string imageAssetPath, string jsonAssetPath, float ppu)
        {
            AssetDatabase.ImportAsset(imageAssetPath, ImportAssetOptions.ForceUpdate);
            var atlas = TpCloneAtlasJsonParser.Load(jsonAssetPath);
            var importer = AssetImporter.GetAtPath(imageAssetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"无法获取 TextureImporter: {imageAssetPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
            importer.maxTextureSize = Mathf.Max(UnityTextureImporterMaxSize, importer.maxTextureSize);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            ApplyAstc6X6Settings(importer);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preservedBorders = BuildPreservedBorderMap(importer);
            var spriteMetaData = new SpriteMetaData[atlas.Frames.Count];
            for (var i = 0; i < atlas.Frames.Count; i++)
            {
                var frame = atlas.Frames[i];
                var spriteName = TpCloneAssetUtility.MakeUniqueAssetName(Path.GetFileNameWithoutExtension(frame.Filename), usedNames);
                var border = frame.Border;
                if (!TpCloneSpriteBorderUtility.HasBorder(border) &&
                    preservedBorders.TryGetValue(spriteName, out var preservedBorder))
                {
                    border = preservedBorder;
                }

                spriteMetaData[i] = new SpriteMetaData
                {
                    name = spriteName,
                    rect = ToUnitySpriteRect(frame, atlas.Height),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = CalculateTrimmedPivot(frame),
                    border = border
                };
            }

#pragma warning disable 618
            importer.spritesheet = spriteMetaData;
#pragma warning restore 618
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 判断当前 Project 选择是否包含可设置压缩格式的贴图。
        /// </summary>
        /// <returns>包含 TextureImporter 时返回 true。</returns>
        public static bool HasSelectedTextureAssets()
        {
            return CollectSelectedTextureAssetPaths().Count > 0;
        }

        /// <summary>
        /// 将当前 Project 选择中的贴图批量设置为 ASTC 6x6。
        /// </summary>
        /// <returns>被修改的贴图数量。</returns>
        public static int ApplyAstc6X6ToSelectedTextures()
        {
            var texturePaths = CollectSelectedTextureAssetPaths();
            if (texturePaths.Count == 0)
            {
                throw new ArgumentException("请选择一个或多个贴图，或包含贴图的目录。");
            }

            var changedCount = 0;
            for (var i = 0; i < texturePaths.Count; i++)
            {
                var importer = AssetImporter.GetAtPath(texturePaths[i]) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                ApplyAstc6X6Settings(importer);
                importer.SaveAndReimport();
                changedCount++;
            }

            return changedCount;
        }

        /// <summary>
        /// 对移动平台写入 ASTC 6x6 导入覆盖。
        /// </summary>
        /// <param name="importer">贴图导入器。</param>
        private static void ApplyAstc6X6Settings(TextureImporter importer)
        {
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;

            for (var i = 0; i < Astc6X6PlatformNames.Length; i++)
            {
                var platformSettings = importer.GetPlatformTextureSettings(Astc6X6PlatformNames[i]);
                platformSettings.overridden = true;
                platformSettings.maxTextureSize = Mathf.Max(UnityTextureImporterMaxSize, platformSettings.maxTextureSize);
                platformSettings.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(platformSettings);
            }
        }

        /// <summary>
        /// 收集 Project 面板选择中的贴图 Asset 路径，支持目录递归。
        /// </summary>
        /// <returns>贴图 Asset 路径列表。</returns>
        private static List<string> CollectSelectedTextureAssetPaths()
        {
            var selectedObjects = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
            var texturePaths = new List<string>();
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < selectedObjects.Length; i++)
            {
                var selectedPath = TpCloneAssetUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(selectedObjects[i]));
                if (string.IsNullOrEmpty(selectedPath))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(selectedPath))
                {
                    CollectTextureAssetPathsInFolder(selectedPath, texturePaths, usedPaths);
                    continue;
                }

                AddTextureAssetPath(selectedPath, texturePaths, usedPaths);
            }

            return texturePaths;
        }

        /// <summary>
        /// 递归收集目录中的贴图 Asset 路径。
        /// </summary>
        /// <param name="folderPath">目录 Asset 路径。</param>
        /// <param name="texturePaths">收集结果。</param>
        /// <param name="usedPaths">去重集合。</param>
        private static void CollectTextureAssetPathsInFolder(
            string folderPath,
            List<string> texturePaths,
            HashSet<string> usedPaths)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            for (var i = 0; i < guids.Length; i++)
            {
                AddTextureAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]), texturePaths, usedPaths);
            }
        }

        /// <summary>
        /// 添加有效贴图路径。
        /// </summary>
        /// <param name="assetPath">Asset 路径。</param>
        /// <param name="texturePaths">收集结果。</param>
        /// <param name="usedPaths">去重集合。</param>
        private static void AddTextureAssetPath(
            string assetPath,
            List<string> texturePaths,
            HashSet<string> usedPaths)
        {
            if (string.IsNullOrEmpty(assetPath) || usedPaths.Contains(assetPath))
            {
                return;
            }

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter)
            {
                texturePaths.Add(assetPath);
                usedPaths.Add(assetPath);
            }
        }

        /// <summary>
        /// 读取当前图集 importer 上已有的子 Sprite 九宫格，用于 JSON 未携带 border 时兜底保留。
        /// </summary>
        /// <param name="importer">图集 TextureImporter。</param>
        /// <returns>子 Sprite 名称到 border 的映射。</returns>
        private static Dictionary<string, Vector4> BuildPreservedBorderMap(TextureImporter importer)
        {
            var map = new Dictionary<string, Vector4>(StringComparer.OrdinalIgnoreCase);
#pragma warning disable 618
            var sheet = importer.spritesheet;
#pragma warning restore 618
            if (sheet == null)
            {
                return map;
            }

            for (var i = 0; i < sheet.Length; i++)
            {
                if (TpCloneSpriteBorderUtility.HasBorder(sheet[i].border))
                {
                    map[sheet[i].name] = sheet[i].border;
                }
            }

            return map;
        }

        /// <summary>
        /// 将 TexturePacker 左上角坐标矩形转换为 Unity 左下角坐标矩形。
        /// </summary>
        /// <param name="frame">TexturePacker 单帧数据。</param>
        /// <param name="textureHeight">图集图片高度。</param>
        /// <returns>Unity Sprite rect。</returns>
        private static Rect ToUnitySpriteRect(TpParsedFrame frame, int textureHeight)
        {
            return new Rect(
                frame.Frame.x,
                textureHeight - frame.Frame.y - frame.Frame.h,
                frame.Frame.w,
                frame.Frame.h);
        }

        /// <summary>
        /// 根据 TexturePacker trim 信息计算 Unity 切片内的 pivot。
        /// </summary>
        /// <param name="frame">TexturePacker 单帧数据。</param>
        /// <returns>Unity Sprite pivot。</returns>
        private static Vector2 CalculateTrimmedPivot(TpParsedFrame frame)
        {
            var pivotPixelX = frame.Pivot.x * frame.SourceSize.x;
            var pivotPixelY = frame.Pivot.y * frame.SourceSize.y;
            var trimmedBottom = frame.SourceSize.y - frame.SpriteSourceSize.y - frame.SpriteSourceSize.h;
            return new Vector2(
                (pivotPixelX - frame.SpriteSourceSize.x) / frame.SpriteSourceSize.w,
                (pivotPixelY - trimmedBottom) / frame.SpriteSourceSize.h);
        }
    }

    /// <summary>
    /// TexturePacker 输出对照比较工具。
    /// </summary>
    internal static class TpCloneComparison
    {
        private const string TexturePackerCommand = "TexturePacker";
        private const string MacTexturePackerExecutable = "/Applications/TexturePacker.app/Contents/MacOS/TexturePacker";
        private const int DefaultMaxSize = 4096;

        /// <summary>
        /// 用 TexturePacker CLI 输出校验 clone 输出差异。
        /// </summary>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <returns>比较报告。</returns>
        public static TpCloneComparisonReport CompareWithTexturePacker(string sourceFolderPath)
        {
            if (string.IsNullOrEmpty(sourceFolderPath) || !AssetDatabase.IsValidFolder(sourceFolderPath))
            {
                throw new ArgumentException($"目录不存在或不是 Assets 下目录: {sourceFolderPath}", nameof(sourceFolderPath));
            }

            var atlasName = Path.GetFileName(sourceFolderPath.TrimEnd('/'));
            var compareRoot = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                "Temp",
                "TexturePackerCloneCompare",
                atlasName);
            if (Directory.Exists(compareRoot))
            {
                Directory.Delete(compareRoot, true);
            }

            Directory.CreateDirectory(compareRoot);
            var officialJson = Path.Combine(compareRoot, $"{atlasName}.texturepacker.json");
            var officialPng = Path.Combine(compareRoot, $"{atlasName}.texturepacker.png");
            var cloneJson = Path.Combine(compareRoot, $"{atlasName}.clone.json");
            var clonePng = Path.Combine(compareRoot, $"{atlasName}.clone.png");

            RunTexturePacker(sourceFolderPath, officialJson, officialPng);
            TpCloneExporter.ExportToAbsolutePaths(sourceFolderPath, cloneJson, clonePng);

            var officialAtlas = TpCloneAtlasJsonParser.LoadAbsolute(officialJson);
            var cloneAtlas = TpCloneAtlasJsonParser.LoadAbsolute(cloneJson);
            return CompareAtlases(officialAtlas, cloneAtlas, officialPng, clonePng, compareRoot);
        }

        /// <summary>
        /// 比较两个 TexturePacker 图集数据和 PNG 像素。
        /// </summary>
        /// <param name="officialAtlas">TexturePacker 图集数据。</param>
        /// <param name="cloneAtlas">clone 图集数据。</param>
        /// <param name="officialPngPath">TexturePacker PNG 路径。</param>
        /// <param name="clonePngPath">clone PNG 路径。</param>
        /// <param name="reportFolder">报告目录。</param>
        /// <returns>比较报告。</returns>
        private static TpCloneComparisonReport CompareAtlases(
            TpParsedAtlas officialAtlas,
            TpParsedAtlas cloneAtlas,
            string officialPngPath,
            string clonePngPath,
            string reportFolder)
        {
            var frameDifferences = new List<string>();
            if (officialAtlas.Width != cloneAtlas.Width || officialAtlas.Height != cloneAtlas.Height)
            {
                frameDifferences.Add(
                    $"meta.size 不一致: TexturePacker={officialAtlas.Width}x{officialAtlas.Height}, Clone={cloneAtlas.Width}x{cloneAtlas.Height}");
            }

            var officialFrames = ToFrameMap(officialAtlas);
            var cloneFrames = ToFrameMap(cloneAtlas);
            foreach (var pair in officialFrames)
            {
                if (!cloneFrames.TryGetValue(pair.Key, out var cloneFrame))
                {
                    frameDifferences.Add($"Clone 缺少 frame: {pair.Key}");
                    continue;
                }

                CompareFrame(pair.Key, pair.Value, cloneFrame, frameDifferences);
            }

            foreach (var pair in cloneFrames)
            {
                if (!officialFrames.ContainsKey(pair.Key))
                {
                    frameDifferences.Add($"Clone 多出 frame: {pair.Key}");
                }
            }

            var pixelDifference = ComparePixels(officialPngPath, clonePngPath);
            return new TpCloneComparisonReport(
                officialFrames.Count,
                cloneFrames.Count,
                frameDifferences,
                pixelDifference,
                reportFolder);
        }

        /// <summary>
        /// 比较单个 frame 坐标和 trim 信息。
        /// </summary>
        /// <param name="name">frame 名称。</param>
        /// <param name="officialFrame">TexturePacker frame。</param>
        /// <param name="cloneFrame">clone frame。</param>
        /// <param name="differences">差异列表。</param>
        private static void CompareFrame(
            string name,
            TpParsedFrame officialFrame,
            TpParsedFrame cloneFrame,
            List<string> differences)
        {
            CompareRect(name, "frame", officialFrame.Frame, cloneFrame.Frame, differences);
            CompareRect(name, "spriteSourceSize", officialFrame.SpriteSourceSize, cloneFrame.SpriteSourceSize, differences);
            if (officialFrame.SourceSize != cloneFrame.SourceSize)
            {
                differences.Add(
                    $"{name} sourceSize 不一致: TexturePacker={officialFrame.SourceSize.x}x{officialFrame.SourceSize.y}, Clone={cloneFrame.SourceSize.x}x{cloneFrame.SourceSize.y}");
            }

            if (officialFrame.Rotated != cloneFrame.Rotated)
            {
                differences.Add($"{name} rotated 不一致: TexturePacker={officialFrame.Rotated}, Clone={cloneFrame.Rotated}");
            }
        }

        /// <summary>
        /// 比较矩形字段。
        /// </summary>
        /// <param name="name">frame 名称。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="officialRect">TexturePacker 矩形。</param>
        /// <param name="cloneRect">clone 矩形。</param>
        /// <param name="differences">差异列表。</param>
        private static void CompareRect(
            string name,
            string fieldName,
            TpIntRect officialRect,
            TpIntRect cloneRect,
            List<string> differences)
        {
            if (officialRect.Equals(cloneRect))
            {
                return;
            }

            differences.Add(
                $"{name} {fieldName} 不一致: TexturePacker=({officialRect.x},{officialRect.y},{officialRect.w},{officialRect.h}), " +
                $"Clone=({cloneRect.x},{cloneRect.y},{cloneRect.w},{cloneRect.h})");
        }

        /// <summary>
        /// 比较两张 PNG 的像素差异。
        /// </summary>
        /// <param name="officialPngPath">TexturePacker PNG 路径。</param>
        /// <param name="clonePngPath">clone PNG 路径。</param>
        /// <returns>像素差异。</returns>
        private static TpPixelDifference ComparePixels(string officialPngPath, string clonePngPath)
        {
            var officialTexture = LoadTexture(officialPngPath);
            var cloneTexture = LoadTexture(clonePngPath);
            try
            {
                var overlapWidth = Mathf.Min(officialTexture.width, cloneTexture.width);
                var overlapHeight = Mathf.Min(officialTexture.height, cloneTexture.height);
                var officialPixels = officialTexture.GetPixels32();
                var clonePixels = cloneTexture.GetPixels32();
                var diffCount = Math.Abs(officialTexture.width * officialTexture.height - cloneTexture.width * cloneTexture.height);
                for (var y = 0; y < overlapHeight; y++)
                {
                    for (var x = 0; x < overlapWidth; x++)
                    {
                        if (!officialPixels[y * officialTexture.width + x].Equals(clonePixels[y * cloneTexture.width + x]))
                        {
                            diffCount++;
                        }
                    }
                }

                return new TpPixelDifference(
                    officialTexture.width,
                    officialTexture.height,
                    cloneTexture.width,
                    cloneTexture.height,
                    diffCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(officialTexture);
                UnityEngine.Object.DestroyImmediate(cloneTexture);
            }
        }

        /// <summary>
        /// 调用 TexturePacker 命令行生成对照数据。
        /// </summary>
        /// <param name="sourceFolderPath">输入 Sprite 目录。</param>
        /// <param name="jsonAbsolutePath">输出 JSON 绝对路径。</param>
        /// <param name="imageAbsolutePath">输出 PNG 绝对路径。</param>
        private static void RunTexturePacker(string sourceFolderPath, string jsonAbsolutePath, string imageAbsolutePath)
        {
            var outputFolderPath = Path.GetDirectoryName(jsonAbsolutePath);
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                throw new ArgumentException($"非法 JSON 输出路径: {jsonAbsolutePath}", nameof(jsonAbsolutePath));
            }

            var arguments =
                $"--format json-array --data {QuoteProcessArg(Path.GetFileName(jsonAbsolutePath))} " +
                $"--sheet {QuoteProcessArg(Path.GetFileName(imageAbsolutePath))} --disable-rotation " +
                $"--max-size {DefaultMaxSize} " +
                QuoteProcessArg(TpCloneAssetUtility.ToAbsolutePath(sourceFolderPath));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ResolveTexturePackerExecutable(),
                Arguments = arguments,
                WorkingDirectory = outputFolderPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException("TexturePacker 进程启动失败。");
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"TexturePacker 执行失败，ExitCode={process.ExitCode}\n{output}\n{error}");
                    }
                }
            }
            catch (Win32Exception e)
            {
                throw new FileNotFoundException(
                    $"找不到 TexturePacker 命令行工具，请安装 TexturePacker 或把 {TexturePackerCommand} 加入 PATH。",
                    e);
            }
        }

        /// <summary>
        /// 获取可用的 TexturePacker 命令行路径。
        /// </summary>
        /// <returns>TexturePacker 可执行文件路径或命令名。</returns>
        private static string ResolveTexturePackerExecutable()
        {
            return File.Exists(MacTexturePackerExecutable) ? MacTexturePackerExecutable : TexturePackerCommand;
        }

        /// <summary>
        /// 给命令行参数添加安全引号。
        /// </summary>
        /// <param name="value">原始参数。</param>
        /// <returns>加引号后的参数。</returns>
        private static string QuoteProcessArg(string value)
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        /// <summary>
        /// 加载 PNG 到可读 Texture2D。
        /// </summary>
        /// <param name="path">PNG 路径。</param>
        /// <returns>纹理。</returns>
        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidDataException($"读取图片失败: {path}");
            }

            return texture;
        }

        /// <summary>
        /// 将 frame 列表转成按名称索引的字典。
        /// </summary>
        /// <param name="atlas">图集数据。</param>
        /// <returns>frame 字典。</returns>
        private static Dictionary<string, TpParsedFrame> ToFrameMap(TpParsedAtlas atlas)
        {
            var map = new Dictionary<string, TpParsedFrame>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < atlas.Frames.Count; i++)
            {
                map[atlas.Frames[i].Filename] = atlas.Frames[i];
            }

            return map;
        }
    }

    /// <summary>
    /// TexturePacker JSON 解析器。
    /// </summary>
    internal static class TpCloneAtlasJsonParser
    {
        /// <summary>
        /// 从 Asset 路径读取图集 JSON。
        /// </summary>
        /// <param name="jsonAssetPath">JSON Asset 路径。</param>
        /// <returns>图集数据。</returns>
        public static TpParsedAtlas Load(string jsonAssetPath)
        {
            return LoadAbsolute(TpCloneAssetUtility.ToAbsolutePath(jsonAssetPath));
        }

        /// <summary>
        /// 从绝对路径读取图集 JSON。
        /// </summary>
        /// <param name="jsonAbsolutePath">JSON 绝对路径。</param>
        /// <returns>图集数据。</returns>
        public static TpParsedAtlas LoadAbsolute(string jsonAbsolutePath)
        {
            var root = JObject.Parse(File.ReadAllText(jsonAbsolutePath));
            var meta = root["meta"] as JObject;
            if (meta == null)
            {
                throw new InvalidDataException($"{jsonAbsolutePath} 缺少 meta。");
            }

            var size = ReadSize(meta["size"], "meta.size");
            var frames = ParseFrames(root["frames"]);
            if (frames.Count == 0)
            {
                throw new InvalidDataException($"{jsonAbsolutePath} 没有可用 frames。");
            }

            return new TpParsedAtlas(size.x, size.y, frames);
        }

        /// <summary>
        /// 解析 TexturePacker frames，兼容数组格式和对象字典格式。
        /// </summary>
        /// <param name="framesToken">frames JSON 节点。</param>
        /// <returns>帧数据列表。</returns>
        private static List<TpParsedFrame> ParseFrames(JToken framesToken)
        {
            if (framesToken == null)
            {
                throw new InvalidDataException("TexturePacker JSON 缺少 frames。");
            }

            var frames = new List<TpParsedFrame>();
            if (framesToken is JArray frameArray)
            {
                for (var i = 0; i < frameArray.Count; i++)
                {
                    frames.Add(ParseFrame((JObject)frameArray[i], null));
                }
            }
            else if (framesToken is JObject frameObject)
            {
                foreach (var property in frameObject.Properties())
                {
                    frames.Add(ParseFrame((JObject)property.Value, property.Name));
                }
            }
            else
            {
                throw new InvalidDataException("TexturePacker frames 必须是数组或对象。");
            }

            return frames;
        }

        /// <summary>
        /// 解析单个 TexturePacker frame 节点。
        /// </summary>
        /// <param name="frameObject">单帧 JSON 对象。</param>
        /// <param name="fallbackName">对象字典格式下的备用名称。</param>
        /// <returns>帧数据。</returns>
        private static TpParsedFrame ParseFrame(JObject frameObject, string fallbackName)
        {
            var filename = frameObject["filename"]?.Value<string>() ?? fallbackName;
            if (string.IsNullOrEmpty(filename))
            {
                throw new InvalidDataException("TexturePacker frame 缺少 filename。");
            }

            return new TpParsedFrame(
                filename,
                ReadRect(frameObject["frame"], "frame"),
                frameObject["rotated"]?.Value<bool>() ?? false,
                ReadRect(frameObject["spriteSourceSize"], "spriteSourceSize"),
                ReadSize(frameObject["sourceSize"], "sourceSize"),
                ReadPivot(frameObject["pivot"]),
                ReadBorder(frameObject["border"]));
        }

        /// <summary>
        /// 读取九宫格边距，兼容 left/bottom/right/top 与 x/y/z/w 字段名。
        /// </summary>
        /// <param name="token">border JSON 节点，可为空。</param>
        /// <returns>Unity border：(left, bottom, right, top)。</returns>
        private static Vector4 ReadBorder(JToken token)
        {
            if (token == null)
            {
                return Vector4.zero;
            }

            return new Vector4(
                token["left"]?.Value<float>() ?? token["x"]?.Value<float>() ?? 0f,
                token["bottom"]?.Value<float>() ?? token["y"]?.Value<float>() ?? 0f,
                token["right"]?.Value<float>() ?? token["z"]?.Value<float>() ?? 0f,
                token["top"]?.Value<float>() ?? token["w"]?.Value<float>() ?? 0f);
        }

        /// <summary>
        /// 读取矩形数据。
        /// </summary>
        /// <param name="token">矩形 JSON 节点。</param>
        /// <param name="fieldName">字段名。</param>
        /// <returns>矩形数据。</returns>
        private static TpIntRect ReadRect(JToken token, string fieldName)
        {
            if (token == null)
            {
                throw new InvalidDataException($"TexturePacker frame 缺少 {fieldName}。");
            }

            return new TpIntRect(
                token["x"]?.Value<int>() ?? 0,
                token["y"]?.Value<int>() ?? 0,
                token["w"]?.Value<int>() ?? 0,
                token["h"]?.Value<int>() ?? 0);
        }

        /// <summary>
        /// 读取尺寸数据。
        /// </summary>
        /// <param name="token">尺寸 JSON 节点。</param>
        /// <param name="fieldName">字段名。</param>
        /// <returns>尺寸数据。</returns>
        private static Vector2Int ReadSize(JToken token, string fieldName)
        {
            if (token == null)
            {
                throw new InvalidDataException($"TexturePacker frame 缺少 {fieldName}。");
            }

            return new Vector2Int(token["w"]?.Value<int>() ?? 0, token["h"]?.Value<int>() ?? 0);
        }

        /// <summary>
        /// 读取 pivot 数据。
        /// </summary>
        /// <param name="token">pivot JSON 节点，可为空。</param>
        /// <returns>pivot 数据。</returns>
        private static Vector2 ReadPivot(JToken token)
        {
            if (token == null)
            {
                return new Vector2(0.5f, 0.5f);
            }

            return new Vector2(token["x"]?.Value<float>() ?? 0.5f, token["y"]?.Value<float>() ?? 0.5f);
        }
    }

    /// <summary>
    /// Asset 路径工具。
    /// </summary>
    internal static class TpCloneAssetUtility
    {
        /// <summary>
        /// 统一 Asset 路径分隔符。
        /// </summary>
        /// <param name="assetPath">原始 Asset 路径。</param>
        /// <returns>规范化后的 Asset 路径。</returns>
        public static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/');
        }

        /// <summary>
        /// 把 Asset 相对路径转换为磁盘绝对路径。
        /// </summary>
        /// <param name="assetPath">Asset 相对路径。</param>
        /// <returns>磁盘绝对路径。</returns>
        public static string ToAbsolutePath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("无法定位 Unity 项目根目录。");
            }

            return Path.Combine(projectRoot, assetPath);
        }

        /// <summary>
        /// 确保指定 Asset 目录存在。
        /// </summary>
        /// <param name="folderPath">Asset 目录路径。</param>
        public static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentException($"非法 Asset 目录: {folderPath}");
            }

            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        /// <summary>
        /// 生成合法且不重复的资源文件名。
        /// </summary>
        /// <param name="rawName">原始文件名。</param>
        /// <param name="usedNames">已使用的文件名集合。</param>
        /// <returns>合法文件名。</returns>
        public static string MakeUniqueAssetName(string rawName, HashSet<string> usedNames)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = rawName.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            var baseName = new string(chars);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "Sprite";
            }

            var name = baseName;
            var index = 1;
            while (!usedNames.Add(name))
            {
                name = $"{baseName}_{index}";
                index++;
            }

            return name;
        }
    }

    /// <summary>
    /// 图集构建结果。
    /// </summary>
    public readonly struct BuildResult
    {
        /// <summary>图集名称。</summary>
        public string AtlasName { get; }

        /// <summary>Sprite 数量。</summary>
        public int SpriteCount { get; }

        /// <summary>生成的 Unity 图集路径。</summary>
        public string AtlasPath { get; }

        /// <summary>源 Sprite 目录。</summary>
        public string SourceFolderPath { get; }

        /// <summary>UI 风格名。</summary>
        public string StyleName { get; }

        /// <summary>
        /// 创建构建结果。
        /// </summary>
        /// <param name="atlasName">图集名称。</param>
        /// <param name="spriteCount">Sprite 数量。</param>
        /// <param name="atlasPath">生成的 Unity 图集路径。</param>
        /// <param name="sourceFolderPath">源 Sprite 目录。</param>
        /// <param name="styleName">UI 风格名。</param>
        public BuildResult(
            string atlasName,
            int spriteCount,
            string atlasPath,
            string sourceFolderPath,
            string styleName)
        {
            AtlasName = atlasName;
            SpriteCount = spriteCount;
            AtlasPath = atlasPath;
            SourceFolderPath = sourceFolderPath;
            StyleName = styleName;
        }
    }

    /// <summary>
    /// 裁剪后的 Sprite 数据。
    /// </summary>
    internal sealed class TpCloneSprite
    {
        /// <summary>Asset 路径。</summary>
        public string AssetPath { get; }

        /// <summary>源文件名。</summary>
        public string FileName { get; }

        /// <summary>原始宽度。</summary>
        public int SourceWidth { get; }

        /// <summary>原始高度。</summary>
        public int SourceHeight { get; }

        /// <summary>裁剪矩形，左下角坐标。</summary>
        public TpIntRect TrimRect { get; }

        /// <summary>裁剪后的像素。</summary>
        public Color32[] TrimmedPixels { get; }

        /// <summary>裁剪后的九宫格边距 (left, bottom, right, top)。</summary>
        public Vector4 Border { get; }

        /// <summary>
        /// 创建 Sprite 数据。
        /// </summary>
        /// <param name="assetPath">Asset 路径。</param>
        /// <param name="fileName">源文件名。</param>
        /// <param name="sourceWidth">原始宽度。</param>
        /// <param name="sourceHeight">原始高度。</param>
        /// <param name="trimRect">裁剪矩形。</param>
        /// <param name="trimmedPixels">裁剪后的像素。</param>
        /// <param name="border">裁剪后的九宫格边距。</param>
        public TpCloneSprite(
            string assetPath,
            string fileName,
            int sourceWidth,
            int sourceHeight,
            TpIntRect trimRect,
            Color32[] trimmedPixels,
            Vector4 border)
        {
            AssetPath = assetPath;
            FileName = fileName;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            TrimRect = trimRect;
            TrimmedPixels = trimmedPixels;
            Border = border;
        }
    }

    /// <summary>
    /// clone 图集数据。
    /// </summary>
    internal sealed class TpCloneAtlas
    {
        /// <summary>图集宽度。</summary>
        public int Width { get; }

        /// <summary>图集高度。</summary>
        public int Height { get; }

        /// <summary>图集帧列表。</summary>
        public List<TpCloneFrame> Frames { get; }

        /// <summary>
        /// 创建 clone 图集数据。
        /// </summary>
        /// <param name="width">图集宽度。</param>
        /// <param name="height">图集高度。</param>
        /// <param name="frames">图集帧列表。</param>
        public TpCloneAtlas(int width, int height, List<TpCloneFrame> frames)
        {
            Width = width;
            Height = height;
            Frames = frames;
        }
    }

    /// <summary>
    /// clone 图集帧数据。
    /// </summary>
    internal readonly struct TpCloneFrame
    {
        /// <summary>源 Sprite。</summary>
        public TpCloneSprite Sprite { get; }

        /// <summary>图集左上角坐标矩形。</summary>
        public TpIntRect FrameRect { get; }

        /// <summary>
        /// 创建图集帧数据。
        /// </summary>
        /// <param name="sprite">源 Sprite。</param>
        /// <param name="frameRect">图集矩形。</param>
        public TpCloneFrame(TpCloneSprite sprite, TpIntRect frameRect)
        {
            Sprite = sprite;
            FrameRect = frameRect;
        }
    }

    /// <summary>
    /// 解析后的 TexturePacker 图集数据。
    /// </summary>
    internal readonly struct TpParsedAtlas
    {
        /// <summary>图集宽度。</summary>
        public int Width { get; }

        /// <summary>图集高度。</summary>
        public int Height { get; }

        /// <summary>帧数据。</summary>
        public List<TpParsedFrame> Frames { get; }

        /// <summary>
        /// 创建解析后的图集数据。
        /// </summary>
        /// <param name="width">图集宽度。</param>
        /// <param name="height">图集高度。</param>
        /// <param name="frames">帧数据。</param>
        public TpParsedAtlas(int width, int height, List<TpParsedFrame> frames)
        {
            Width = width;
            Height = height;
            Frames = frames;
        }
    }

    /// <summary>
    /// 解析后的 TexturePacker 帧数据。
    /// </summary>
    internal readonly struct TpParsedFrame
    {
        /// <summary>源文件名。</summary>
        public string Filename { get; }

        /// <summary>图集矩形。</summary>
        public TpIntRect Frame { get; }

        /// <summary>是否旋转。</summary>
        public bool Rotated { get; }

        /// <summary>裁剪区域。</summary>
        public TpIntRect SpriteSourceSize { get; }

        /// <summary>源尺寸。</summary>
        public Vector2Int SourceSize { get; }

        /// <summary>pivot。</summary>
        public Vector2 Pivot { get; }

        /// <summary>九宫格边距 (left, bottom, right, top)。</summary>
        public Vector4 Border { get; }

        /// <summary>
        /// 创建解析后的帧数据。
        /// </summary>
        /// <param name="filename">源文件名。</param>
        /// <param name="frame">图集矩形。</param>
        /// <param name="rotated">是否旋转。</param>
        /// <param name="spriteSourceSize">裁剪区域。</param>
        /// <param name="sourceSize">源尺寸。</param>
        /// <param name="pivot">pivot。</param>
        /// <param name="border">九宫格边距。</param>
        public TpParsedFrame(
            string filename,
            TpIntRect frame,
            bool rotated,
            TpIntRect spriteSourceSize,
            Vector2Int sourceSize,
            Vector2 pivot,
            Vector4 border)
        {
            Filename = filename;
            Frame = frame;
            Rotated = rotated;
            SpriteSourceSize = spriteSourceSize;
            SourceSize = sourceSize;
            Pivot = pivot;
            Border = border;
        }
    }

    /// <summary>
    /// 像素比较结果。
    /// </summary>
    internal readonly struct TpPixelDifference
    {
        /// <summary>TexturePacker PNG 宽度。</summary>
        public int OfficialWidth { get; }

        /// <summary>TexturePacker PNG 高度。</summary>
        public int OfficialHeight { get; }

        /// <summary>clone PNG 宽度。</summary>
        public int CloneWidth { get; }

        /// <summary>clone PNG 高度。</summary>
        public int CloneHeight { get; }

        /// <summary>差异像素数量。</summary>
        public int DifferenceCount { get; }

        /// <summary>
        /// 创建像素差异结果。
        /// </summary>
        /// <param name="officialWidth">TexturePacker PNG 宽度。</param>
        /// <param name="officialHeight">TexturePacker PNG 高度。</param>
        /// <param name="cloneWidth">clone PNG 宽度。</param>
        /// <param name="cloneHeight">clone PNG 高度。</param>
        /// <param name="differenceCount">差异像素数量。</param>
        public TpPixelDifference(
            int officialWidth,
            int officialHeight,
            int cloneWidth,
            int cloneHeight,
            int differenceCount)
        {
            OfficialWidth = officialWidth;
            OfficialHeight = officialHeight;
            CloneWidth = cloneWidth;
            CloneHeight = cloneHeight;
            DifferenceCount = differenceCount;
        }
    }

    /// <summary>
    /// 对照比较报告。
    /// </summary>
    internal sealed class TpCloneComparisonReport
    {
        /// <summary>TexturePacker frame 数。</summary>
        public int OfficialFrameCount { get; }

        /// <summary>clone frame 数。</summary>
        public int CloneFrameCount { get; }

        /// <summary>frame 差异。</summary>
        public List<string> FrameDifferences { get; }

        /// <summary>像素差异。</summary>
        public TpPixelDifference PixelDifference { get; }

        /// <summary>报告目录。</summary>
        public string ReportFolder { get; }

        /// <summary>是否完全匹配。</summary>
        public bool IsMatch => FrameDifferences.Count == 0 && PixelDifference.DifferenceCount == 0;

        /// <summary>
        /// 创建对照比较报告。
        /// </summary>
        /// <param name="officialFrameCount">TexturePacker frame 数。</param>
        /// <param name="cloneFrameCount">clone frame 数。</param>
        /// <param name="frameDifferences">frame 差异。</param>
        /// <param name="pixelDifference">像素差异。</param>
        /// <param name="reportFolder">报告目录。</param>
        public TpCloneComparisonReport(
            int officialFrameCount,
            int cloneFrameCount,
            List<string> frameDifferences,
            TpPixelDifference pixelDifference,
            string reportFolder)
        {
            OfficialFrameCount = officialFrameCount;
            CloneFrameCount = cloneFrameCount;
            FrameDifferences = frameDifferences;
            PixelDifference = pixelDifference;
            ReportFolder = reportFolder;
        }

        /// <summary>
        /// 转成对话框摘要。
        /// </summary>
        /// <returns>对话框摘要。</returns>
        public string ToDialogString()
        {
            var preview = FrameDifferences.Count > 0 ? $"\n首条差异: {FrameDifferences[0]}" : string.Empty;
            return $"TexturePacker Frames: {OfficialFrameCount}\n" +
                $"Clone Frames: {CloneFrameCount}\n" +
                $"PNG 尺寸: TexturePacker={PixelDifference.OfficialWidth}x{PixelDifference.OfficialHeight}, Clone={PixelDifference.CloneWidth}x{PixelDifference.CloneHeight}\n" +
                $"Frame 差异数: {FrameDifferences.Count}\n" +
                $"像素差异数: {PixelDifference.DifferenceCount}\n" +
                $"报告目录: {ReportFolder}" +
                preview;
        }

        /// <summary>
        /// 转成完整日志。
        /// </summary>
        /// <returns>完整日志。</returns>
        public string ToLogString()
        {
            var message = ToDialogString();
            if (FrameDifferences.Count == 0)
            {
                return message;
            }

            return message + "\n" + string.Join("\n", FrameDifferences);
        }
    }

    /// <summary>
    /// 整数矩形，x/y 为左上角或左下角由调用处约定。
    /// </summary>
    internal readonly struct TpIntRect : IEquatable<TpIntRect>
    {
        public readonly int x;
        public readonly int y;
        public readonly int w;
        public readonly int h;

        /// <summary>右边界。</summary>
        public int Right => x + w;

        /// <summary>下边界或上边界，取决于调用处坐标系。</summary>
        public int Bottom => y + h;

        /// <summary>
        /// 创建整数矩形。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <param name="w">宽度。</param>
        /// <param name="h">高度。</param>
        public TpIntRect(int x, int y, int w, int h)
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }

        /// <summary>
        /// 判断两个矩形是否相等。
        /// </summary>
        /// <param name="other">另一个矩形。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(TpIntRect other)
        {
            return x == other.x && y == other.y && w == other.w && h == other.h;
        }

        /// <summary>
        /// 判断对象是否为相同矩形。
        /// </summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相等时返回 true。</returns>
        public override bool Equals(object obj)
        {
            return obj is TpIntRect other && Equals(other);
        }

        /// <summary>
        /// 获取哈希值。
        /// </summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = x;
                hashCode = (hashCode * 397) ^ y;
                hashCode = (hashCode * 397) ^ w;
                hashCode = (hashCode * 397) ^ h;
                return hashCode;
            }
        }

        /// <summary>
        /// 比较两个矩形是否相等。
        /// </summary>
        /// <param name="left">左值。</param>
        /// <param name="right">右值。</param>
        /// <returns>相等时返回 true。</returns>
        public static bool operator ==(TpIntRect left, TpIntRect right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 比较两个矩形是否不相等。
        /// </summary>
        /// <param name="left">左值。</param>
        /// <param name="right">右值。</param>
        /// <returns>不相等时返回 true。</returns>
        public static bool operator !=(TpIntRect left, TpIntRect right)
        {
            return !left.Equals(right);
        }
    }
}

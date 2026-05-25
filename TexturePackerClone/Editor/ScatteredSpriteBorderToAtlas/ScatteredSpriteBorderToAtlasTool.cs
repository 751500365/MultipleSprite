using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.ScatteredSpriteBorderToAtlas
{
    /// <summary>
    /// 将 <c>Assets/GameRes/UI/Sprite</c> 散图上的九宫格边距同步到
    /// <c>SpriteAtlasPathMap.json</c> 对应图集 Multiple 子 Sprite。
    /// </summary>
    public static class ScatteredSpriteBorderToAtlasTool
    {
        public const string SpriteRoot = "Assets/GameRes/UI/Sprite/";
        public const string AtlasRoot = "Assets/GameRes/UI/Atlas/";
        public const string SpriteAtlasMapPath = AtlasRoot + "SpriteAtlasPathMap.json";

        /// <summary>单条同步记录。</summary>
        public sealed class ApplyRecord
        {
            public string SpriteName;
            public string StyleName;
            public string ScatteredPath;
            public string AtlasPath;
            public Vector4 OldBorder;
            public Vector4 NewBorder;
        }

        /// <summary>同步汇总。</summary>
        public sealed class ApplyReport
        {
            public int MappingEntryCount;
            public int AtlasTextureCount;
            public int ModifiedAtlasCount;
            public int AppliedCount;
            public int SkippedNoScattered;
            public int SkippedNoBorderOnScattered;
            public int SkippedNoAtlasSubSprite;
            public int SkippedAlreadyHasBorder;
            public int SkippedUnchanged;
            public readonly List<ApplyRecord> Records = new List<ApplyRecord>();
        }

        /// <summary>
        /// 按 <see cref="SpriteAtlasMapPath"/> 全量同步九宫格到图集子 Sprite。
        /// </summary>
        /// <param name="applyChanges">为 true 时写回 TextureImporter。</param>
        /// <param name="onlyIfAtlasMissingBorder">图集子 Sprite 已有 border 时跳过。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>映射表加载成功时返回 true。</returns>
        public static bool ApplyFromSpriteAtlasMap(
            bool applyChanges,
            bool onlyIfAtlasMissingBorder,
            out ApplyReport report)
        {
            report = new ApplyReport();
            if (!TryLoadMapEntries(out List<MapEntry> entries, out string error))
            {
                Debug.LogError($"[ScatteredSpriteBorderToAtlas] {error}");
                return false;
            }

            report.MappingEntryCount = entries.Count;
            return ApplyEntries(entries, applyChanges, onlyIfAtlasMissingBorder, report);
        }

        /// <summary>
        /// 对选中的图集贴图同步九宫格（从同风格散图目录查找同名散图）。
        /// </summary>
        /// <param name="atlasAssetPaths">图集 png Asset 路径。</param>
        /// <param name="applyChanges">为 true 时写回 TextureImporter。</param>
        /// <param name="onlyIfAtlasMissingBorder">图集子 Sprite 已有 border 时跳过。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>至少处理一张图集时返回 true。</returns>
        public static bool ApplyForAtlasTextures(
            IReadOnlyList<string> atlasAssetPaths,
            bool applyChanges,
            bool onlyIfAtlasMissingBorder,
            out ApplyReport report)
        {
            report = new ApplyReport();
            if (atlasAssetPaths == null || atlasAssetPaths.Count == 0)
            {
                Debug.LogError("[ScatteredSpriteBorderToAtlas] 未指定图集贴图。");
                return false;
            }

            TryLoadMapEntries(out List<MapEntry> mapEntries, out _);
            var entries = new List<MapEntry>();
            for (var i = 0; i < atlasAssetPaths.Count; i++)
            {
                var atlasPath = NormalizePath(atlasAssetPaths[i]);
                if (!TryParseStyleFromAtlasPath(atlasPath, out var styleName))
                {
                    continue;
                }

                var atlasFileName = Path.GetFileNameWithoutExtension(atlasPath);
                if (mapEntries != null && mapEntries.Count > 0)
                {
                    for (var j = 0; j < mapEntries.Count; j++)
                    {
                        if (string.Equals(mapEntries[j].AtlasPath, atlasPath, StringComparison.OrdinalIgnoreCase))
                        {
                            entries.Add(mapEntries[j]);
                        }
                    }
                }

                if (!TryBuildEntriesFromAtlasTexture(atlasPath, styleName, atlasFileName, entries))
                {
                    Debug.LogWarning($"[ScatteredSpriteBorderToAtlas] 跳过无法解析的图集: {atlasPath}");
                }
            }

            if (entries.Count == 0)
            {
                Debug.LogError("[ScatteredSpriteBorderToAtlas] 未找到可处理的图集子 Sprite。");
                return false;
            }

            return ApplyEntries(entries, applyChanges, onlyIfAtlasMissingBorder, report);
        }

        /// <summary>
        /// 对选中的散图目录同步九宫格到对应风格图集（Sprite/Style/AtlasName → Atlas/Style/AtlasName.png）。
        /// </summary>
        /// <param name="scatteredFolderPaths">散图目录 Asset 路径。</param>
        /// <param name="applyChanges">为 true 时写回 TextureImporter。</param>
        /// <param name="onlyIfAtlasMissingBorder">图集子 Sprite 已有 border 时跳过。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>至少处理一个目录时返回 true。</returns>
        public static bool ApplyForScatteredFolders(
            IReadOnlyList<string> scatteredFolderPaths,
            bool applyChanges,
            bool onlyIfAtlasMissingBorder,
            out ApplyReport report)
        {
            report = new ApplyReport();
            var entries = new List<MapEntry>();
            for (var i = 0; i < scatteredFolderPaths.Count; i++)
            {
                var folderPath = NormalizePath(scatteredFolderPaths[i]);
                if (!TryResolveAtlasPathFromScatteredFolder(folderPath, out var atlasPath, out var styleName, out var atlasFolderName))
                {
                    Debug.LogWarning($"[ScatteredSpriteBorderToAtlas] 跳过无法推导图集的散图目录: {folderPath}");
                    continue;
                }

                if (!TryBuildEntriesFromScatteredFolder(folderPath, styleName, atlasFolderName, atlasPath, entries))
                {
                    Debug.LogWarning($"[ScatteredSpriteBorderToAtlas] 散图目录无可用贴图: {folderPath}");
                }
            }

            if (entries.Count == 0)
            {
                Debug.LogError("[ScatteredSpriteBorderToAtlas] 未找到可处理的散图。");
                return false;
            }

            return ApplyEntries(entries, applyChanges, onlyIfAtlasMissingBorder, report);
        }

        /// <summary>
        /// 从映射表加载全部 (sprite, style, atlas) 条目。
        /// </summary>
        /// <param name="entries">输出条目。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>加载成功时返回 true。</returns>
        private static bool TryLoadMapEntries(out List<MapEntry> entries, out string error)
        {
            entries = new List<MapEntry>();
            error = null;
            if (!File.Exists(SpriteAtlasMapPath))
            {
                error = $"映射表不存在: {SpriteAtlasMapPath}";
                return false;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(SpriteAtlasMapPath));
                if (!(root["sprites"] is JObject sprites))
                {
                    error = "映射表缺少 sprites 节点。";
                    return false;
                }

                foreach (var property in sprites.Properties())
                {
                    if (!(property.Value is JArray mappingsArray))
                    {
                        continue;
                    }

                    for (var i = 0; i < mappingsArray.Count; i++)
                    {
                        if (!(mappingsArray[i] is JObject mappingObject))
                        {
                            continue;
                        }

                        var style = mappingObject["style"]?.Value<string>() ?? string.Empty;
                        var atlasFolder = mappingObject["atlas"]?.Value<string>() ?? string.Empty;
                        var atlasPath = mappingObject["path"]?.Value<string>() ?? string.Empty;
                        if (string.IsNullOrEmpty(style) || string.IsNullOrEmpty(atlasPath))
                        {
                            continue;
                        }

                        entries.Add(new MapEntry(property.Name, style, atlasFolder, NormalizePath(atlasPath)));
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// 按图集路径分组并写回 border。
        /// </summary>
        /// <param name="entries">待处理条目。</param>
        /// <param name="applyChanges">是否写回。</param>
        /// <param name="onlyIfAtlasMissingBorder">图集已有 border 时跳过。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>处理成功时返回 true。</returns>
        private static bool ApplyEntries(
            List<MapEntry> entries,
            bool applyChanges,
            bool onlyIfAtlasMissingBorder,
            ApplyReport report)
        {
            entries = DeduplicateEntries(entries);
            report.MappingEntryCount = entries.Count;

            var byAtlas = new Dictionary<string, List<MapEntry>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!byAtlas.TryGetValue(entry.AtlasPath, out List<MapEntry> list))
                {
                    list = new List<MapEntry>();
                    byAtlas[entry.AtlasPath] = list;
                }

                list.Add(entry);
            }

            report.AtlasTextureCount = byAtlas.Count;
            var atlasesToReimport = new List<string>();

            foreach (var pair in byAtlas)
            {
                ApplyForSingleAtlas(
                    pair.Key,
                    pair.Value,
                    onlyIfAtlasMissingBorder,
                    applyChanges,
                    report,
                    out var atlasChanged);
                if (atlasChanged && applyChanges)
                {
                    atlasesToReimport.Add(pair.Key);
                }
            }

            if (applyChanges)
            {
                ReimportAtlases(atlasesToReimport);
                report.ModifiedAtlasCount = atlasesToReimport.Count;
            }

            return true;
        }

        /// <summary>
        /// 对修改过的图集执行 SaveAndReimport（勿在 AssetDatabase.StartAssetEditing 内调用）。
        /// </summary>
        /// <param name="atlasPaths">图集 Asset 路径列表。</param>
        private static void ReimportAtlases(List<string> atlasPaths)
        {
            for (var i = 0; i < atlasPaths.Count; i++)
            {
                var importer = AssetImporter.GetAtPath(atlasPaths[i]) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 写回单张图集 Multiple 子 Sprite 的 border。
        /// </summary>
        /// <param name="atlasPath">图集 Asset 路径。</param>
        /// <param name="entries">该图集相关条目。</param>
        /// <param name="onlyIfAtlasMissingBorder">图集已有 border 时跳过。</param>
        /// <param name="writeImporter">为 true 时写回 importer.spritesheet。</param>
        /// <param name="report">输出报告。</param>
        /// <param name="atlasChanged">该图集 spritesheet 是否有修改。</param>
        private static void ApplyForSingleAtlas(
            string atlasPath,
            List<MapEntry> entries,
            bool onlyIfAtlasMissingBorder,
            bool writeImporter,
            ApplyReport report,
            out bool atlasChanged)
        {
            atlasChanged = false;
            var importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer == null || importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    report.SkippedNoAtlasSubSprite++;
                }

                return;
            }

#pragma warning disable 618
            var sheet = importer.spritesheet;
#pragma warning restore 618
            if (sheet == null || sheet.Length == 0)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    report.SkippedNoAtlasSubSprite++;
                }

                return;
            }

            var nameToIndex = BuildSpriteNameIndex(sheet);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!TryResolveScatteredPath(entry, out var scatteredPath))
                {
                    report.SkippedNoScattered++;
                    continue;
                }

                if (!SpriteBorderTransferUtility.HasBorder(SpriteBorderTransferUtility.ReadSourceSpriteBorder(scatteredPath)))
                {
                    report.SkippedNoBorderOnScattered++;
                    continue;
                }

                if (!nameToIndex.TryGetValue(entry.SpriteName, out var spriteIndex))
                {
                    report.SkippedNoAtlasSubSprite++;
                    continue;
                }

                var meta = sheet[spriteIndex];
                if (onlyIfAtlasMissingBorder && SpriteBorderTransferUtility.HasBorder(meta.border))
                {
                    report.SkippedAlreadyHasBorder++;
                    continue;
                }

                var newBorder = SpriteBorderTransferUtility.ResolveAtlasSubSpriteBorder(scatteredPath, meta.rect);
                if (!SpriteBorderTransferUtility.HasBorder(newBorder))
                {
                    report.SkippedNoBorderOnScattered++;
                    continue;
                }

                if (SpriteBorderTransferUtility.BordersApproximatelyEqual(meta.border, newBorder))
                {
                    report.SkippedUnchanged++;
                    continue;
                }

                report.Records.Add(new ApplyRecord
                {
                    SpriteName = entry.SpriteName,
                    StyleName = entry.StyleName,
                    ScatteredPath = scatteredPath,
                    AtlasPath = atlasPath,
                    OldBorder = meta.border,
                    NewBorder = newBorder
                });

                meta.border = newBorder;
                sheet[spriteIndex] = meta;
                atlasChanged = true;
                report.AppliedCount++;
            }

            if (!atlasChanged || !writeImporter)
            {
                return;
            }

#pragma warning disable 618
            importer.spritesheet = sheet;
#pragma warning restore 618
        }

        /// <summary>
        /// 按图集路径 + 风格 + 子 Sprite 名去重。
        /// </summary>
        /// <param name="entries">原始条目。</param>
        /// <returns>去重后的条目。</returns>
        private static List<MapEntry> DeduplicateEntries(List<MapEntry> entries)
        {
            var result = new List<MapEntry>(entries.Count);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var key = $"{entry.AtlasPath}|{entry.StyleName}|{entry.SpriteName}";
                if (used.Add(key))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// 构建子 Sprite 名称到 spritesheet 下标的映射。
        /// </summary>
        /// <param name="sheet">图集 spritesheet。</param>
        /// <returns>名称到下标映射。</returns>
        private static Dictionary<string, int> BuildSpriteNameIndex(SpriteMetaData[] sheet)
        {
            var map = new Dictionary<string, int>(sheet.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sheet.Length; i++)
            {
                if (!string.IsNullOrEmpty(sheet[i].name))
                {
                    map.TryAdd(sheet[i].name, i);
                }
            }

            return map;
        }

        /// <summary>
        /// 解析散图路径：映射表 atlas 目录名优先，再按图集文件名，最后在风格目录下搜索。
        /// </summary>
        /// <param name="entry">映射条目。</param>
        /// <param name="scatteredPath">输出散图路径。</param>
        /// <returns>找到散图时返回 true。</returns>
        private static bool TryResolveScatteredPath(MapEntry entry, out string scatteredPath)
        {
            scatteredPath = null;
            if (!string.IsNullOrEmpty(entry.AtlasFolderName))
            {
                var mappedPath = BuildScatteredSpritePath(entry.StyleName, entry.AtlasFolderName, entry.SpriteName);
                if (ScatteredTextureExists(mappedPath))
                {
                    scatteredPath = mappedPath;
                    return true;
                }
            }

            var atlasFileName = Path.GetFileNameWithoutExtension(entry.AtlasPath);
            if (!string.IsNullOrEmpty(atlasFileName)
                && !string.Equals(atlasFileName, entry.AtlasFolderName, StringComparison.OrdinalIgnoreCase))
            {
                var fallbackPath = BuildScatteredSpritePath(entry.StyleName, atlasFileName, entry.SpriteName);
                if (ScatteredTextureExists(fallbackPath))
                {
                    scatteredPath = fallbackPath;
                    return true;
                }
            }

            return TryFindScatteredInStyleFolder(entry.SpriteName, entry.StyleName, out scatteredPath);
        }

        /// <summary>
        /// 判断散图资源是否存在（磁盘或 AssetDatabase）。
        /// </summary>
        /// <param name="assetPath">散图 Asset 路径。</param>
        /// <returns>存在时返回 true。</returns>
        private static bool ScatteredTextureExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (File.Exists(ToAbsolutePath(assetPath)))
            {
                return true;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null;
        }

        /// <summary>
        /// 在风格目录下按名称搜索散图。
        /// </summary>
        /// <param name="spriteName">子 Sprite 名称。</param>
        /// <param name="styleName">风格名。</param>
        /// <param name="scatteredPath">输出散图路径。</param>
        /// <returns>找到时返回 true。</returns>
        private static bool TryFindScatteredInStyleFolder(string spriteName, string styleName, out string scatteredPath)
        {
            scatteredPath = null;
            var searchFolder = SpriteRoot + styleName;
            if (!AssetDatabase.IsValidFolder(searchFolder.TrimEnd('/')))
            {
                return false;
            }

            var guids = AssetDatabase.FindAssets($"{spriteName} t:Texture2D", new[] { searchFolder });
            for (var i = 0; i < guids.Length; i++)
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!candidatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileNameWithoutExtension(candidatePath), spriteName, StringComparison.OrdinalIgnoreCase))
                {
                    scatteredPath = candidatePath;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从图集贴图现有子 Sprite 列表构建处理条目（不依赖映射表时使用）。
        /// </summary>
        /// <param name="atlasPath">图集路径。</param>
        /// <param name="styleName">风格名。</param>
        /// <param name="atlasFileName">图集文件名（无扩展名）。</param>
        /// <param name="entries">输出条目列表（会追加）。</param>
        /// <returns>成功追加条目时返回 true。</returns>
        private static bool TryBuildEntriesFromAtlasTexture(
            string atlasPath,
            string styleName,
            string atlasFileName,
            List<MapEntry> entries)
        {
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
            var appended = false;
            for (var i = 0; i < assets.Length; i++)
            {
                if (!(assets[i] is Sprite subSprite) || string.IsNullOrEmpty(subSprite.name))
                {
                    continue;
                }

                if (!added.Add(subSprite.name))
                {
                    continue;
                }

                entries.Add(new MapEntry(subSprite.name, styleName, atlasFileName, atlasPath));
                appended = true;
            }

            return appended;
        }

        /// <summary>
        /// 从散图目录扫描 png 并构建处理条目。
        /// </summary>
        /// <param name="scatteredFolderPath">散图目录。</param>
        /// <param name="styleName">风格名。</param>
        /// <param name="atlasFolderName">图集目录名。</param>
        /// <param name="atlasPath">图集路径。</param>
        /// <param name="entries">输出条目列表（会追加）。</param>
        /// <returns>成功追加条目时返回 true。</returns>
        private static bool TryBuildEntriesFromScatteredFolder(
            string scatteredFolderPath,
            string styleName,
            string atlasFolderName,
            string atlasPath,
            List<MapEntry> entries)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { scatteredFolderPath });
            var appended = false;
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var spriteName = Path.GetFileNameWithoutExtension(path);
                entries.Add(new MapEntry(spriteName, styleName, atlasFolderName, atlasPath));
                appended = true;
            }

            return appended;
        }

        /// <summary>
        /// 从散图目录推导图集路径：Sprite/Style/AtlasFolder → Atlas/Style/AtlasFolder.png。
        /// </summary>
        /// <param name="scatteredFolderPath">散图目录。</param>
        /// <param name="atlasPath">输出图集路径。</param>
        /// <param name="styleName">输出风格名。</param>
        /// <param name="atlasFolderName">输出图集目录名。</param>
        /// <returns>推导成功时返回 true。</returns>
        private static bool TryResolveAtlasPathFromScatteredFolder(
            string scatteredFolderPath,
            out string atlasPath,
            out string styleName,
            out string atlasFolderName)
        {
            atlasPath = null;
            styleName = null;
            atlasFolderName = null;

            var normalized = NormalizePath(scatteredFolderPath).TrimEnd('/');
            if (!normalized.StartsWith(SpriteRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative = normalized.Substring(SpriteRoot.Length);
            var parts = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            styleName = parts[0];
            atlasFolderName = parts[parts.Length - 1];
            atlasPath = $"{AtlasRoot}{styleName}/{atlasFolderName}.png";
            return File.Exists(ToAbsolutePath(atlasPath));
        }

        /// <summary>
        /// 从图集路径解析风格名：Atlas/Style/Name.png。
        /// </summary>
        /// <param name="atlasPath">图集路径。</param>
        /// <param name="styleName">输出风格名。</param>
        /// <returns>解析成功时返回 true。</returns>
        private static bool TryParseStyleFromAtlasPath(string atlasPath, out string styleName)
        {
            styleName = null;
            var normalized = NormalizePath(atlasPath);
            if (!normalized.StartsWith(AtlasRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative = normalized.Substring(AtlasRoot.Length);
            var slashIndex = relative.IndexOf('/');
            if (slashIndex <= 0)
            {
                return false;
            }

            styleName = relative.Substring(0, slashIndex);
            return !string.IsNullOrEmpty(styleName);
        }

        /// <summary>
        /// 构建散图 Asset 路径。
        /// </summary>
        /// <param name="styleName">风格名。</param>
        /// <param name="folderName">图集目录名。</param>
        /// <param name="spriteName">Sprite 名。</param>
        /// <returns>散图路径。</returns>
        private static string BuildScatteredSpritePath(string styleName, string folderName, string spriteName)
        {
            return $"{SpriteRoot}{styleName}/{folderName}/{spriteName}.png";
        }

        /// <summary>
        /// 统一 Asset 路径分隔符。
        /// </summary>
        /// <param name="path">原始路径。</param>
        /// <returns>规范化路径。</returns>
        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        /// <summary>
        /// 把 Asset 相对路径转换为磁盘绝对路径。
        /// </summary>
        /// <param name="assetPath">Asset 相对路径。</param>
        /// <returns>磁盘绝对路径。</returns>
        private static string ToAbsolutePath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot) ? assetPath : Path.Combine(projectRoot, assetPath);
        }

        /// <summary>
        /// 映射表单条工作项。
        /// </summary>
        private readonly struct MapEntry
        {
            public string SpriteName { get; }
            public string StyleName { get; }
            public string AtlasFolderName { get; }
            public string AtlasPath { get; }

            /// <summary>
            /// 创建映射条目。
            /// </summary>
            /// <param name="spriteName">子 Sprite 名称。</param>
            /// <param name="styleName">风格名。</param>
            /// <param name="atlasFolderName">图集目录名。</param>
            /// <param name="atlasPath">图集 Asset 路径。</param>
            public MapEntry(string spriteName, string styleName, string atlasFolderName, string atlasPath)
            {
                SpriteName = spriteName;
                StyleName = styleName;
                AtlasFolderName = atlasFolderName;
                AtlasPath = atlasPath;
            }
        }
    }
}

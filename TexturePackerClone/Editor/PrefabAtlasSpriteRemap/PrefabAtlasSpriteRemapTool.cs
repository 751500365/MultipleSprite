using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.PrefabAtlasSpriteRemap
{
    /// <summary>
    /// 将 Prefab 中 <c>Assets/GameRes/UI/Sprite/</c> 散图 Sprite 引用替换为
    /// <c>SpriteAtlasPathMap.json</c> 中同风格图集 Multiple Sprites 同名子图；无同风格映射则不替换。
    /// 撤销请使用 <see cref="RevertPrefabsToScatteredSprites"/>。
    /// </summary>
    public static class PrefabAtlasSpriteRemapTool
    {
        public const string SpriteRoot = "Assets/GameRes/UI/Sprite/";
        public const string AtlasRoot = "Assets/GameRes/UI/Atlas/";
        public const string SpriteAtlasMapPath = AtlasRoot + "SpriteAtlasPathMap.json";

        /// <summary>打图集后 Prefab 引用替换的默认搜索根目录。</summary>
        public const string DefaultPrefabSearchRoot = "Assets/GameRes/UI/Preafab";

        /// <summary>单条替换记录。</summary>
        public sealed class ReplaceRecord
        {
            public string PrefabPath;
            public string PropertyPath;
            public string SpriteName;
            public string OldAssetPath;
            public string NewAtlasPath;
        }

        /// <summary>单条撤销记录。</summary>
        public sealed class RevertRecord
        {
            public string PrefabPath;
            public string PropertyPath;
            public string SpriteName;
            public string OldAtlasPath;
            public string NewScatteredPath;
        }

        /// <summary>扫描/替换汇总。</summary>
        public sealed class RemapReport
        {
            public int PrefabCount;
            public int ModifiedPrefabCount;
            public int ReplacedReferenceCount;
            public int SkippedNotScattered;
            public int SkippedNoMapping;
            public int SkippedNoSameStyleMapping;
            public int SkippedNoSubSprite;
            public readonly List<ReplaceRecord> Records = new List<ReplaceRecord>();
        }

        /// <summary>撤销替换汇总。</summary>
        public sealed class RevertReport
        {
            public int PrefabCount;
            public int ModifiedPrefabCount;
            public int RevertedReferenceCount;
            public int SkippedAlreadyScattered;
            public int SkippedNotAtlas;
            public int SkippedNoScattered;
            public readonly List<RevertRecord> Records = new List<RevertRecord>();
        }

        /// <summary>映射表与图集子 Sprite 一致性校验结果。</summary>
        public sealed class MapValidationReport
        {
            public int TotalMappingEntries;
            public int ResolvedSubSpriteCount;
            public int MissingSubSpriteCount;
            public readonly List<string> MissingEntries = new List<string>();
        }

        /// <summary>
        /// 校验 <see cref="SpriteAtlasPathMap.json"/> 每条同风格映射是否能在对应图集 png 中
        /// 通过 <see cref="AssetDatabase.LoadAllAssetsAtPath"/> 找到同名子 Sprite（与 Prefab 替换、运行时 GetSubAssetObject 一致）。
        /// </summary>
        /// <param name="report">校验报告。</param>
        /// <returns>映射表加载成功时返回 true。</returns>
        public static bool ValidateSpriteAtlasMap(out MapValidationReport report)
        {
            report = new MapValidationReport();
            if (!AtlasSpriteLookup.TryCreate(out AtlasSpriteLookup lookup, out string error))
            {
                Debug.LogError($"[PrefabAtlasSpriteRemap] {error}");
                return false;
            }

            lookup.ValidateAllMappings(report);
            return true;
        }

        private sealed class AtlasSpriteLookup
        {
            private readonly Dictionary<string, List<SpriteAtlasPathMapping>> spriteNameToMappings =
                new Dictionary<string, List<SpriteAtlasPathMapping>>(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, Dictionary<string, Sprite>> atlasPathToSprites =
                new Dictionary<string, Dictionary<string, Sprite>>(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, Sprite> scatteredSpriteCache =
                new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

            public static bool TryCreate(out AtlasSpriteLookup lookup, out string error)
            {
                lookup = null;
                error = null;
                if (!File.Exists(SpriteAtlasMapPath))
                {
                    error = $"映射表不存在: {SpriteAtlasMapPath}";
                    return false;
                }

                try
                {
                    lookup = new AtlasSpriteLookup();
                    lookup.ParseMap(File.ReadAllText(SpriteAtlasMapPath));
                    return true;
                }
                catch (Exception e)
                {
                    error = e.Message;
                    return false;
                }
            }

            public bool TryResolveAtlasSprite(
                string spriteName,
                string styleName,
                out Sprite sprite,
                out string atlasPath,
                out bool hasSpriteMapping,
                out bool hasSameStyleMapping)
            {
                sprite = null;
                atlasPath = null;
                hasSpriteMapping = false;
                hasSameStyleMapping = false;
                if (string.IsNullOrEmpty(spriteName)
                    || !spriteNameToMappings.TryGetValue(spriteName, out List<SpriteAtlasPathMapping> mappings))
                {
                    return false;
                }

                hasSpriteMapping = true;
                atlasPath = FindAtlasPathForStyle(mappings, styleName);
                if (string.IsNullOrEmpty(atlasPath))
                {
                    return false;
                }

                hasSameStyleMapping = true;
                return TryGetSubSprite(atlasPath, spriteName, out sprite);
            }

            private void ParseMap(string json)
            {
                spriteNameToMappings.Clear();
                atlasPathToSprites.Clear();

                JObject root = JObject.Parse(json);
                if (!(root["sprites"] is JObject sprites))
                {
                    return;
                }

                foreach (JProperty property in sprites.Properties())
                {
                    if (!(property.Value is JArray mappingsArray))
                    {
                        continue;
                    }

                    var mappings = new List<SpriteAtlasPathMapping>(mappingsArray.Count);
                    for (int i = 0; i < mappingsArray.Count; i++)
                    {
                        if (!(mappingsArray[i] is JObject mappingObject))
                        {
                            continue;
                        }

                        string path = mappingObject["path"]?.Value<string>();
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        mappings.Add(new SpriteAtlasPathMapping(
                            mappingObject["style"]?.Value<string>() ?? string.Empty,
                            mappingObject["atlas"]?.Value<string>() ?? string.Empty,
                            path));
                    }

                    if (mappings.Count > 0)
                    {
                        spriteNameToMappings[property.Name] = mappings;
                    }
                }
            }

            /// <summary>
            /// 仅匹配与散图路径一致的风格，不回退 DefaultStyle 或其它风格。
            /// </summary>
            private static string FindAtlasPathForStyle(List<SpriteAtlasPathMapping> mappings, string styleName)
            {
                for (int i = 0; i < mappings.Count; i++)
                {
                    if (string.Equals(mappings[i].Style, styleName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(mappings[i].Path))
                    {
                        return mappings[i].Path;
                    }
                }

                return null;
            }

            public bool TryGetSubSprite(string atlasPath, string spriteName, out Sprite sprite)
            {
                sprite = null;
                if (!atlasPathToSprites.TryGetValue(atlasPath, out Dictionary<string, Sprite> nameToSprite))
                {
                    nameToSprite = BuildAtlasSpriteDict(atlasPath);
                    atlasPathToSprites[atlasPath] = nameToSprite;
                }

                return nameToSprite.TryGetValue(spriteName, out sprite) && sprite != null;
            }

            /// <summary>
            /// 按映射表 atlas 目录名与图集文件名，在 <c>Sprite/{style}/</c> 下查找同名散图。
            /// </summary>
            public bool TryResolveScatteredSprite(
                string spriteName,
                string styleName,
                string atlasAssetPath,
                string atlasFileName,
                out Sprite scatteredSprite,
                out string scatteredPath)
            {
                scatteredSprite = null;
                scatteredPath = null;
                if (string.IsNullOrEmpty(spriteName) || string.IsNullOrEmpty(styleName))
                {
                    return false;
                }

                string atlasFolderFromMap = null;
                if (spriteNameToMappings.TryGetValue(spriteName, out List<SpriteAtlasPathMapping> mappings))
                {
                    for (int i = 0; i < mappings.Count; i++)
                    {
                        SpriteAtlasPathMapping mapping = mappings[i];
                        if (string.Equals(mapping.Style, styleName, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrEmpty(atlasAssetPath)
                                || string.Equals(mapping.Path, atlasAssetPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            atlasFolderFromMap = mapping.Atlas;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(atlasFolderFromMap)
                    && TryLoadScatteredSpriteAtPath(
                        BuildScatteredSpritePath(styleName, atlasFolderFromMap, spriteName),
                        out scatteredSprite,
                        out scatteredPath))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(atlasFileName)
                    && !string.Equals(atlasFileName, atlasFolderFromMap, StringComparison.OrdinalIgnoreCase)
                    && TryLoadScatteredSpriteAtPath(
                        BuildScatteredSpritePath(styleName, atlasFileName, spriteName),
                        out scatteredSprite,
                        out scatteredPath))
                {
                    return true;
                }

                return TryFindScatteredSpriteInStyleFolder(spriteName, styleName, out scatteredSprite, out scatteredPath);
            }

            private bool TryLoadScatteredSpriteAtPath(string assetPath, out Sprite sprite, out string resolvedPath)
            {
                sprite = null;
                resolvedPath = null;
                if (string.IsNullOrEmpty(assetPath))
                {
                    return false;
                }

                if (scatteredSpriteCache.TryGetValue(assetPath, out Sprite cached) && cached != null)
                {
                    sprite = cached;
                    resolvedPath = assetPath;
                    return true;
                }

                Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (loaded == null)
                {
                    scatteredSpriteCache[assetPath] = null;
                    return false;
                }

                scatteredSpriteCache[assetPath] = loaded;
                sprite = loaded;
                resolvedPath = assetPath;
                return true;
            }

            private bool TryFindScatteredSpriteInStyleFolder(
                string spriteName,
                string styleName,
                out Sprite sprite,
                out string resolvedPath)
            {
                sprite = null;
                resolvedPath = null;
                string searchFolder = SpriteRoot + styleName;
                if (!AssetDatabase.IsValidFolder(searchFolder.TrimEnd('/')))
                {
                    return false;
                }

                string[] guids = AssetDatabase.FindAssets($"{spriteName} t:Sprite", new[] { searchFolder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!candidatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.Equals(
                            Path.GetFileNameWithoutExtension(candidatePath),
                            spriteName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (TryLoadScatteredSpriteAtPath(candidatePath, out sprite, out resolvedPath))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static string BuildScatteredSpritePath(string styleName, string folderName, string spriteName)
            {
                return $"{SpriteRoot}{styleName}/{folderName}/{spriteName}.png";
            }

            /// <summary>
            /// 与运行时 <c>GetSubAssetObject&lt;Sprite&gt;(spriteName)</c> 等价：按子资源名取图集内 Sprite。
            /// </summary>
            private static Dictionary<string, Sprite> BuildAtlasSpriteDict(string atlasPath)
            {
                var dict = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is Sprite subSprite && !string.IsNullOrEmpty(subSprite.name))
                    {
                        dict.TryAdd(subSprite.name, subSprite);
                    }
                }

                return dict;
            }

            /// <summary>
            /// 遍历映射表，确认每条 style+path 下存在与 JSON key 同名的 Multiple 子 Sprite。
            /// </summary>
            public void ValidateAllMappings(MapValidationReport report)
            {
                foreach (KeyValuePair<string, List<SpriteAtlasPathMapping>> pair in spriteNameToMappings)
                {
                    string spriteName = pair.Key;
                    List<SpriteAtlasPathMapping> mappings = pair.Value;
                    for (int i = 0; i < mappings.Count; i++)
                    {
                        SpriteAtlasPathMapping mapping = mappings[i];
                        report.TotalMappingEntries++;
                        if (TryGetSubSprite(mapping.Path, spriteName, out Sprite subSprite)
                            && subSprite != null
                            && string.Equals(subSprite.name, spriteName, StringComparison.OrdinalIgnoreCase))
                        {
                            report.ResolvedSubSpriteCount++;
                        }
                        else
                        {
                            report.MissingSubSpriteCount++;
                            report.MissingEntries.Add(
                                $"{spriteName} | style={mapping.Style} | atlas={mapping.Atlas} | path={mapping.Path}");
                        }
                    }
                }
            }
        }

        private readonly struct SpriteAtlasPathMapping
        {
            public string Style { get; }
            public string Atlas { get; }
            public string Path { get; }

            public SpriteAtlasPathMapping(string style, string atlas, string path)
            {
                Style = style;
                Atlas = atlas;
                Path = path;
            }
        }

        /// <summary>
        /// 扫描并（可选）写回 Prefab。
        /// </summary>
        /// <param name="prefabPaths">Prefab 资源路径列表。</param>
        /// <param name="applyChanges">为 true 时保存修改后的 Prefab。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>映射表或 Prefab 处理是否成功。</returns>
        public static bool ProcessPrefabs(IReadOnlyList<string> prefabPaths, bool applyChanges, out RemapReport report)
        {
            report = new RemapReport();
            if (!AtlasSpriteLookup.TryCreate(out AtlasSpriteLookup lookup, out string error))
            {
                Debug.LogError($"[PrefabAtlasSpriteRemap] {error}");
                return false;
            }

            report.PrefabCount = prefabPaths.Count;
            if (applyChanges)
            {
                AssetDatabase.StartAssetEditing();
            }

            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    string prefabPath = prefabPaths[i];
                    if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ProcessSinglePrefab(prefabPath, lookup, applyChanges, report);
                }
            }
            finally
            {
                if (applyChanges)
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            return true;
        }

        /// <summary>
        /// 将 Prefab 上图集 Multiple 子 Sprite 引用撤销为 <c>Sprite/{style}/</c> 下同名散图。
        /// </summary>
        /// <param name="prefabPaths">Prefab 资源路径列表。</param>
        /// <param name="applyChanges">为 true 时保存修改后的 Prefab。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>映射表加载成功时返回 true。</returns>
        public static bool RevertPrefabsToScatteredSprites(
            IReadOnlyList<string> prefabPaths,
            bool applyChanges,
            out RevertReport report)
        {
            report = new RevertReport();
            if (!AtlasSpriteLookup.TryCreate(out AtlasSpriteLookup lookup, out string error))
            {
                Debug.LogError($"[PrefabAtlasSpriteRemap] {error}");
                return false;
            }

            report.PrefabCount = prefabPaths.Count;
            if (applyChanges)
            {
                AssetDatabase.StartAssetEditing();
            }

            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    string prefabPath = prefabPaths[i];
                    if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ProcessSinglePrefabRevert(prefabPath, lookup, applyChanges, report);
                }
            }
            finally
            {
                if (applyChanges)
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            return true;
        }

        /// <summary>
        /// 收集指定根目录下全部 Prefab 路径。
        /// </summary>
        /// <param name="searchRoot">搜索根目录，如 Assets。</param>
        /// <returns>Prefab 资源路径。</returns>
        public static List<string> CollectPrefabPaths(string searchRoot)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(searchRoot))
            {
                searchRoot = "Assets";
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                result.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// 打图集后：仅处理依赖本次打包散图目录的 Prefab，将散图引用替换为图集子 Sprite。
        /// </summary>
        /// <param name="sourceSpriteFolder">本次打包的 Sprite 目录。</param>
        /// <param name="prefabSearchRoot">Prefab 搜索根目录。</param>
        /// <param name="applyChanges">为 true 时写回 Prefab。</param>
        /// <param name="report">输出报告。</param>
        /// <returns>映射表加载成功时返回 true。</returns>
        public static bool RemapPrefabsForPackedSpriteFolder(
            string sourceSpriteFolder,
            string prefabSearchRoot,
            bool applyChanges,
            out RemapReport report)
        {
            if (string.IsNullOrEmpty(sourceSpriteFolder))
            {
                report = new RemapReport();
                return false;
            }

            List<string> scatteredTexturePaths = CollectTexturePathsInFolder(sourceSpriteFolder);
            if (scatteredTexturePaths.Count == 0)
            {
                report = new RemapReport();
                return true;
            }

            string searchRoot = string.IsNullOrEmpty(prefabSearchRoot) ? DefaultPrefabSearchRoot : prefabSearchRoot;
            List<string> prefabPaths = CollectPrefabsReferencingTextures(searchRoot, scatteredTexturePaths);
            return ProcessPrefabs(prefabPaths, applyChanges, out report);
        }

        /// <summary>
        /// 收集目录下全部贴图 Asset 路径。
        /// </summary>
        /// <param name="folderPath">目录 Asset 路径。</param>
        /// <returns>贴图路径列表。</returns>
        private static List<string> CollectTexturePathsInFolder(string folderPath)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath.TrimEnd('/')))
            {
                return paths;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>
        /// 收集依赖指定贴图的 Prefab 路径。
        /// </summary>
        /// <param name="searchRoot">Prefab 搜索根目录。</param>
        /// <param name="texturePaths">贴图 Asset 路径列表。</param>
        /// <returns>Prefab 路径列表。</returns>
        private static List<string> CollectPrefabsReferencingTextures(
            string searchRoot,
            List<string> texturePaths)
        {
            var textureSet = new HashSet<string>(texturePaths, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            var usedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { searchRoot });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrEmpty(prefabPath) || !usedPrefabs.Add(prefabPath))
                {
                    continue;
                }

                string[] dependencies = AssetDatabase.GetDependencies(prefabPath, false);
                for (int d = 0; d < dependencies.Length; d++)
                {
                    if (textureSet.Contains(dependencies[d]))
                    {
                        result.Add(prefabPath);
                        break;
                    }
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static void ProcessSinglePrefab(
            string prefabPath,
            AtlasSpriteLookup lookup,
            bool applyChanges,
            RemapReport report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                return;
            }

            bool prefabChanged = false;
            try
            {
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    SerializedObject serializedObject = new SerializedObject(behaviour);
                    SerializedProperty iterator = serializedObject.GetIterator();
                    bool enterChildren = true;
                    bool behaviourChanged = false;
                    while (iterator.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        if (iterator.objectReferenceValue is not Sprite oldSprite)
                        {
                            continue;
                        }

                        if (!TryResolveReplacement(oldSprite, lookup, out Sprite newSprite, out string atlasPath, out RemapSkipReason skipReason))
                        {
                            switch (skipReason)
                            {
                                case RemapSkipReason.NotScattered:
                                    report.SkippedNotScattered++;
                                    break;
                                case RemapSkipReason.NoMapping:
                                    report.SkippedNoMapping++;
                                    break;
                                case RemapSkipReason.NoSameStyleMapping:
                                    report.SkippedNoSameStyleMapping++;
                                    break;
                                case RemapSkipReason.NoSubSprite:
                                    report.SkippedNoSubSprite++;
                                    break;
                            }

                            continue;
                        }

                        if (oldSprite == newSprite)
                        {
                            continue;
                        }

                        iterator.objectReferenceValue = newSprite;
                        behaviourChanged = true;
                        report.ReplacedReferenceCount++;
                        report.Records.Add(new ReplaceRecord
                        {
                            PrefabPath = prefabPath,
                            PropertyPath = iterator.propertyPath,
                            SpriteName = oldSprite.name,
                            OldAssetPath = AssetDatabase.GetAssetPath(oldSprite),
                            NewAtlasPath = atlasPath,
                        });
                    }

                    if (behaviourChanged)
                    {
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                        prefabChanged = true;
                    }
                }

                if (prefabChanged && applyChanges)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    report.ModifiedPrefabCount++;
                }
                else if (prefabChanged)
                {
                    report.ModifiedPrefabCount++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private enum RemapSkipReason
        {
            None,
            NotScattered,
            NoMapping,
            NoSameStyleMapping,
            NoSubSprite,
        }

        private static bool TryResolveReplacement(
            Sprite oldSprite,
            AtlasSpriteLookup lookup,
            out Sprite newSprite,
            out string atlasPath,
            out RemapSkipReason skipReason)
        {
            newSprite = null;
            atlasPath = null;
            skipReason = RemapSkipReason.None;

            string oldPath = AssetDatabase.GetAssetPath(oldSprite);
            if (string.IsNullOrEmpty(oldPath))
            {
                skipReason = RemapSkipReason.NotScattered;
                return false;
            }

            if (!TryExtractStyleFromScatteredPath(oldPath, out string styleName))
            {
                skipReason = RemapSkipReason.NotScattered;
                return false;
            }

            string spriteName = oldSprite.name;
            if (!lookup.TryResolveAtlasSprite(
                    spriteName,
                    styleName,
                    out newSprite,
                    out atlasPath,
                    out bool hasSpriteMapping,
                    out bool hasSameStyleMapping))
            {
                if (!hasSpriteMapping)
                {
                    skipReason = RemapSkipReason.NoMapping;
                }
                else if (!hasSameStyleMapping)
                {
                    skipReason = RemapSkipReason.NoSameStyleMapping;
                }
                else
                {
                    skipReason = RemapSkipReason.NoSubSprite;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// 从散图路径解析风格目录名。
        /// </summary>
        /// <param name="assetPath">Sprite 资源路径。</param>
        /// <param name="styleName">如 DefaultStyle。</param>
        /// <returns>属于 UI Sprite 散图目录时返回 true。</returns>
        public static bool TryExtractStyleFromScatteredPath(string assetPath, out string styleName)
        {
            styleName = null;
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith(SpriteRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rest = assetPath.Substring(SpriteRoot.Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0)
            {
                return false;
            }

            styleName = rest.Substring(0, slash);
            return !string.IsNullOrEmpty(styleName);
        }

        /// <summary>
        /// 从图集 png 路径解析风格与图集文件名（不含扩展名）。
        /// </summary>
        /// <param name="assetPath">图集资源路径。</param>
        /// <param name="styleName">如 DefaultStyle。</param>
        /// <param name="atlasFileName">如 Common。</param>
        /// <returns>属于 UI Atlas 目录时返回 true。</returns>
        public static bool TryExtractStyleAndAtlasFromAtlasPath(
            string assetPath,
            out string styleName,
            out string atlasFileName)
        {
            styleName = null;
            atlasFileName = null;
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith(AtlasRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rest = assetPath.Substring(AtlasRoot.Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0)
            {
                return false;
            }

            styleName = rest.Substring(0, slash);
            string filePart = rest.Substring(slash + 1);
            atlasFileName = Path.GetFileNameWithoutExtension(filePart);
            return !string.IsNullOrEmpty(styleName) && !string.IsNullOrEmpty(atlasFileName);
        }

        private static void ProcessSinglePrefabRevert(
            string prefabPath,
            AtlasSpriteLookup lookup,
            bool applyChanges,
            RevertReport report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                return;
            }

            bool prefabChanged = false;
            try
            {
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    SerializedObject serializedObject = new SerializedObject(behaviour);
                    SerializedProperty iterator = serializedObject.GetIterator();
                    bool enterChildren = true;
                    bool behaviourChanged = false;
                    while (iterator.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        if (iterator.objectReferenceValue is not Sprite atlasSprite)
                        {
                            continue;
                        }

                        if (!TryResolveRevert(
                                atlasSprite,
                                lookup,
                                out Sprite scatteredSprite,
                                out string scatteredPath,
                                out RevertSkipReason skipReason))
                        {
                            switch (skipReason)
                            {
                                case RevertSkipReason.AlreadyScattered:
                                    report.SkippedAlreadyScattered++;
                                    break;
                                case RevertSkipReason.NotAtlas:
                                    report.SkippedNotAtlas++;
                                    break;
                                case RevertSkipReason.NoScattered:
                                    report.SkippedNoScattered++;
                                    break;
                            }

                            continue;
                        }

                        if (atlasSprite == scatteredSprite)
                        {
                            continue;
                        }

                        string atlasPath = AssetDatabase.GetAssetPath(atlasSprite);
                        iterator.objectReferenceValue = scatteredSprite;
                        behaviourChanged = true;
                        report.RevertedReferenceCount++;
                        report.Records.Add(new RevertRecord
                        {
                            PrefabPath = prefabPath,
                            PropertyPath = iterator.propertyPath,
                            SpriteName = atlasSprite.name,
                            OldAtlasPath = atlasPath,
                            NewScatteredPath = scatteredPath,
                        });
                    }

                    if (behaviourChanged)
                    {
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                        prefabChanged = true;
                    }
                }

                if (prefabChanged && applyChanges)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    report.ModifiedPrefabCount++;
                }
                else if (prefabChanged)
                {
                    report.ModifiedPrefabCount++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private enum RevertSkipReason
        {
            None,
            AlreadyScattered,
            NotAtlas,
            NoScattered,
        }

        private static bool TryResolveRevert(
            Sprite currentSprite,
            AtlasSpriteLookup lookup,
            out Sprite scatteredSprite,
            out string scatteredPath,
            out RevertSkipReason skipReason)
        {
            scatteredSprite = null;
            scatteredPath = null;
            skipReason = RevertSkipReason.None;

            string currentPath = AssetDatabase.GetAssetPath(currentSprite);
            if (string.IsNullOrEmpty(currentPath))
            {
                skipReason = RevertSkipReason.NotAtlas;
                return false;
            }

            if (TryExtractStyleFromScatteredPath(currentPath, out _))
            {
                skipReason = RevertSkipReason.AlreadyScattered;
                return false;
            }

            if (!TryExtractStyleAndAtlasFromAtlasPath(currentPath, out string styleName, out string atlasFileName))
            {
                skipReason = RevertSkipReason.NotAtlas;
                return false;
            }

            if (!lookup.TryGetSubSprite(currentPath, currentSprite.name, out Sprite atlasSubSprite)
                || atlasSubSprite != currentSprite)
            {
                skipReason = RevertSkipReason.NotAtlas;
                return false;
            }

            if (!lookup.TryResolveScatteredSprite(
                    currentSprite.name,
                    styleName,
                    currentPath,
                    atlasFileName,
                    out scatteredSprite,
                    out scatteredPath))
            {
                skipReason = RevertSkipReason.NoScattered;
                return false;
            }

            return true;
        }
    }
}

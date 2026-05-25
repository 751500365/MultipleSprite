using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
/*
namespace Script.Tools
{
    /// <summary>
    /// 基于 Unity Multiple Sprites 子资源的图集 Sprite 加载器。
    /// </summary>
    public static class MultipleSpriteAtlasLoader
    {
        private const string LogTag = "MultipleSpriteAtlasLoader";
        private const string DefaultStyleName = "DefaultStyle";
        private const string AtlasRoot = "Assets/GameRes/UI/Atlas";
        private const string SpriteAtlasMapPath = AtlasRoot + "/SpriteAtlasPathMap.json";

        private static readonly Dictionary<string, SubAssetsOperationHandle> atlasHandleCache =
            new Dictionary<string, SubAssetsOperationHandle>();
        private static readonly Dictionary<string, List<SpriteAtlasPathMapping>> spriteAtlasPathMap =
            new Dictionary<string, List<SpriteAtlasPathMapping>>(StringComparer.OrdinalIgnoreCase);

        private static AssetOperationHandle spriteAtlasMapHandle;
        private static bool hasTriedLoadSpriteAtlasPathMap;
        private static bool hasLoggedYooAssetsDefaultPackageNull;

        /// <summary>是否启用 Multiple Sprites 图集加载方案。</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>启用时是否优先使用 Multiple Sprites；false 表示仅作为原方案失败后的兜底。</summary>
        public static bool PreferMultipleSprites { get; set; } = true;

        /// <summary>
        /// 尝试从 Multiple Sprites 图集加载 Sprite。
        /// </summary>
        /// <param name="atlasName">图集名，例如 JigsawPuzzle。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        public static bool TryLoadSprite(string atlasName, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (!Enabled || string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            string currentStyle = GetCurrentStyleName();
            if (TryLoadSpriteFromMap(currentStyle, atlasName, spriteName, out sprite))
            {
                return true;
            }

            if (currentStyle != DefaultStyleName && TryLoadSpriteFromMap(DefaultStyleName, atlasName, spriteName, out sprite))
            {
                return true;
            }

            if (string.IsNullOrEmpty(atlasName))
            {
                return false;
            }

            if (TryLoadSpriteFromStyle(currentStyle, atlasName, spriteName, out sprite))
            {
                return true;
            }

            return currentStyle != DefaultStyleName && TryLoadSpriteFromStyle(DefaultStyleName, atlasName, spriteName, out sprite);
        }

        /// <summary>
        /// 根据 Sprite 名称和当前风格，从映射表加载 Sprite。
        /// </summary>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        public static bool TryLoadSprite(string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (!Enabled || string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            string currentStyle = GetCurrentStyleName();
            if (TryLoadSpriteFromMap(currentStyle, null, spriteName, out sprite))
            {
                return true;
            }

            return currentStyle != DefaultStyleName && TryLoadSpriteFromMap(DefaultStyleName, null, spriteName, out sprite);
        }

        /// <summary>
        /// 按资源地址或逻辑名加载 Sprite（映射表 → 风格路径 → 按路径子资源加载）。
        /// </summary>
        /// <param name="location">逻辑 Sprite 名，或 <c>Assets/...</c> 形式完整资源路径。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        public static bool TryLoadAtLocation(string location, out Sprite sprite)
        {
            sprite = null;
            if (!Enabled || string.IsNullOrEmpty(location))
            {
                return false;
            }

            bool isAssetPath = location.IndexOf('/') >= 0 || location.IndexOf('\\') >= 0;
            if (!isAssetPath)
            {
                if (TryLoadSprite(location, out sprite))
                {
                    return true;
                }

                string styledPath = WordSolitaire.Experiment.UIStyleCache.Instance.GetStyledSpritePath(location);
                if (!string.IsNullOrEmpty(styledPath))
                {
                    return TryLoadSpriteFromAtlasPath(styledPath, location, out sprite);
                }

                return false;
            }

            string spriteName = Path.GetFileNameWithoutExtension(location);
            if (!string.IsNullOrEmpty(spriteName) && TryLoadSprite(spriteName, out sprite))
            {
                return true;
            }

            return TryLoadSpriteFromAtlasPath(location, spriteName, out sprite);
        }

        /// <summary>
        /// 释放所有 Multiple Sprites 图集加载句柄。
        /// </summary>
        public static void ReleaseAll()
        {
            foreach (var handle in atlasHandleCache.Values)
            {
                handle?.Release();
            }

            atlasHandleCache.Clear();
            spriteAtlasMapHandle?.Release();
            spriteAtlasMapHandle = null;
            hasTriedLoadSpriteAtlasPathMap = false;
            hasLoggedYooAssetsDefaultPackageNull = false;
            spriteAtlasPathMap.Clear();
        }

        /// <summary>
        /// 释放指定图集加载句柄。
        /// </summary>
        /// <param name="atlasName">图集名。</param>
        public static void ReleaseAtlas(string atlasName)
        {
            if (string.IsNullOrEmpty(atlasName))
            {
                return;
            }

            ReleaseAtlasByPath(BuildAtlasPath(GetCurrentStyleName(), atlasName));
            ReleaseAtlasByPath(BuildAtlasPath(DefaultStyleName, atlasName));
        }

        /// <summary>
        /// 尝试按指定风格加载 Sprite。
        /// </summary>
        /// <param name="styleName">风格目录名。</param>
        /// <param name="atlasName">图集名。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        private static bool TryLoadSpriteFromStyle(string styleName, string atlasName, string spriteName, out Sprite sprite)
        {
            sprite = null;
            string atlasPath = BuildAtlasPath(styleName, atlasName);
            return TryLoadSpriteFromAtlasPath(atlasPath, spriteName, out sprite);
        }

        /// <summary>
        /// 按映射表中的风格加载 Sprite。
        /// </summary>
        /// <param name="styleName">风格目录名。</param>
        /// <param name="atlasName">图集名，为空时不限制图集。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        private static bool TryLoadSpriteFromMap(string styleName, string atlasName, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (!TryGetAtlasPathsFromMap(styleName, atlasName, spriteName, out List<string> atlasPaths))
            {
                return false;
            }

            for (int i = 0; i < atlasPaths.Count; i++)
            {
                if (TryLoadSpriteFromAtlasPath(atlasPaths[i], spriteName, out sprite))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按图集路径加载 Sprite 子资源。
        /// </summary>
        /// <param name="atlasPath">图集 png 资源路径。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="sprite">加载到的 Sprite。</param>
        /// <returns>是否加载成功。</returns>
        private static bool TryLoadSpriteFromAtlasPath(string atlasPath, string spriteName, out Sprite sprite)
        {
            sprite = null;
            SubAssetsOperationHandle handle = GetOrLoadAtlasHandle(atlasPath);
            if (handle == null)
            {
                return false;
            }

            sprite = handle.GetSubAssetObject<Sprite>(spriteName);
            return sprite != null;
        }

        /// <summary>
        /// 从映射表获取指定风格下的图集路径列表。
        /// </summary>
        /// <param name="styleName">风格目录名。</param>
        /// <param name="atlasName">图集名，为空时不限制图集。</param>
        /// <param name="spriteName">Sprite 子资源名。</param>
        /// <param name="atlasPaths">图集 png 资源路径列表。</param>
        /// <returns>找到映射时返回 true。</returns>
        private static bool TryGetAtlasPathsFromMap(
            string styleName,
            string atlasName,
            string spriteName,
            out List<string> atlasPaths)
        {
            atlasPaths = null;
            EnsureSpriteAtlasPathMap();
            if (!spriteAtlasPathMap.TryGetValue(spriteName, out List<SpriteAtlasPathMapping> mappings))
            {
                return false;
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                SpriteAtlasPathMapping mapping = mappings[i];
                if (string.Equals(mapping.Style, styleName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(atlasName) ||
                     string.Equals(mapping.Atlas, atlasName, StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(mapping.Path))
                {
                    if (atlasPaths == null)
                    {
                        atlasPaths = new List<string>();
                    }

                    atlasPaths.Add(mapping.Path);
                }
            }

            return atlasPaths != null && atlasPaths.Count > 0;
        }

        /// <summary>
        /// 延迟加载 Sprite 名称到图集路径的映射表。
        /// </summary>
        private static void EnsureSpriteAtlasPathMap()
        {
            if (hasTriedLoadSpriteAtlasPathMap)
            {
                return;
            }

            ResourcePackage package = YooAssets.GetPackage(Define.DefaultPackage);
            if (package == null)
            {
                if (!hasLoggedYooAssetsDefaultPackageNull)
                {
                    hasLoggedYooAssetsDefaultPackageNull = true;
                    Log.LogWarning(LogTag, "YooAssets 默认包未初始化，无法加载图集映射表: {0}", SpriteAtlasMapPath);
                }

                return;
            }

            hasTriedLoadSpriteAtlasPathMap = true;
            spriteAtlasMapHandle = package.LoadAssetSync<TextAsset>(SpriteAtlasMapPath);
            if (spriteAtlasMapHandle == null || spriteAtlasMapHandle.Status != EOperationStatus.Succeed)
            {
                Log.LogWarning(LogTag, "图集映射表加载失败: {0}, Error={1}", SpriteAtlasMapPath, spriteAtlasMapHandle?.LastError);
                spriteAtlasMapHandle?.Release();
                spriteAtlasMapHandle = null;
                return;
            }

            TextAsset textAsset = spriteAtlasMapHandle.AssetObject as TextAsset;
            if (textAsset == null || string.IsNullOrEmpty(textAsset.text))
            {
                Log.LogWarning(LogTag, "图集映射表内容为空: {0}", SpriteAtlasMapPath);
                return;
            }

            try
            {
                ParseSpriteAtlasPathMap(textAsset.text);
            }
            catch (Exception e)
            {
                spriteAtlasPathMap.Clear();
                Log.LogWarning(LogTag, "图集映射表解析失败: {0}, Error={1}", SpriteAtlasMapPath, e.Message);
            }
        }

        /// <summary>
        /// 解析 Sprite 名称到图集路径的映射表。
        /// </summary>
        /// <param name="json">映射表 JSON。</param>
        private static void ParseSpriteAtlasPathMap(string json)
        {
            spriteAtlasPathMap.Clear();
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
                    spriteAtlasPathMap[property.Name] = mappings;
                }
            }
        }

        /// <summary>
        /// 获取或加载图集子资源句柄。
        /// </summary>
        /// <param name="atlasPath">图集 png 资源路径。</param>
        /// <returns>图集子资源句柄。</returns>
        private static SubAssetsOperationHandle GetOrLoadAtlasHandle(string atlasPath)
        {
            if (atlasHandleCache.TryGetValue(atlasPath, out SubAssetsOperationHandle cachedHandle))
            {
                if (cachedHandle != null
                    && cachedHandle.IsValid
                    && cachedHandle.Status == EOperationStatus.Succeed)
                {
                    return cachedHandle;
                }

                cachedHandle?.Release();
                atlasHandleCache.Remove(atlasPath);
            }

            ResourcePackage package = YooAssets.GetPackage(Define.DefaultPackage);
            if (package == null)
            {
                if (!hasLoggedYooAssetsDefaultPackageNull)
                {
                    hasLoggedYooAssetsDefaultPackageNull = true;
                    Log.LogWarning(LogTag, "YooAssets 默认包未初始化，无法加载图集: {0}", atlasPath);
                }

                return null;
            }

            SubAssetsOperationHandle handle = package.LoadSubAssetsSync<Sprite>(atlasPath);
            if (handle == null || handle.Status != EOperationStatus.Succeed)
            {
                Log.LogWarning(LogTag, "Multiple Sprites 图集加载失败: {0}, Error={1}", atlasPath, handle?.LastError);
                handle?.Release();
                return null;
            }

            atlasHandleCache[atlasPath] = handle;
            return handle;
        }

        /// <summary>
        /// 获取当前 UI 风格名。
        /// </summary>
        /// <returns>当前 UI 风格目录名。</returns>
        private static string GetCurrentStyleName()
        {
            int styleId = WordSolitaireMain.Instance != null ? WordSolitaireMain.Instance.StyleId : 1;
            return LocationGetter.StyleMap.TryGetValue(styleId, out string styleName)
                ? styleName
                : DefaultStyleName;
        }

        /// <summary>
        /// 生成 Multiple Sprites 图集 png 路径。
        /// </summary>
        /// <param name="styleName">风格目录名。</param>
        /// <param name="atlasName">图集名。</param>
        /// <returns>图集 png 资源路径。</returns>
        private static string BuildAtlasPath(string styleName, string atlasName)
        {
            return $"{AtlasRoot}/{styleName}/{atlasName}.png";
        }

        /// <summary>
        /// 释放指定路径图集句柄。
        /// </summary>
        /// <param name="atlasPath">图集 png 资源路径。</param>
        private static void ReleaseAtlasByPath(string atlasPath)
        {
            if (!atlasHandleCache.TryGetValue(atlasPath, out SubAssetsOperationHandle handle))
            {
                return;
            }

            handle?.Release();
            atlasHandleCache.Remove(atlasPath);
        }

        /// <summary>
        /// Sprite 名称映射到图集路径的一条记录。
        /// </summary>
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
    }
}
*/

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WordSolitaire.EditorTools.ScatteredSpriteBorderToAtlas
{
    /// <summary>
    /// 散图与图集子 Sprite 之间的九宫格边距读取、透明裁剪与换算。
    /// </summary>
    internal static class SpriteBorderTransferUtility
    {
        private const byte DefaultAlphaThreshold = 1;
        private const int DefaultTrimMargin = 1;

        /// <summary>
        /// 从散图资源读取九宫格边距（优先已导入 Sprite，再回退 TextureImporter）。
        /// </summary>
        /// <param name="assetPath">源贴图 Asset 路径。</param>
        /// <returns>Unity border：(left, bottom, right, top)。</returns>
        public static Vector4 ReadSourceSpriteBorder(string assetPath)
        {
            var fileBaseName = Path.GetFileNameWithoutExtension(assetPath);
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (!(assets[i] is Sprite sprite) || !SpriteNameMatches(sprite.name, fileBaseName))
                {
                    continue;
                }

                if (HasBorder(sprite.border))
                {
                    return sprite.border;
                }
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return Vector4.zero;
            }

            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                if (HasBorder(importer.spriteBorder))
                {
                    return importer.spriteBorder;
                }

#pragma warning disable 618
                var sheet = importer.spritesheet;
#pragma warning restore 618
                if (sheet != null)
                {
                    for (var i = 0; i < sheet.Length; i++)
                    {
                        if (HasBorder(sheet[i].border))
                        {
                            return sheet[i].border;
                        }
                    }
                }

                return Vector4.zero;
            }

            var spriteName = fileBaseName;
#pragma warning disable 618
            var multipleSheet = importer.spritesheet;
#pragma warning restore 618
            for (var i = 0; i < multipleSheet.Length; i++)
            {
                if (SpriteNameMatches(multipleSheet[i].name, spriteName) && HasBorder(multipleSheet[i].border))
                {
                    return multipleSheet[i].border;
                }
            }

            return Vector4.zero;
        }

        /// <summary>
        /// 判断子 Sprite 名称是否与散图文件名匹配（兼容 _0 后缀）。
        /// </summary>
        /// <param name="spriteAssetName">子 Sprite 名称。</param>
        /// <param name="fileBaseName">散图文件名（无扩展名）。</param>
        /// <returns>匹配时返回 true。</returns>
        private static bool SpriteNameMatches(string spriteAssetName, string fileBaseName)
        {
            if (string.IsNullOrEmpty(spriteAssetName) || string.IsNullOrEmpty(fileBaseName))
            {
                return false;
            }

            if (string.Equals(spriteAssetName, fileBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(spriteAssetName, fileBaseName + "_0", StringComparison.OrdinalIgnoreCase);
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
            IntRect trimRect,
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
        /// 读取散图 border，并按与打图集相同的透明裁剪规则换算到图集子 Sprite 尺寸。
        /// </summary>
        /// <param name="scatteredAssetPath">散图 Asset 路径。</param>
        /// <param name="atlasSubSpriteRect">图集子 Sprite 在图集中的 rect。</param>
        /// <returns>适用于图集子 Sprite 的 border。</returns>
        public static Vector4 ResolveAtlasSubSpriteBorder(string scatteredAssetPath, Rect atlasSubSpriteRect)
        {
            var sourceBorder = ReadSourceSpriteBorder(scatteredAssetPath);
            if (!HasBorder(sourceBorder))
            {
                return Vector4.zero;
            }

            var sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(scatteredAssetPath);
            if (sourceTexture != null &&
                Mathf.Approximately(sourceTexture.width, atlasSubSpriteRect.width) &&
                Mathf.Approximately(sourceTexture.height, atlasSubSpriteRect.height))
            {
                return sourceBorder;
            }

            if (!TryLoadReadablePixels(scatteredAssetPath, out var pixels, out var width, out var height))
            {
                return ScaleBorderToRect(sourceBorder, sourceTexture, atlasSubSpriteRect);
            }

            var trim = FindTrimRect(pixels, width, height, DefaultAlphaThreshold, DefaultTrimMargin);
            var adjusted = AdjustBorderForTrim(sourceBorder, trim, width, height);

            var trimmedWidth = trim.w;
            var trimmedHeight = trim.h;
            if (Mathf.Approximately(trimmedWidth, atlasSubSpriteRect.width) &&
                Mathf.Approximately(trimmedHeight, atlasSubSpriteRect.height))
            {
                return HasBorder(adjusted) ? adjusted : ScaleBorderToRect(sourceBorder, width, height, atlasSubSpriteRect);
            }

            if (trimmedWidth > 0f && trimmedHeight > 0f)
            {
                var scaled = new Vector4(
                    adjusted.x * (atlasSubSpriteRect.width / trimmedWidth),
                    adjusted.y * (atlasSubSpriteRect.height / trimmedHeight),
                    adjusted.z * (atlasSubSpriteRect.width / trimmedWidth),
                    adjusted.w * (atlasSubSpriteRect.height / trimmedHeight));
                if (HasBorder(scaled))
                {
                    return scaled;
                }
            }

            return ScaleBorderToRect(sourceBorder, width, height, atlasSubSpriteRect);
        }

        /// <summary>
        /// 按源图与目标 rect 尺寸比例缩放 border。
        /// </summary>
        /// <param name="border">源 border。</param>
        /// <param name="sourceTexture">源纹理，可为 null。</param>
        /// <param name="targetRect">目标 rect。</param>
        /// <returns>缩放后的 border。</returns>
        private static Vector4 ScaleBorderToRect(Vector4 border, Texture2D sourceTexture, Rect targetRect)
        {
            if (sourceTexture == null)
            {
                return border;
            }

            return ScaleBorderToRect(border, sourceTexture.width, sourceTexture.height, targetRect);
        }

        /// <summary>
        /// 按源图与目标 rect 尺寸比例缩放 border。
        /// </summary>
        /// <param name="border">源 border。</param>
        /// <param name="sourceWidth">源宽度。</param>
        /// <param name="sourceHeight">源高度。</param>
        /// <param name="targetRect">目标 rect。</param>
        /// <returns>缩放后的 border。</returns>
        private static Vector4 ScaleBorderToRect(Vector4 border, int sourceWidth, int sourceHeight, Rect targetRect)
        {
            if (!HasBorder(border) || sourceWidth <= 0 || sourceHeight <= 0)
            {
                return Vector4.zero;
            }

            var scaleX = targetRect.width / sourceWidth;
            var scaleY = targetRect.height / sourceHeight;
            return new Vector4(
                border.x * scaleX,
                border.y * scaleY,
                border.z * scaleX,
                border.w * scaleY);
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

        /// <summary>
        /// 判断两个 border 是否近似相等。
        /// </summary>
        /// <param name="a">边距 A。</param>
        /// <param name="b">边距 B。</param>
        /// <returns>近似相等时返回 true。</returns>
        public static bool BordersApproximatelyEqual(Vector4 a, Vector4 b)
        {
            return Mathf.Approximately(a.x, b.x)
                && Mathf.Approximately(a.y, b.y)
                && Mathf.Approximately(a.z, b.z)
                && Mathf.Approximately(a.w, b.w);
        }

        /// <summary>
        /// 加载可读像素用于透明裁剪。
        /// </summary>
        /// <param name="assetPath">贴图 Asset 路径。</param>
        /// <param name="pixels">输出像素。</param>
        /// <param name="width">输出宽度。</param>
        /// <param name="height">输出高度。</param>
        /// <returns>加载成功时返回 true。</returns>
        private static bool TryLoadReadablePixels(
            string assetPath,
            out Color32[] pixels,
            out int width,
            out int height)
        {
            pixels = null;
            width = 0;
            height = 0;

            var absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            var extension = Path.GetExtension(assetPath);
            if (!string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (texture.LoadImage(File.ReadAllBytes(absolutePath)))
                    {
                        width = texture.width;
                        height = texture.height;
                        pixels = texture.GetPixels32();
                        return true;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            var sourceAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (sourceAsset == null)
            {
                return false;
            }

            var copy = MakeReadableCopy(sourceAsset);
            try
            {
                width = copy.width;
                height = copy.height;
                pixels = copy.GetPixels32();
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
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
        private static IntRect FindTrimRect(
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
                return new IntRect(0, 0, width, height);
            }

            minX = Mathf.Max(0, minX - trimMargin);
            minY = Mathf.Max(0, minY - trimMargin);
            maxX = Mathf.Min(width - 1, maxX + trimMargin);
            maxY = Mathf.Min(height - 1, maxY + trimMargin);
            return new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
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
        /// 整数矩形（左下角坐标）。
        /// </summary>
        internal readonly struct IntRect
        {
            public readonly int x;
            public readonly int y;
            public readonly int w;
            public readonly int h;

            /// <summary>右边界。</summary>
            public int Right => x + w;

            /// <summary>上边界（左下角坐标系）。</summary>
            public int Bottom => y + h;

            /// <summary>
            /// 创建整数矩形。
            /// </summary>
            /// <param name="x">X 坐标。</param>
            /// <param name="y">Y 坐标。</param>
            /// <param name="w">宽度。</param>
            /// <param name="h">高度。</param>
            public IntRect(int x, int y, int w, int h)
            {
                this.x = x;
                this.y = y;
                this.w = w;
                this.h = h;
            }
        }
    }
}

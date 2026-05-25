using System;

namespace WordSolitaire.EditorTools.TexturePackerClone
{
    /// <summary>
    /// 图集构建完成后的回调（供 Assembly-CSharp-Editor 中的 Prefab 替换等逻辑订阅）。
    /// </summary>
    public static class TpCloneAtlasPostBuild
    {
        /// <summary>EditorPrefs：打图集后是否自动替换 Prefab 散图引用。</summary>
        public const string RemapPrefabsPrefKey = "TexturePackerClone.RemapPrefabsAfterBuild";

        /// <summary>图集构建完成上下文。</summary>
        public readonly struct Context
        {
            /// <summary>源 Sprite 目录。</summary>
            public string SourceFolderPath { get; }

            /// <summary>生成的图集 png 路径。</summary>
            public string AtlasPath { get; }

            /// <summary>图集名（目录名）。</summary>
            public string AtlasName { get; }

            /// <summary>UI 风格名，如 DefaultStyle。</summary>
            public string StyleName { get; }

            /// <summary>打入图集的子 Sprite 数量。</summary>
            public int SpriteCount { get; }

            /// <summary>
            /// 创建构建完成上下文。
            /// </summary>
            /// <param name="sourceFolderPath">源 Sprite 目录。</param>
            /// <param name="atlasPath">图集路径。</param>
            /// <param name="atlasName">图集名。</param>
            /// <param name="styleName">风格名。</param>
            /// <param name="spriteCount">子 Sprite 数量。</param>
            public Context(
                string sourceFolderPath,
                string atlasPath,
                string atlasName,
                string styleName,
                int spriteCount)
            {
                SourceFolderPath = sourceFolderPath;
                AtlasPath = atlasPath;
                AtlasName = atlasName;
                StyleName = styleName;
                SpriteCount = spriteCount;
            }
        }

        /// <summary>图集构建完成事件。</summary>
        public static event Action<Context> Completed;

        /// <summary>
        /// 触发图集构建完成事件。
        /// </summary>
        /// <param name="context">构建上下文。</param>
        internal static void RaiseCompleted(Context context)
        {
            Completed?.Invoke(context);
        }
    }
}

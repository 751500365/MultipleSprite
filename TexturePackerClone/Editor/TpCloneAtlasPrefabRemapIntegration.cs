using UnityEditor;
using UnityEngine;
using WordSolitaire.EditorTools.PrefabAtlasSpriteRemap;

namespace WordSolitaire.EditorTools.TexturePackerClone
{
    /// <summary>
    /// 打图集完成后自动将 Prefab 散图 Sprite 引用替换为图集 Multiple 子 Sprite。
    /// </summary>
    [InitializeOnLoad]
    internal static class TpCloneAtlasPrefabRemapIntegration
    {
        static TpCloneAtlasPrefabRemapIntegration()
        {
            TpCloneAtlasPostBuild.Completed += OnAtlasBuildCompleted;
        }

        /// <summary>
        /// 图集构建完成后的 Prefab 引用替换。
        /// </summary>
        /// <param name="context">构建上下文。</param>
        private static void OnAtlasBuildCompleted(TpCloneAtlasPostBuild.Context context)
        {
            if (!EditorPrefs.GetBool(TpCloneAtlasPostBuild.RemapPrefabsPrefKey, true))
            {
                return;
            }

            if (string.IsNullOrEmpty(context.SourceFolderPath))
            {
                return;
            }

            if (!PrefabAtlasSpriteRemapTool.RemapPrefabsForPackedSpriteFolder(
                    context.SourceFolderPath,
                    PrefabAtlasSpriteRemapTool.DefaultPrefabSearchRoot,
                    applyChanges: true,
                    out PrefabAtlasSpriteRemapTool.RemapReport report))
            {
                return;
            }

            Debug.Log(
                "[TexturePackerClone] Prefab 散图→图集子 Sprite: "
                + $"扫描 {report.PrefabCount} 个 Prefab, 修改 {report.ModifiedPrefabCount} 个, "
                + $"替换 {report.ReplacedReferenceCount} 处引用 "
                + $"(源目录 {context.SourceFolderPath}, 图集 {context.AtlasPath}).");
        }
    }
}

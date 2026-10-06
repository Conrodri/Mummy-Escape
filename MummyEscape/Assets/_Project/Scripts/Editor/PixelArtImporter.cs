using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// PNGs under Resources/Art replace the painted art (Visual.ArtLibrary): they come in as crisp pixel sprites —
    /// point filter, no compression or mipmaps, centred pivot — at 32 px per tile (16 for the HUD icons).
    /// </summary>
    public sealed class PixelArtImporter : AssetPostprocessor
    {
        const string Root = "Assets/_Project/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = assetPath.Contains("/Icons/") ? 16 : 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}

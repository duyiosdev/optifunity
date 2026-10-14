using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.Core
{
    [Serializable] public class TextureChange
    {
        public string id, project, assetPath, guid, platform, sourceHash, metaBeforeHash, metaAfterHash, status, createdAt, note;
        public int beforeSize, afterSize;
        public bool beforeOverride;
    }
    public static class VerifiedTextureChange
    {
        public const string Policy = "Only explicit Unity UI approval can apply a previewed platform Texture Max Size reduction. No automatic or MCP writes. Import-setting verification is not device/visual acceptance.";
        private static string Root => Path.Combine("Library", "OptifunityChanges");
        private static string Folder(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid change ID.");
            return Path.Combine(Root, id);
        }
        private static string FileHash(string path)
        {
            using var input = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static TextureImporter Importer(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\'))
                throw new ArgumentException("Only project Assets texture paths are allowed.");
            string full = Path.GetFullPath(path), root = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path outside Assets.");
            for (string current = full; current != null && current.Length >= root.Length - 1; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked assets are not eligible.");
            if ((File.GetAttributes(path + ".meta") & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked metadata is not eligible.");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D)
                throw new InvalidOperationException("Only Default 2D textures are eligible; sprites, normal maps and other specialized textures are excluded.");
            if (!AssetDatabase.IsOpenForEdit(path) || !AssetDatabase.IsOpenForEdit(path + ".meta")) throw new InvalidOperationException("Asset or metadata is not editable.");
            return importer;
        }
        public static TextureChange Preview(string path, string platform, int size)
        {
            if (platform != "Android" && platform != "iPhone") throw new ArgumentException("Choose Android or iPhone explicitly.");
            if (size < 32 || size > 8192 || (size & (size - 1)) != 0) throw new ArgumentException("Max Size must be a power of two from 32 to 8192.");
            var importer = Importer(path); var settings = importer.GetPlatformTextureSettings(platform);
            int before = settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
            if (size >= before) throw new InvalidOperationException("Preview must reduce the effective platform Max Size.");
            var plan = new TextureChange { id = Guid.NewGuid().ToString("N"), project = OptimizationReviewStore.Project, assetPath = path,
                guid = AssetDatabase.AssetPathToGUID(path), platform = platform, beforeSize = before, afterSize = size, beforeOverride = settings.overridden,
                sourceHash = FileHash(path), metaBeforeHash = FileHash(path + ".meta"), status = "Preview", createdAt = DateTime.UtcNow.ToString("o"),
                note = "Lower resolution may blur UI/world art, affect alpha detail and change compression output. Review actual usage and compare visual quality/device capture. Source image is unchanged. " + Policy };
            Directory.CreateDirectory(Folder(plan.id)); File.WriteAllBytes(Path.Combine(Folder(plan.id), "before.meta"), File.ReadAllBytes(path + ".meta")); Save(plan); return plan;
        }
        private static void Save(TextureChange plan)
        {
            string path = Path.Combine(Folder(plan.id), "change.json"), temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(plan, true));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static TextureChange Load(string id)
        {
            var plan = JsonUtility.FromJson<TextureChange>(File.ReadAllText(Path.Combine(Folder(id), "change.json")));
            if (plan == null || plan.id != id || plan.project != OptimizationReviewStore.Project) throw new InvalidDataException("Invalid or wrong-project change.");
            if ((plan.platform != "Android" && plan.platform != "iPhone") || plan.afterSize < 32 || plan.afterSize > 8192 ||
                (plan.afterSize & (plan.afterSize - 1)) != 0 || plan.afterSize >= plan.beforeSize || string.IsNullOrEmpty(plan.status))
                throw new InvalidDataException("Invalid change preconditions.");
            Importer(plan.assetPath);
            if (AssetDatabase.AssetPathToGUID(plan.assetPath) != plan.guid) throw new InvalidOperationException("Asset identity changed.");
            return plan;
        }
        public static TextureChange Apply(string id, bool usageAndQualityReviewed)
        {
            if (!usageAndQualityReviewed) throw new InvalidOperationException("Review usage/quality and explicitly approve this preview in Unity first.");
            var plan = Load(id);
            if (plan.status != "Preview" || FileHash(plan.assetPath) != plan.sourceHash || FileHash(plan.assetPath + ".meta") != plan.metaBeforeHash)
                throw new InvalidOperationException("Preview is stale or already applied. Create a fresh preview.");
            if (FileHash(Path.Combine(Folder(id), "before.meta")) != plan.metaBeforeHash) throw new InvalidDataException("Backup integrity failed.");
            plan.status = "Applying"; Save(plan);
            try
            {
                var importer = Importer(plan.assetPath); var settings = importer.GetPlatformTextureSettings(plan.platform);
                settings.overridden = true; settings.maxTextureSize = plan.afterSize;
                importer.SetPlatformTextureSettings(settings); importer.SaveAndReimport();
                var actual = Importer(plan.assetPath).GetPlatformTextureSettings(plan.platform);
                if (!actual.overridden || actual.maxTextureSize != plan.afterSize || FileHash(plan.assetPath) != plan.sourceHash)
                    throw new InvalidOperationException("Post-import verification failed.");
                plan.metaAfterHash = FileHash(plan.assetPath + ".meta"); plan.status = "Applied; visual/device verification pending"; Save(plan); return plan;
            }
            catch
            {
                File.Copy(Path.Combine(Folder(id), "before.meta"), plan.assetPath + ".meta", true);
                AssetDatabase.ImportAsset(plan.assetPath, ImportAssetOptions.ForceUpdate);
                plan.status = FileHash(plan.assetPath + ".meta") == plan.metaBeforeHash ? "Failed; restored metadata" : "Failed; recovery required"; Save(plan); throw;
            }
        }
        public static TextureChange Rollback(string id)
        {
            var plan = Load(id);
            if (!plan.status.StartsWith("Applied;") || FileHash(plan.assetPath) != plan.sourceHash || FileHash(plan.assetPath + ".meta") != plan.metaAfterHash)
                throw new InvalidOperationException("Rollback refused: asset changed since apply or transaction not applied.");
            if (FileHash(Path.Combine(Folder(id), "before.meta")) != plan.metaBeforeHash) throw new InvalidDataException("Backup integrity failed.");
            plan.status = "Rolling back"; Save(plan);
            File.Copy(Path.Combine(Folder(id), "before.meta"), plan.assetPath + ".meta", true);
            AssetDatabase.ImportAsset(plan.assetPath, ImportAssetOptions.ForceUpdate);
            if (FileHash(plan.assetPath + ".meta") != plan.metaBeforeHash) { plan.status = "Rollback verification failed"; Save(plan); throw new IOException(plan.status); }
            plan.status = "Rolled back"; Save(plan); return plan;
        }
        public static TextureChange[] History() => Directory.Exists(Root) ? Directory.GetDirectories(Root).Select(p =>
        {
            try { return JsonUtility.FromJson<TextureChange>(File.ReadAllText(Path.Combine(p, "change.json"))); } catch { return null; }
        }).Where(p => p != null && p.project == OptimizationReviewStore.Project).OrderByDescending(p => p.createdAt).ToArray() : Array.Empty<TextureChange>();
    }
}

using SecureExchangesSDK.Models.Messenging;
using System.IO;

namespace SESARWebHook.Core.Files
{
  /// <summary>
  /// Chemins des fichiers d'un manifest SESAR, partages par les connecteurs (FileSystem, Zoho CRM). Les valeurs viennent
  /// du manifest: les noms sont toujours reduits a un nom de fichier nu (SafePaths) avant usage.
  /// </summary>
  public static class ManifestFilePaths
  {
    /// <summary>
    /// Chemin source du fichier : FullPath de l'entree quand SESAR l'a rempli, sinon
    /// DirectoryPath + FileName. SESAR laisse FullPath vide (et IsDownloaded a false) pour un
    /// fichier dont le contenu est identique a un autre du meme envoi, alors que son .secf est bien
    /// present dans le dossier de l'echange. Retourne null si aucun chemin ne peut etre construit.
    /// </summary>
    public static string ResolveSourcePath(StoreManifest manifest, int index)
    {
      var location = manifest.FilesLocation[index];
      if (location == null)
      {
        return null;
      }

      if (!string.IsNullOrWhiteSpace(location.FullPath))
      {
        return location.FullPath;
      }

      var fileName = SafePaths.SanitizeFileName(location.FileName);
      if (string.IsNullOrWhiteSpace(manifest.DirectoryPath) || string.IsNullOrEmpty(fileName))
      {
        return null;
      }

      return Path.Combine(manifest.DirectoryPath, fileName);
    }

    /// <summary>
    /// Nom visible du fichier livre : RealFileName des metadonnees, sinon FileName de l'entree,
    /// sinon le nom contenu dans FullPath. Toujours reduit a un nom de fichier nu.
    /// </summary>
    public static string ResolveTargetFileName(StoreManifest manifest, int index, string sourcePath)
    {
      string candidate = null;

      var metaData = manifest.FilesMetaData;
      if (metaData != null && index < metaData.Count && metaData[index] != null)
      {
        candidate = metaData[index].RealFileName;
      }

      if (string.IsNullOrWhiteSpace(candidate))
      {
        candidate = manifest.FilesLocation[index].FileName;
      }

      if (string.IsNullOrWhiteSpace(candidate))
      {
        candidate = Path.GetFileName(sourcePath);
      }

      var sanitized = SafePaths.SanitizeFileName(candidate);
      return string.IsNullOrEmpty(sanitized) ? $"file_{index}" : sanitized;
    }
  }
}

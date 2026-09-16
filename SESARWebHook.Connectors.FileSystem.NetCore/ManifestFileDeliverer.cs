using SecureExchangesSDK.Models.Messenging;
using System;
using System.Collections.Generic;
using System.IO;

namespace SESARWebHook.Connectors.FileSystem
{
  /// <summary>
  /// Livre les fichiers d'un manifest SESAR dans un dossier : copie du fichier s'il existe en
  /// clair, sinon dechiffrement de sa version ".secf". Un fichier en echec n'interrompt pas les
  /// autres ; les echecs sont accumules dans le <see cref="DeliveryReport"/>.
  /// </summary>
  internal sealed class ManifestFileDeliverer
  {
    private readonly SecfFileDecryptor _decryptor;

    public ManifestFileDeliverer(SecfFileDecryptor decryptor)
    {
      _decryptor = decryptor ?? throw new ArgumentNullException(nameof(decryptor));
    }

    /// <summary>
    /// Livre chaque entree de manifest.FilesLocation dans <paramref name="filesFolder"/>.
    /// Le dossier n'est cree que si le manifest reference au moins un fichier.
    /// </summary>
    public DeliveryReport Deliver(StoreManifest manifest, string filesFolder)
    {
      var report = new DeliveryReport();
      var locations = manifest?.FilesLocation;
      if (locations == null || locations.Count == 0)
      {
        return report;
      }

      report.TotalFiles = locations.Count;
      Directory.CreateDirectory(filesFolder);
      var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      for (int i = 0; i < locations.Count; i++)
      {
        var sourcePath = locations[i]?.FullPath;

        try
        {
          if (string.IsNullOrWhiteSpace(sourcePath))
          {
            throw new FileNotFoundException("manifest entry has no FullPath");
          }

          var targetName = SafePaths.MakeUniqueFileName(filesFolder, ResolveTargetFileName(manifest, i, sourcePath), usedNames);
          var targetPath = SafePaths.GetSafeDestinationPath(filesFolder, targetName);

          if (File.Exists(sourcePath))
          {
            File.Copy(sourcePath, targetPath, overwrite: false);
            report.Copied++;
          }
          else if (File.Exists(sourcePath + SecfFileDecryptor.Extension))
          {
            _decryptor.DecryptTo(sourcePath + SecfFileDecryptor.Extension, filesFolder, targetPath);
            report.Decrypted++;
          }
          else
          {
            throw new FileNotFoundException($"neither '{sourcePath}' nor its '{SecfFileDecryptor.Extension}' version exists. Content file not found. TODO: implement .see file extension");
          }

          report.DeliveredPaths.Add(targetPath);
          report.TotalSize += new FileInfo(targetPath).Length;
        }
        catch (Exception ex)
        {
          report.Failed++;
          report.AddError($"File #{i} ({Path.GetFileName(sourcePath ?? string.Empty)})", ex);
        }
      }

      return report;
    }

    /// <summary>
    /// Nom visible du fichier livre : RealFileName des metadonnees, sinon FileName de l'entree,
    /// sinon le nom contenu dans FullPath. Toujours reduit a un nom de fichier nu.
    /// </summary>
    private static string ResolveTargetFileName(StoreManifest manifest, int index, string sourcePath)
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

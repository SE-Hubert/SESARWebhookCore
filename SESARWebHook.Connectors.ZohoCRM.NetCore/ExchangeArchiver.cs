using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Core.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Archive un echange, en clair, dans son propre dossier: OutputPath\categorie...\{yyyy-MM-dd_HHmmss} {sujet}\fichier
  /// (categorie: <see cref="ArchiveFolders"/>). Chaque fichier du manifest (pieces jointes et corps du message,
  /// EmailSent.html) est copie s'il est deja en clair, sinon son .secf est dechiffre avec la PrivateAESKey. Les noms
  /// viennent du manifest: ils sont reduits a des noms nus et chaque chemin final doit rester sous OutputPath. Jamais
  /// d'ecrasement: un dossier ou un fichier deja present recoit un suffixe " (n)".
  /// </summary>
  internal sealed class ExchangeArchiver
  {
    /// <summary>Longueur maximale du sujet dans un nom de dossier (les chemins restent courts malgre deux GUID).</summary>
    private const int MaxSubjectLength = 80;
    private const string DefaultSubject = "message";

    private readonly string _outputPath;
    private readonly SecfFileDecryptor _decryptor;

    public ExchangeArchiver(string outputPath, SecfFileDecryptor decryptor)
    {
      if (string.IsNullOrWhiteSpace(outputPath))
      {
        throw new ArgumentException("OutputPath is required", nameof(outputPath));
      }
      _outputPath = outputPath;
      _decryptor = decryptor ?? throw new ArgumentNullException(nameof(decryptor));
    }

    /// <summary>
    /// Dossier (non cree) de l'echange: OutputPath\categorie...\{date} {sujet}, suffixe " (n)" s'il existe deja. Chaque
    /// segment est reduit a un nom nu et le chemin doit rester sous OutputPath.
    /// </summary>
    public string BuildTargetFolder(StoreManifest manifest, IReadOnlyList<string> category, DateTime date)
    {
      var folder = Path.GetFullPath(_outputPath);
      foreach (var segment in category ?? Array.Empty<string>())
      {
        var safe = SafePaths.SanitizeFileName(segment);
        if (string.IsNullOrEmpty(safe))
        {
          throw new ArgumentException("Invalid archive category segment", nameof(category));
        }
        folder = SafePaths.GetSafeDestinationPath(folder, safe);
      }

      var name = date.ToString("yyyy-MM-dd_HHmmss") + " " + ResolveSubject(manifest);
      var candidate = SafePaths.GetSafeDestinationPath(folder, name);
      int n = 2;
      while (Directory.Exists(candidate) || File.Exists(candidate))
      {
        candidate = SafePaths.GetSafeDestinationPath(folder, $"{name} ({n++})");
      }
      return candidate;
    }

    /// <summary>Copie (ou dechiffre) chaque fichier du manifest dans le dossier de l'echange.</summary>
    public ArchiveReport Archive(StoreManifest manifest, IReadOnlyList<string> category, DateTime date)
    {
      var report = new ArchiveReport();
      var locations = manifest?.FilesLocation;
      if (locations == null || locations.Count == 0)
      {
        return report;
      }

      var folder = BuildTargetFolder(manifest, category, date);
      Directory.CreateDirectory(folder);
      report.Folder = folder;
      var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      for (int i = 0; i < locations.Count; i++)
      {
        var sourcePath = ManifestFilePaths.ResolveSourcePath(manifest, i);
        try
        {
          if (string.IsNullOrWhiteSpace(sourcePath))
          {
            throw new FileNotFoundException("manifest entry has neither FullPath nor FileName");
          }

          var targetName = SafePaths.MakeUniqueFileName(folder, ManifestFilePaths.ResolveTargetFileName(manifest, i, sourcePath), usedNames);
          var targetPath = SafePaths.GetSafeDestinationPath(folder, targetName);

          // Meme ordre que le connecteur FileSystem: fichier deja en clair, sinon son .secf dechiffre.
          if (File.Exists(sourcePath))
          {
            File.Copy(sourcePath, targetPath, overwrite: false);
            report.Copied++;
          }
          else if (File.Exists(sourcePath + SecfFileDecryptor.Extension))
          {
            _decryptor.DecryptTo(sourcePath + SecfFileDecryptor.Extension, folder, targetPath);
            report.Decrypted++;
          }
          else
          {
            throw new FileNotFoundException("neither the file nor its .secf version exists");
          }
          report.ArchivedPaths.Add(targetPath);
        }
        catch (Exception ex)
        {
          report.Failed++;
          // Nom du fichier et type d'erreur seulement: jamais de contenu ni de cle.
          report.Errors.Add($"File #{i} ({Path.GetFileName(sourcePath ?? string.Empty)}): {ex.GetType().Name}: {ex.Message}");
        }
      }

      // Rien d'archive: on ne laisse pas de dossier vide derriere.
      if (report.Archived == 0)
      {
        TryDeleteEmptyFolder(folder);
        report.Folder = null;
      }
      return report;
    }

    /// <summary>Sujet du message original, sinon le sujet de la reponse (Base64Subject), reduit a un nom de dossier court.</summary>
    private static string ResolveSubject(StoreManifest manifest)
    {
      var subject = SafePaths.SanitizeFileName(manifest?.OriginalRecipientInfo?.Subject);
      if (string.IsNullOrEmpty(subject))
      {
        subject = SafePaths.SanitizeFileName(DecodeBase64(manifest?.Base64Subject));
      }
      if (string.IsNullOrEmpty(subject))
      {
        return DefaultSubject;
      }
      if (subject.Length > MaxSubjectLength)
      {
        subject = subject.Substring(0, MaxSubjectLength).TrimEnd('.', ' ');
      }
      return string.IsNullOrEmpty(subject) ? DefaultSubject : subject;
    }

    private static string DecodeBase64(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        return null;
      }
      try
      {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
      }
      catch (FormatException)
      {
        return null;
      }
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
      try
      {
        if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
        {
          Directory.Delete(folder);
        }
      }
      catch (IOException)
      {
        // nettoyage au mieux
      }
      catch (UnauthorizedAccessException)
      {
        // nettoyage au mieux
      }
    }
  }
}

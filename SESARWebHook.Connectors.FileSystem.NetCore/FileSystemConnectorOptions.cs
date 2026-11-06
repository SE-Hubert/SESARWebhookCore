using SESARWebHook.Core.Configuration;
using SESARWebHook.Core.Files;
using System;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.FileSystem
{
  /// <summary>
  /// Parametres du connecteur FileSystem, valides et types une seule fois a l'initialisation.
  ///
  /// Sources : la section Connector:filesystem d'appsettings.json pour les valeurs non sensibles,
  /// connectors.secrets.json pour la PrivateAESKey (propre au connecteur, sinon globale).
  /// </summary>
  internal sealed class FileSystemConnectorOptions
  {
    /// <summary>Limite d'attachement par defaut : 2 Gio.</summary>
    public const long DefaultEmlMaxTotalSizeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Dossier racine de sortie.</summary>
    public string OutputPath { get; private set; }

    /// <summary>Vrai pour ecrire dans un sous-dossier par jour (yyyy-MM-dd).</summary>
    public bool CreateSubfolders { get; private set; }

    /// <summary>Total maximal des fichiers, en octets, pour qu'ils soient attaches au .eml.</summary>
    public long EmlMaxTotalSizeBytes { get; private set; }

    /// <summary>Cle AES de dechiffrement des fichiers .secf ; null si aucune n'est configuree.</summary>
    public byte[] Key { get; private set; }

    /// <summary>IV associe a <see cref="Key"/> ; null si aucune cle n'est configuree.</summary>
    public byte[] Iv { get; private set; }

    /// <summary>Vrai quand une cle et un IV sont disponibles pour dechiffrer les .secf.</summary>
    public bool HasDecryptionKey => Key != null && Iv != null;

    private FileSystemConnectorOptions()
    {
    }

    /// <summary>
    /// Construit les options a partir du dictionnaire de parametres du connecteur.
    /// Une valeur mal formee (limite non numerique, cle non base64) leve une
    /// InvalidOperationException : mieux vaut refuser de demarrer que tourner mal configure.
    /// </summary>
    public static FileSystemConnectorOptions FromSettings(Dictionary<string, string> settings)
    {
      if (settings == null)
      {
        throw new ArgumentNullException(nameof(settings));
      }

      var options = new FileSystemConnectorOptions
      {
        OutputPath = Get(settings, "OutputPath") ?? @"C:\SESARWebHook\Output\",
        CreateSubfolders = bool.TryParse(Get(settings, "CreateSubfolders"), out var create) && create,
        EmlMaxTotalSizeBytes = DefaultEmlMaxTotalSizeBytes
      };

      var maxSize = Get(settings, "EmlMaxTotalSizeBytes");
      if (maxSize != null)
      {
        if (!long.TryParse(maxSize, out var parsed) || parsed < 0)
        {
          throw new InvalidOperationException("EmlMaxTotalSizeBytes must be a non-negative integer number of bytes");
        }
        options.EmlMaxTotalSizeBytes = parsed;
      }

      // Priorite a la cle propre au connecteur, repli sur la cle globale des secrets
      var privateAesKey = Get(settings, "PrivateAESKey");
      if (string.IsNullOrWhiteSpace(privateAesKey))
      {
        privateAesKey = SecureConfigManager.GetPrivateAESKey();
      }

      if (!string.IsNullOrWhiteSpace(privateAesKey))
      {
        PrivateAesKey.Parse(privateAesKey, out var key, out var iv);
        options.Key = key;
        options.Iv = iv;
      }

      return options;
    }

    /// <summary>Retourne la valeur du parametre, ou null si absent.</summary>
    private static string Get(Dictionary<string, string> settings, string name)
    {
      return settings.TryGetValue(name, out var value) ? value : null;
    }
  }
}

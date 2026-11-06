using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>Bilan de l'archivage d'un echange (compteurs, dossier et chemins produits, erreurs sans contenu).</summary>
  internal sealed class ArchiveReport
  {
    public int Decrypted { get; set; }
    public int Copied { get; set; }
    public int Failed { get; set; }
    /// <summary>Dossier de l'echange (null si le manifest ne contenait aucun fichier).</summary>
    public string Folder { get; set; }
    public List<string> ArchivedPaths { get; } = new List<string>();
    public List<string> Errors { get; } = new List<string>();
    public int Archived => Decrypted + Copied;
  }
}

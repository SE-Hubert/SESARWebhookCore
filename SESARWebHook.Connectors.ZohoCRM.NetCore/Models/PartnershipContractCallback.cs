using System;

namespace SESARWebHook.Connectors.ZohoCRM.Models
{
  /// <summary>
  /// Contrat de partenariat reconnu dans le parametre de rappel (StoreManifest.CallBackParameter), pose par le site Secure
  /// Exchanges a l'envoi du contrat: [{"Key":"Contract","Value":"PartnershipContract"},{"Key":"AffiliateId","Value":"..."},
  /// {"Key":"ApplicationId","Value":"..."}]. Aucune donnee personnelle: identifiants internes seulement.
  /// </summary>
  public sealed class PartnershipContractCallback
  {
    public Guid AffiliateId { get; set; }
    public Guid? ApplicationId { get; set; }
  }
}

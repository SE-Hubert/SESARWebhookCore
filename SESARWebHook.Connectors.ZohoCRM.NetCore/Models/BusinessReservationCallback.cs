using System;

namespace SESARWebHook.Connectors.ZohoCRM.Models
{
  /// <summary>
  /// Message lie a une reservation d'entreprise, reconnu dans le parametre de rappel (StoreManifest.CallBackParameter) de
  /// l'enveloppe creee par le site Secure Exchanges: [{"Key":"Context","Value":"BusinessReservation"},
  /// {"Key":"AffiliateId","Value":"..."},{"Key":"ReservationId","Value":"..."}]. Aucune donnee personnelle: identifiants
  /// internes seulement.
  /// </summary>
  public sealed class BusinessReservationCallback
  {
    public Guid AffiliateId { get; set; }
    public Guid ReservationId { get; set; }
    /// <summary>Note du fil qui demandait les documents; null pour un lien anterieur a cette information.</summary>
    public Guid? NoteId { get; set; }
  }
}

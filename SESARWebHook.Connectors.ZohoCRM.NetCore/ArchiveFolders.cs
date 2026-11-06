using System;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Classement des echanges archives sous OutputPath, un dossier racine par type d'echange puis par identifiant interne:
  ///   Contrats-partenariat\{AffiliateId}\{yyyy-MM-dd_HHmmss} {sujet}\fichiers
  ///   Reservations-entreprise\{AffiliateId}\{ReservationId}\{yyyy-MM-dd_HHmmss} {sujet}\fichiers
  /// Les identifiants viennent des parseurs (GUID valides, format "D"): aucun nom de dossier n'est tire d'une donnee
  /// personnelle.
  /// </summary>
  internal static class ArchiveFolders
  {
    public const string PartnershipContracts = "Contrats-partenariat";
    public const string BusinessReservations = "Reservations-entreprise";

    public static IReadOnlyList<string> ForPartnershipContract(Guid affiliateId)
    {
      return new[] { PartnershipContracts, affiliateId.ToString("D") };
    }

    public static IReadOnlyList<string> ForBusinessReservation(Guid affiliateId, Guid reservationId)
    {
      return new[] { BusinessReservations, affiliateId.ToString("D"), reservationId.ToString("D") };
    }
  }
}

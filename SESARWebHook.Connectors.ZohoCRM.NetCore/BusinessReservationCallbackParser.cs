using SESARWebHook.Connectors.ZohoCRM.Models;
using System;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Reconnaissance STRICTE du parametre de rappel d'une enveloppe de reservation d'entreprise (site Secure Exchanges,
  /// BusinessReservationEnvelopeHelper.BuildCallbackParameters). Meme format et memes regles que le contrat de
  /// partenariat; les deux jeux de cles sont disjoints (Context et non Contract), un echange n'est donc jamais reconnu
  /// par les deux parseurs.
  /// </summary>
  public static class BusinessReservationCallbackParser
  {
    public const string ContextKey = "Context";
    public const string BusinessReservationValue = "BusinessReservation";
    public const string AffiliateIdKey = "AffiliateId";
    public const string ReservationIdKey = "ReservationId";
    /// <summary>Note du fil qui demande les documents (optionnel: le site l'ajoute depuis la version a un lien par demande).</summary>
    public const string NoteIdKey = "NoteId";

    /// <summary>Longueur maximale acceptee (le parametre ne contient que quelques identifiants).</summary>
    public const int MaxLength = CallbackPairs.MaxLength;

    private static readonly HashSet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal) { ContextKey, AffiliateIdKey, ReservationIdKey, NoteIdKey };

    /// <summary>
    /// Vrai si <paramref name="callbackParameter"/> contient exactement Context = BusinessReservation, un AffiliateId et
    /// un ReservationId (GUID non vides), NoteId (GUID) en option. Toute autre forme est refusee.
    /// </summary>
    public static bool TryParse(string callbackParameter, out BusinessReservationCallback callback)
    {
      callback = null;
      if (!CallbackPairs.TryRead(callbackParameter, AllowedKeys, out var values))
      {
        return false;
      }

      if (!values.TryGetValue(ContextKey, out var context) || !string.Equals(context, BusinessReservationValue, StringComparison.Ordinal))
      {
        return false;
      }
      if (!CallbackPairs.TryGetRequiredGuid(values, AffiliateIdKey, out var affiliateId)
          || !CallbackPairs.TryGetRequiredGuid(values, ReservationIdKey, out var reservationId))
      {
        return false;
      }

      Guid? noteId = null;
      if (values.ContainsKey(NoteIdKey))
      {
        if (!CallbackPairs.TryGetRequiredGuid(values, NoteIdKey, out var parsedNote))
        {
          return false;
        }
        noteId = parsedNote;
      }

      callback = new BusinessReservationCallback { AffiliateId = affiliateId, ReservationId = reservationId, NoteId = noteId };
      return true;
    }
  }
}

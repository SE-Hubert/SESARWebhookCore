using SESARWebHook.Connectors.ZohoCRM.Models;
using System;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Reconnaissance STRICTE du parametre de rappel d'un contrat de partenariat: liste JSON de paires {"Key","Value"}
  /// (format CallBackParameter du SDK). La valeur arrive du webhook: elle est traitee comme non fiable. Tout ce qui n'est
  /// pas exactement le format attendu est refuse (refus par defaut): le connecteur ne fait alors rien.
  /// </summary>
  public static class PartnershipContractCallbackParser
  {
    public const string ContractKey = "Contract";
    public const string PartnershipContractValue = "PartnershipContract";
    public const string AffiliateIdKey = "AffiliateId";
    public const string ApplicationIdKey = "ApplicationId";

    /// <summary>Longueur maximale acceptee (le parametre ne contient que quelques identifiants).</summary>
    public const int MaxLength = CallbackPairs.MaxLength;

    private static readonly HashSet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal) { ContractKey, AffiliateIdKey, ApplicationIdKey };

    /// <summary>
    /// Vrai si <paramref name="callbackParameter"/> est un tableau JSON de paires {"Key":string,"Value":string} contenant
    /// Contract = PartnershipContract et un AffiliateId (GUID non vide), ApplicationId (GUID) en option. Une cle inconnue,
    /// en double, ou une paire avec d'autres proprietes est refusee; aucun type n'est instancie depuis le JSON.
    /// </summary>
    public static bool TryParse(string callbackParameter, out PartnershipContractCallback callback)
    {
      callback = null;
      if (!CallbackPairs.TryRead(callbackParameter, AllowedKeys, out var values))
      {
        return false;
      }

      if (!values.TryGetValue(ContractKey, out var contract) || !string.Equals(contract, PartnershipContractValue, StringComparison.Ordinal))
      {
        return false;
      }
      if (!CallbackPairs.TryGetRequiredGuid(values, AffiliateIdKey, out var affiliateId))
      {
        return false;
      }

      Guid? applicationId = null;
      if (values.ContainsKey(ApplicationIdKey))
      {
        if (!CallbackPairs.TryGetRequiredGuid(values, ApplicationIdKey, out var parsed))
        {
          return false;
        }
        applicationId = parsed;
      }

      callback = new PartnershipContractCallback { AffiliateId = affiliateId, ApplicationId = applicationId };
      return true;
    }
  }
}

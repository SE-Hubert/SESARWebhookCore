using SecureExchangesSDK.Models.Messenging;
using SESARWebHook.Connectors.ZohoCRM.Models;
using SESARWebHook.Core.Auth;
using SESARWebHook.Core.Configuration;
using SESARWebHook.Core.Files;
using SESARWebHook.Core.Interfaces;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Connecteur Zoho CRM: archive les echanges Secure Exchanges que le site a marques d'un parametre de rappel (JSON,
  /// transmis par SESAR dans StoreManifest.CallBackParameter), classes par type puis par identifiant interne
  /// (<see cref="ArchiveFolders"/>):
  ///   - contrat de partenariat (Contract = PartnershipContract): version finale signee seulement, sous
  ///     OutputPath\Contrats-partenariat\{AffiliateId}\{date} {sujet}\;
  ///   - message lie a une reservation d'entreprise (Context = BusinessReservation): chaque echange (corps du message et
  ///     pieces jointes), sous OutputPath\Reservations-entreprise\{AffiliateId}\{ReservationId}\{date} {sujet}\.
  /// Le connecteur ne fait RIEN pour un echange sans parametre reconnu (ou mal forme). Les fichiers (.secf) sont dechiffres
  /// avec la PrivateAESKey.
  ///
  /// Premiere etape: aucun appel a l'API Zoho pour l'instant; les cles OAuth sont deja chargees pour l'etape suivante.
  ///
  /// CONFIGURATION
  ///   appsettings.json, Connector:zoho-crm : Domain, OutputPath (dossier du webhook).
  ///   connectors.secrets.json, Connectors:zoho-crm:Secrets : ClientId, ClientSecret, RefreshToken, PrivateAESKey
  ///   ("base64key_base64iv"; a defaut, la PrivateAESKey globale des secrets).
  /// </summary>
  public class ZohoCRMConnector : IIntegrationConnector
  {
    private OAuth2RefreshTokenHelper _authHelper;
    private string _apiDomain;
    private ExchangeArchiver _archiver;

    public string ConnectorId => "zoho-crm";
    public string DisplayName => "Zoho CRM";
    public string Description => "Archives signed Secure Exchanges partnership contracts and business reservation messages, filed by partner (Zoho CRM sync to come)";
    public string Version => "1.2.0";

    public IEnumerable<string> RequiredConfigurationKeys => new[]
    {
      "ClientId",
      "ClientSecret",
      "RefreshToken",
      "Domain",
      "OutputPath"
    };

    public void Initialize(Dictionary<string, string> settings)
    {
      if (settings == null)
      {
        throw new ArgumentNullException(nameof(settings));
      }

      var clientId = settings.GetValueOrDefault("ClientId", "");
      var clientSecret = settings.GetValueOrDefault("ClientSecret", "");
      var refreshToken = settings.GetValueOrDefault("RefreshToken", "");
      var accountsDomain = settings.GetValueOrDefault("AccountsDomain", "https://accounts.zoho.com");
      _apiDomain = settings.GetValueOrDefault("Domain", "https://www.zohoapis.com");

      // Cles OAuth Zoho chargees des maintenant (etape suivante: joindre le contrat au dossier Zoho).
      _authHelper = OAuth2RefreshTokenHelper.ForZoho(clientId, clientSecret, refreshToken, accountsDomain);

      // Cle de dechiffrement des .secf: celle du connecteur, sinon la cle globale des secrets.
      byte[] key = null, iv = null;
      var privateAesKey = settings.GetValueOrDefault("PrivateAESKey", null);
      if (string.IsNullOrWhiteSpace(privateAesKey))
      {
        privateAesKey = SecureConfigManager.GetPrivateAESKey();
      }
      if (!string.IsNullOrWhiteSpace(privateAesKey))
      {
        PrivateAesKey.Parse(privateAesKey, out key, out iv);
      }

      var outputPath = settings.GetValueOrDefault("OutputPath", null);
      _archiver = string.IsNullOrWhiteSpace(outputPath) ? null : new ExchangeArchiver(outputPath, new SecfFileDecryptor(key, iv));
    }

    public async Task<bool> ValidateConfigurationAsync(Dictionary<string, string> settings)
    {
      foreach (var key in RequiredConfigurationKeys)
      {
        if (!settings.ContainsKey(key) || string.IsNullOrWhiteSpace(settings[key]))
        {
          return false;
        }
      }

      return await Task.FromResult(true);
    }

    public async Task<bool> TestConnectionAsync()
    {
      if (_authHelper == null)
      {
        return false;
      }

      return await _authHelper.TestConnectionAsync();
    }

    public Task<IntegrationResult> ProcessManifestAsync(StoreManifest manifest, WebhookContext context)
    {
      // Refus par defaut: sans un de nos parametres de rappel (ou mal forme), on ne fait rien. Les deux jeux de cles sont
      // disjoints: un echange est reconnu par au plus un parseur.
      if (manifest == null)
      {
        return Task.FromResult(Ignored("No manifest: nothing to do"));
      }
      if (PartnershipContractCallbackParser.TryParse(manifest.CallBackParameter, out var contract))
      {
        return Task.FromResult(ProcessPartnershipContract(manifest, contract, context));
      }
      if (BusinessReservationCallbackParser.TryParse(manifest.CallBackParameter, out var reservation))
      {
        return Task.FromResult(ProcessBusinessReservation(manifest, reservation, context));
      }
      return Task.FromResult(Ignored("No recognized Secure Exchanges callback parameter: nothing to do"));
    }

    /// <summary>Contrat de partenariat: seule la version finale signee est archivee (les envois intermediaires sont ignores).</summary>
    private IntegrationResult ProcessPartnershipContract(StoreManifest manifest, PartnershipContractCallback callback, WebhookContext context)
    {
      var metadata = new Dictionary<string, object>
      {
        { "Kind", "PartnershipContract" },
        { "AffiliateId", callback.AffiliateId },
        { "ApplicationId", callback.ApplicationId }
      };
      if (!manifest.ContainsFinalVersionSignedFiles)
      {
        return Ignored("Partnership contract exchange is not the final signed version: nothing to do", metadata);
      }
      return ArchiveExchange(manifest, ArchiveFolders.ForPartnershipContract(callback.AffiliateId), context, metadata,
        callback.AffiliateId.ToString("D"), "Signed partnership contract");
    }

    /// <summary>Message lie a une reservation: chaque echange est archive (corps du message et pieces jointes).</summary>
    private IntegrationResult ProcessBusinessReservation(StoreManifest manifest, BusinessReservationCallback callback, WebhookContext context)
    {
      var metadata = new Dictionary<string, object>
      {
        { "Kind", "BusinessReservation" },
        { "AffiliateId", callback.AffiliateId },
        { "ReservationId", callback.ReservationId },
        { "IsReply", manifest.IsReply }
      };
      return ArchiveExchange(manifest, ArchiveFolders.ForBusinessReservation(callback.AffiliateId, callback.ReservationId), context, metadata,
        callback.ReservationId.ToString("D"), "Business reservation message");
    }

    private IntegrationResult ArchiveExchange(StoreManifest manifest, IReadOnlyList<string> category, WebhookContext context,
      Dictionary<string, object> metadata, string externalReferenceId, string label)
    {
      if (_archiver == null)
      {
        return IntegrationResult.Fail("Zoho CRM connector is not configured (OutputPath)", null, ConnectorId);
      }

      try
      {
        var date = context != null && context.ReceivedAt != default(DateTime) ? context.ReceivedAt.ToLocalTime() : DateTime.Now;
        var report = _archiver.Archive(manifest, category, date);
        metadata["Decrypted"] = report.Decrypted;
        metadata["Copied"] = report.Copied;
        metadata["Failed"] = report.Failed;

        if (report.Archived == 0 || report.Failed > 0)
        {
          var failed = IntegrationResult.Fail(label + " could not be fully archived", string.Join(Environment.NewLine, report.Errors), ConnectorId);
          failed.ItemsProcessed = report.Archived;
          failed.ItemsFailed = report.Failed;
          failed.Metadata = metadata;
          return failed;
        }

        return new IntegrationResult
        {
          Success = true,
          Message = label + " archived",
          ConnectorId = ConnectorId,
          ExternalReferenceId = externalReferenceId,
          ItemsProcessed = report.Archived,
          Metadata = metadata
        };
      }
      catch (Exception ex)
      {
        // Type et message seulement: aucune cle ni aucun contenu de fichier.
        return IntegrationResult.Fail("Failed to archive: " + label, ex.GetType().Name + ": " + ex.Message, ConnectorId);
      }
    }

    private IntegrationResult Ignored(string message, Dictionary<string, object> metadata = null)
    {
      var result = IntegrationResult.Ok(message, ConnectorId);
      result.ItemsProcessed = 0;
      result.Metadata = metadata ?? new Dictionary<string, object>();
      result.Metadata["Ignored"] = true;
      return result;
    }
  }

  /// <summary>
  /// Extension methods for Dictionary
  /// </summary>
  internal static class DictionaryExtensions
  {
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
    {
      return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }
  }
}

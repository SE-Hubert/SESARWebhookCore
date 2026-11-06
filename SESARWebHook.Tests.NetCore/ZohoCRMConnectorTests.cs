using Microsoft.VisualStudio.TestTools.UnitTesting;
using SecureExchangesSDK.Helpers;
using SecureExchangesSDK.Models.Messenging;
using SecureExchangesSDK.Models.Transport;
using SESARWebHook.Connectors.ZohoCRM;
using SESARWebHook.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SESARWebHook.Tests
{
  /// <summary>
  /// Tests du ZohoCRMConnector: reconnaissance stricte des parametres de rappel (contrat de partenariat et message de
  /// reservation d'entreprise), refus par defaut, dechiffrement et classement sous
  /// OutputPath\Contrats-partenariat\{AffiliateId}\{date} {sujet}\ et
  /// OutputPath\Reservations-entreprise\{AffiliateId}\{ReservationId}\{date} {sujet}\.
  /// Tous les fichiers et identifiants sont fictifs et crees dans un dossier temporaire propre au test.
  /// </summary>
  [TestClass]
  public class ZohoCRMConnectorTests
  {
    private static readonly Guid AffiliateId = new Guid("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ApplicationId = new Guid("66666666-7777-8888-9999-000000000000");
    private static readonly Guid ReservationId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTime ReceivedAt = new DateTime(2026, 10, 9, 14, 0, 0, DateTimeKind.Local);
    private const string Subject = "Contrat de partenariat Secure Exchanges a signer";
    private const string ReservationSubject = "Documents de reservation - Entreprise Fictive";

    private string _root;
    private string _sourceDir;
    private string _outputDir;
    private byte[] _key;
    private byte[] _iv;
    private ZohoCRMConnector _connector;

    [TestInitialize]
    public void Setup()
    {
      _root = Path.Combine(Path.GetTempPath(), "SESARWebHookTests", Guid.NewGuid().ToString("N"));
      _sourceDir = Path.Combine(_root, "source");
      _outputDir = Path.Combine(_root, "output");
      Directory.CreateDirectory(_sourceDir);
      Directory.CreateDirectory(_outputDir);

      _key = RandomNumberGenerator.GetBytes(32);
      _iv = RandomNumberGenerator.GetBytes(16);

      _connector = new ZohoCRMConnector();
      _connector.Initialize(new Dictionary<string, string>
      {
        { "ClientId", "<VALEUR_DEPUIS_COFFRE>" },
        { "ClientSecret", "<VALEUR_DEPUIS_COFFRE>" },
        { "RefreshToken", "<VALEUR_DEPUIS_COFFRE>" },
        { "Domain", "https://www.zohoapis.com" },
        { "OutputPath", _outputDir },
        { "PrivateAESKey", Convert.ToBase64String(_key) + "_" + Convert.ToBase64String(_iv) }
      });
    }

    [TestCleanup]
    public void Cleanup()
    {
      try
      {
        if (Directory.Exists(_root))
        {
          Directory.Delete(_root, recursive: true);
        }
      }
      catch
      {
        // nettoyage au mieux
      }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static string Pair(string key, string value)
    {
      return "{\"Key\":\"" + key + "\",\"Value\":\"" + value + "\"}";
    }

    /// <summary>Parametre tel que pose par le site: [{"Key":"Contract",...},{"Key":"AffiliateId",...},{"Key":"ApplicationId",...}].</summary>
    private static string Callback(Guid? affiliateId = null, Guid? applicationId = null, string contract = "PartnershipContract")
    {
      var pairs = new List<string> { Pair("Contract", contract), Pair("AffiliateId", (affiliateId ?? AffiliateId).ToString("D")) };
      if (applicationId.HasValue)
      {
        pairs.Add(Pair("ApplicationId", applicationId.Value.ToString("D")));
      }
      return "[" + string.Join(",", pairs) + "]";
    }

    private static WebhookContext NewContext()
    {
      return new WebhookContext { RequestId = Guid.NewGuid().ToString("N"), ReceivedAt = ReceivedAt, ConnectorId = "zoho-crm" };
    }

    /// <summary>Cree name.secf a partir du contenu, sans laisser le fichier clair.</summary>
    private string WriteEncrypted(string name, byte[] content)
    {
      var plain = Path.Combine(_sourceDir, name);
      File.WriteAllBytes(plain, content);
      CryptoHelper.EncryptSecfFile(plain, _sourceDir, _key, _iv);
      File.Delete(plain);
      Assert.IsTrue(File.Exists(plain + ".secf"), "test setup: .secf was not produced where expected");
      return plain;
    }

    private StoreManifest SignedContractManifest(string callback, bool finalVersion = true, string subject = Subject)
    {
      var path = WriteEncrypted("contrat.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 contrat fictif signe"));
      return new StoreManifest
      {
        CallBackParameter = callback,
        ContainsFinalVersionSignedFiles = finalVersion,
        OriginalRecipientInfo = new MessageRecipientInfo { Subject = subject, ContactInfo = "expediteur@exemple.test", CreateOn = ReceivedAt },
        FilesLocation = new List<FileLocation> { new FileLocation { FullPath = path, FileName = Path.GetFileName(path) } },
        FilesMetaData = new List<FileMetaData> { new FileMetaData { RealFileName = "Contrat-Partenariat-SecureExchanges.pdf" } }
      };
    }

    /// <summary>Parametre tel que pose par le site (BusinessReservationEnvelopeHelper.BuildCallbackParameters).</summary>
    private static string ReservationCallback(Guid? affiliateId = null, Guid? reservationId = null, string context = "BusinessReservation", Guid? noteId = null)
    {
      var pairs = Pair("Context", context) + "," + Pair("AffiliateId", (affiliateId ?? AffiliateId).ToString("D")) + ","
        + Pair("ReservationId", (reservationId ?? ReservationId).ToString("D"));
      if (noteId.HasValue) { pairs += "," + Pair("NoteId", noteId.Value.ToString("D")); }
      return "[" + pairs + "]";
    }

    /// <summary>Reponse d'enveloppe: corps du message (EmailSent.html, ecrit par SESAR) et une piece jointe, chiffres.</summary>
    private StoreManifest ReservationManifest(string callback, string subject = ReservationSubject, bool withFiles = true)
    {
      var files = new List<FileLocation>();
      var meta = new List<FileMetaData>();
      if (withFiles)
      {
        var body = WriteEncrypted("EmailSent.html", Encoding.UTF8.GetBytes("<p>Message fictif du partenaire</p>"));
        var attachment = WriteEncrypted("piece.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 preuve d'entente fictive"));
        files.Add(new FileLocation { FullPath = body, FileName = Path.GetFileName(body) });
        files.Add(new FileLocation { FullPath = attachment, FileName = Path.GetFileName(attachment) });
        meta.Add(new FileMetaData { RealFileName = "EmailSent.html" });
        meta.Add(new FileMetaData { RealFileName = "Preuve-entente.pdf" });
      }
      return new StoreManifest
      {
        CallBackParameter = callback,
        IsReply = true,
        ContainsFinalVersionSignedFiles = false,
        OriginalRecipientInfo = new MessageRecipientInfo { Subject = subject, ContactInfo = "partenaire@exemple.test", CreateOn = ReceivedAt },
        FilesLocation = files,
        FilesMetaData = meta
      };
    }

    private static string ExchangeFolderName(string subject)
    {
      return ReceivedAt.ToString("yyyy-MM-dd_HHmmss") + " " + subject;
    }

    private string ExpectedFolder(string subject = Subject, Guid? affiliateId = null)
    {
      return Path.Combine(_outputDir, "Contrats-partenariat", (affiliateId ?? AffiliateId).ToString("D"), ExchangeFolderName(subject));
    }

    private string ExpectedReservationFolder(string subject = ReservationSubject)
    {
      return Path.Combine(_outputDir, "Reservations-entreprise", AffiliateId.ToString("D"), ReservationId.ToString("D"), ExchangeFolderName(subject));
    }

    private bool OutputIsEmpty()
    {
      return Directory.GetFileSystemEntries(_outputDir).Length == 0;
    }

    // ──────────────────────────────────────────────
    // Parametre de rappel: reconnaissance stricte
    // ──────────────────────────────────────────────

    [TestMethod]
    public void Parser_FormatAttendu_Reconnu()
    {
      Assert.IsTrue(PartnershipContractCallbackParser.TryParse(Callback(AffiliateId, ApplicationId), out var c));
      Assert.AreEqual(AffiliateId, c.AffiliateId);
      Assert.AreEqual(ApplicationId, c.ApplicationId);

      Assert.IsTrue(PartnershipContractCallbackParser.TryParse(Callback(AffiliateId, null), out var direct), "Sous-partenaire cree directement: sans demande.");
      Assert.IsNull(direct.ApplicationId);
    }

    /// <summary>Chaines exactes produites par le site (PartnershipContractHelper.BuildCallbackParameters): contrat entre les deux depots.</summary>
    [TestMethod]
    public void Parser_ChainesProduitesParLeSite_Reconnues()
    {
      Assert.IsTrue(PartnershipContractCallbackParser.TryParse("[{\"Key\":\"Contract\",\"Value\":\"PartnershipContract\"},{\"Key\":\"AffiliateId\",\"Value\":\"11111111-2222-3333-4444-555555555555\"},{\"Key\":\"ApplicationId\",\"Value\":\"66666666-7777-8888-9999-000000000000\"}]", out var withApplication));
      Assert.AreEqual(AffiliateId, withApplication.AffiliateId);
      Assert.AreEqual(ApplicationId, withApplication.ApplicationId);
      Assert.IsTrue(PartnershipContractCallbackParser.TryParse("[{\"Key\":\"Contract\",\"Value\":\"PartnershipContract\"},{\"Key\":\"AffiliateId\",\"Value\":\"11111111-2222-3333-4444-555555555555\"}]", out var direct));
      Assert.IsNull(direct.ApplicationId);
    }

    [TestMethod]
    public void Parser_ToutCeQuiNEstPasLeFormat_Refuse()
    {
      var affiliate = Pair("AffiliateId", AffiliateId.ToString("D"));
      var refused = new[]
      {
        null,
        "",
        "PartnershipContract",                                                    // ancienne valeur fixe
        "[{\"Key\":\"Contract\",\"Value\":\"hihi\"}]",                    // autre contrat
        "{\"Key\":\"Contract\",\"Value\":\"PartnershipContract\"}",       // objet au lieu d'une liste
        "[ pas du json",
        "[1,2,3]",
        Callback(contract: "partnershipcontract"),                                // casse exacte
        Callback(affiliateId: Guid.Empty),
        "[" + Pair("Contract", "PartnershipContract") + "]",                       // AffiliateId manquant
        "[" + Pair("Contract", "PartnershipContract") + "," + Pair("AffiliateId", "pas-un-guid") + "]",
        "[" + Pair("Contract", "PartnershipContract") + "," + affiliate + "," + Pair("ApplicationId", "pas-un-guid") + "]",
        "[" + Pair("Contract", "PartnershipContract") + "," + affiliate + "," + Pair("Autre", "x") + "]",          // cle inconnue
        "[" + Pair("Contract", "PartnershipContract") + "," + affiliate + "," + affiliate + "]",                   // cle en double
        "[{\"Key\":\"Contract\",\"Value\":\"PartnershipContract\",\"$type\":\"System.Object\"}," + affiliate + "]",
        "[{\"Key\":\"Contract\",\"Key\":\"Contract\",\"Value\":\"PartnershipContract\"}," + affiliate + "]",
        "[{\"Key\":\"Contract\",\"Value\":1}," + affiliate + "]",
        new string('x', PartnershipContractCallbackParser.MaxLength + 1)
      };
      foreach (var value in refused)
      {
        Assert.IsFalse(PartnershipContractCallbackParser.TryParse(value, out var c), "Refuse attendu: " + (value == null ? "null" : value.Substring(0, Math.Min(60, value.Length))));
        Assert.IsNull(c);
      }
    }

    // ──────────────────────────────────────────────
    // Connecteur
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task SansParametreDeContrat_RienNEstFait()
    {
      var result = await _connector.ProcessManifestAsync(SignedContractManifest(null), NewContext());
      Assert.IsTrue(result.Success);
      Assert.AreEqual(0, result.ItemsProcessed);
      Assert.IsTrue(OutputIsEmpty(), "Aucun fichier ne doit etre dechiffre ni copie.");
    }

    [TestMethod]
    public async Task ParametreEtranger_RienNEstFait()
    {
      var result = await _connector.ProcessManifestAsync(SignedContractManifest("[{\"Key\":\"Contract\",\"Value\":\"hihi\"}]"), NewContext());
      Assert.IsTrue(result.Success);
      Assert.IsTrue(OutputIsEmpty());
    }

    [TestMethod]
    public async Task ContratPasEncoreSigne_RienNEstFait()
    {
      var result = await _connector.ProcessManifestAsync(SignedContractManifest(Callback(), finalVersion: false), NewContext());
      Assert.IsTrue(result.Success);
      Assert.IsTrue(OutputIsEmpty(), "Seule la version finale signee est archivee.");
    }

    [TestMethod]
    public async Task VersionFinaleSignee_DechiffreeEtCopieeSousDateSujetFichier()
    {
      var result = await _connector.ProcessManifestAsync(SignedContractManifest(Callback(AffiliateId, ApplicationId)), NewContext());

      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      Assert.AreEqual(1, result.ItemsProcessed);
      Assert.AreEqual(AffiliateId.ToString("D"), result.ExternalReferenceId);

      var target = Path.Combine(ExpectedFolder(), "Contrat-Partenariat-SecureExchanges.pdf");
      Assert.IsTrue(File.Exists(target), "Fichier attendu: " + target);
      Assert.AreEqual("%PDF-1.7 contrat fictif signe", File.ReadAllText(target));
      Assert.IsFalse(Directory.GetDirectories(ExpectedFolder()).Any(d => Path.GetFileName(d).StartsWith(".decrypt_")), "Aucun dossier temporaire de dechiffrement residuel.");
    }

    [TestMethod]
    public async Task DeuxContratsDuMemePartenaire_DeuxDossiersAucunEcrasement()
    {
      await _connector.ProcessManifestAsync(SignedContractManifest(Callback()), NewContext());
      var second = await _connector.ProcessManifestAsync(SignedContractManifest(Callback()), NewContext());

      Assert.IsTrue(second.Success);
      Assert.IsTrue(File.Exists(Path.Combine(ExpectedFolder(), "Contrat-Partenariat-SecureExchanges.pdf")));
      Assert.IsTrue(File.Exists(Path.Combine(ExpectedFolder() + " (2)", "Contrat-Partenariat-SecureExchanges.pdf")), "Le second echange a son propre dossier suffixe.");
    }

    [TestMethod]
    public async Task ContratsDePartenairesDifferents_ClassesParPartenaire()
    {
      var other = Guid.NewGuid();
      await _connector.ProcessManifestAsync(SignedContractManifest(Callback()), NewContext());
      await _connector.ProcessManifestAsync(SignedContractManifest(Callback(other)), NewContext());

      Assert.AreEqual(1, Directory.GetFiles(ExpectedFolder()).Length);
      Assert.AreEqual(1, Directory.GetFiles(ExpectedFolder(affiliateId: other)).Length);
    }

    [TestMethod]
    public async Task SujetMalveillant_RestedansLeDossierDuWebhook()
    {
      var result = await _connector.ProcessManifestAsync(SignedContractManifest(Callback(), subject: @"..\..\evil"), NewContext());

      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      var written = Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories);
      Assert.AreEqual(1, written.Length);
      Assert.IsTrue(Path.GetFullPath(written[0]).StartsWith(Path.GetFullPath(_outputDir), StringComparison.OrdinalIgnoreCase));
      Assert.AreEqual(0, Directory.GetFiles(_root, "*", SearchOption.TopDirectoryOnly).Length, "Rien d'ecrit hors du dossier du webhook.");
    }

    [TestMethod]
    public async Task MauvaiseCle_EchecSansContenuEnClair()
    {
      var manifest = SignedContractManifest(Callback());
      var otherKey = RandomNumberGenerator.GetBytes(32);
      var otherIv = RandomNumberGenerator.GetBytes(16);
      var wrong = new ZohoCRMConnector();
      wrong.Initialize(new Dictionary<string, string>
      {
        { "OutputPath", _outputDir },
        { "PrivateAESKey", Convert.ToBase64String(otherKey) + "_" + Convert.ToBase64String(otherIv) }
      });

      var result = await wrong.ProcessManifestAsync(manifest, NewContext());

      Assert.IsFalse(result.Success);
      Assert.AreEqual(1, result.ItemsFailed);
      Assert.AreEqual(0, Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories).Length, "Aucun fichier produit avec une mauvaise cle.");
    }

    [TestMethod]
    public async Task SansOutputPath_EchecDeConfiguration()
    {
      var unconfigured = new ZohoCRMConnector();
      unconfigured.Initialize(new Dictionary<string, string> { { "PrivateAESKey", Convert.ToBase64String(_key) + "_" + Convert.ToBase64String(_iv) } });

      var result = await unconfigured.ProcessManifestAsync(SignedContractManifest(Callback()), NewContext());

      Assert.IsFalse(result.Success);
    }

    // ──────────────────────────────────────────────
    // Reservation d'entreprise: parseur
    // ──────────────────────────────────────────────

    [TestMethod]
    public void ParserReservation_FormatAttendu_Reconnu()
    {
      Assert.IsTrue(BusinessReservationCallbackParser.TryParse(ReservationCallback(), out var c));
      Assert.AreEqual(AffiliateId, c.AffiliateId);
      Assert.AreEqual(ReservationId, c.ReservationId);
      Assert.IsNull(c.NoteId, "Lien anterieur: sans note.");

      var noteId = Guid.NewGuid();
      Assert.IsTrue(BusinessReservationCallbackParser.TryParse(ReservationCallback(noteId: noteId), out var withNote), "Un lien par demande porte la note visee.");
      Assert.AreEqual(noteId, withNote.NoteId);
      Assert.IsFalse(BusinessReservationCallbackParser.TryParse(ReservationCallback(noteId: Guid.Empty), out _), "NoteId vide refuse.");
    }

    /// <summary>Chaine exacte produite par le site (BusinessReservationEnvelopeHelper.BuildCallbackParameters): contrat entre les deux depots.</summary>
    [TestMethod]
    public void ParserReservation_ChaineProduiteParLeSite_Reconnue()
    {
      Assert.IsTrue(BusinessReservationCallbackParser.TryParse("[{\"Key\":\"Context\",\"Value\":\"BusinessReservation\"},{\"Key\":\"AffiliateId\",\"Value\":\"11111111-2222-3333-4444-555555555555\"},{\"Key\":\"ReservationId\",\"Value\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}]", out var c));
      Assert.AreEqual(AffiliateId, c.AffiliateId);
      Assert.AreEqual(ReservationId, c.ReservationId);
    }

    [TestMethod]
    public void ParserReservation_ToutCeQuiNEstPasLeFormat_Refuse()
    {
      var context = Pair("Context", "BusinessReservation");
      var affiliate = Pair("AffiliateId", AffiliateId.ToString("D"));
      var reservation = Pair("ReservationId", ReservationId.ToString("D"));
      var refused = new[]
      {
        null,
        "",
        "[ pas du json",
        "{\"Key\":\"Context\",\"Value\":\"BusinessReservation\"}",
        ReservationCallback(context: "businessreservation"),                          // casse exacte
        ReservationCallback(context: "PartnershipContract"),
        ReservationCallback(affiliateId: Guid.Empty),
        ReservationCallback(reservationId: Guid.Empty),
        "[" + context + "," + affiliate + "]",                                          // ReservationId manquant
        "[" + context + "," + reservation + "]",                                        // AffiliateId manquant
        "[" + context + "," + affiliate + "," + Pair("ReservationId", "pas-un-guid") + "]",
        "[" + context + "," + affiliate + "," + reservation + "," + Pair("Contract", "PartnershipContract") + "]",  // cle d'un autre contexte
        "[" + context + "," + affiliate + "," + reservation + "," + reservation + "]",                             // cle en double
        "[{\"Key\":\"Context\",\"Value\":\"BusinessReservation\",\"$type\":\"System.Object\"}," + affiliate + "," + reservation + "]",
        Callback(AffiliateId, ApplicationId),                                           // parametre de contrat
        new string('x', BusinessReservationCallbackParser.MaxLength + 1)
      };
      foreach (var value in refused)
      {
        Assert.IsFalse(BusinessReservationCallbackParser.TryParse(value, out var c), "Refuse attendu: " + (value == null ? "null" : value.Substring(0, Math.Min(60, value.Length))));
        Assert.IsNull(c);
      }
    }

    [TestMethod]
    public void Parsers_JeuxDeClesDisjoints()
    {
      Assert.IsFalse(PartnershipContractCallbackParser.TryParse(ReservationCallback(), out _), "Un message de reservation n'est jamais pris pour un contrat.");
      Assert.IsFalse(BusinessReservationCallbackParser.TryParse(Callback(), out _), "Un contrat n'est jamais pris pour un message de reservation.");
    }

    // ──────────────────────────────────────────────
    // Reservation d'entreprise: connecteur
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task MessageDeReservation_DechiffreEtClasseParPartenaireEtReservation()
    {
      var result = await _connector.ProcessManifestAsync(ReservationManifest(ReservationCallback()), NewContext());

      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      Assert.AreEqual(2, result.ItemsProcessed);
      Assert.AreEqual(ReservationId.ToString("D"), result.ExternalReferenceId);
      Assert.AreEqual("BusinessReservation", result.Metadata["Kind"]);

      var folder = ExpectedReservationFolder();
      Assert.AreEqual("<p>Message fictif du partenaire</p>", File.ReadAllText(Path.Combine(folder, "EmailSent.html")), "Le corps du message est archive.");
      Assert.AreEqual("%PDF-1.7 preuve d'entente fictive", File.ReadAllText(Path.Combine(folder, "Preuve-entente.pdf")));
      Assert.IsFalse(Directory.Exists(Path.Combine(_outputDir, "Contrats-partenariat")), "Rien n'est classe avec les contrats.");
    }

    [TestMethod]
    public async Task MessageDeReservation_PasBesoinDeVersionFinaleSignee()
    {
      var manifest = ReservationManifest(ReservationCallback());
      manifest.ContainsFinalVersionSignedFiles = false;
      var result = await _connector.ProcessManifestAsync(manifest, NewContext());
      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      Assert.IsTrue(Directory.Exists(ExpectedReservationFolder()));
    }

    [TestMethod]
    public async Task DeuxMessagesSurLaMemeReservation_DeuxDossiers()
    {
      await _connector.ProcessManifestAsync(ReservationManifest(ReservationCallback()), NewContext());
      var second = await _connector.ProcessManifestAsync(ReservationManifest(ReservationCallback()), NewContext());

      Assert.IsTrue(second.Success, second.Message + " " + second.ErrorDetails);
      var reservationRoot = Path.Combine(_outputDir, "Reservations-entreprise", AffiliateId.ToString("D"), ReservationId.ToString("D"));
      Assert.AreEqual(2, Directory.GetDirectories(reservationRoot).Length, "Chaque echange a son propre dossier.");
    }

    [TestMethod]
    public async Task MessageDeReservationSansFichier_EchecSansDossierVide()
    {
      var result = await _connector.ProcessManifestAsync(ReservationManifest(ReservationCallback(), withFiles: false), NewContext());

      Assert.IsFalse(result.Success);
      Assert.AreEqual(0, Directory.GetFileSystemEntries(_outputDir, "*", SearchOption.AllDirectories).Count(e => File.Exists(e)), "Aucun fichier produit.");
    }

    [TestMethod]
    public async Task MessageDeReservation_SujetMalveillant_ResteSousLaReservation()
    {
      var result = await _connector.ProcessManifestAsync(ReservationManifest(ReservationCallback(), subject: @"..\..\..\..\evil"), NewContext());

      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      var reservationRoot = Path.GetFullPath(Path.Combine(_outputDir, "Reservations-entreprise", AffiliateId.ToString("D"), ReservationId.ToString("D")));
      var written = Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories);
      Assert.AreEqual(2, written.Length);
      Assert.IsTrue(written.All(w => Path.GetFullPath(w).StartsWith(reservationRoot, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task MessageDeReservation_SujetDeLaReponseSiAucunSujetOriginal()
    {
      var manifest = ReservationManifest(ReservationCallback(), subject: null);
      manifest.Base64Subject = Convert.ToBase64String(Encoding.UTF8.GetBytes("Reponse fictive"));
      var result = await _connector.ProcessManifestAsync(manifest, NewContext());

      Assert.IsTrue(result.Success, result.Message + " " + result.ErrorDetails);
      Assert.IsTrue(Directory.Exists(ExpectedReservationFolder("Reponse fictive")));
    }

    [TestMethod]
    public async Task Validation_ExigeOutputPath()
    {
      var settings = new Dictionary<string, string>
      {
        { "ClientId", "x" }, { "ClientSecret", "x" }, { "RefreshToken", "x" }, { "Domain", "https://www.zohoapis.com" }
      };
      Assert.IsFalse(await _connector.ValidateConfigurationAsync(settings));
      settings["OutputPath"] = _outputDir;
      Assert.IsTrue(await _connector.ValidateConfigurationAsync(settings));
    }
  }
}

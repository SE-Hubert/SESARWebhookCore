using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace SESARWebHook.Connectors.ZohoCRM
{
  /// <summary>
  /// Lecture STRICTE d'un parametre de rappel au format CallBackParameter du SDK: tableau JSON de paires
  /// {"Key":string,"Value":string}, pose par le site Secure Exchanges. La valeur arrive du webhook: elle est traitee comme
  /// non fiable. Refus par defaut: cle hors de la liste permise, cle en double, propriete en trop, type inattendu ou JSON
  /// invalide font echouer toute la lecture. Aucun type n'est instancie depuis le JSON.
  /// </summary>
  internal static class CallbackPairs
  {
    /// <summary>Longueur maximale acceptee (le parametre ne contient que quelques identifiants).</summary>
    public const int MaxLength = 1024;

    public static bool TryRead(string callbackParameter, ISet<string> allowedKeys, out Dictionary<string, string> values)
    {
      values = null;
      if (string.IsNullOrWhiteSpace(callbackParameter) || callbackParameter.Length > MaxLength || allowedKeys == null)
      {
        return false;
      }

      JArray array;
      try
      {
        var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
        array = JArray.Parse(callbackParameter, settings);
      }
      catch (JsonException)
      {
        return false;
      }

      var read = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var item in array)
      {
        if (!(item is JObject pair) || pair.Count != 2
            || pair["Key"]?.Type != JTokenType.String || pair["Value"]?.Type != JTokenType.String)
        {
          return false;
        }
        var key = (string)pair["Key"];
        if (!allowedKeys.Contains(key) || read.ContainsKey(key))
        {
          return false;
        }
        read[key] = (string)pair["Value"];
      }

      values = read;
      return true;
    }

    /// <summary>GUID obligatoire au format "D", non vide.</summary>
    public static bool TryGetRequiredGuid(Dictionary<string, string> values, string key, out Guid value)
    {
      value = Guid.Empty;
      return values.TryGetValue(key, out var raw) && Guid.TryParseExact(raw, "D", out value) && value != Guid.Empty;
    }
  }
}

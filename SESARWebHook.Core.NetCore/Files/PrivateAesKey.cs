using System;

namespace SESARWebHook.Core.Files
{
  /// <summary>
  /// Lecture de la PrivateAESKey SESAR (format "base64key_base64iv"), partagee par les connecteurs qui dechiffrent des
  /// fichiers .secf. Une valeur mal formee leve une InvalidOperationException sans jamais citer la valeur recue.
  /// </summary>
  public static class PrivateAesKey
  {
    /// <summary>Decode une PrivateAESKey au format SESAR "base64key_base64iv".</summary>
    public static void Parse(string privateAesKey, out byte[] key, out byte[] iv)
    {
      var parts = (privateAesKey ?? string.Empty).Split('_');
      if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
      {
        throw new InvalidOperationException("PrivateAESKey must be in format 'base64key_base64iv' separated by underscore");
      }

      try
      {
        key = Convert.FromBase64String(parts[0]);
        iv = Convert.FromBase64String(parts[1]);
      }
      catch (FormatException ex)
      {
        throw new InvalidOperationException("PrivateAESKey must be in format 'base64key_base64iv' with valid base64 strings", ex);
      }
    }
  }
}

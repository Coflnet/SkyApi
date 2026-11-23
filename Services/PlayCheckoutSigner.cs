using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

#nullable enable

namespace Coflnet.Sky.Api.Services;

internal static class PlayCheckoutSigner
{
    internal const string ConfigurationKey = "PLAY_CHECKOUT_SIGNING_KEY";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    internal static bool TryLoadKey(ECDsa key, string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(configuredKey);
            try
            {
                key.ImportPkcs8PrivateKey(bytes, out var bytesRead);
                return bytesRead == bytes.Length && key.ExportParameters(false).Curve.Oid.Value == P256Oid;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    internal static string? Sign(ECDsa key, string accountId, string playProductId, string basePlanId,
        string productSlug, string accountLabel, long expiresAt, string? offerId = null)
    {
        var version = offerId == null ? "coflnet-play-checkout-v1" : "coflnet-play-checkout-v2";
        string[] lines = [version, accountId, playProductId, basePlanId, productSlug,
            accountLabel, expiresAt.ToString(CultureInfo.InvariantCulture)];
        if (offerId != null)
            lines = [.. lines, offerId];
        if (lines.Any(line => string.IsNullOrWhiteSpace(line) || line.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')))
            return null;

        var payload = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return WebEncoders.Base64UrlEncode(key.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
    }
}

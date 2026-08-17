using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace PassingTrace.Identity.AuthorizationServer.Security;

public static class SecretEncoding
{
    public static string Generate(int byteCount = 32) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    public static string Hash(string value) =>
        WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool Verify(string value, string expectedHash)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        byte[] expected;
        try
        {
            expected = WebEncoders.Base64UrlDecode(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return actual.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IsS256Challenge(string value)
    {
        try
        {
            return WebEncoders.Base64UrlDecode(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var apiKey = "9UA5+IEHP68a8i7pwRRiahubuj14J/LIfs05vhupHyFbT7GWyFs9gXr1";
var secretKey = "bZI23Z8QfS1PhQjm/hynRtZmCVv+IaITiU8f9pBwKOlcy1w14jDRkdOs9pe4wlYpPRXKHyVxCZX3z1wHrG2zOw==";

Console.WriteLine($"API Key: {apiKey}");
Console.WriteLine($"API Key length: {apiKey.Length}");
Console.WriteLine($"Secret Key present: {!string.IsNullOrEmpty(secretKey)}");
Console.WriteLine($"Secret Key length (base64): {secretKey.Length}");

var httpClient = new HttpClient { BaseAddress = new Uri("https://api.kraken.com") };

var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
var body = $"{{\"nonce\": \"{nonce}\"}}";  // JSON with space after colon to match Python
var queryStr = "";

Console.WriteLine($"Nonce: {nonce}");
Console.WriteLine($"Body (JSON with space): {body}");
Console.WriteLine($"QueryStr: {queryStr}");

var signature = GetSignature(secretKey, queryStr + body, nonce, "/0/private/Balance");
Console.WriteLine($"Signature: {signature}");

var request = new HttpRequestMessage(HttpMethod.Post, "/0/private/Balance")
{
    Content = new StringContent(body, Encoding.UTF8, "application/json")
};
request.Headers.Add("API-Key", apiKey);
request.Headers.Add("API-Sign", signature);

Console.WriteLine("Sending request...");
var response = await httpClient.SendAsync(request);
var content = await response.Content.ReadAsStringAsync();

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine($"Response: {content}");

static string GetSignature(string secret, string data, string nonce, string path)
{
    if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(path))
        return "";

    // SHA256(nonce + data) - nonce appears twice
    var encodedPayload = nonce + data;
    var sha256Hash = SHA256.HashData(Encoding.UTF8.GetBytes(encodedPayload));

    // HMAC-SHA512(secret, path_bytes + sha256_raw_bytes)
    var secretBytes = Convert.FromBase64String(secret);
    using var hmac = new HMACSHA512(secretBytes);

    var signatureInput = Encoding.UTF8.GetBytes(path).Concat(sha256Hash).ToArray();
    var signature = hmac.ComputeHash(signatureInput);

    return Convert.ToBase64String(signature);
}

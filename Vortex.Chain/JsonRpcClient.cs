using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Chain;

/// <summary>
/// The node, over JSON-RPC. One method, because JSON-RPC is one method: a name, some parameters, and
/// either a result or an error.
///
/// <para>
/// A node reports a failed call as HTTP 200 with an <c>error</c> member, so the status code says
/// almost nothing — "out of gas", "nonce too low" and "execution reverted" all arrive as successful
/// responses. <see cref="JsonRpcException" /> is what turns that back into something a caller can
/// fail on.
/// </para>
/// </summary>
/// <remarks>
/// It builds and owns its own <see cref="HttpClient" /> rather than being handed one. Taking it as a
/// parameter reads as more testable and is not: a caller that news one up and passes it in has
/// handed over an object nobody can prove it still owns, which is precisely what CA2000 objects to.
/// One node, one endpoint, one client, disposed here.
/// </remarks>
public sealed class JsonRpcClient(Uri endpoint, TimeSpan timeout) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = endpoint, Timeout = timeout };

    /// <summary>
    /// JSON-RPC wants a distinct id per request. Nothing here pipelines or batches, so it is only
    /// ever echoed back — but a node is entitled to key on it, and sending 1 forever is the kind of
    /// thing that works until something in front of the node starts caching.
    /// </summary>
    private int _nextId;

    private static readonly JsonSerializerOptions SERIALIZER = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Calls <paramref name="method" /> and returns its <c>result</c>.
    /// </summary>
    /// <exception cref="JsonRpcException">The node answered with an error member.</exception>
    public async Task<JsonElement> CallAsync(
        string method,
        IReadOnlyList<object?> parameters,
        CancellationToken ct
    )
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextId),
            method,
            @params = parameters,
        };

        using HttpResponseMessage response = await _http
            .PostAsJsonAsync(string.Empty, request, SERIALIZER, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        JsonElement body = await response
            .Content.ReadFromJsonAsync<JsonElement>(SERIALIZER, ct)
            .ConfigureAwait(false);

        if (body.TryGetProperty("error", out JsonElement error))
        {
            string message = error.TryGetProperty("message", out JsonElement text)
                ? text.GetString() ?? "unknown error"
                : error.ToString();
            int code = error.TryGetProperty("code", out JsonElement number) ? number.GetInt32() : 0;

            throw new JsonRpcException(method, code, message);
        }

        if (!body.TryGetProperty("result", out JsonElement result))
        {
            throw new JsonRpcException(
                method,
                0,
                "the answer carried neither a result nor an error"
            );
        }

        return result;
    }

    /// <summary>
    /// A hex-quantity answer as a number. The node writes these MINIMALLY — <c>0x0</c>, not
    /// <c>0x00</c>, and <c>0x2a</c> rather than a padded word — so they cannot be parsed as a
    /// fixed-width value and an odd number of digits is normal rather than a fault.
    /// </summary>
    public static ulong ParseQuantity(JsonElement value)
    {
        string text = value.GetString() ?? "0x0";

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length == 0
            ? 0
            : ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Bytes as the <c>0x</c> hex a node expects. Lower case, which anvil does not care about — it
    /// answers identically either way, checked — but which is the convention the JSON-RPC spec
    /// writes in, and there is no promise that the next node this points at is as forgiving.
    /// <c>Convert.ToHexString</c> produces upper case, so this is not the default.
    /// </summary>
    public static string ToHexData(ReadOnlySpan<byte> value) =>
        "0x" + Convert.ToHexString(value).ToLowerInvariant();

    /// <summary>A hex-data answer as bytes -- what <c>eth_call</c> returns.</summary>
    public static byte[] ParseData(JsonElement value)
    {
        string text = value.GetString() ?? string.Empty;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length == 0 ? [] : Convert.FromHexString(text);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// A node that answered, and said no. Carries the JSON-RPC code as well as the message because that
/// is what separates "your transaction is wrong" (-32000 and friends) from "I do not know that
/// method" (-32601), and the two call for opposite responses.
/// </summary>
public sealed class JsonRpcException(string method, int code, string message)
    : Exception($"{method} failed ({code}): {message}")
{
    public string Method { get; } = method;

    public int Code { get; } = code;
}

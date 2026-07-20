// Port of the Go datamuse client tests: verifies status transitions
// (Online / Error / Offline / Timeout) and response filtering against a
// local HTTP test server rather than the real Datamuse API.
using System.Net;
using System.Text;
using WordBombTool;
using Xunit;

namespace WordBombTool.Tests;

/// <summary>Minimal local HTTP server so tests don't depend on the real
/// Datamuse API being reachable. Each test binds an ephemeral port, serves
/// exactly one canned response (or refuses the connection), then stops.</summary>
internal sealed class FakeHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    public string BaseUrl { get; }

    public FakeHttpServer()
    {
        // Port 0 isn't supported by HttpListener directly; scan a small range
        // of high, unlikely-to-collide ports instead.
        var rng = new Random();
        Exception? last = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var port = 34000 + rng.Next(2000);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                BaseUrl = $"http://127.0.0.1:{port}/";
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new InvalidOperationException("could not bind a local test port", last);
    }

    /// <summary>Serves a single request with the given status code and JSON body,
    /// then returns.</summary>
    public void ServeOnce(HttpStatusCode status, string jsonBody)
    {
        var ctx = _listener.GetContext();
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(jsonBody);
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}

public class DatamuseClientTests
{
    [Fact]
    public void Suggestions_SuccessfulResponse_ReturnsWordsAndSetsOnlineStatus()
    {
        using var server = new FakeHttpServer();
        var client = new DatamuseClient();
        client.SetBaseUrl(server.BaseUrl);

        List<string>? result = null;
        var serverThread = new Thread(() => server.ServeOnce(
            HttpStatusCode.OK,
            "[{\"word\":\"cat\"},{\"word\":\"cattle\"},{\"word\":\"two words\"}]"));
        serverThread.Start();
        result = client.Suggestions("cat", "Starts With");
        serverThread.Join();

        // Multi-word results are filtered out; single-word results pass through.
        Assert.Equal(new[] { "cat", "cattle" }, result);
        Assert.Equal(AppConfig.StatusOnline, client.Status());
    }

    [Fact]
    public void Suggestions_ServerError_ReturnsEmptyAndSetsErrorStatus()
    {
        using var server = new FakeHttpServer();
        var client = new DatamuseClient();
        client.SetBaseUrl(server.BaseUrl);

        var serverThread = new Thread(() => server.ServeOnce(HttpStatusCode.InternalServerError, "{}"));
        serverThread.Start();
        var result = client.Suggestions("cat", "Starts With");
        serverThread.Join();

        Assert.Empty(result);
        Assert.Equal(AppConfig.StatusError, client.Status());
    }

    [Fact]
    public void Suggestions_ServerUnreachable_ReturnsEmptyAndSetsOfflineOrTimeoutStatus()
    {
        // Nothing is listening on this port. Depending on the OS network
        // stack, an unreachable local port either fails fast with a
        // connection-refused (HttpRequestException -> Offline) or simply
        // never responds within the client's short timeout window
        // (OperationCanceledException -> Timeout) -- both are the correct,
        // non-Online outcome for a server that can't be reached, so accept
        // either rather than pinning to one platform-dependent code path.
        var client = new DatamuseClient();
        client.SetBaseUrl("http://127.0.0.1:1/");

        var result = client.Suggestions("cat", "Starts With");

        Assert.Empty(result);
        Assert.True(
            client.Status() is AppConfig.StatusOffline or AppConfig.StatusTimeout,
            $"expected Offline or Timeout status, got '{client.Status()}'");
    }

    [Fact]
    public void Suggestions_EmptyLetters_ReturnsEmptyWithoutMakingARequest()
    {
        var client = new DatamuseClient();
        client.SetBaseUrl("http://127.0.0.1:1/"); // would fail if actually called
        var result = client.Suggestions("", "Starts With");
        Assert.Empty(result);
        // Status is untouched (still the default Online) since no request was made.
        Assert.Equal(AppConfig.StatusOnline, client.Status());
    }

    [Fact]
    public void Definitions_SuccessfulResponse_ReturnsDefsForFirstMatch()
    {
        using var server = new FakeHttpServer();
        var client = new DatamuseClient();
        client.SetBaseUrl(server.BaseUrl);

        var serverThread = new Thread(() => server.ServeOnce(
            HttpStatusCode.OK,
            "[{\"word\":\"cat\",\"defs\":[\"n\\tsmall domesticated feline\"]}]"));
        serverThread.Start();
        var result = client.Definitions("cat");
        serverThread.Join();

        Assert.Equal(new[] { "n\tsmall domesticated feline" }, result);
        Assert.Equal(AppConfig.StatusOnline, client.Status());
    }
}

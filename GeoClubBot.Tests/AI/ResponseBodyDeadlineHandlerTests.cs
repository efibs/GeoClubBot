using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Infrastructure.OutputAdapters.Http;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Pins the guard against a response that opens and never ends. On 2026-11-11 OpenRouter held a chat
/// completion open for 57 minutes — headers at once, then blank padding — and nothing that bounds a
/// request was still counting: Refit takes a response at its headers and reads the body afterwards.
/// Driven by a supplied clock, so nothing waits in real time.
/// </summary>
public sealed class ResponseBodyDeadlineHandlerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ABodyThatArrives_IsHandedOnWhole_AndReadableMoreThanOnce()
    {
        using var invoker = Invoker(new FakeTimeProvider(), _ => Respond(new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")));

        using var response = await invoker.SendAsync(Request(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"ok":true}""");
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"ok":true}""", "it was read to its end, so it is kept");
    }

    [Fact]
    public async Task ABodyThatNeverEnds_IsGivenUpOn_OnceTheDeadlineHasPassed()
    {
        var clock = new FakeTimeProvider();
        var body = new StalledContent();
        using var invoker = Invoker(clock, _ => Respond(body));

        var sending = invoker.SendAsync(Request(), CancellationToken.None);
        await body.Started;

        clock.Advance(Deadline - TimeSpan.FromSeconds(1));
        sending.IsCompleted.Should().BeFalse("the body still has a second");

        clock.Advance(TimeSpan.FromSeconds(1));

        var failure = (await FluentActions.Awaiting(() => sending).Should().ThrowAsync<ResponseBodyTimeoutException>()).Which;
        failure.Message.Should().Be("openrouter.ai began to answer and had not finished after 60 s.");
        body.Disposed.Should().BeTrue("nothing is left reading a body nobody waits for");
    }

    [Fact]
    public async Task TheDeadlineCountsFromTheHeaders_NotFromTheRequest()
    {
        // The wait for the headers is the pipeline's to bound, and can include queueing for the rate
        // limiter: a body that then arrives at once must not be refused for it.
        var clock = new FakeTimeProvider();
        using var invoker = Invoker(clock, _ =>
        {
            clock.Advance(Deadline * 2);
            return Respond(new WatchfulContent("late, and whole"));
        });

        using var response = await invoker.SendAsync(Request(), CancellationToken.None);

        (await response.Content.ReadAsStringAsync()).Should().Be("late, and whole");
    }

    [Fact]
    public async Task TheCallerGivingUp_StaysACancellation()
    {
        // The client's own overall timeout arrives as the caller's token too, and must stay what it was.
        var body = new StalledContent();
        using var invoker = Invoker(new FakeTimeProvider(), _ => Respond(body));
        using var caller = new CancellationTokenSource();

        var sending = invoker.SendAsync(Request(), caller.Token);
        await body.Started;
        await caller.CancelAsync();

        await FluentActions.Awaiting(() => sending).Should().ThrowAsync<OperationCanceledException>();
        body.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ABodyThatBreaksOff_BeforeTheDeadline_IsNotCalledATimeout()
    {
        using var invoker = Invoker(new FakeTimeProvider(), _ => Respond(new BrokenContent()));

        var failure = (await FluentActions.Awaiting(() => invoker.SendAsync(Request(), CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>()).Which;

        failure.Should().NotBeOfType<ResponseBodyTimeoutException>("the connection failed; nobody waited too long");
    }

    [Fact]
    public async Task AFailureToGetAnyResponse_PassesThrough()
    {
        var refused = new HttpRequestException("Connection refused");
        using var invoker = Invoker(new FakeTimeProvider(), _ => throw refused);

        (await FluentActions.Awaiting(() => invoker.SendAsync(Request(), CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>()).Which.Should().BeSameAs(refused);
    }

    [Fact]
    public async Task OverARealConnection_AResponsePaddedWithoutEnd_IsCutOff()
    {
        // The wire shape of that night, on a socket rather than a stub: status and headers at once,
        // then a chunk of padding every so often. It pins what a cancelled network read surfaces as,
        // which no in-memory body can. In real time, half a second of patience.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = PadWithoutEndAsync(listener);

        using var client = new HttpClient(
            new ResponseBodyDeadlineHandler(TimeSpan.FromMilliseconds(500)) { InnerHandler = new SocketsHttpHandler() });

        // As Refit asks: the response is handed over at its headers.
        var asking = () => client.GetAsync(new Uri($"http://127.0.0.1:{port}/"), HttpCompletionOption.ResponseHeadersRead);

        await asking.Should().ThrowAsync<ResponseBodyTimeoutException>();
        (await serving).Should().BeTrue("the connection was closed on the server, not left reading");
    }

    /// <summary>
    /// Answers one request with headers and endless padding. True when the client hung up; false when
    /// the server gave up first, long after the test's patience, so a missing guard fails the test
    /// instead of hanging the run.
    /// </summary>
    private static async Task<bool> PadWithoutEndAsync(TcpListener listener)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        var stream = connection.GetStream();

        var request = new StringBuilder();
        var buffer = new byte[2048];
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return false;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n"u8.ToArray());

        var givingUp = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        try
        {
            while (DateTime.UtcNow < givingUp)
            {
                await stream.WriteAsync("1\r\n\n\r\n"u8.ToArray());
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");

    private static HttpResponseMessage Respond(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    private static HttpMessageInvoker Invoker(TimeProvider clock, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new ResponseBodyDeadlineHandler(Deadline, clock) { InnerHandler = new StubHandler(respond) });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>A body as the provider sent it that night: padding, and then nothing, for as long as anyone reads.</summary>
    private sealed class StalledContent : HttpContent
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync("\n\n\n"u8.ToArray(), cancellationToken);
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>A body that refuses to be read once reading has been cancelled, as a network stream does.</summary>
    private sealed class WatchfulContent(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("The response ended prematurely.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

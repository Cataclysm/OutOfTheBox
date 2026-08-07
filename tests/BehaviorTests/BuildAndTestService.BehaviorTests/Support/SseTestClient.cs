using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BuildAndTestService.BehaviorTests.Support;

/// <summary>One parsed Server-Sent Event: its <c>event:</c> name and <c>data:</c> payload.</summary>
public sealed record SseEvent(string Name, string Data);

/// <summary>The result of posting a command-execution request and reading its SSE response to completion.</summary>
public sealed record SseRunResult(HttpResponseMessage Response, IReadOnlyList<SseEvent> Events);

/// <summary>Minimal test-only SSE client for driving <c>POST /run</c> and parsing its event stream.</summary>
public static class SseTestClient
{
    /// <summary>
    /// Posts <paramref name="body"/> to <paramref name="requestUri"/> and reads every SSE event
    /// from the response until the stream ends. <paramref name="streaming"/> controls whether the
    /// response is read incrementally (<see cref="HttpCompletionOption.ResponseHeadersRead"/>) or
    /// fully buffered before parsing - both must yield the same events, per the requirement that
    /// streaming is a delivery optimization, not a correctness requirement.
    /// </summary>
    public static async Task<SseRunResult> PostAndReadAllEventsAsync(
        HttpClient client,
        string requestUri,
        object body,
        string? bearerToken,
        bool streaming,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body),
        };

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        var completionOption = streaming
            ? HttpCompletionOption.ResponseHeadersRead
            : HttpCompletionOption.ResponseContentRead;

        var response = await client.SendAsync(request, completionOption, cancellationToken);
        var events = new List<SseEvent>();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? currentEventName = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                events.Add(new SseEvent(currentEventName ?? string.Empty, line["data: ".Length..]));
            }
        }

        return new SseRunResult(response, events);
    }
}

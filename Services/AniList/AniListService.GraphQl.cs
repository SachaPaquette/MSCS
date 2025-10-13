using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class AniListService
    {
        private async Task<JsonDocument> SendGraphQlRequestAsync(string query, object variables, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new GraphQlRequest(query, variables));
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(GraphQlEndpoint, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array &&
                errorsElement.GetArrayLength() > 0)
            {
                var firstError = errorsElement[0];
                var message = firstError.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "AniList request failed.";
                throw new InvalidOperationException(message ?? "AniList request failed.");
            }

            return document;
        }
        private async Task<JsonDocument?> TrySendGraphQlRequestAsync(string query, object variables, CancellationToken cancellationToken)
        {
            try
            {
                return await SendGraphQlRequestAsync(query, variables, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"AniList request timed out: {ex.Message}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"AniList request failed: {ex.Message}");
                return null;
            }
        }
        private sealed record GraphQlRequest(string query, object variables);

    }
}
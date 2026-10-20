using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Recovery;

/// <summary>Gets a new set of codes to your friend without ever showing them on this PC.</summary>
public interface IRecoveryCodeDelivery
{
    /// <summary>
    /// Sends the codes. Throws unless the provider confirmed it accepted the message: a
    /// <see cref="DeliveryNotSentException"/> when nothing went out, anything else when it might have.
    /// </summary>
    Task DeliverAsync(IReadOnlyList<string> formattedCodes, CancellationToken cancellationToken);
}

/// <summary>The codes certainly didn't reach anyone, so they can be thrown away.</summary>
public sealed class DeliveryNotSentException(string message) : Exception(message);

public sealed class RecoveryDeliveryOptions
{
    public const string SectionName = "RecoveryDelivery";

    /// <summary>"None" or "Resend".</summary>
    public string Provider { get; set; } = "None";

    /// <summary>A send-only API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Sender address on a domain verified with the provider.</summary>
    public string? From { get; set; }

    /// <summary>Your friend's email address.</summary>
    public string? To { get; set; }
}

public sealed class NotConfiguredDelivery : IRecoveryCodeDelivery
{
    public Task DeliverAsync(IReadOnlyList<string> formattedCodes, CancellationToken cancellationToken) =>
        Task.FromException(new DeliveryNotSentException(
            "Recovery code delivery isn't set up. Add a RecoveryDelivery section to settings.json."));
}

/// <summary>Sends the codes through the Resend email API (https://resend.com/docs/api-reference/emails/send-email).</summary>
public sealed class ResendDelivery(IHttpClientFactory httpClientFactory, IOptions<RecoveryDeliveryOptions> options) : IRecoveryCodeDelivery
{
    private static readonly Uri Endpoint = new("https://api.resend.com/emails");

    public async Task DeliverAsync(IReadOnlyList<string> formattedCodes, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To))
            throw new DeliveryNotSentException("RecoveryDelivery needs ApiKey, From and To to send through Resend.");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                from = settings.From,
                to = new[] { settings.To },
                subject = $"Be Productive recovery codes ({Environment.MachineName})",
                text = RecoveryEmail.Body(formattedCodes),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new DeliveryNotSentException($"Resend rejected the email ({(int)response.StatusCode}): {body}");
        }
    }
}

internal static class RecoveryEmail
{
    public static string Body(IReadOnlyList<string> formattedCodes)
    {
        var body = new StringBuilder();
        body.AppendLine("Hi,");
        body.AppendLine();
        body.AppendLine($"These are the new recovery codes for Be Productive on {Environment.MachineName}.");
        body.AppendLine("Each one ends a focus lock once. Please keep them somewhere safe and only give me one if I really need it.");
        body.AppendLine("Any older codes you have stopped working when this email was sent.");
        body.AppendLine();
        for (var i = 0; i < formattedCodes.Count; i++)
            body.AppendLine($"{i + 1,2}. {formattedCodes[i]}");
        return body.ToString();
    }
}

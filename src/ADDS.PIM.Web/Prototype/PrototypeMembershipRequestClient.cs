using System.Net;
using System.Net.Http.Json;
using ADDS.PIM.Contracts.Prototype;

namespace ADDS.PIM.Web.Prototype;

public sealed class PrototypeMembershipRequestClient(HttpClient httpClient) : IPrototypeMembershipRequestClient
{
    public async Task<PrototypeApiSubmissionResult> SubmitAsync(
        PrototypeGroup group,
        PrototypeRequestForm form,
        CancellationToken cancellationToken)
    {
        var request = new PrototypeMembershipRequest(
            Guid.NewGuid(),
            group.Id,
            form.RequestedTtlSeconds,
            form.Reason.Trim(),
            form.TicketReference);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/v1/prototype/membership-requests",
                request,
                cancellationToken);

            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                return new PrototypeApiSubmissionResult(
                    false,
                    null,
                    null,
                    "Der Prototyp-Request wurde von der API nicht angenommen.");
            }

            return new PrototypeApiSubmissionResult(
                true,
                request.RequestId,
                null,
                "Die API hat den Demo-Request angenommen. Es wurde nichts gespeichert oder ausgeführt.");
        }
        catch (HttpRequestException)
        {
            return new PrototypeApiSubmissionResult(
                false,
                null,
                null,
                "Die Prototyp-API ist nicht erreichbar. Starten Sie API und Web-App gemeinsam.");
        }
    }
}

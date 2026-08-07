using System.Text.RegularExpressions;

namespace ADDS.PIM.Application.Authorization;

public sealed record TicketReferencePattern(Guid PatternId, string Label, string Expression);
public sealed record TicketReferencePolicy(bool RequiresTicket, IReadOnlyList<TicketReferencePattern> ActivePatterns);

public enum TicketReferenceValidationResult { Valid, Missing, DoesNotMatch, InvalidConfiguration }

public interface ITicketReferencePolicySource
{
    Task<TicketReferencePolicy?> GetCurrentPolicyAsync(Guid targetGroupId, CancellationToken cancellationToken);
}

public sealed class TicketReferenceValidator(ITicketReferencePolicySource policySource)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public async Task<TicketReferenceValidationResult> ValidateAsync(Guid targetGroupId, string? ticketReference, CancellationToken cancellationToken)
    {
        var policy = await policySource.GetCurrentPolicyAsync(targetGroupId, cancellationToken);
        if (policy is null || !policy.RequiresTicket) return TicketReferenceValidationResult.Valid;
        if (string.IsNullOrWhiteSpace(ticketReference)) return TicketReferenceValidationResult.Missing;
        if (ticketReference.Trim().Length > 256 || policy.ActivePatterns.Count == 0) return TicketReferenceValidationResult.InvalidConfiguration;

        var matched = false;
        foreach (var pattern in policy.ActivePatterns)
        {
            try
            {
                var regex = TicketReferencePatternFormat.Create(pattern.Expression);
                matched |= regex.IsMatch(ticketReference.Trim());
            }
            catch (ArgumentException) { return TicketReferenceValidationResult.InvalidConfiguration; }
            catch (RegexMatchTimeoutException) { return TicketReferenceValidationResult.InvalidConfiguration; }
        }
        return matched ? TicketReferenceValidationResult.Valid : TicketReferenceValidationResult.DoesNotMatch;
    }
}

public static class TicketReferencePatternFormat
{
    public static Regex Create(string expression) => new(expression, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
}

using Mist.Application.Contracts;

namespace Mist.Application.Assistance;
public sealed class DisabledProvider : IAssistanceProvider
{
    public ValueTask<AssistanceResult> RequestAsync(AssistanceRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AssistanceResult(request.RequestId, "provider_disabled", null));
    }
}

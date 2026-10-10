using System.Security.Cryptography;
using System.Text;
using Mist.Application.Contracts;
using Mist.Core.Contracts;

namespace Mist.Application.Drafts;

public sealed class DraftEditor(IDraftStore store, SaveDraft initial)
{
    private readonly SemaphoreSlim _saves = new(1, 1);
    private SaveDraft _request = initial;
    private int _generation;
    public string Text { get; private set; } = initial.Text;
    public bool IsSaved { get; private set; }
    public string? LastError { get; private set; }
    public DraftSaved? LastAck { get; private set; }
    public void Edit(string text) { Text = text; _generation++; IsSaved = false; }
    public async ValueTask<DraftSaved> SaveAsync(CancellationToken cancellationToken = default)
    {
        int generation = _generation; string body = Text;
        await _saves.WaitAsync(cancellationToken);
        try
        {
            var ack = await store.SaveAsync(_request with { Text = body }, cancellationToken);
            _request = _request with { Draft = ack.Draft, ExpectedHeadRevision = ack.Revision };
            LastAck = ack; LastError = null;
            string currentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UnicodeOffsets.NormalizeNewlines(Text)))).ToLowerInvariant();
            IsSaved = generation == _generation && currentHash == ack.Utf8Sha256;
            return ack;
        }
        catch (Exception error) { LastError = error.Message; IsSaved = false; throw; }
        finally { _saves.Release(); }
    }
}

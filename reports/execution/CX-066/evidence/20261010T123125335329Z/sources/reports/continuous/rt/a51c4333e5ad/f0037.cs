using System.Collections.Immutable;
using Mist.Application.Learning;
using Mist.Core.Contracts;
using Mist.Core.Learning;

namespace Mist.Infrastructure.Storage;

public sealed partial class ProfileStore : IRegionalLearningRepository
{
    public ValueTask<RegionalLearningSnapshot> RegionalLearningAsync()=>_writer.Enqueue(()=>
    {
        using var control=SqliteSupport.Open(ControlPath,readOnly:true);var privacy=Control(control);
        using var db=SqliteSupport.Open(_mainPath,readOnly:true);
        var events=Rows(db,null,"SELECT payload_json FROM observations WHERE profile_epoch=$e AND support_state='regional_lexical_review' ORDER BY rowid",
            r=>StrictJson.Deserialize<RegionalReviewEvent>(r.GetString(0)),("$e",privacy.Epoch));
        var families=Rows(db,null,"SELECT DISTINCT family_id FROM observations WHERE profile_epoch=$e AND family_id<>''",r=>r.GetString(0),("$e",privacy.Epoch));
        var session=Session;return new RegionalLearningSnapshot(new(session.Profile,session.Branch,session.Load,privacy.Epoch,session.Sequence),events,families);
    });
    public ValueTask<RegionalReviewEvent> AppendRegionalReviewAsync(LearningIdentity expected,RegionalReviewEvent observation)=>_writer.Enqueue(()=>
    {
        using var control=SqliteSupport.Open(ControlPath,readOnly:true);var privacy=Control(control);var session=Session;
        if(expected!=new LearningIdentity(session.Profile,session.Branch,session.Load,privacy.Epoch,session.Sequence))
            throw new InvalidOperationException("Learning context changed or data was erased; stale observations cannot reappear.");
        if(string.IsNullOrWhiteSpace(observation.AttemptId)||string.IsNullOrWhiteSpace(observation.SenseId)||observation.Due<observation.At||
            observation.IntervalDays is <1 or >30||observation.Response.Length>10000)throw new InvalidDataException("Invalid local review observation.");
        using var db=SqliteSupport.Open(_mainPath);using var tx=db.BeginTransaction(deferred:false);
        string attempt="regional-lexical:"+observation.AttemptId,json=StrictJson.Serialize(observation);
        string? previous=SqliteSupport.Text(db,tx,"SELECT payload_json FROM observations WHERE profile_epoch=$e AND attempt_id=$a",("$e",privacy.Epoch),("$a",attempt));
        if(previous is not null)
        {
            var existing=StrictJson.Deserialize<RegionalReviewEvent>(previous);
            if(existing!=observation)throw new InvalidOperationException("Duplicate learning attempt has a different payload.");
            tx.Commit();return existing;
        }
        SqliteSupport.Exec(db,tx,"INSERT INTO observations(observation_id,profile_epoch,source_branch_id,family_id,attempt_id,support_state,payload_json) VALUES($i,$e,$b,$f,$a,$s,$j)",("$i",observation.Id),("$e",privacy.Epoch),("$b",session.Branch.Value.ToString()),
            ("$f",observation.FamilyId),("$a",attempt),("$s","regional_lexical_review"),("$j",json));
        tx.Commit();return observation;
    });
}

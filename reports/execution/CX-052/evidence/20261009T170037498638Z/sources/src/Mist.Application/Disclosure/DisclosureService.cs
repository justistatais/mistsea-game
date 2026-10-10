using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Contracts;

namespace Mist.Application.Disclosure;

public sealed record ScopedMaterial(string Id,string Version,string Subject,string SourceRoot,ImmutableDictionary<string,string> Fields);
public sealed record DisclosureGrant(string Id,string Subject,string Material,string Version,ImmutableArray<string> Fields,ImmutableArray<string> Recipients,string Purpose,string WorkSlot,bool Active,string AuthorityReceipt);
public sealed record DisclosureAuthority(string Subject,string GrantId,string Material,string Version,ImmutableArray<string> Fields,ImmutableArray<string> Recipients,string Purpose,string WorkSlot);
public sealed record DisclosureSelection(string WorkSlot,string Material,string Version,ImmutableArray<string> Fields,string Recipient,string Purpose,string? FreeBody=null);
public sealed record DisclosurePreview(Guid Token,string Text,ImmutableArray<string> Fields,bool Ready,string? Issue=null);
public sealed class DisclosureService(IAdventureRepository repository)
{
    private readonly Dictionary<Guid,CommitPlanCommand> _previews=new();
    public async ValueTask<DisclosurePreview> PreviewAsync(DisclosureSelection selection)
    {
        var s=await repository.SnapshotAsync();var material=BranchOperations.Read<ScopedMaterial>(s,"scoped-material:"+selection.Material);
        if(!string.IsNullOrWhiteSpace(selection.FreeBody))return new(Guid.Empty,"",[],false,"任意正文尚不能可靠检查受限信息；请使用受控字段文本或先澄清范围。");
        if(material is null||material.Version!=selection.Version||selection.Fields.Length==0||selection.Fields.Distinct().Count()!=selection.Fields.Length||selection.Fields.Any(f=>!material.Fields.ContainsKey(f)))return new(Guid.Empty,"",[],false,"资料版本或字段不可用。");
        var grants=s.Entities.Where(e=>e.Kind=="disclosure_grant").Select(e=>StrictJson.Deserialize<DisclosureGrant>(e.Json)).Where(g=>g.Active&&g.Subject==material.Subject&&g.Material==material.Id&&g.Version==material.Version&&g.Recipients.Contains(selection.Recipient)&&g.Purpose==selection.Purpose&&selection.Fields.All(g.Fields.Contains)).ToArray();
        var grant=grants.FirstOrDefault(g=>g.WorkSlot==selection.WorkSlot);if(grant is null)return new(Guid.Empty,"",[],false,"当前本人许可未覆盖这些字段、收件人、用途、工作槽与版本。");
        if(BranchOperations.Entity(s,"grant:"+grant.Id) is not{Kind:"disclosure_grant"})throw new InvalidOperationException("许可身份未登记。");
        var authority=BranchOperations.Evidence(s,grant.AuthorityReceipt,"disclosure_authority_verified");var check=StrictJson.Deserialize<DisclosureAuthority>(authority.ResultJson);
        if(check.Subject!=material.Subject||check.GrantId!=grant.Id||check.Material!=grant.Material||check.Version!=grant.Version||check.Purpose!=grant.Purpose||check.WorkSlot!=grant.WorkSlot||!check.Fields.ToHashSet().SetEquals(grant.Fields)||!check.Recipients.ToHashSet().SetEquals(grant.Recipients))throw new InvalidOperationException("许可与该当事人的实际字段授权回执不符。");
        string text=string.Join("\n",selection.Fields.Select(f=>f+": "+material.Fields[f]));var request=new CommandId(Guid.NewGuid());string semantic="disclosure:"+selection.WorkSlot;
        if(string.IsNullOrWhiteSpace(selection.WorkSlot))throw new InvalidOperationException("投递需要稳定工作槽。");
        var previous=s.Receipts.SingleOrDefault(r=>r.SemanticKey==semantic);if(previous is not null)
        {
            using var prior=System.Text.Json.JsonDocument.Parse(previous.ResultJson);var j=prior.RootElement;
            if(j.GetProperty("material").GetString()!=selection.Material||j.GetProperty("version").GetString()!=selection.Version||j.GetProperty("recipient").GetString()!=selection.Recipient||j.GetProperty("purpose").GetString()!=selection.Purpose||!j.GetProperty("fields").EnumerateArray().Select(f=>f.GetString()).ToHashSet().SetEquals(selection.Fields))throw new InvalidOperationException("此工作槽已经按原范围投递；请查看原送达事实。");
        }
        var result=new{selection.WorkSlot,selection.Material,selection.Version,selection.Fields,selection.Recipient,selection.Purpose,material.SourceRoot,text_sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()};
        var changes=ImmutableArray.Create(BranchOperations.Change(s,"delivery:"+selection.WorkSlot,"delivery",result));Guid token=Guid.NewGuid();
        _previews[token]=BranchOperations.Plan(repository,s,semantic,"controlled_delivery",changes,["scoped-material:"+material.Id,"grant:"+grant.Id],result,request:request);return new(token,text,selection.Fields,true);
    }
    public ValueTask<CommandResult> ConfirmAsync(Guid token)=>_previews.TryGetValue(token,out var command)?repository.ExecuteAsync(command):ValueTask.FromResult(new CommandResult(CommitStatus.Rejected,null,[new("preview_closed","","发送预览已关闭，请重新检查。") ]));
    public void Close(Guid token)=>_previews.Remove(token);
    public async ValueTask<CommandResult> RevokeAsync(string grantId,string actualRevocationReceipt)
    {
        var s=await repository.SnapshotAsync();var old=BranchOperations.Read<DisclosureGrant>(s,"grant:"+grantId)??throw new InvalidOperationException("许可未登记。");var receipt=BranchOperations.Evidence(s,actualRevocationReceipt,"disclosure_revocation_verified");using var d=System.Text.Json.JsonDocument.Parse(receipt.ResultJson);
        if(d.RootElement.GetProperty("subject").GetString()!=old.Subject||d.RootElement.GetProperty("grant_id").GetString()!=grantId)throw new InvalidOperationException("撤回须来自该当事人的许可版本。");
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"grant:"+grantId+":revoke:"+actualRevocationReceipt,"revoke_future_disclosure",[BranchOperations.Change(s,"grant:"+grantId,"disclosure_grant",old with{Active=false})],["grant:"+grantId],new{grant_id=grantId,prior_deliveries_preserved=true}));
    }
}

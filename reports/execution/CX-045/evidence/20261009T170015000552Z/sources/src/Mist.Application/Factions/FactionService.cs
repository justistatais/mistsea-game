using System.Collections.Immutable;
using System.Text.Json;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Storage;
using Mist.Core.Content;
using Mist.Core.Contracts;

namespace Mist.Application.Factions;

public enum MandateRole { Operator,Representative }
public sealed record RegionalAppointment(string Region,MandateRole Role,string Instance,string Holder,string Scope,string ProtocolVersion,string SourceReceipt);
public sealed record AppointmentHistory(RegionalAppointment Current,ImmutableArray<RegionalAppointment> Previous);
public sealed record AgreementSignature(string Region,MandateRole Role,string AppointmentInstance,string Holder,string ProtocolVersion,string SourceReceipt);
public sealed record RegionalAgreement(string Region,string ProtocolVersion,AgreementSignature? Operator,AgreementSignature? Representative,ImmutableArray<AgreementSignature> History);
public sealed record CooperationView(string Scope,string Faction,string Result,ImmutableArray<string> Missing,bool Ready);
public sealed class FactionService(IAdventureRepository repository,RuntimeCatalog catalog)
{
    private static string AppointmentId(string region,MandateRole role)=>"mandate:"+region+":"+role;
    private JsonElement Mandate(string region)
    {
        using var doc=JsonDocument.Parse(catalog.Definitions["npc_regional_mandates"][0].PayloadJson);return doc.RootElement.EnumerateArray().Single(j=>j.GetProperty("region").GetString()==region).Clone();
    }
    public async ValueTask<ImmutableArray<CooperationView>> CooperationAsync()
    {
        var s=await repository.SnapshotAsync();using var doc=JsonDocument.Parse(catalog.Definitions["npc_cooperation_scopes"][0].PayloadJson);
        return doc.RootElement.EnumerateArray().Select(j=>
        {
            var missing=j.GetProperty("required_receipts").EnumerateArray().Select(x=>x.GetString()!).Where(k=>!BranchOperations.Receipt(s,k)).Concat(j.GetProperty("required_capacity").EnumerateArray().Select(x=>x.GetString()!).Where(k=>s.Balances.GetValueOrDefault("capacity:"+k)<=0)).ToImmutableArray();
            return new CooperationView(j.GetProperty("id").GetString()!,j.GetProperty("faction").GetString()!,j.GetProperty("result").GetString()!,missing,missing.Length==0);
        }).ToImmutableArray();
    }
    public async ValueTask<CommandResult> RegisterAppointmentAsync(string authorityReceipt)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var receipt=BranchOperations.Evidence(s,authorityReceipt,"regional_appointment_verified");var record=StrictJson.Deserialize<RegionalAppointment>(receipt.ResultJson);var rule=Mandate(record.Region);
        string scope=rule.GetProperty(record.Role==MandateRole.Operator?"operator_scope":"representative_scope").GetString()!;
        if(record.Scope!=scope||record.ProtocolVersion!=rule.GetProperty("protocol_version").GetString()||string.IsNullOrWhiteSpace(record.Holder)||string.IsNullOrWhiteSpace(record.Instance))throw new InvalidOperationException("地区任职范围或条款版本不符。");
        var all=s.Entities.Where(e=>e.Kind=="regional_appointment").Select(e=>StrictJson.Deserialize<AppointmentHistory>(e.Json)).ToArray();
        if(all.Any(a=>a.Current.Instance==record.Instance&&(a.Current.Region!=record.Region||a.Current.Role!=record.Role)))throw new InvalidOperationException("同一任职实例不能复制为另一地区或另一职能。");
        string id=AppointmentId(record.Region,record.Role);var old=BranchOperations.Read<AppointmentHistory>(s,id);var next=new AppointmentHistory(record with{SourceReceipt=authorityReceipt},old is null?[]:old.Previous.Add(old.Current));
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"appointment:"+record.Instance,"register_regional_appointment",[BranchOperations.Change(s,id,"regional_appointment",next)],s.Entities.Where(e=>e.Kind=="regional_appointment").Select(e=>e.Id).ToArray(),new{authority_receipt=authorityReceipt}));
    }
    public async ValueTask<CommandResult> AcceptSignatureAsync(string signatureReceipt)
    {
        var s=await repository.SnapshotAsync();BranchOperations.Safe(s);var receipt=BranchOperations.Evidence(s,signatureReceipt,"regional_signature_verified");var signature=StrictJson.Deserialize<AgreementSignature>(receipt.ResultJson);var rule=Mandate(signature.Region);
        string id=AppointmentId(signature.Region,signature.Role);var appointment=BranchOperations.Read<AppointmentHistory>(s,id)?.Current??throw new InvalidOperationException("该地区职能尚未得到正式任职。");
        if(signature.AppointmentInstance!=appointment.Instance||signature.Holder!=appointment.Holder||signature.ProtocolVersion!=appointment.ProtocolVersion||signature.ProtocolVersion!=rule.GetProperty("protocol_version").GetString())throw new InvalidOperationException("签名不属于当前任职实例和条款版本。");
        string agreementId="agreement:"+signature.Region;var old=BranchOperations.Read<RegionalAgreement>(s,agreementId)??new(signature.Region,signature.ProtocolVersion,null,null,[]);var actual=signature with{SourceReceipt=signatureReceipt};
        if((signature.Role==MandateRole.Operator?old.Representative:old.Operator)?.AppointmentInstance==signature.AppointmentInstance)throw new InvalidOperationException("操作与代表需要两个不同任职实例。");
        var next=old with{Operator=signature.Role==MandateRole.Operator?actual:old.Operator,Representative=signature.Role==MandateRole.Representative?actual:old.Representative,History=old.History.Add(actual)};
        return await repository.ExecuteAsync(BranchOperations.Plan(repository,s,"signature:"+signature.Region+":"+signature.Role+":"+signature.ProtocolVersion+":"+signature.AppointmentInstance,"accept_regional_signature",[BranchOperations.Change(s,agreementId,"regional_agreement",next)],[id,agreementId],new{signature_receipt=signatureReceipt}));
    }
    public async ValueTask<ImmutableArray<RegionalAgreement>> AgreementsAsync()
    {
        var s=await repository.SnapshotAsync();var result=ImmutableArray.CreateBuilder<RegionalAgreement>();foreach(string region in new[]{"R1","R2","R3","R4","R5","R6"})
        {
            var rule=Mandate(region);var a=BranchOperations.Read<RegionalAgreement>(s,"agreement:"+region)??new(region,rule.GetProperty("protocol_version").GetString()!,null,null,[]);
            bool Current(AgreementSignature? signature)=>signature is not null&&BranchOperations.Read<AppointmentHistory>(s,AppointmentId(region,signature.Role))?.Current is {} post&&post.Instance==signature.AppointmentInstance&&post.ProtocolVersion==signature.ProtocolVersion;
            result.Add(a with{Operator=Current(a.Operator)?a.Operator:null,Representative=Current(a.Representative)?a.Representative:null});
        }return result.ToImmutable();
    }
}

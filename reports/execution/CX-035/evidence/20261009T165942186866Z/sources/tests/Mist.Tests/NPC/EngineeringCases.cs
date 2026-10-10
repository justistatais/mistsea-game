using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Factions;
using Mist.Application.WorkOrders;
using Mist.Core.Contracts;
using Mist.Core.Engineering;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.NPC;

public static class EngineeringCases
{
    private static readonly Mist.Core.Content.RuntimeCatalog Catalog=Q102.Q102Cases.Catalog();
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"engineering-"+name+"-"+Guid.NewGuid()),Catalog.ReleaseId);
    private static void Success(CommandResult r)=>Require(r.Status is CommitStatus.Committed or CommitStatus.AlreadyCommitted,StrictJson.Serialize(r));
    private static void Procurement(ProfileStore s,WorkOrderService service,string source)
    {
        var def=service.Rules.Sources[source];for(int step=1;step<=def.RequiredSteps;step++){var r=Fixture(s,"fixture:"+source+":step:"+step,"engineering_procurement_step_verified",new ProcurementStep(source,step,def.Region,"explicit rule fixture operation"));Success(Done(service.ConsumeProcurementStepAsync(r.Receipt!.Id.Value.ToString())));}
    }
    private static void Prepare(ProfileStore s,WorkOrderService service,string region="R1")
    {
        Fixture(s,"Q-404:resolution","explicit_engineering_fixture",new{scope="preparation window fixture"});var safety=Fixture(s,"fixture:safety:"+region,"tower_safety_verified",new TowerSafety("TWR-"+region,true,"early tower fixture work"));Success(Done(service.RegisterSafetyAsync(safety.Receipt!.Id.Value.ToString())));
        var authority=new WorkTeamAuthority("crew-"+region,region,true,["operator-"+region+"-1","operator-"+region+"-2"],true);var r=Fixture(s,"fixture:team:"+region,"engineering_team_verified",authority);var team=new QualifiedWorkTeam(authority.Id,region,true,authority.Operators,true,r.Receipt!.Id.Value.ToString());Fixture(s,"fixture:team-record:"+region,"explicit_engineering_fixture",new{},[Entity(s,"engineering-team:"+team.Id,"engineering_team",team)]);
    }
    private static void Finish(ProfileStore s,WorkOrderService service,WorkType type,string region="R1")
    {
        Success(Done(service.ReserveAsync("TWR-"+region,type,"technical-v1","crew-"+region)));for(int n=0;n<4;n++)Success(Done(service.NextStepAsync("TWR-"+region,type)));
    }
    public static void Register()
    {
        Add("CX-058","Five scopes depend on actual receipts/capacity, not policy stance or gifts",()=>
        {
            using var s=Store("scopes");var f=new FactionService(s,Catalog);Require(Done(f.CooperationAsync()).Length==5&&Done(f.CooperationAsync()).All(v=>!v.Ready));Fixture(s,"policy:oppose","explicit_engineering_fixture",new{gifts=100,stance="oppose"});Require(Done(f.CooperationAsync()).All(v=>!v.Ready));Fixture(s,"archive_request_registered","explicit_engineering_fixture",new{});Fixture(s,"source_scope_verified","explicit_engineering_fixture",new{});Require(Done(f.CooperationAsync()).Single(v=>v.Scope=="SCOPE-ARCHIVE").Ready&&Done(f.CooperationAsync()).Single(v=>v.Scope=="SCOPE-TRANSPORT").Missing.Length==3);
        });
        Add("CX-058","Six regions require distinct operator/representative appointments and cannot copy one signature",()=>
        {
            using var s=Store("agreements");var f=new FactionService(s,Catalog);const string version="protocol-d3-v1";
            var a=new RegionalAppointment("R1",MandateRole.Operator,"r1-op-original","r1-person-op","operate:R1",version,"fixture authority");var ar=Fixture(s,"appointment-original","regional_appointment_verified",a);Success(Done(f.RegisterAppointmentAsync(ar.Receipt!.Id.Value.ToString())));
            var sig=new AgreementSignature("R1",MandateRole.Operator,a.Instance,a.Holder,version,"fixture signature");var sr=Fixture(s,"sig-original","regional_signature_verified",sig);Success(Done(f.AcceptSignatureAsync(sr.Receipt!.Id.Value.ToString())));Require(Done(f.AgreementsAsync()).Single(r=>r.Region=="R1").Representative is null&&Done(f.AgreementsAsync()).Count(r=>r.Operator is not null)==1);
            var copy=Fixture(s,"bad-copy","regional_appointment_verified",a with{Region="R2",Scope="operate:R2"});Throws<InvalidOperationException>(()=>Done(f.RegisterAppointmentAsync(copy.Receipt!.Id.Value.ToString())));
            var rep=a with{Role=MandateRole.Representative,Instance="r1-rep-original",Holder="r1-person-rep",Scope="represent:R1"};var rr=Fixture(s,"appointment-rep","regional_appointment_verified",rep);Success(Done(f.RegisterAppointmentAsync(rr.Receipt!.Id.Value.ToString())));var rs=Fixture(s,"sig-rep","regional_signature_verified",sig with{Role=rep.Role,AppointmentInstance=rep.Instance,Holder=rep.Holder});Success(Done(f.AcceptSignatureAsync(rs.Receipt!.Id.Value.ToString())));
            var replacement=a with{Instance="r1-op-replacement",Holder="r1-person-new"};var nr=Fixture(s,"appointment-new","regional_appointment_verified",replacement);Success(Done(f.RegisterAppointmentAsync(nr.Receipt!.Id.Value.ToString())));var view=Done(f.AgreementsAsync()).Single(r=>r.Region=="R1");Require(view.Operator is null&&view.Representative is not null&&view.History.Length==2);
            Throws<InvalidOperationException>(()=>Done(f.AcceptSignatureAsync(sr.Receipt!.Id.Value.ToString())));
        });
        Add("CX-059","Finite contracts deliver only after prescribed steps; repeat receipt never credits twice",()=>
        {
            using var s=Store("supplies");var service=new WorkOrderService(s,Catalog);Require(Done(s.SnapshotAsync()).Balances.GetValueOrDefault(EngineeringRules.Available("controller"))==0);Procurement(s,service,"SUP-CTRL");var after=Done(s.SnapshotAsync());Require(after.Balances[EngineeringRules.Available("controller")]==6&&after.Balances.GetValueOrDefault("marks")==0);var last=after.Receipts.Single(r=>r.SemanticKey=="fixture:SUP-CTRL:step:4");Success(Done(service.ConsumeProcurementStepAsync(last.ReceiptId)));Require(Done(s.SnapshotAsync()).Balances[EngineeringRules.Available("controller")]==6&&service.Rules.Sources.Values.Where(v=>v.Resource=="coupler").Sum(v=>v.Quantity)==12&&service.Rules.Sources.Values.Where(v=>v.Resource=="transition_crate").Sum(v=>v.Quantity)==18);
        });
        Add("CX-059","Missing second material rejects the whole reservation, no crew or first resource is taken",()=>
        {
            using var s=Store("missing-material");var service=new WorkOrderService(s,Catalog);Prepare(s,service);Procurement(s,service,"SUP-CTRL");var p=Fixture(s,"technical","technical_package_verified",new{version="technical-v1"});Fixture(s,"technical-record","explicit_engineering_fixture",new{},[Entity(s,"technical-package","technical_package",new TechnicalPackage("technical-v1",true,p.Receipt!.Id.Value.ToString()))]);long seq=Done(s.SnapshotAsync()).Sequence;Throws<InvalidOperationException>(()=>Done(service.ReserveAsync("TWR-R1",WorkType.EnhancedInstall,"technical-v1","crew-R1")));var after=Done(s.SnapshotAsync());Require(after.Sequence==seq&&after.Balances[EngineeringRules.Available("controller")]==6&&after.Entities.All(e=>e.Kind!="work_post"));
        });
        Add("CX-059","Reserved cancellation returns only unused quantities; explicit steps install once and cannot refund",()=>
        {
            using var s=Store("reserve-install");var service=new WorkOrderService(s,Catalog);Prepare(s,service);foreach(string source in service.Rules.Sources.Keys.Where(k=>k.StartsWith("BASE-TWR-R1-")))Procurement(s,service,source);
            Success(Done(service.ReserveAsync("TWR-R1",WorkType.BaselineCheck,"baseline-v1","crew-R1")));Throws<InvalidOperationException>(()=>Done(service.ReserveAsync("TWR-R1",WorkType.OperatorAssignment,"baseline-v1","crew-R1")));Success(Done(service.CancelAsync("TWR-R1",WorkType.BaselineCheck)));Require(Done(s.SnapshotAsync()).Balances[EngineeringRules.Available("base_provision")]==2);
            Success(Done(service.ReserveAsync("TWR-R1",WorkType.BaselineCheck,"baseline-v1","crew-R1")));for(int n=0;n<3;n++)Success(Done(service.NextStepAsync("TWR-R1",WorkType.BaselineCheck)));Throws<InvalidOperationException>(()=>Done(service.CancelAsync("TWR-R1",WorkType.BaselineCheck)));Success(Done(service.NextStepAsync("TWR-R1",WorkType.BaselineCheck)));
            var after=Done(s.SnapshotAsync());Require(after.Balances[EngineeringRules.Installed("TWR-R1","base_provision")]==2&&after.Balances[EngineeringRules.Available("base_provision")]==0&&after.Entities.Count(e=>e.Kind=="operator_duty")==2);Done(s.ContinueAsync());Require(Done(s.SnapshotAsync()).Entities.Count(e=>e.Kind=="operator_duty")==2&&Done(service.ReadinessAsync()).Single(t=>t.Tower=="TWR-R1").Baseline);
        });
        Add("CX-059","Field trial uses local permission before final signatures and returns its reusable fixture",()=>
        {
            using var s=Store("trial");var service=new WorkOrderService(s,Catalog);Prepare(s,service);foreach(string source in service.Rules.Sources.Keys.Where(k=>k.StartsWith("BASE-TWR-R1-")))Procurement(s,service,source);Finish(s,service,WorkType.BaselineCheck);Procurement(s,service,"SUP-CTRL");Procurement(s,service,"SUP-COUP");Procurement(s,service,"SUP-TEST");var p=Fixture(s,"technical","technical_package_verified",new{version="technical-v1"});Fixture(s,"technical-record","explicit_engineering_fixture",new{},[Entity(s,"technical-package","technical_package",new TechnicalPackage("technical-v1",true,p.Receipt!.Id.Value.ToString()))]);Finish(s,service,WorkType.EnhancedInstall);
            Throws<InvalidOperationException>(()=>Done(service.ReserveAsync("TWR-R1",WorkType.FieldTrial,"technical-v1","crew-R1")));var permit=Fixture(s,"permit","local_trial_permission_verified",new{tower="TWR-R1",configuration="technical-v1"});Fixture(s,"permit-record","explicit_engineering_fixture",new{},[Entity(s,"trial-permission:TWR-R1","trial_permission",new TrialPermission("TWR-R1","technical-v1",true,permit.Receipt!.Id.Value.ToString()))]);long available=Done(s.SnapshotAsync()).Balances[EngineeringRules.Available("trial_fixture")];Finish(s,service,WorkType.FieldTrial);var ready=Done(service.ReadinessAsync()).Single(t=>t.Tower=="TWR-R1");Require(ready.Trial&&ready.TrialVersion=="technical-v1"&&Done(s.SnapshotAsync()).Balances[EngineeringRules.Available("trial_fixture")]==available&&Done(new FactionService(s,Catalog).AgreementsAsync()).All(a=>a.Operator is null&&a.Representative is null));
        });
    }
}

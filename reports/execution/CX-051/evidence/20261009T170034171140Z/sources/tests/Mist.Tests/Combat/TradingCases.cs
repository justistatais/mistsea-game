using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.Inventory;
using Mist.Application.Q102;
using Mist.Application.Roster;
using Mist.Core.Contracts;
using Mist.Core.Progression;
using Mist.Infrastructure.Storage;
using static Mist.Tests.NPC.NpcCases;
using static Mist.Tests.Storage.StorageCases;
using static Mist.Tests.TestRegistry;

namespace Mist.Tests.Combat;

public static class TradingCases
{
    private static ProfileStore Store(string name)=>new(Path.Combine(Environment.GetEnvironmentVariable("MISTSEA_TEST_OUTPUT")??Path.GetTempPath(),"trading-"+name+"-"+Guid.NewGuid()),Q102.Q102Cases.Catalog().ReleaseId);
    private static PartyService Prepare(ProfileStore store){var cat=Q102.Q102Cases.Catalog();Done(new Q102Service(store,cat).InitializeSliceAsync());var party=new PartyService(store,cat);Done(party.InitializeStarterCollectionAsync());return party;}
    public static void Register()
    {
        Add("CX-062","A paid supply sale has stable request recovery, payload binding and a single conserved refund",()=>
        {
            using var store=Store("sale");var party=Prepare(store);Fixture(store,"fixture:paid-funds","explicit_engineering_fixture",new{},balances:[new("marks",100)]);var buy=new CommandId(Guid.NewGuid());Require(Done(party.BuyConsumableAsync("CON-01",buy)).Status==CommitStatus.Committed);string item="supply:"+buy.Value;long afterBuy=Done(store.SnapshotAsync()).Balances["marks"];var sell=new CommandId(Guid.NewGuid());Require(Done(party.SellAsync(item,sell)).Status==CommitStatus.Committed);var sold=Done(store.SnapshotAsync());Require(sold.Items.Single(i=>i.Id==item).OwnerKind=="sold"&&sold.Balances["marks"]>afterBuy&&sold.Balances["marks"]<100);
            Require(Done(party.SellAsync(item,sell)).Status==CommitStatus.AlreadyCommitted&&sold.Sequence==Done(store.SnapshotAsync()).Sequence&&Done(store.FindReceiptAsync(store.Session.Branch,sell)) is not null);Throws<InvalidOperationException>(()=>Done(party.SellAsync("different-instance",sell)));Throws<InvalidOperationException>(()=>Done(party.SellAsync(item,new(Guid.NewGuid()))));Require(StrictJson.Serialize(sold)==StrictJson.Serialize(Done(store.SnapshotAsync())));
            Done(store.ContinueAsync());Require(Done(party.SellAsync(item,sell)).Status==CommitStatus.AlreadyCommitted);
        });
        Add("CX-062","Free supply instances and forged paid-looking names cannot become a coin loop",()=>
        {
            using var store=Store("free");var party=Prepare(store);string forged="supply:"+Guid.NewGuid();Fixture(store,"fixture:free-supplies","explicit_engineering_fixture",new{scope="negative ownership fixture"},items:[new("free-aid","CON-01","new","","player","hero",null),new(forged,"CON-01","new","","player","hero",null)]);string before=StrictJson.Serialize(Done(store.SnapshotAsync()));Throws<InvalidOperationException>(()=>Done(party.SellAsync("free-aid",new(Guid.NewGuid()))));Throws<InvalidOperationException>(()=>Done(party.SellAsync(forged,new(Guid.NewGuid()))));Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));
        });
        Add("CX-062","Illegal full build proposal leaves existing build, resources and earned upgrade budget unchanged",()=>
        {
            using var store=Store("build");var party=Prepare(store);string before=StrictJson.Serialize(Done(store.SnapshotAsync()));Throws<InvalidOperationException>(()=>Done(party.ApplyBuildAsync(new(new(25,25,25,25,25),[],[]))));Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));var build=Done(party.InspectAsync()).Build;Require(Done(party.ApplyBuildAsync(build)).Status==CommitStatus.Committed);Require(Done(party.ApplyBuildAsync(build)).Status==CommitStatus.Committed);Require(Done(store.SnapshotAsync()).Balances["experience"]==340&&Done(party.InspectAsync()).UnspentSkills==ProgressionRules.SkillBudget(3));
        });
        Add("CX-056","A permanently assigned operator cannot depart, borrow an additional relic or fill assistance",()=>
        {
            using var store=Store("operator");var party=Prepare(store);Fixture(store,"Q-104:resolution","explicit_engineering_fixture",new{});var roster=new RosterService(store,Q102.Q102Cases.Catalog());Done(roster.InviteAsync("CP-LINA"));Done(roster.DecideAsync("CP-LINA",true));Fixture(store,"fixture:operator-post","explicit_engineering_fixture",new{},entities:[Entity(store,"duty:CP-LINA","operator_duty",new{tower="R1",role="operator"})],items:[new("borrowable","RE-01","new","","player","hero",null)]);Throws<InvalidOperationException>(()=>Done(party.EquipAsync("borrowable","relic1","CP-LINA")));Throws<InvalidOperationException>(()=>Done(roster.SelectAsync(["CP-LINA"])));Require(roster.Helpers(Done(store.SnapshotAsync())).SequenceEqual(new[]{"AUX-GUARD","AUX-SUPPORT"}),"An actual permanent work post needs the two free auxiliaries, never the occupied companion.");Done(roster.RequestDepartureAsync("CP-LINA"));string before=StrictJson.Serialize(Done(store.SnapshotAsync()));Throws<InvalidOperationException>(()=>Done(roster.FinishDepartureAsync("CP-LINA")));Require(before==StrictJson.Serialize(Done(store.SnapshotAsync())));
        });
    }
}

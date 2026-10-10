using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.ChapterTwo;
using Mist.Core.Contracts;
using Mist.Core.Exploration;

namespace Mist.Core.R1Life;

public sealed record PublicBooking(string Id,int Version,string Stay,int Beds,bool Quiet,bool Level,int Supersedes,string ConfirmedBy);
public sealed record PublicRoom(string Id,int Beds,bool Quiet,bool Level);
public sealed record R1LifePackage(string Version,ImmutableArray<SceneGeometry> Scenes,Spot StreetSpot,Port StreetPort,
    ImmutableArray<ChapterMaterial> Materials,ImmutableArray<ChapterStation> Stations,ImmutableArray<ChapterWritingTask> Writing,
    ImmutableArray<PublicBooking> Bookings,ImmutableArray<PublicRoom> Rooms,ImmutableDictionary<string,string> PortGates)
{
    public static R1LifePackage Parse(string json)
    {
        using var d=JsonDocument.Parse(json);var r=d.RootElement;StrictJson.CheckDuplicates(r);
        if(!r.GetProperty("player_projection").GetBoolean()||r.GetProperty("scope").GetString()!="authoring_candidate_not_runtime"||r.GetProperty("human_review").GetString()!="human_pending")throw new InvalidDataException("R1 life author cards cannot enter the learner package.");
        foreach(var band in r.GetProperty("target_bands").EnumerateObject())if(band.Value.GetDecimal()!=6.5m)throw new InvalidDataException("Reading/listening/writing target remains 6.5.");
        static string S(JsonElement e,string p)=>e.GetProperty(p).GetString()??throw new InvalidDataException("Missing field "+p);
        static Point2 Pt(JsonElement e)=>new(e.GetProperty("x").GetDouble(),e.GetProperty("y").GetDouble());
        static Bounds B(JsonElement e)=>new(e.GetProperty("left").GetDouble(),e.GetProperty("top").GetDouble(),e.GetProperty("right").GetDouble(),e.GetProperty("bottom").GetDouble());
        static Spot Sp(JsonElement e)=>new(S(e,"id"),Pt(e.GetProperty("stand")),S(e,"purpose_zh"));
        var gates=ImmutableDictionary.CreateBuilder<string,string>();
        Port Po(JsonElement e){gates.Add(S(e,"id"),S(e,"gate"));return new(S(e,"id"),S(e,"target_scene"),S(e,"target_port"),Pt(e.GetProperty("stand")));}
        var scenes=r.GetProperty("scenes").EnumerateArray().Select(s=>new SceneGeometry(S(s,"id"),B(s.GetProperty("walk")),s.GetProperty("blocked").EnumerateArray().Select(B).ToImmutableArray(),[],s.GetProperty("ports").EnumerateArray().Select(Po).ToImmutableArray(),s.GetProperty("spots").EnumerateArray().Select(Sp).ToImmutableArray())).ToImmutableArray();
        var street=r.GetProperty("street_extension");var port=Po(street.GetProperty("port"));
        var materials=r.GetProperty("materials").EnumerateArray().Select(m=>new ChapterMaterial(S(m,"id"),S(m,"content_version"),S(m,"translation_version"),S(m,"title_zh"),S(m,"quest_ref"),S(m,"kind"),S(m.GetProperty("location"),"scene"),S(m.GetProperty("location"),"spot"),S(m,"source_root"),S(m,"access_class"),S(m,"review_status"),S(m,"human_review"),S(m,"audio_asset"),m.GetProperty("paragraphs").EnumerateArray().Select(p=>new ChapterParagraph(S(p,"id"),S(p,"en"),S(p,"zh"))).ToImmutableArray())).ToImmutableArray();
        var stations=r.GetProperty("stations").EnumerateArray().Select(s=>new ChapterStation(S(s,"id"),S(s,"spot"),S(s,"label_zh"),S(s,"action"))).ToImmutableArray();
        var writing=r.GetProperty("writing_tasks").EnumerateArray().Select(w=>new ChapterWritingTask(S(w,"id"),S(w,"quest_ref"),S(w,"kind"),S(w,"workspace_spot"),S(w,"prompt_en"),S(w,"prompt_zh"))).ToImmutableArray();
        var bookings=r.GetProperty("public_booking_records").EnumerateArray().Select(b=>StrictJson.Deserialize<PublicBooking>(b.GetRawText())).ToImmutableArray();
        var rooms=r.GetProperty("public_room_cards").EnumerateArray().Select(b=>StrictJson.Deserialize<PublicRoom>(b.GetRawText())).ToImmutableArray();
        var package=new R1LifePackage(S(r,"content_version"),scenes,Sp(street.GetProperty("spot")),port,materials,stations,writing,bookings,rooms,gates.ToImmutable());Validate(package);return package;
    }
    public static void Validate(R1LifePackage p)
    {
        if(!p.Scenes.Select(s=>s.Id).Order().SequenceEqual(new[]{"R1-O01","R1-O02"})||p.Materials.Length!=11||p.Stations.Length!=20||p.Writing.Length!=2||p.Bookings.Length!=3||p.Rooms.Length!=2)
            throw new InvalidDataException("Two optional spaces, eleven full materials and two local writing tasks are required.");
        if(p.PortGates.Count!=4||p.StreetPort.Id!="R1-S02.to.R1-O01"||p.StreetPort.TargetPort!="R1-O01.to.R1-S02"||p.StreetPort.Stand!=new Point2(92,32)||p.StreetPort.TargetScene!="R1-O01"||p.StreetSpot.Id!="R1-S02.inn_notice")throw new InvalidDataException("Original street/inn/theatre registration and public approach must remain fixed.");
        foreach(var s in p.Scenes)
        {
            var nav=new NavigationGrid(s);if(s.Spots.Any(x=>!nav.CanStand(x.Stand,true))||s.Ports.Any(x=>!nav.CanStand(x.Stand,true)))throw new InvalidDataException("Actual accessible station and port geometry is required.");
        }
        var ports=p.Scenes.SelectMany(s=>s.Ports.Select(po=>(Scene:s.Id,Port:po))).Append((Scene:"R1-S02",Port:p.StreetPort)).ToArray();
        foreach(var a in ports)
        {
            var b=ports.SingleOrDefault(b=>b.Scene==a.Port.TargetScene&&b.Port.Id==a.Port.TargetPort);
            if(b.Port is null||b.Port.TargetScene!=a.Scene||b.Port.TargetPort!=a.Port.Id||p.PortGates[a.Port.Id]!=p.PortGates[b.Port.Id])throw new InvalidDataException("Optional exits must be reciprocal.");
        }
        if(p.Bookings.Any(b=>b.ConfirmedBy!="N-INNKEEPER")||p.Bookings.Count(b=>b.Id=="BKG-17")!=2||p.Bookings.Single(b=>b.Id=="BKG-17"&&b.Version==2).Supersedes!=1||p.Bookings.Single(b=>b.Id=="BKG-22").Supersedes!=0)
            throw new InvalidDataException("A corrected copy is one order, not a new group.");
        foreach(var m in p.Materials)if(m.ReviewStatus!="original_unreviewed"||m.HumanReview!="human_pending"||m.AudioAsset!=""||m.Paragraphs.Length<2||m.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese))||!p.Scenes.Any(s=>s.Id==m.Scene&&s.Spots.Any(x=>x.Id==m.Spot)))throw new InvalidDataException("Full bilingual learner text and honest audio/review state are required.");
    }
}
public enum ExtraLodgingArrangement { LodgingDiscount, LocalInformation }
public enum TheatreProgramme { Unselected, FullIndoor, SmallDaylight }
public sealed record RoomAllocation(string Booking,string Room);
public sealed record TheatreRoles(TheatreProgramme Programme,string Speaker,string SecondRole,string SecondPerson,string Starts,string Ends);
public sealed record R1LifeState
{
    public required string ContentVersion {get;init;}
    public bool InnDiscovered {get;init;}
    public bool TheatreDiscovered {get;init;}
    public bool RoomsPaused {get;init;}
    public bool ShowPaused {get;init;}
    public bool GuestsConfirmed {get;init;}
    public bool QuietRoomInspected {get;init;}
    public bool GroundRoomInspected {get;init;}
    public ImmutableArray<RoomAllocation> Allocations {get;init;}=[];
    public ExtraLodgingArrangement? Extra {get;init;}
    public bool StageInspected {get;init;}
    public bool LampIsolated {get;init;}=true;
    public bool BraceInstalled {get;init;}
    public bool CellInstalled {get;init;}
    public bool LampPowered {get;init;}
    public long LampConfiguration {get;init;}
    public long? LampTestConfiguration {get;init;}
    public bool ScreenPlaced {get;init;}
    public bool DrumPlaced {get;init;}
    public TheatreRoles? Roles {get;init;}
    public bool ShowWatched {get;init;}
    public bool LampReady=>!LampIsolated&&LampPowered&&BraceInstalled&&CellInstalled&&LampTestConfiguration==LampConfiguration;
}
public sealed record LocalStoryMeaning(string Quest,string DraftHash,bool Confirmed,bool StructuredAssistance);
public static class R1LifeRules
{
    public const string Rooms="SC-R1-001",Show="SC-R1-002",Speaker="ROLE-R1-SHOW-SPEAKER",Second="ROLE-R1-SHOW-SECOND",Store="LOCAL-R1-THEATRE-STORE",Stage="LOCAL-R1-THEATRE-STAGE",Lamp="LOCAL-R1-THEATRE-LAMP";
    public static ImmutableArray<PublicBooking> CurrentBookings(R1LifePackage p)=>p.Bookings.GroupBy(b=>b.Id,StringComparer.Ordinal).Select(g=>g.OrderByDescending(b=>b.Version).First()).ToImmutableArray();
    public static bool Suitable(PublicBooking b,PublicRoom r)=>r.Beds>=b.Beds&&(!b.Quiet||r.Quiet)&&(!b.Level||r.Level);
    public static bool ValidAllocation(R1LifePackage p,IReadOnlyCollection<RoomAllocation> allocations)
    {
        var bookings=CurrentBookings(p);return allocations.Count==bookings.Length&&allocations.Select(a=>a.Booking).Distinct().Count()==bookings.Length&&allocations.Select(a=>a.Room).Distinct().Count()==allocations.Count&&
            bookings.All(b=>allocations.SingleOrDefault(a=>a.Booking==b.Id) is{} a&&p.Rooms.SingleOrDefault(r=>r.Id==a.Room) is{} r&&Suitable(b,r));
    }
    public static bool ValidRoles(TheatreRoles r)=>r.Speaker==Speaker&&r.SecondPerson==Second&&r.Speaker!=r.SecondPerson&&(r.Programme switch
    {
        TheatreProgramme.FullIndoor=>r.SecondRole=="lamp_operator"&&r.Starts=="18:00"&&r.Ends=="18:30",
        TheatreProgramme.SmallDaylight=>r.SecondRole=="drummer"&&r.Starts=="16:00"&&r.Ends=="16:15",_=>false
    });
    public static bool ShowReady(R1LifeState s)=>s.StageInspected&&s.ScreenPlaced&&s.Roles is{} r&&ValidRoles(r)&&
        (r.Programme==TheatreProgramme.FullIndoor?s.LampReady:s.LampIsolated&&!s.LampPowered&&s.DrumPlaced);
}

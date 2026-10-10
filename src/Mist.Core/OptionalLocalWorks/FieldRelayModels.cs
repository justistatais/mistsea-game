using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mist.Core.Combat;
using Mist.Core.Devices;

namespace Mist.Core.OptionalLocalWorks;

public sealed record FieldRelayParagraph(string Id,string English,string Chinese);
public sealed record FieldRelayContract(string Version,string Encounter,string ParentQuest,string Scene,string Spot,string Template,string Owner,
    string RelaySerial,string PinSerial,string Scope,string ReviewStatus,ImmutableArray<FieldRelayParagraph> Paragraphs,string Fingerprint)
{
    public static FieldRelayContract ParseJson(string json)
    {
        using var d=JsonDocument.Parse(json);Contracts.StrictJson.CheckDuplicates(d.RootElement);var j=d.RootElement;static string S(JsonElement x,string key)=>x.GetProperty(key).GetString()??throw new InvalidDataException("Missing local field-repair contract field.");
        if(!j.GetProperty("player_projection").GetBoolean())throw new InvalidDataException("Author-only field repair is not a player contract.");var p=new FieldRelayContract(S(j,"version"),S(j,"encounter"),S(j,"parent_quest"),S(j,"scene"),S(j,"spot"),S(j,"template"),S(j,"owner"),S(j,"relay_serial"),S(j,"pin_serial"),S(j,"scope"),S(j,"review_status"),j.GetProperty("paragraphs").EnumerateArray().Select(x=>new FieldRelayParagraph(S(x,"id"),S(x,"en"),S(x,"zh"))).ToImmutableArray(),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());Validate(p);return p;
    }
    public static void Validate(FieldRelayContract p)
    {if(p.Version!="r6-field-relay-candidate-1"||p.Encounter!="ENC-R6-06"||p.ParentQuest!="Q-603"||p.Scene!="R6-S06"||p.Spot!="R6-S06.repair"||p.Template!="relay_drone"||p.Owner!="ROLE-R6-CARGO-WATCH"||p.RelaySerial!="R6-AUX-RELAY-01"||p.PinSerial!="R6-AUX-BRAKE-PIN-01"||p.Scope!="same_auxiliary_relay_not_tower_or_protected_cargo"||p.ReviewStatus!="original_unreviewed_human_pending"||p.Fingerprint.Length!=64||p.Fingerprint.Any(c=>!char.IsAsciiHexDigit(c)||char.IsUpper(c))||p.Paragraphs.IsDefaultOrEmpty||p.Paragraphs.Length!=3||!p.Paragraphs.Select(x=>x.Id).SequenceEqual(new[]{"R6-FIELD-RELAY-R-SCOPE-p01","R6-FIELD-RELAY-R-SCOPE-p02","R6-FIELD-RELAY-R-SCOPE-p03"})||p.Paragraphs.Any(x=>string.IsNullOrWhiteSpace(x.English)||string.IsNullOrWhiteSpace(x.Chinese)))throw new InvalidDataException("Unbound optional field-repair source candidate.");}
}
public sealed record FieldRelayState(Guid Instance,string SourceFingerprint,string ScopeDecision,string AuxSwitch="LIVE",long Configuration=0,
    long? ZeroCheck=null,bool ThreatControlled=false,bool Repaired=false,long? ReturnCheck=null,bool Completed=false);
public sealed record FieldRelayGauge(string RelaySerial,string Channel,int ActualReading,long Configuration,bool InputDisconnected,bool TrackFixed);
public sealed record FieldRelayEncounter(BattleSnapshot Battle,BattleSnapshot Entry,ActorState ReturnActor);
public sealed record FieldRelayCompletion(Guid Instance,string RelaySerial,string PinSerial,string SourceFingerprint,long Configuration,
    string ThreatReceipt,string GaugeReceipt,string ReturnReceipt,string Scope,int Experience,int Marks,int Parts);

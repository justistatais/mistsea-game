using System.Collections.Immutable;
using System.Text.Json;
using Mist.Core.Content;

namespace Mist.Core.Inventory;

public enum ItemKind { Weapon,Armor,Relic,FieldTool }
public enum WeaponScaling { Melee,Ranged,Tech }
public sealed record ItemDefinition(string Id,string Name,ItemKind Kind,string Slot,long MarksPrice,bool BoundCollection,int SellValue,string Effect,WeaponScaling? Scaling,int BaseWeaponUnits,int PerGradeUnits,int BasicCoefficientBp,int ArmorBaseBp,int PerGradeBp);
public sealed record ModuleDefinition(string Id,string Name,string Slot,string Stat,int Value,int MinimumRank,int Marks,int Parts,string? ConsumesModule);
public sealed class ItemCatalog(RuntimeCatalog catalog)
{
    public ImmutableDictionary<string,ItemDefinition> Items {get;}=catalog.Definitions["items"].Select(d=>
    {
        using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;int I(string key,int fallback=0)=>j.TryGetProperty(key,out var e)?e.GetInt32():fallback;
        var kind=j.GetProperty("kind").GetString() switch{"weapon"=>ItemKind.Weapon,"armor"=>ItemKind.Armor,"relic"=>ItemKind.Relic,"field_tool"=>ItemKind.FieldTool,_=>throw new InvalidDataException("Unknown item kind.")};
        WeaponScaling? scaling=j.TryGetProperty("scaling",out var s)?s.GetString() switch{"melee"=>WeaponScaling.Melee,"ranged"=>WeaponScaling.Ranged,"tech"=>WeaponScaling.Tech,_=>throw new InvalidDataException("Unknown weapon scaling.")}:null;
        return new ItemDefinition(d.Id,d.Name,kind,j.GetProperty("slot").GetString()!,I("marks_price"),j.GetProperty("bound_collection").GetBoolean(),I("sell_value"),j.GetProperty("effect").GetString()!,scaling,I("base_weapon_units"),I("per_grade_units"),I("basic_coefficient_bp",10000),I("armor_base_bp"),I("per_grade_bp"));
    }).ToImmutableDictionary(i=>i.Id);
    public ImmutableDictionary<string,ModuleDefinition> Modules {get;}=catalog.Definitions["modules"].Select(d=>
    {
        using var doc=JsonDocument.Parse(d.PayloadJson);var j=doc.RootElement;
        return new ModuleDefinition(d.Id,d.Name,j.GetProperty("slot").GetString()!,j.GetProperty("stat").GetString()!,j.GetProperty("value").GetInt32(),j.GetProperty("minimum_rank").GetInt32(),j.GetProperty("marks_cost").GetInt32(),j.GetProperty("parts_cost").GetInt32(),j.GetProperty("consumes_module").ValueKind==JsonValueKind.Null?null:j.GetProperty("consumes_module").GetString());
    }).ToImmutableDictionary(m=>m.Id);
}

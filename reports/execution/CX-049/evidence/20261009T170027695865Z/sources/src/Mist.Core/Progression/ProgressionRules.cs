using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Mist.Core.Inventory;

namespace Mist.Core.Progression;

public sealed record Attributes(int Body=5,int Strength=5,int Agility=5,int Perception=5,int Focus=5)
{
    [JsonIgnore] public int[] Values=>[Body,Strength,Agility,Perception,Focus];
    public void Validate(int level)
    {
        ProgressionRules.CheckLevel(level);if(Values.Any(v=>v<5||v>25)||Values.Sum(v=>v-5)>ProgressionRules.AttributeBudget(level))throw new InvalidOperationException("属性超出已获得预算或单项范围。");
    }
}
public sealed record DerivedStats(int MaxHp,int MaxFocus,int Initiative,int AttackUnits,int TechUnits,int ArmorBp,int GuardBp,int ExposureBp,int DamageBonusBp=0,int FlatPressure=0);
public static class ProgressionRules
{
    public static void CheckLevel(int level){if(level is <1 or >30)throw new ArgumentOutOfRangeException(nameof(level));}
    public static int Threshold(int level){CheckLevel(level);return 100*(level-1)+20*(level-1)*(level-2);}
    public static int Level(long xp){if(xp<0)throw new ArgumentOutOfRangeException(nameof(xp));return Enumerable.Range(1,30).Last(l=>xp>=Threshold(l));}
    public static int AttributeBudget(int level){CheckLevel(level);return 4+2*(level-1);}
    public static int SkillBudget(int level){CheckLevel(level);return level<2?0:1+level/2;}
    public static int Grade(IEnumerable<string> keys)
    {
        var receipts=keys.ToHashSet(StringComparer.Ordinal);int grade=0;foreach(var pair in new[]{("Q-104",1),("Q-304",2),("Q-504",3),("Q-604",4)})if(receipts.Contains(pair.Item1+":resolution"))grade=pair.Item2;return grade;
    }
    public static Attributes AllocateTemplate(int level,int[] weights)
    {
        if(weights.Length!=5||weights.Any(w=>w<=0))throw new InvalidDataException("Invalid companion profile.");int[] bonus=new int[5];
        for(int count=0;count<AttributeBudget(level);count++)
        {
            int best=Enumerable.Range(0,5).Where(i=>bonus[i]<20).Aggregate((a,b)=>(long)weights[b]*(bonus[a]+1)>(long)weights[a]*(bonus[b]+1)?b:a);bonus[best]++;
        }
        return new(bonus[0]+5,bonus[1]+5,bonus[2]+5,bonus[3]+5,bonus[4]+5);
    }
    public static DerivedStats Derive(int level,int grade,Attributes stats,ItemDefinition weapon,ItemDefinition armor,IEnumerable<string>? relics=null,IEnumerable<ModuleDefinition>? modules=null)
    {
        stats.Validate(level);if(grade is <0 or >4||weapon.Kind!=ItemKind.Weapon||armor.Kind!=ItemKind.Armor)throw new InvalidOperationException("Invalid calibrated equipment.");
        var re=(relics??[]).ToHashSet();var mods=(modules??[]).ToArray();int Value(string key)=>mods.Where(m=>m.Stat==key).Sum(m=>m.Value);
        int common=120+20*(level-1)+weapon.BaseWeaponUnits+weapon.PerGradeUnits*grade;
        int Power(WeaponScaling scaling)=>scaling switch{WeaponScaling.Melee=>common+8*(stats.Strength-5)+4*(stats.Agility-5),WeaponScaling.Ranged=>common+8*(stats.Agility-5)+4*(stats.Strength-5),WeaponScaling.Tech=>common+8*(stats.Focus-5)+4*(stats.Perception-5),_=>throw new InvalidOperationException("Unknown scaling.")};
        return new(80+8*(level-1)+6*(stats.Body-5),Math.Min(14,6+(stats.Focus-5)/4+Value("focus_bonus")+(re.Contains("RE-09")?1:0)),10+(stats.Agility-5)/2+(armor.Id=="AR-02"?-2:armor.Id=="AR-03"?2:0)+(re.Contains("RE-04")?2:0),
            Power(weapon.Scaling??throw new InvalidOperationException()),Power(WeaponScaling.Tech),Math.Min(4500,armor.ArmorBaseBp+armor.PerGradeBp*grade+Value("armor_bonus_bp")+(re.Contains("RE-03")?300:0)),
            Math.Min(3500,2000+50*(stats.Body-5)+(re.Contains("RE-11")?500:0)),Math.Min(4000,500+100*(stats.Perception-5)+(re.Contains("RE-07")?500:0)),Value("damage_bonus_bp"),Value("flat_stagger"));
    }
}

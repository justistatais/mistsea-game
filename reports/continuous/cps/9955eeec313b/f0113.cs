using System.Collections.Immutable;
using Mist.Application.Commands;
using Mist.Application.Contracts;
using Mist.Application.Roster;
using Mist.Core.Contracts;
using Mist.Core.Finale;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

namespace Mist.Application.Finale;

public sealed partial class FinaleService
{
    public async ValueTask<FinaleEpilogue> PresentEpilogueAsync()
    {var ending=await FinalReadiness.SnapshotAsync()??throw new InvalidOperationException("实际结局尚未提交，不能显示假结局。");return ProjectEpilogue(ending);}
    public static FinaleEpilogue ProjectEpilogue(FinalEndingSnapshot ending)
    {
        var frozenRecord=ending.History.SingleOrDefault(h=>h.Id=="final:preparation"&&h.Kind=="frozen_preparation")??throw new InvalidOperationException("结局快照未保留真实封存准备。");var frozen=StrictJson.Deserialize<FrozenPreparation>(frozenRecord.Json);Require(frozen.Id==ending.PreparationId,"尾声准备来源与该结局快照不符。");
        var immediate=ending.Choice switch
        {
            EndingChoice.Central=>new FinaleBilingualText("The checked command retains restricted central coordination. Unlimited renewal is disabled; the actual local preservation and duties remain the basis of protection. Cross-region assistance continues within the recorded limit, with continuing supervision and review.","受检命令保留有限中央协调。无限续启已禁，实际本地保全及岗位仍是保护基础。跨区互助在已记录边界内继续，并保持续监督与复核。"),
            EndingChoice.Island=>new FinaleBilingualText("The checked sequence separates forced central dispatch and the main interregional links. The prepared local preservation and duties remain in operation. The decision preserves protection rather than destroying the towers or switching off every supply.","受检顺序解除中央强制调度及主要跨区联络，已准备本地保全与岗位继续运行。这决定保留保护，没有毁塔或关掉全部供给。"),
            EndingChoice.Distributed=>new FinaleBilingualText("The recorded enhanced equipment, transition supply, accepted protocol and current field trials support a bounded distributed changeover. The seventh tower becomes a constrained hub; local operators retain declared fault-isolation and review responsibilities.","已记录增强设备、过渡供给、接受协议及当前试运行支持有限分布切换。第七塔成为受约束枢纽，本地操作方保留声明的故障隔离及复核责任。"),
            _=>throw new InvalidOperationException("Unknown recorded ending.")
        };
        string[] regionNames=["Saltmist harbour","Mosswood","the mining district","Glasshaven","the old capital","the frontier"];string[] regionNamesZh=["盐雾港","苔林","矿区","玻璃学城","旧都","边境"];
        var cards=ImmutableArray.CreateBuilder<FinaleRegionalCard>();
        for(int index=0;index<6;index++)
        {
            string region="R"+(index+1),name=regionNames[index],zh=regionNamesZh[index];var tower=frozen.Preparation.Towers.Single(t=>t.Tower=="TWR-"+region);var references=ending.History.Where(h=>h.Id=="readiness:TWR-"+region||h.Id=="quest:Q-"+(index+1)+"04").Select(h=>h.Id).ToImmutableArray();
            string status=tower.Safe&&tower.Baseline&&tower.Operators?"The captured preparation records actual safe local preservation and duties.":"The captured record does not support a complete local-preservation claim.";string statusZh=tower.Safe&&tower.Baseline&&tower.Operators?"封存准备记录了实际安全本地保全与岗位。":"封存记录不足以支持完整本地保全的说法。";
            string follow=ending.Choice switch
            {
                EndingChoice.Central=>"In this six-month stage, limited central balancing can help the district through uneven supply. Rationing and maintenance of the retained system continue; the record makes no promise of permanent protection or the end of central-power disputes.",
                EndingChoice.Island=>"In this six-month stage, local operators hold more direct control. Reduced balancing and assistance leave the district with continued supply and protection limits; later neighbour agreements and transport arrangements remain work, rather than automatic replacements for the lost links.",
                _=>"In this six-month stage, the recorded common protocol and enhanced field preparation support bounded assistance and local fault isolation. Shared maintenance, standards and dispute handling remain duties; a passed trial does not certify that later failure is impossible."
            };
            string followZh=ending.Choice switch
            {
                EndingChoice.Central=>"六个月阶段中，有限中央调峰可助本地区应对供给波动。轮流限供与旧系统维护继续，记录没有保证永久保护，也没有消除集中权力争议。",
                EndingChoice.Island=>"六个月阶段中，地方岗位有更直接控制。调峰与互助减少，供给和保护边界仍在；后续邻区协定及运输安排还需工作，没有自动替代失去的联络。",
                _=>"六个月阶段中，已记录共同协议及增强现场准备支持有限互助与本地隔离。共享维护、标准及争议处理仍是岗位，试运行通过不认证今后不可能失败。"
            };
            var local=RegionalCapturedFragment(ending,region);
            references=references.AddRange(local.Sources).Distinct(StringComparer.Ordinal).ToImmutableArray();
            cards.Add(new(region,"six_months",new($"At {name}, {status} {local.Text.English} {follow} Natural mist remains.",$"在{zh}，{statusZh}{local.Text.Chinese}{followZh}自然雾仍在。"),references));
        }
        var notes=ImmutableArray.CreateBuilder<FinaleCompanionNote>();
        foreach(string id in new[]{"CP-LINA","CP-OREN","CP-MIRA","CP-TESS"})
        {
            var entry=ending.History.SingleOrDefault(h=>h.Id=="roster:"+id&&h.Kind=="roster");bool joined=entry is not null&&StrictJson.Deserialize<RosterEntry>(entry.Json).Membership==Membership.Joined;string cq="CQ-"+id[3..]+"-03";var privateResult=ending.History.SingleOrDefault(h=>h.Id=="quest:"+cq&&h.Kind=="quest");bool completed=privateResult is not null&&StrictJson.Deserialize<QuestState>(privateResult.Json) is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed};
            string en=!joined?"No letter from a recruited travelling companion is recorded for this person. Their actual prior contact remains in the snapshot; this follow-up does not invent a shared voyage or private reconciliation.":!completed?"This person actually joined, but the captured private work is unfinished. The follow-up retains that boundary and does not supply a missing reconciliation, consent or message.":"This person actually joined and the captured private work is completed. That result remains part of the follow-up, with its existing access limits. Continuing maintenance and cooperation still matter; no new private message is claimed without an actual delivery source.";
            string cn=!joined?"没有此人为已招募同行者投递的来信记录。其实际先前接触仍在快照，本段不编共同航行或私人和解。":!completed?"此人确已加入，但快照私人工作尚未完成。跟进保留这边界，不补缺失和解、同意或消息。":"此人确已加入且快照私人工作已完成。该结果及原访问范围保留。维护合作仍需继续，没有实际投递来源就不声称收到新私信。";
            var references=ending.History.Where(h=>h.Id=="roster:"+id||h.Id=="quest:"+cq).Select(h=>h.Id).ToImmutableArray();notes.Add(new(id,joined,completed,new(en,cn),references));
        }
        var archive=new FinaleBilingualText("The captured public account retains historical responsibility, source versions and access limits under every policy direction. Rook's custody does not replace the historical review, and retaining coordination does not erase an official's documented conduct. This is a bounded six-month outcome; natural mist and accumulated damage remain. Learning assessment stays separate and unassessed where no actual assessment exists.","各政策方向的快照公共记录均保历史责任、来源版本与访问范围。鲁克看管不替代历史审查，保中央也不会抹去官员已记录行为。这是有限六个月结果，自然雾与累积损伤仍在。学习评价独立保存，没有实际评价时仍未评估。");
        return new(ending.Id,ending.EndingId,immediate,cards.ToImmutable(),notes.ToImmutable(),archive,true);
    }
    public async ValueTask<CommandResult> AcknowledgeEpilogueAsync(bool skipPresentation)
    {var s=await _repository.SnapshotAsync();Safe(s);At(s,"RF-S07.epilogue");var st=State(s);Require(Closed(s,"Q-F04"),"需要当前实际结局提交。");var ending=await FinalReadiness.SnapshotAsync()??throw new InvalidOperationException("结局快照未提交。");ProjectEpilogue(ending);if(Closed(s,"Q-F05")){Require(st.PresentedEnding==ending.Id,"尾声必须对应同一结局快照。");return new(CommitStatus.AlreadyCommitted,null,[]);}var q=Quest(s,"Q-F05")!;Require(q.Lifecycle is QuestLifecycle.Unaccepted or QuestLifecycle.Active,"尾声状态不允许补写结果。");if(q.Lifecycle==QuestLifecycle.Unaccepted)q=q.Accept();var reward=_catalog.Quests.Single(x=>x.Id==q.Id).Reward;Require(reward.Bundle.Experience==0&&reward.Bundle.Marks==0&&reward.Bundle.Parts==0&&reward.Items.Length==0,"尾声不应有新增奖励。");return await Commit(s,"main_quest_resolution",[Change(s,st with{PresentedEnding=ending.Id}),BranchOperations.Change(s,"quest:Q-F05","quest",q.Close(QuestOutcome.Completed)),BranchOperations.Change(s,"RF:epilogue-acknowledgment","ending_presentation",new{ending_id=ending.Id,skip_presentation=skipPresentation,no_new_required_essay=true,learning_evaluated=false})],new MainQuestResolution(q.Id,q.Instance,"completed"),"Q-F05:resolution");}
}

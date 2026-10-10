using System.Collections.Immutable;
using Mist.Core.ChapterOne;
using Mist.Core.ChapterTwo;
using Mist.Core.ChapterThree;
using Mist.Core.ChapterFour;
using Mist.Core.ChapterFive;
using Mist.Core.ChapterSix;
using Mist.Core.Contracts;
using Mist.Core.Finale;
using Mist.Core.Quests;
using Mist.Core.ReleaseReadiness;

namespace Mist.Application.Finale;

public sealed partial class FinaleService
{
    // These fragments consume the committed ending's captured public results. They never
    // look at a later branch, infer a private reconciliation, or extend a timed permit.
    private static (FinaleBilingualText Text,ImmutableArray<string> Sources) RegionalCapturedFragment(FinalEndingSnapshot ending,string region)
    {
        T? Record<T>(string id,string kind) where T:class=>ending.History.SingleOrDefault(h=>h.Id==id&&h.Kind==kind) is{} h?StrictJson.Deserialize<T>(h.Json):null;
        bool Closure(string quest,string action)=>Record<QuestState>("quest:"+quest,"quest") is{Lifecycle:QuestLifecycle.Closed,Outcome:QuestOutcome.Completed}&&ending.History.Any(h=>h.Id==quest+":resolution"&&h.Kind=="receipt:"+action);
        var sources=ImmutableArray.CreateBuilder<string>();FinaleBilingualText text=new("No additional public regional-choice result is present in this snapshot. The account leaves that choice open rather than filling it from a general policy preference.","快照没有此项额外公开地区选择结果，记录保留空缺，不从最终政策偏好补写。 ");
        void Source(params string[] ids){foreach(string id in ids)if(ending.History.Any(h=>h.Id==id)&&!sources.Contains(id))sources.Add(id);}
        switch(region)
        {
            case "R1" when Closure("Q-104","tidegate_vessel_handover"):
                var receipt=ending.History.Single(h=>h.Id=="Q-104:resolution"&&h.Kind=="receipt:tidegate_vessel_handover");var hand=StrictJson.Deserialize<TidegateHandover>(receipt.Json);
                text=new($"The harbour's retained handover names the original {hand.VesselDefinition} and watch officer {hand.WatchOfficer}. The voyage record does not turn a temporary use transfer into ownership or erase the planned-test and removed-check findings. A later ferry or land-bridge priority is not invented where no separate result was recorded.",$"港口原移交仍记同一艘{hand.VesselDefinition}及值守{hand.WatchOfficer}。航行记录不会把临时使用权变成所有权，也不抹去预定测试与撤掉复核的发现。没有单独成果的额外渡运或陆桥优先安排不补写。");Source("Q-104:resolution","quest:Q-104","finding:K-ACCIDENT-CAUSE");break;
            case "R2" when Closure("Q-204","chapter_two_archive_handover_received")&&Record<ChapterTwoState>("chapter2","chapter_two_state") is{} moss:
                string schedule=moss.Load==LocalLoad.Kitchen?"the kitchen branch only":moss.Load==LocalLoad.Alternating?"alternating kitchen and washing windows":"an unverified additional schedule";string scheduleZh=moss.Load==LocalLoad.Kitchen?"只供厨房支路":moss.Load==LocalLoad.Alternating?"厨房与洗涤轮流窗口":"未经核验的额外排程";
                text=new($"The retained local reserve check records {schedule}. Its configuration was {(moss.LocalStable?"actually checked locally":"not backed by a current local check")}; it is not a spare capacity claim for every tower. The necessary-field archive handover remains distinct from any witness's permission to release an identity. Later public work still uses that boundary.",$"保留的局部备用检查记载{scheduleZh}。该配置{(moss.LocalStable?"有真实本地当前检查":"没有当前本地检查支持")}，不等于每座塔都多出相同容量。必要字段档案移交仍不同于证人身份公开同意，后续公共工作保留这条边界。");Source("chapter2","Q-203:resolution","Q-204:resolution","R2:w04_scope","R2:w04_handover");break;
            case "R3" when Closure("Q-304","chapter_three_tower_handover_received")&&Record<ChapterThreeState>("chapter3","chapter_three_state") is{} mine:
                bool reduce=mine.Policy?.Mode=="early_reduce";string choice=reduce?"the earlier immediate reduction":mine.Policy?.Mode=="time_limited_transition"?"the earlier transition bounded to its declared watch":"no additional load-policy choice";string choiceZh=reduce?"此前立即降载":mine.Policy?.Mode=="time_limited_transition"?"此前仅限声明班次的过渡":"没有额外降载选择";
                text=new($"The mining record retains {choice}. That watch is not silently extended for six months. {(mine.WorkersReceived?"The actual worker reception remains recorded.":"No completed worker reception is supplied here.")} Basic household protection precedes the recorded discretionary allocation; MC-2's incomplete checks and retained instruments remain available for responsibility review.",$"矿区保留{choiceZh}，没有把原班次静默续成六个月许可。{(mine.WorkersReceived?"真实工人接收仍有记录。":"此处不补已完成工人接收。")}家庭基本保障先于已记自由资源安排；MC-2未齐检查及原始仪表仍供责任复核。");Source("chapter3","Q-301:resolution","Q-302:resolution","Q-303:resolution","Q-304:resolution","R3:actual_handover");break;
            case "R4" when Closure("Q-404","glasshaven_technical_handover_received")&&Record<ChapterFourState>("chapter4","chapter_four_state") is{} glass:
                var trials=ending.History.Where(h=>h.Kind=="experiment_run").Select(h=>(History:h,Run:StrictJson.Deserialize<ExperimentRun>(h.Json))).ToArray();bool failed=trials.Any(t=>t.Run.Minutes==10&&t.Run.Outcome=="safe_stopped"),stable=trials.Any(t=>t.Run.Configuration.Controller=="C"&&t.Run.Minutes==10&&t.Run.Outcome=="stable");
                text=new($"Glasshaven keeps {(failed?"the recorded safely stopped counterexamples":"the existing experiment record without supplying a missing counterexample")} and {(stable?"the bounded ten-minute C trial":"no invented successful C trial")}. Neither becomes a certificate that every later network condition is safe. The captured prism watch preserves protection {glass.PrismLoad.Protection}, calibration {glass.PrismLoad.Calibration} and demonstration {glass.PrismLoad.Demonstration}; public technical scrutiny remains separate from an official's documented responsibility.",$"玻璃学城保留{(failed?"已记录安全中止反例":"原有实验记录，不补缺失反例")}与{(stable?"有限十分钟C试验":"未经补写的C成功结果空缺")}，均不成为今后一切网络条件安全的证书。快照棱镜班次保留保护{glass.PrismLoad.Protection}、校准{glass.PrismLoad.Calibration}、展示{glass.PrismLoad.Demonstration}；公共技术复核与官员已记责任分别保留。");Source("chapter4","Q-402:resolution","Q-403:resolution","Q-404:resolution","R4:actual_handover");Source(trials.Select(t=>t.History.Id).ToArray());break;
            case "R5" when Closure("Q-504","old_city_joint_handover_received")&&Record<ChapterFiveState>("chapter5","chapter_five_state") is{} capital:
                var rescue=Record<MaraRescue>("R5:mara","mara_rescue");bool received=rescue is{Status:"received_alive",Reception:"N-ADA_and_R5-local-receiver"}&&ending.History.Any(h=>h.Id=="R5:mara_received"&&h.Kind=="receipt:old_city_receive_mara");
                text=new($"The old-capital account retains {(received?"the directly verified living rescue and actual reception at that time":"no invented living rescue result")}; it does not rewrite the historical D-1 entry or supply a later medical report. {(capital.OriginalCasePreserved?"The original responsibility case remains preserved.":"No new preserved-case claim is added.")} Its captured custody is '{capital.Custody?.Mode??"unrecorded"}', with publication '{capital.Custody?.Publication??"unrecorded"}'. Design, policy and execution responsibility do not merge into a single pardon under the selected final direction.",$"旧都记录保留{(received?"当时直接核验的存活救援与真实接收":"没有补写的存活救援成果")}，不改历史D-1条目，也不补后来医疗报告。{(capital.OriginalCasePreserved?"原责任案仍保管。":"没有新增原案已保管说法。")}快照保管为“{capital.Custody?.Mode??"未记录"}”，公开范围为“{capital.Custody?.Publication??"未记录"}”。设计、政策及执行责任不会因最终方向合并成统一赦免。");Source("chapter5","R5:mara","R5:mara_received","Q-502:resolution","Q-503:resolution","Q-504:resolution","R5:actual_handover");break;
            case "R6" when Closure("Q-603","main_quest_resolution")&&Record<ChapterSixState>("chapter6","chapter_six_state") is{} frontier:
                text=new($"The frontier retains {(frontier.CargoDelivered&&frontier.CargoProtected?"the actually delivered and protected tower cargo":"no invented complete tower delivery")} and the checked ridge return. That tower consignment is not counted again as a household gift. {(frontier.SupplementDelivered>0?"A separate supplementary delivery records "+frontier.SupplementDelivered+" units.":"No supplementary household delivery is inserted into this account.")} The measured current overload finding remains scoped to its survey window; continued watch work cannot promise that natural mist has vanished.",$"边境保留{(frontier.CargoDelivered&&frontier.CargoProtected?"真实交付并保护的塔站货物":"没有补写的塔站完整交付")}及受检山脊返程。这批塔料不再重复算家庭赠送。{(frontier.SupplementDelivered>0?"另行补充交付记录了"+frontier.SupplementDelivered+"单位。":"本记录不插入额外家庭交付。")}当前过载实测发现仍限原调查窗口，继续值守不能承诺自然雾已消失。");Source("chapter6","Q-601:resolution","Q-602:resolution","Q-603:resolution","Q-604:resolution");break;
        }
        return(text,sources.ToImmutable());
    }
}

using Godot;
using System.Collections.Immutable;
using Mist.Application.Contracts;
using Mist.Application.UI;
using Mist.Core.Contracts;
using Mist.Core.Progression;
using Mist.Core.Skills;

public partial class Root
{
    private async ValueTask OpenParty()
    {
        var hero=await _party.InspectAsync();var snapshot=await _store.SnapshotAsync();var flow=Panel("队伍与装备",InputOwner.Workspace);
        Label(flow,$"主角 · 等级 {hero.Level} / 校准 {hero.Grade}\n经验 {snapshot.Balances.GetValueOrDefault("experience")} · 刻币 {snapshot.Balances.GetValueOrDefault("marks")} · 零件 {snapshot.Balances.GetValueOrDefault("parts")}",24);
        var d=hero.Derived;Label(flow,$"最大生命 {d.MaxHp} · 最大专注 {d.MaxFocus} · 先手 {d.Initiative}\n当前武器输出 {d.AttackUnits/10m:0.0} · 技术输出 {d.TechUnits/10m:0.0}\n装备减伤 {d.ArmorBp/100m:0.##}% · 基础防御 {d.GuardBp/100m:0.##}%\n未分配属性 {hero.UnspentAttributes} · 未学习技能点 {hero.UnspentSkills}",20);
        Label(flow,"地方护卫与地方支援填补两个援助席。正式同行邀请按实际剧情交接开放。",18);Button(flow,"人物、再邀请与同行安排",()=>Execute(async()=>await OpenPeople()));
        Button(flow,"调整整套属性与天赋",()=>OpenBuild(hero));Button(flow,"查看个人商店与改件",()=>Execute(async()=>await OpenShop()));
        Label(flow,"实际收藏与占用",22);
        foreach(var item in snapshot.Items.Where(i=>i.OwnerId=="hero"&&i.OwnerKind is "player" or "equipped"))
        {
            string name=_party.Definitions.Items.GetValueOrDefault(item.DefinitionId)?.Name??_party.Definitions.Modules.GetValueOrDefault(item.DefinitionId)?.Name??item.DefinitionId;
            var row=new VBoxContainer();flow.AddChild(row);Label(row,name+(item.OwnerKind=="equipped"?" · 已装备 / "+item.Slot:" · 当前持有"),20);
            if(_party.Definitions.Items.TryGetValue(item.DefinitionId,out var eq))
            {
                Label(row,eq.Effect,18);string slot=eq.Slot=="relic"?"relic1":eq.Slot;
                Button(row,"装备到 "+slot,()=>Execute(async()=>{CheckCommit(await _party.EquipAsync(item.Id,slot));ClosePanel();await Refresh();await OpenParty();}));
                if(eq.Slot=="relic"&&hero.Grade>=2)Button(row,"装备到第二遗物槽",()=>Execute(async()=>{CheckCommit(await _party.EquipAsync(item.Id,"relic2"));ClosePanel();await Refresh();await OpenParty();}));
            }
            else if(_party.Definitions.Modules.ContainsKey(item.DefinitionId))Button(row,"选择安装目标",()=>OpenModuleTransfer(item.Id));
        }
        foreach(var installed in snapshot.Items.Where(i=>i.OwnerKind=="module_installed"))
        {
            Label(flow,installed.DefinitionId+" · 安装在 "+installed.OwnerId,18);Button(flow,"免费转移这个原件",()=>OpenModuleTransfer(installed.Id));
        }
        Label(flow,"关键支架、电池、随船设备和收藏没有出售入口。",18);
    }
    private void OpenBuild(Mist.Application.Inventory.CharacterView hero)
    {
        var flow=Panel("整套构筑提案",InputOwner.Workspace);Label(flow,"在广场或事务所安全点确认。提案不合法时当前构筑保留；重配免费，既有升级点不会重新领取。",20);
        var stats=new List<SpinBox>();string[] names=["体魄","力量","敏捷","感知","专注"];
        for(int i=0;i<names.Length;i++){var row=new HBoxContainer();flow.AddChild(row);Label(row,names[i],20);var number=new SpinBox { MinValue=5,MaxValue=25,Step=1,Value=hero.Build.Attributes.Values[i],AccessibilityName=names[i]+"提案数值",CustomMinimumSize=new(170,46) };row.AddChild(number);stats.Add(number);}
        var definitions=TalentRules.Read(_catalog);var learned=new Dictionary<string,CheckBox>();Label(flow,$"已获技能点 {ProgressionRules.SkillBudget(hero.Level)}；主动栏最多四项。被动无需占栏。",20);
        foreach(var talent in definitions.Values.OrderBy(t=>t.Id))
        {
            var check=new CheckBox { Text=talent.Id+" · "+talent.Name+" · 最低等级 "+talent.MinimumLevel,ButtonPressed=hero.Build.Learned.Contains(talent.Id),AccessibilityName=talent.Name+"学习提案" };flow.AddChild(check);learned[talent.Id]=check;
            if(talent.Prerequisites.Length>0)Label(flow,"直接前置："+string.Join("、",talent.Prerequisites)+" · 此前本树投入 "+talent.RequiredTreePoints,16);
        }
        var activeIds=new[]{""}.Concat(definitions.Values.Where(t=>t.Active).OrderBy(t=>t.Id).Select(t=>t.Id)).ToArray();var bars=new List<OptionButton>();
        for(int i=0;i<4;i++)
        {
            var slot=new OptionButton { FitToLongestItem=false,AccessibilityName="主动槽 "+(i+1) };flow.AddChild(slot);foreach(string id in activeIds)slot.AddItem(id==""?"留空":id+" · "+definitions[id].Name);slot.Select(i<hero.Build.Active.Length?Array.IndexOf(activeIds,hero.Build.Active[i]):0);bars.Add(slot);
        }
        Button(flow,"核对并确认整套重配",()=>Execute(async()=>
        {
            int[] values=stats.Select(s=>(int)s.Value).ToArray();var build=new BuildState(new(values[0],values[1],values[2],values[3],values[4]),learned.Where(p=>p.Value.ButtonPressed).Select(p=>p.Key).ToImmutableArray(),bars.Where(b=>b.Selected>0).Select(b=>activeIds[b.Selected]).ToImmutableArray());
            TalentRules.Validate(hero.Level,build,definitions);CheckCommit(await _party.ApplyBuildAsync(build));ClosePanel();ClosePanel();await Refresh();await OpenParty();
        }));
    }
    private void OpenModuleTransfer(string module)
    {
        var flow=Panel("转移已有改件",InputOwner.Confirmation);Label(flow,"使用同一个原件，转移费用为零；目标已有改件会移回个人持有物。",20);
        var def=_party.Definitions.Modules[_view.Snapshot.Items.Single(i=>i.Id==module).DefinitionId];
        foreach(var equipment in _view.Snapshot.Items.Where(i=>i.OwnerId=="hero"&&i.OwnerKind is "player" or "equipped"&&_party.Definitions.Items.TryGetValue(i.DefinitionId,out var d)&&d.Slot==def.Slot))
        {
            Button(flow,"安装到 "+_party.Definitions.Items[equipment.DefinitionId].Name,()=>Execute(async()=>{CheckCommit(await _party.InstallModuleAsync(module,equipment.Id));ClosePanel();ClosePanel();await Refresh();await OpenParty();}));
        }
    }
    private async ValueTask OpenShop()
    {
        var snapshot=await _store.SnapshotAsync();var hero=_party.Inspect(snapshot);var flow=Panel("公开目录与个人补给",InputOwner.Workspace);Label(flow,"采购与制作在广场或事务所安全点办理。目录型号按实际任务回执开放。",20);
        Label(flow,$"刻币 {snapshot.Balances.GetValueOrDefault("marks")} · 零件 {snapshot.Balances.GetValueOrDefault("parts")} · 校准 {hero.Grade}",22);
        foreach(var item in _party.Definitions.Items.Values.Where(i=>i.MarksPrice>0).OrderBy(i=>i.Id))
        {
            var button=Button(flow,item.Name+" · "+item.MarksPrice+"刻币"+(snapshot.Items.Any(i=>i.DefinitionId==item.Id)?" · 已收藏":""),()=>PurchaseConfirmation(item.Name+" · 扣除 "+item.MarksPrice+"刻币",request=>_party.BuyCollectionAsync(item.Id,request)));button.Disabled=snapshot.Items.Any(i=>i.DefinitionId==item.Id);
        }
        Label(flow,"改件制作与升级",22);
        foreach(var module in _party.Definitions.Modules.Values.OrderBy(m=>m.Id))
        {
            Label(flow,module.Name+$" · {module.Marks}刻币 + {module.Parts}零件 · 最低校准 {module.MinimumRank}",20);
            if(module.ConsumesModule is null)Button(flow,"制作 "+module.Name,()=>PurchaseConfirmation(module.Name+$" · 扣除 {module.Marks}刻币与 {module.Parts}零件，制作新的个人原件",request=>_party.CraftModuleAsync(module.Id,null,request)));
            else foreach(var original in snapshot.Items.Where(i=>i.DefinitionId==module.ConsumesModule&&i.OwnerKind is "player" or "module_installed"))
            {
                Button(flow,"消耗 "+original.Id[^6..]+" 升级为 "+module.Name,()=>PurchaseConfirmation($"消耗原件 {original.Id[^6..]}、{module.Marks}刻币与 {module.Parts}零件，制作 "+module.Name,request=>_party.CraftModuleAsync(module.Id,original.Id,request)));
            }
        }
        Label(flow,"个人增强补给（入口最多携带两份）",22);
        foreach(var definition in _catalog.Definitions["consumables"])
        {
            using var json=System.Text.Json.JsonDocument.Parse(definition.PayloadJson);int price=json.RootElement.GetProperty("marks_price").GetInt32();Button(flow,definition.Name+" · "+price+"刻币",()=>PurchaseConfirmation(definition.Name+" · 扣除 "+price+"刻币",request=>_party.BuyConsumableAsync(definition.Id,request)));
        }
        foreach(var item in snapshot.Items.Where(i=>i.OwnerKind=="player"&&i.OwnerId=="hero"&&i.DefinitionId.StartsWith("CON-",StringComparison.Ordinal)))
        {
            Button(flow,"出售个人补给 "+item.DefinitionId+" · 原价四分之一",()=>PurchaseConfirmation("出售这一个付费个人补给实例；出售后不再能带入遭遇。",request=>_party.SellAsync(item.Id,request),"明确出售此件"));
        }
    }
    private void PurchaseConfirmation(string description,Func<CommandId,ValueTask<CommandResult>> operation,string actionLabel="明确执行此项采购或制作")
    {
        var confirm=Panel("确认个人交易",InputOwner.Confirmation);Label(confirm,description,22);Label(confirm,"如果结果暂时不明，保留本页并再次使用同一确认按钮，先按原请求核对回执。",18);var request=new CommandId(Guid.NewGuid());
        Button(confirm,actionLabel,()=>Execute(async()=>
        {
            try{CheckCommit(await operation(request));}
            catch
            {
                var receipt=await _store.FindReceiptAsync(_store.Session.Branch,request);if(receipt is null)throw;_status.Text="已按原请求查到提交回执。";
            }
            ClosePanel();ClosePanel();await Refresh();await OpenShop();
        }));
    }
}

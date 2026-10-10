using System.Collections.Immutable;
using Mist.Application.Storage;
using Mist.Core.Content;

namespace Mist.Application.Materials;

public static class MaterialJournal
{
    public static ImmutableArray<MaterialDefinition> Visible(RuntimeCatalog catalog, BranchSnapshot snapshot) => catalog.Materials.Where(m => snapshot.Entities.Any(e => e.Id == "material:" + m.Id)).ToImmutableArray();
    public static MaterialDefinition Open(RuntimeCatalog catalog, BranchSnapshot snapshot, string id) => Visible(catalog, snapshot).SingleOrDefault(m => m.Id == id) ?? throw new InvalidOperationException("材料尚未取得，搜索和读屏不显示隐藏正文。");
    public static string Lookup(string word) => word.Trim().ToLowerInvariant() switch
    {
        "draught" or "draft" => "draught（航运）：船体在水面以下的深度，吃水。draft 也可指草稿；按句子语境区分。",
        "subject" => "be subject to：受到某条件或限制约束。",
        "bracket" => "bracket：固定部件的支架。",
        "isolate" or "isolated" or "isolation" => "isolate：隔离、切断与其他设备的连接；此处按设备说明理解。",
        "operational" => "operational：能正常运行、可投入使用。",
        "supersede" or "supersedes" => "supersede：取代先前有效版本或决定。需核对其替代范围。",
        "interlock" => "interlock：只有指定条件成立才允许操作的联锁。",
        "reserve" => "reserve：备用、储备。核对所属设备及使用范围。",
        "beacon" => "beacon：用于导航或标识位置的信号灯。",
        _ => "本地词典未收录这个词。可保留选区和笔记；联网词典默认关闭。"
    };
}

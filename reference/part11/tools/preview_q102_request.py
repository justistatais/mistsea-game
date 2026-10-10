"""Produce one limited request from actual inherited source paragraphs.
No online model call. The illustrative reply is manually specified, then checked.
"""
from pathlib import Path
import json,sys
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'validation'))
from pipeline_reference import RuntimeContext,canonical_hash,project_sources,build_request,validate_reply
raw=json.loads((ROOT/'data/Q102_Canonical_Input_v0.1.json').read_text(encoding='utf-8'))
rows=[]
for m in raw['materials']:
 for p in m['paragraphs']:
  rows.append({'asset_id':m['id'],'version':m['version'],'segment_id':p['id'],'text':p['text'],'audience':'player_visible'})
selected='Q102-R-NOTICE';pid=selected+'-p02'
view=project_sources(rows,{selected+'::'+pid:'0.1'},{selected},cloud_consent=True)
question='Explain the words only if in this paragraph.'
ctx=RuntimeContext('synthetic-demo',0,1,1,'demo-branch',1,'CB-Q102-CANDIDATE-011','demo-question',1,canonical_hash(question),'sentence_only','no-provider','PR-CAI-10@0.1')
req=build_request(ctx,'explain',question,view)
source=view[0];start=source['text'].index('only if')
reply={'request_id':req['request_id'],'mode':'explain','status':'candidate','items':[{'text':'only if 表示必要条件：不满足这个条件就不能做前面的事，但这一处措辞没有声称它就是全部条件。','citation':{'asset_id':selected,'version':'0.1','segment_id':pid,'start':start,'end':start+7,'quote':'only if'}}]}
validate_reply(reply,req)
(ROOT/'data/Projected_Request_Example_v0.1.json').write_text(json.dumps({'model_call_performed':False,'request':req,'manual_fixture_reply':reply,'validation':'shape_and_span_only_not_model_or_language_calibration'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Projected',len(view),'actual source paragraph; original items/answers not included; no model call.')

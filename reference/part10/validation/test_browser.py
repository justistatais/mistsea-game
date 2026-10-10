"""Browser checks for the exact supplied HTML, rendered via set_content.

The managed Chromium environment blocks URL navigation (including localhost and
file:). These checks do NOT demonstrate file loading or persistent-origin storage.
The opaque-origin storage failure is deliberately left intact, not shimmed.
"""
from pathlib import Path
import os, shutil
import json, time, traceback
from playwright.sync_api import sync_playwright
ROOT=Path(__file__).resolve().parents[1]
HTML=(ROOT/'prototype/UI_Wireframe_v0.1.html').read_text()
TESTS=[]
def case(name):
    def deco(fn):TESTS.append((name,fn));return fn
    return deco

def writer(p):p.get_by_role('button',name='继续同一份草稿',exact=True).click()
def go(p,name):p.locator(f'nav button[data-go="{name}"]').click()
def prepare(p):
    go(p,'tasks');p.locator('#fixtureDetails summary').click();p.locator('#fixtureBracket').check();p.locator('#fixtureBattery').check();p.locator('#fixtureTest').click();p.locator('section[data-page="tasks"] button[data-go="writing"]').click()
def plan(p,route='C'):
    if not p.locator('#assistDetails').evaluate('(el)=>el.open'):p.locator('#assistDetails summary').click()
    p.locator('#routeChoice').select_option(route)
    for id in ['planRepair','planTest','planMainOff','planLimits']:p.locator('#'+id).check()
    p.locator('#confirmPlan').click()
def no_horizontal(p):
    dims=p.evaluate('({width:innerWidth, scroll:document.documentElement.scrollWidth})')
    assert dims['scroll']<=dims['width']+1,dims

@case('B01 页面装载、六入口、明确非游戏范围')
def _(p):
    assert p.locator('nav button').count()==6
    assert p.locator('#scopeNotice').inner_text().find('不是游戏')>=0
    assert p.evaluate('typeof UI_DEMO')=='object';no_horizontal(p)

@case('B02 编辑器字母、数字和回车不导航或执行')
def _(p):
    writer(p);p.locator('#draft').fill('');p.locator('#draft').press_sequentially('wasd mjbcl 1234')
    p.keyboard.press('Enter');p.keyboard.press('Control+Enter')
    assert p.evaluate('UI_DEMO.getState().view')=='writing'
    assert p.evaluate('UI_DEMO.getState().commitCount')==0
    assert '\n' in p.locator('#draft').input_value()

@case('B03 合成输入法事件优先处理Esc和Enter')
def _(p):
    writer(p);p.locator('#draft').focus();p.locator('#draft').dispatch_event('compositionstart',{'data':'输'})
    p.keyboard.press('Escape');p.keyboard.press('Enter')
    assert p.evaluate('UI_DEMO.getState().view')=='writing'
    assert p.evaluate('UI_DEMO.getState().commitCount')==0
    p.locator('#draft').dispatch_event('compositionend',{'data':'输入'})

@case('B04 受限存储时Ctrl+S明示失败并保留正文')
def _(p):
    writer(p);p.locator('#draft').fill('Do not restart the main generator.');p.keyboard.press('Control+s')
    assert '失败' in p.locator('#saveStatus').inner_text()
    assert p.locator('#draft').input_value()=='Do not restart the main generator.'
    assert p.evaluate('UI_DEMO.getState().savedRevision < UI_DEMO.getState().revision')

@case('B05 材料和任务两个入口共用同一草稿')
def _(p):
    writer(p);p.locator('#draft').fill('One canonical draft.');go(p,'archive')
    p.locator('section[data-page="archive"] button[data-go="writing"]').click()
    assert p.locator('#draft').input_value()=='One canonical draft.'
    go(p,'tasks');p.locator('section[data-page="tasks"] button[data-go="writing"]').click()
    assert p.locator('#draft').input_value()=='One canonical draft.'

@case('B06 无音轨不模拟播放，脚本主动展开并记录')
def _(p):
    p.get_by_role('button',name='打开录音工作区',exact=True).click()
    assert p.locator('#archiveBody').inner_text()==''
    assert '音轨尚未制作' in p.locator('#audioUnavailable').inner_text()
    assert p.locator('audio').count()==0
    p.locator('#revealScript').click()
    assert len(p.locator('#archiveBody').inner_text())>100
    events=p.evaluate('UI_DEMO.getState().support')
    assert len(events)==1 and '非听力播放' in events[0]['kind']

@case('B07 词义弹窗取消焦点与关闭恢复')
def _(p):
    writer(p);p.locator('#dictionaryWriter').click()
    assert p.evaluate('document.activeElement.id')=='modalCancel'
    p.locator('#modalCancel').click()
    assert p.evaluate('document.activeElement.id')=='dictionaryWriter'
    assert p.evaluate('UI_DEMO.getState().commitCount')==0

@case('B08 准备、方案确认、预览不自动离港')
def _(p):
    prepare(p);p.locator('#draft').fill('Use the permitted route after the check.');plan(p)
    assert not p.evaluate('UI_DEMO.getState().departed')
    p.locator('#previewDeparture').click()
    assert p.evaluate('document.activeElement.id')=='modalCancel'
    assert p.evaluate('UI_DEMO.getState().commitCount')==0
    p.locator('#modalCancel').click();assert not p.evaluate('UI_DEMO.getState().departed')

@case('B09 配置拔插使旧预览失效')
def _(p):
    prepare(p);plan(p);p.locator('#previewDeparture').click()
    p.evaluate("UI_DEMO.setFixture('battery',false);UI_DEMO.setFixture('battery',true)")
    p.locator('#modalCommit').click();assert '过期' in p.locator('#modalError').inner_text()
    assert p.locator('#modalCommit').is_disabled();assert p.evaluate('UI_DEMO.getState().commitCount')==0

@case('B10 正文修改使旧预览失效')
def _(p):
    prepare(p);plan(p);p.locator('#previewDeparture').click();p.evaluate("UI_DEMO.setBody('A changed draft.')")
    p.locator('#modalCommit').click();assert '过期' in p.locator('#modalError').inner_text()
    assert p.evaluate('UI_DEMO.getState().commitCount')==0

@case('B11 字号变化不改变合法行动条件')
def _(p):
    prepare(p);plan(p);p.locator('#previewDeparture').click()
    p.evaluate("document.getElementById('fontScale').value='1.25';document.getElementById('fontScale').dispatchEvent(new Event('change'))")
    p.locator('#modalCommit').click();assert p.evaluate('UI_DEMO.getState().commitCount')==1

@case('B12 正常执行只生成一次演示回执')
def _(p):
    prepare(p);p.locator('#draft').fill('Retain this draft after departure.');plan(p);p.locator('#previewDeparture').click();p.locator('#modalCommit').click()
    assert p.evaluate('UI_DEMO.getState().view')=='tasks';assert '玩家留在广场' in p.locator('#taskStatus').inner_text()
    p.evaluate("document.getElementById('modalCommit').click()")
    assert p.evaluate('UI_DEMO.getState().commitCount')==1
    assert p.evaluate('UI_DEMO.getState().body')=='Retain this draft after departure.'

@case('B13 1280窗口200%字重排且按钮可达')
def _(p):
    go(p,'settings');p.locator('#fontScale').select_option('2');p.locator('#home').click();writer(p)
    assert not p.locator('#refSide').is_visible();assert p.locator('#draft').is_visible()
    p.locator('#previewDeparture').scroll_into_view_if_needed();assert p.locator('#previewDeparture').is_visible();no_horizontal(p)
    p.locator('#showRefs').click();assert p.locator('#refSide').is_visible()
    p.locator('#showEditor').click();assert p.locator('#draft').is_visible()
    p.screenshot(path=str(ROOT/'validation/qa_writing_200.png'),full_page=True)

@case('B14 900窗口材料与编辑切换保留正文')
def _(p):
    p.set_viewport_size({'width':900,'height':700});writer(p);p.locator('#draft').fill('Keep this in narrow mode.')
    p.locator('#showRefs').click();assert p.locator('#refSide').is_visible();p.locator('#showEditor').click()
    assert p.locator('#draft').input_value()=='Keep this in narrow mode.';no_horizontal(p)

@case('B15 1920窗口双栏显示且无整体横向溢出')
def _(p):
    p.set_viewport_size({'width':1920,'height':1080});writer(p)
    assert p.locator('#refSide').is_visible() and p.locator('#draft').is_visible();no_horizontal(p)
    p.screenshot(path=str(ROOT/'validation/qa_writing_1920.png'),full_page=True)

@case('B16 正文中的HTML不作为脚本执行')
def _(p):
    writer(p);sample='<img src=x onerror="window.INJECTED=1">';p.locator('#draft').fill(sample)
    go(p,'tasks');p.locator('section[data-page="tasks"] button[data-go="writing"]').click()
    assert p.locator('#draft').input_value()==sample;assert p.evaluate('window.INJECTED === undefined');assert p.locator('img').count()==0

@case('B17 确认弹窗键盘焦点不落入背景')
def _(p):
    prepare(p);plan(p);p.locator('#previewDeparture').click()
    for _ in range(8):
        p.keyboard.press('Tab');assert p.evaluate("Boolean(document.activeElement.closest('dialog'))")
    p.keyboard.press('Escape');assert not p.locator('#modal').evaluate('(el)=>el.open')
    assert p.evaluate('document.activeElement.id')=='previewDeparture'

@case('B18 使用帮助及演示完成后仍不造语言分数')
def _(p):
    prepare(p);plan(p);p.locator('#previewDeparture').click();p.locator('#modalCommit').click();go(p,'learning')
    content=p.locator('section[data-page="learning"]').inner_text()
    assert '没有语言评分' in content and '证据不足' in content and '结构化方案确认' in content

@case('B19 另一写入者冲突变只读且不覆盖当前稿')
def _(p):
    writer(p);p.locator('#draft').fill('Local current text.')
    p.evaluate("window.dispatchEvent(new StorageEvent('storage',{key:UI_DEMO.key,newValue:JSON.stringify({body:'Other window',revision:99})}))")
    assert p.locator('#draft').get_attribute('readonly') is not None
    assert p.locator('#draft').input_value()=='Local current text.'
    p.locator('#previewDeparture').click();assert '另一写入者' in p.locator('#precheck').inner_text()

@case('B20 不可行航道不会被非空正文放行')
def _(p):
    prepare(p);p.locator('#draft').fill('A fluent-looking but unchecked answer.');plan(p,'B');p.locator('#previewDeparture').click()
    assert '适用限制' in p.locator('#precheck').inner_text();assert not p.locator('#modal').evaluate('(el)=>el.open')
    assert p.evaluate('UI_DEMO.getState().commitCount')==0

if __name__=='__main__':
    results=[]
    with sync_playwright() as pw:
        browser=pw.chromium.launch(executable_path=os.environ.get('CHROMIUM_EXECUTABLE') or shutil.which('chromium'),headless=True,args=['--no-sandbox'])
        browser_version=browser.version
        for name,fn in TESTS:
            page=browser.new_page(viewport={'width':1280,'height':720},device_scale_factor=1)
            page.set_default_timeout(4000);errors=[];requests=[]
            page.on('pageerror',lambda e:errors.append(str(e)))
            page.on('request',lambda req:requests.append(req.url))
            start=time.monotonic()
            try:
                page.set_content(HTML,wait_until='load');fn(page)
                assert not errors,errors
                # set_content uses no network; the prototype has no remote resources.
                assert not requests,requests
                result={'name':name,'status':'passed','seconds':round(time.monotonic()-start,3)}
            except Exception as e:
                result={'name':name,'status':'failed','error':str(e),'traceback':traceback.format_exc()}
                try:page.screenshot(path=str(ROOT/'validation'/f'failure_{name[:3]}.png'),full_page=True)
                except Exception:pass
            results.append(result);print(name,result['status'],flush=True);page.close(run_before_unload=False)
        browser.close()
    report={'browser':'Chromium '+browser_version,'loader':'Playwright set_content; no navigation; opaque origin','storage_scope':'Browser storage unavailable at opaque origin; failure UX exercised, real local persistence/reload NOT tested', 'native_ime_tested':False,'screen_reader_tested':False,'real_engine_tested':False,'cases':results,'passed':sum(x['status']=='passed' for x in results),'failed':sum(x['status']=='failed' for x in results)}
    (ROOT/'validation/browser_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print('TOTAL',len(results),'PASSED',report['passed'],'FAILED',report['failed'])
    raise SystemExit(bool(report['failed']))

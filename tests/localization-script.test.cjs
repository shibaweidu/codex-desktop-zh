// Local fixtures only: no real Codex process, account, or debugging port.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const read = name => fs.readFileSync(path.join(__dirname, '../shared', name), 'utf8');
const helper = read('app-services-script.js');
const locale = (value, allowReload=true) => helper + ';\n' + read('locale-script.js')
  .replace('__LOCALE_JSON__', JSON.stringify(value)).replace('__ALLOW_RELOAD_JSON__', JSON.stringify(allowReload));
const verify = value => read('locale-verification.js').replace('__LOCALE_JSON__', JSON.stringify(value));
const groups = JSON.parse(read('menu-translations.json'));
const menu = helper + ';\n' + read('menu-script.js')
  .replace('__TRANSLATIONS_JSON__', JSON.stringify({...groups.common,...groups.windows}))
  .replace('__PLATFORM_JSON__', '"windows"');

(async () => {
  const browser = await chromium.launch({headless:true, executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined});
  const page = await browser.newPage();
  let passed = 0;
  const check = (ok, name) => { assert.ok(ok, name); console.log('PASS '+name); passed++; };
  const runtime = String.raw`
    const settings = new Proxy(function(){}, {
      get: (target,key) => key==='read' ? async () => ({configured:window.fixtureLocale,effective:window.fixtureLocale}) :
        key==='write' ? async (key,value) => {
          if(key!=='localeOverride')throw Error('unexpected setting');
          if(window.refuseWrite)throw Error('write rejected');
          window.writes++; window.fixtureLocale=value;
          document.documentElement.lang=value;
          document.querySelector('#root').innerHTML=value==='zh-CN'?'<button>文件</button><button>编辑</button><button>帮助</button>':'<button>File</button><button>Edit</button><button>Help</button>';
        } : undefined
    });
    const original = {
      settings,
      applicationMenu: {
        getSnapshot: async () => window.snapshot,
        invokeItem: async function(id) { if(this!==original.applicationMenu)throw Error('lost receiver'); window.invoked=id; }
      }
    };
    let current=original;
    const provider={get appServices(){return current},setAppServices(s){current=s}};
    function runtime(id){if(id!=='services-module')throw Error('unexpected module');return provider}
    runtime.m={'services-module':function(){return {setAppServices:true,appServices:true,setAppHost:true}}};
    export {runtime as __webpack_require__};
  `;
  await page.route('http://codex-fixture.test/**', route => {
    if (route.request().url().endsWith('.js')) return route.fulfill({contentType:'text/javascript', body:runtime});
    return route.fulfill({contentType:'text/html',body:'<html lang="en"><head><script type="module" src="/assets/runtime.js"></script></head><body><div id="root"><button>File</button><button>Edit</button></div></body></html>'});
  });
  const reset = async () => {
    await page.goto('http://codex-fixture.test/');
    await page.evaluate(() => {
      window.fixtureLocale='en-US';window.writes=0;
      window.snapshot={'file-menu':[{type:'item',nativeCommandId:10,label:'New Chat',enabled:true}], 'view-menu':[{type:'item',nativeCommandId:20,label:'Switch between Chat and tabs',enabled:false}]};
      window.electronBridge={sendMessageFromView:()=>{throw Error('legacy fetch must not run')}};
    });
  };
  try {
    await reset();
    const setting=JSON.parse(await page.evaluate(locale('zh-CN')));
    check(setting.status==='ok'&&setting.source==='app-services'&&setting.changed,'modern_settings_service_used');
    check((await page.evaluate(()=>window.writes))===1,'writes_locale_only_once');
    check(JSON.parse(await page.evaluate(verify('zh-CN'))).status==='ok','actual_chinese_controls_verified');
    await page.evaluate(locale('zh-CN'));
    check((await page.evaluate(()=>window.writes))===1,'existing_chinese_setting_not_rewritten');
    const report=JSON.parse(await page.evaluate(menu));
    check(report.status==='ok'&&report.inspected===2&&report.changed===2,'new_windows_menu_service_translated');
    const snapshot=await page.evaluate(async()=>{const s=await window.__codexZhGetAppServices();await s.applicationMenu.invokeItem(20);return s.applicationMenu.getSnapshot()});
    check(snapshot['view-menu'][0].label==='在聊天与标签页之间切换','new_english_menu_label_covered');
    check(snapshot['view-menu'][0].nativeCommandId===20&&!snapshot['view-menu'][0].enabled,'menu_commands_and_enabled_state_preserved');
    check(await page.evaluate(()=>window.invoked===20),'menu_invocation_receiver_preserved');
    await page.evaluate(menu);
    check(JSON.parse(await page.evaluate(menu)).inspected===2,'reapply_does_not_nest_menu_wrappers');
    check(JSON.parse(await page.evaluate(locale('zh-CN'))).status==='ok','callable_rpc_settings_survives_menu_patch');
    await page.evaluate(()=>window.snapshot['view-menu'].push({label:'A future untranslated item',nativeCommandId:30}));
    check(JSON.parse(await page.evaluate(menu)).untranslated.includes('A future untranslated item'),'unknown_english_menu_reported');
    await page.evaluate(locale('en-US'));
    check(await page.evaluate(()=>!window.__codexZhMenuServicePatch),'english_mode_restores_original_menu_service');
    check(JSON.parse(await page.evaluate(verify('en-US'))).status==='ok','english_ui_verified');

    await reset();
    await page.evaluate(()=>window.refuseWrite=true);
    check(JSON.parse(await page.evaluate(locale('zh-CN'))).status==='partial','failed_setting_write_not_success');
    await page.evaluate(()=>{document.documentElement.lang='zh-CN';document.querySelector('#root').innerHTML='<article data-message-author-role="assistant"><button>文件</button><button>编辑</button></article><button>File</button><button>Edit</button>';});
    check(JSON.parse(await page.evaluate(verify('zh-CN'))).status==='partial','chinese_chat_content_cannot_fake_ui_success');
    await page.evaluate(()=>{document.querySelector('#root').innerHTML='<button>文件</button><button>编辑</button>';document.documentElement.lang='zh-CN';});
    check(JSON.parse(await page.evaluate(verify('zh-CN'))).status==='ok','new_menu_labels_replace_old_task_markers');
    await page.evaluate(()=>document.querySelector('#root').insertAdjacentHTML('beforeend','<button>Settings</button>'));
    check(JSON.parse(await page.evaluate(verify('zh-CN'))).status==='partial','mixed_language_controls_not_complete');

    await reset();
    await page.evaluate(()=>window.snapshot={});
    check(JSON.parse(await page.evaluate(menu)).status==='partial','empty_menu_not_complete');
    await page.evaluate(()=>{document.querySelectorAll('script').forEach(s=>s.remove());window.legacyCalls=0;window.electronBridge={sendMessageFromView:()=>{window.legacyCalls++}};});
    const safeFallback=JSON.parse(await page.evaluate(locale('zh-CN',false)));
    check(safeFallback.status==='unavailable'&&safeFallback.reason==='non-disruptive-locale-service-unavailable','non_restart_repair_refuses_legacy_reload');
    check(await page.evaluate(()=>window.legacyCalls===0),'non_restart_repair_never_sends_legacy_setting');
    const legacyMenu={items:[{label:'New Chat',submenu:{items:[{label:'Switch between Chat and tabs'}]}}]};
    const electronMenu={getApplicationMenu:()=>legacyMenu,setApplicationMenu:function(value){assert.equal(this,electronMenu);assert.equal(value,legacyMenu)}};
    const legacyScript=read('menu-script.js').replace('__TRANSLATIONS_JSON__',JSON.stringify({...groups.common,...groups.windows})).replace('__PLATFORM_JSON__','"windows"');
    const context=vm.createContext({require:name=>{assert.equal(name,'electron');return {Menu:electronMenu}}});
    const legacyReport=JSON.parse(await vm.runInContext(legacyScript,context));
    check(legacyReport.status==='ok'&&legacyMenu.items[0].label==='新建对话','legacy_electron_menu_still_translated');
    check(legacyMenu.items[0].submenu.items[0].label==='在聊天与标签页之间切换','legacy_nested_menu_translated');
    console.log('localization_script_tests='+passed+' passed');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});

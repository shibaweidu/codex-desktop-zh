// Isolated Chromium fixture only. Never connects to a running Codex/CDP port.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const script = fs.readFileSync(path.join(__dirname, '../shared/theme-script.js'), 'utf8');
const theme = { id:'violet',fontSize:14,background:'#1c1d23',sidebar:'#27272f',foreground:'#f5f4f8',accent:'#c4b5fd' };
(async()=>{
 const browser = await chromium.launch({headless:true, executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined});
 const page = await browser.newPage();
 let passed = 0;
 const check = (condition, name) => { assert.ok(condition, name);passed++;console.log('PASS '+name); };
 const reset = async (bridge=true) => {
   await page.setContent('<!doctype html><html><head><style id="original">#root{background:#eee;color:#111}code{color:red}</style></head><body><div id="root"><aside>sidebar</aside><code>syntax</code></div></body></html>');
   await page.evaluate(v=>{window.electronBridge=v ? {} : null;},bridge);
 };
 const apply = async value => JSON.parse(await page.evaluate(script.replace('__THEME_JSON__',JSON.stringify(value))));
 try {
   const windows=fs.readFileSync(path.join(__dirname,'../src/WorkspaceSettings.cs'),'utf8');
   const macos=fs.readFileSync(path.join(__dirname,'../macos/Sources/CodexZhLauncherMac/WorkspaceServices.swift'),'utf8');
   const w=[...windows.matchAll(/Create\("([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)"\)/g)].map(m=>m.slice(1));
   const m=[...macos.matchAll(/\.init\(id: "([^"]+)", name: "([^"]+)", background: "([^"]+)", sidebar: "([^"]+)", foreground: "([^"]+)", accent: "([^"]+)"\)/g)].map(m=>m.slice(1));
   assert.deepEqual(w,m);check(w.length===6,'windows_macos_palette_parity');
   for(const [id,name,background,sidebar,foreground,accent] of w){
     await reset();
     check((await apply({id,fontSize:14,background,sidebar,foreground,accent})).status==='ok','preset_'+id);
   }
   await reset();
   check((await apply(theme)).status==='ok','computed_colors_verified');
   check(await page.locator('#codex-zh-launcher-theme').count()===1,'one_owned_style');
   check(await page.locator('code').evaluate(el=>getComputedStyle(el).color)==='rgb(255, 0, 0)','syntax_color_untouched');
   check((await apply({...theme,background:'#121c29'})).status==='ok','replace_theme');
   check(await page.locator('#codex-zh-launcher-theme').count()===1,'no_duplicate_owned_styles');
   check((await apply({id:'default'})).status==='ok','reset_success');
   check(await page.locator('#original').count()===1 && await page.locator('#codex-zh-launcher-theme').count()===0,'reset_only_own_style');
   check(await page.locator('#root').evaluate(el=>getComputedStyle(el).backgroundColor)==='rgb(238, 238, 238)','original_appearance_restored');
   await reset(false);
   check((await apply(theme)).status==='unsupported','missing_bridge_rejected');
   await reset();
   check((await apply({...theme,background:'red;display:none'})).status==='error','css_injection_rejected');
   check((await apply({...theme,fontSize:19})).status==='error','font_bounds_rejected');
   await apply(theme);
   const previous=await page.locator('#codex-zh-launcher-theme').textContent();
   await page.locator('#root').evaluate(el=>el.style.setProperty('color','#123456','important'));
   check((await apply({...theme,background:'#121c29'})).status==='unsupported','computed_mismatch_not_success');
   check(await page.locator('#codex-zh-launcher-theme').textContent()===previous,'failed_apply_rolls_back');
   await page.locator('#root').evaluate(el=>el.remove());
   check((await apply(theme)).status==='unsupported','missing_root_rejected');
   console.log('theme_script_tests='+passed+' passed');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});

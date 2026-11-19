// Run through agent-browser eval --stdin after opening index.html freshly.
// Tests only the design prototype. Never connects to a native application.
(async () => {
  const results = [];
  const q = selector => document.querySelector(selector);
  const tick = () => new Promise(resolve => setTimeout(resolve, 40));
  const assert = (name, condition) => {
    results.push({name,passed:Boolean(condition)});
    if(!condition) throw new Error(name);
  };
  const click = async selector => {const el=q(selector);if(!el)throw new Error('Missing '+selector);el.click();await tick();};
  const go = page => click(`nav [data-page="${page}"]`);
  const set = async (selector,value,event='change') => {q(selector).value=value;q(selector).dispatchEvent(new Event(event,{bubbles:true}));await tick();};
  const waitFor = async condition => {const deadline=Date.now()+8000;while(!condition()){if(Date.now()>deadline)throw new Error('Timed out');await tick();}};
  const text = () => q('main').textContent;
  try {
    assert('赞助入口位于左侧栏而非顶部',q('.sidebar .sponsor')&&!q('.workspace-top .sponsor'));
    // file:// stylesheets may load while their cssRules are origin-protected.
    assert('苹果质感样式层已加载',q('link[href="apple-materials.css"]').sheet&&getComputedStyle(q('.sidebar .sponsor')).display==='grid');
    for(const page of ['overview','downloads','localization','themes','diagnostics']){
      await go(page);
      assert(`导航 ${page}`,q('h1') && q(`nav [data-page="${page}"]`).getAttribute('aria-current')==='page');
    }
    await go('downloads');
    assert('Windows 显示版本与包版本分开',text().includes('26.1111.21308')&&text().includes('26.1111.2228.0'));
    assert('Windows 官方指向 Store',q('main a.text-link').href==='https://apps.microsoft.com/detail/9plm9xgg6vks');
    await set('#platform','macos');
    await set('#arch','arm64');
    assert('macOS ARM64 官方 DMG 与独立版本',q('main a.text-link').href.endsWith('/Codex.dmg') && text().includes('26.1111.71025'));
    await set('#arch','x64');
    assert('macOS Intel 官方 DMG',q('main a.text-link').href.endsWith('/Codex-latest-x64.dmg'));
    await click('input[name="source"][value="mirror"]');
    assert('镜像标明第三方来源',text().includes('第三方维护')&&q('main a.text-link').href.endsWith('/mac-intel'));
    await click('[data-action="download"]');
    assert('下载前显示演示确认',q('dialog').open&&q('#dialog-title').textContent==='预览获取流程');
    await click('#dialog-cancel');
    assert('取消确认不启动进度',!q('#download-progress .progress-box'));
    await click('[data-action="download"]');
    await click('#dialog-confirm');
    assert('下载演示锁定选择器',q('#platform').disabled&&q('#arch').disabled);
    await waitFor(()=>Number(q('[role="progressbar"]').getAttribute('aria-valuenow'))>0);
    await click('[data-action="cancel-download"]');
    assert('可取消获取演示',text().includes('演示已取消')&&!q('#platform').disabled);
    const cancelledProgress=q('[role="progressbar"]').getAttribute('aria-valuenow');
    await click('[data-action="download"]');
    await click('#dialog-confirm');
    await waitFor(()=>text().includes('演示结束，等待用户安装确认'));
    assert('获取完成不声称实际安装成功',q('[role="progressbar"]').getAttribute('aria-valuenow')==='100'&&text().includes('未下载、未校验、未安装真实文件。'));
    assert('取消与完成后选择器均可操作',!q('#platform').disabled&&cancelledProgress!==null);
    await go('themes');
    await click('[data-action="reset-theme"]');
    await click('[data-theme="midnight"]');
    assert('选择主题只预览',q('[data-theme="violet"]').textContent.includes('已应用')&&!q('[data-theme="midnight"]').textContent.includes('已应用'));
    await set('#font-size','17','input');
    assert('字体滑块更新预览',q('#font-value').textContent==='17 px'&&q('.preview-panel .mini-content b').style.fontSize==='17px');
    await click('[data-action="apply-theme"]');
    assert('Codex 演示偏好保存',q('[data-theme="midnight"]').textContent.includes('已应用'));
    await click('[data-target="tool"]');
    assert('Codex 和工具外观分别保存',q('[data-theme="violet"]').getAttribute('aria-pressed')==='true');
    await click('[data-theme="forest"]');
    await click('[data-action="apply-theme"]');
    assert('工具配色应用到原型',getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()==='#a2c9ae');
    await click('[data-action="reset-theme"]');
    assert('工具配色恢复默认',getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()==='#c4b5fd');
    await click('[data-target="codex"]');
    assert('工具恢复不改变 Codex 偏好',q('[data-theme="midnight"]').getAttribute('aria-pressed')==='true');
    await click('[data-action="reset-theme"]');
    await go('overview');
    await click('main [data-action="launch"]');
    assert('首次演示启动后可重启',q('main [data-action="launch"]').textContent.includes('关闭并汉化重启'));
    const trigger=q('main [data-action="launch"]');
    trigger.focus();await click('main [data-action="launch"]');
    assert('运行后必须确认且提醒输入丢失',q('dialog').open&&q('dialog').textContent.includes('未保存的输入可能丢失'));
    await click('#dialog-cancel');
    assert('取消后状态不变且焦点返回',!q('dialog').open&&q('main [data-action="launch"]').textContent.includes('关闭并汉化重启')&&document.activeElement===trigger);
    await click('main [data-action="launch"]');
    await click('#dialog-confirm');
    assert('确认重启仅记录模拟流程',!q('dialog').open&&text().includes('未访问本机进程'));
    await go('diagnostics');
    await set('#log-search','不存在的日志关键词','input');
    assert('日志搜索空态',q('#log-list').textContent.includes('没有匹配的记录'));
    await set('#log-search','','input');
    await set('#log-level','WARN');
    assert('日志级别筛选',Array.from(document.querySelectorAll('.log-level')).every(el=>el.textContent==='WARN'));
    await click('[data-action="diagnose"]');
    assert('诊断不声称接入真实应用',text().includes('真实应用连接、菜单覆盖、下载校验均未接入'));
    await click('.sidebar [data-action="settings"]');
    assert('关于区分预览和发行版本',q('dialog').textContent.includes('v0.7.7')&&q('dialog').textContent.includes('v0.8 界面设计预览'));
    assert('对外链接安全属性',Array.from(document.querySelectorAll('a[target="_blank"]')).every(a=>a.rel.includes('noopener')&&a.href.startsWith('https://')));
    await click('#dialog-cancel');
    await go('overview');
    return {passed:results.length,failed:0,results};
  } catch(error) {
    return {passed:results.filter(r=>r.passed).length,failed:1,error:error.message,results};
  }
})()

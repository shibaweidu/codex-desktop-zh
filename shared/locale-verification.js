(async function () {
  var requested = __LOCALE_JSON__;
  var expectsChinese = /^zh/i.test(requested);
  var started = Date.now();
  var zhMarkers = ['新建任务', '新任务', '新聊天', '新建聊天', '定时任务', '拉取请求', '已安排', '插件', '技能', '自定义', '设置', '搜索', '文件', '编辑', '视图', '帮助', '常规', '应用语言'];
  var enMarkers = ['New task', 'New chat', 'Scheduled', 'Plugins', 'Skills', 'Customize', 'Settings', 'Search', 'File', 'Edit', 'View', 'Help', 'General', 'App language'];
  var result;
  do {
    // Inspect application controls, never arbitrary chat/message text.
    var labels = [];
    document.querySelectorAll('button,a,[role="button"],[role="menuitem"],[role="tab"],[role="heading"],[role="combobox"],h1,h2,input[placeholder]').forEach(function (element) {
      if (!element.getClientRects().length || element.closest('[hidden],[aria-hidden="true"],[data-message-author-role],article')) return;
      [element.textContent, element.getAttribute('aria-label'), element.getAttribute('title'), element.getAttribute('placeholder')].forEach(function (label) {
        if (label) labels.push(label.trim().replace(/\s+/g, ' '));
      });
    });
    var matches = function (markers) { return markers.filter(function (marker) { return labels.indexOf(marker) >= 0; }).length; };
    var zhMatches = matches(zhMarkers), enMatches = matches(enMarkers);
    var documentLanguage = document.documentElement.lang || '';
    var languageMatches = expectsChinese ? /^zh/i.test(documentLanguage) : /^en/i.test(documentLanguage);
    var ownMarkers = expectsChinese ? zhMatches : enMatches;
    var otherMarkers = expectsChinese ? enMatches : zhMatches;
    var ok = otherMarkers === 0 && (ownMarkers >= 2 || (languageMatches && ownMarkers >= 1));
    result = {
      status: ok ? 'ok' : 'partial', requested: requested,
      documentLanguage: documentLanguage, zhMarkers: zhMatches, enMarkers: enMatches,
      controlsInspected: labels.length, reason: ok ? '' : 'ui-language-not-confirmed'
    };
    if (ok || Date.now() - started >= 6500) return JSON.stringify(result);
    await new Promise(function (resolve) { window.setTimeout(resolve, 250); });
  } while (true);
})()

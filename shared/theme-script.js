(function () {
  'use strict';
  var theme = __THEME_JSON__;
  var id = 'codex-zh-launcher-theme';
  var previous = document.getElementById(id);
  if (!document.querySelector('#root') || !window.electronBridge) {
    return JSON.stringify({status:'unsupported', reason:'codex-renderer-unavailable'});
  }
  if (theme.id === 'default') {
    if (previous) previous.remove();
    return JSON.stringify({status:'ok', theme:'default', scope:'runtime-style-only'});
  }
  var valid = function (value) { return /^#[0-9a-f]{6}$/i.test(value); };
  if (!valid(theme.background) || !valid(theme.sidebar) || !valid(theme.foreground) || !valid(theme.accent) ||
      !Number.isInteger(theme.fontSize) || theme.fontSize < 12 || theme.fontSize > 18) {
    return JSON.stringify({status:'error', reason:'invalid-theme'});
  }
  var style = document.createElement('style');
  style.id = id;
  style.textContent = ':root{--color-background:' + theme.background + '!important;--color-background-primary:' + theme.background +
    '!important;--color-background-secondary:' + theme.sidebar + '!important;--color-text-primary:' + theme.foreground +
    '!important;--color-accent:' + theme.accent + '!important;color-scheme:' + (theme.id === 'paper' ? 'light' : 'dark') +
    '}body,#root{background-color:' + theme.background + '!important;color:' + theme.foreground +
    '!important}#root{font-size:' + theme.fontSize + 'px!important}#root :where(aside,nav){background-color:' + theme.sidebar + '!important}';
  if (previous) previous.remove();
  (document.head || document.documentElement).appendChild(style);
  var probe = document.createElement('span');
  probe.style.color = theme.foreground;
  probe.style.backgroundColor = theme.background;
  document.body.appendChild(probe);
  var expected = getComputedStyle(probe), actual = getComputedStyle(document.querySelector('#root'));
  var ok = actual.color === expected.color && actual.backgroundColor === expected.backgroundColor;
  probe.remove();
  if (!ok) {
    style.remove();
    if (previous) (document.head || document.documentElement).appendChild(previous);
  }
  return JSON.stringify({status:ok?'ok':'unsupported', theme:theme.id, scope:'runtime-style-only',
    reason:ok?'root-colors-verified':'computed-style-mismatch', note:'Native settings, menus and code syntax colors are not changed.'});
})()

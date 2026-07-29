(function () {
  var configId = '72216192';
  var locale = 'zh-CN';
  var state = globalThis.__codexZhI18nState || {
    configId: configId,
    patchedClients: 0,
    patchedConfigs: 0,
    lastPatchAt: 0
  };
  try { globalThis.__codexZhI18nState = state; } catch (_) {}

  function forceConfig(config) {
    if (!config || (typeof config !== 'object' && typeof config !== 'function')) return config;
    if (config.__codexZhI18nConfig) return config;
    try {
      Object.defineProperty(config, '__codexZhI18nConfig', { value: true, configurable: true });
    } catch (_) {}
    if (typeof config.get === 'function') {
      var originalGet = config.get;
      try {
        Object.defineProperty(config, 'get', {
          configurable: true,
          value: function (key, fallback) {
            if (key === 'enable_i18n') return true;
            if (key === 'locale_source') return 'SYSTEM';
            return originalGet.call(this, key, fallback);
          }
        });
      } catch (_) {}
    }
    try {
      if (config.value && typeof config.value === 'object') {
        config.value.enable_i18n = true;
        config.value.locale_source = 'SYSTEM';
      }
    } catch (_) {}
    state.patchedConfigs += 1;
    state.lastPatchAt = Date.now();
    return config;
  }

  function patchClient(client) {
    if (!client || (typeof client !== 'object' && typeof client !== 'function')) return;
    if (client.__codexZhI18nClient) return;
    var original = client.getDynamicConfig;
    if (typeof original !== 'function') return;
    try {
      Object.defineProperty(client, 'getDynamicConfig', {
        configurable: true,
        value: function (key) {
          var result = original.apply(this, arguments);
          return String(key) === configId ? forceConfig(result) : result;
        }
      });
      Object.defineProperty(client, '__codexZhI18nClient', { value: true, configurable: true });
      state.patchedClients += 1;
      state.lastPatchAt = Date.now();
    } catch (_) {}
  }

  function patchGlobal(statsig) {
    if (!statsig || typeof statsig !== 'object') return;
    patchClient(statsig);
    patchClient(statsig.firstInstance);
    patchClient(statsig.instance);
    var instances = statsig.instances;
    if (instances && typeof instances === 'object') {
      Object.keys(instances).forEach(function (key) { patchClient(instances[key]); });
    }
  }

  function installStatsigHook() {
    var patchCurrent = function () {
      try { patchGlobal(globalThis.__STATSIG__); } catch (_) {}
    };
    patchCurrent();
    var attempts = 0;
    var timer = window.setInterval(function () {
      patchCurrent();
      attempts += 1;
      if (attempts >= 400 || (state.patchedClients > 0 && state.patchedConfigs > 0)) {
        window.clearInterval(timer);
      }
    }, 50);
  }

  try {
    Object.defineProperty(Navigator.prototype, 'language', {
      configurable: true,
      get: function () { return locale; }
    });
    Object.defineProperty(Navigator.prototype, 'languages', {
      configurable: true,
      get: function () { return [locale, 'zh']; }
    });
  } catch (_) {}

  installStatsigHook();
  return JSON.stringify({
    status: state.patchedClients > 0 && state.patchedConfigs > 0 ? 'ok' : 'pending',
    configId: configId,
    enable_i18n: true,
    locale_source: 'SYSTEM',
    patchedClients: state.patchedClients,
    patchedConfigs: state.patchedConfigs,
    lastPatchAt: state.lastPatchAt
  });
})()

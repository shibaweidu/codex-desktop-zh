(function () {
  var configId = '72216192';
  var locale = 'zh-CN';
  var patchedClients = 0;
  var patchedConfigs = 0;

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
    patchedConfigs += 1;
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
      patchedClients += 1;
    } catch (_) {}
  }

  function patchGlobal(statsig) {
    if (!statsig || typeof statsig !== 'object') return;
    patchClient(statsig.firstInstance);
    patchClient(statsig.instance);
    var instances = statsig.instances;
    if (instances && typeof instances === 'object') {
      Object.keys(instances).forEach(function (key) { patchClient(instances[key]); });
    }
  }

  function installStatsigHook() {
    var current;
    try { current = globalThis.__STATSIG__; } catch (_) {}
    try {
      Object.defineProperty(globalThis, '__STATSIG__', {
        configurable: true,
        get: function () { return current; },
        set: function (value) {
          current = value;
          patchGlobal(value);
        }
      });
    } catch (_) {}
    patchGlobal(current);
    window.setInterval(function () {
      try { patchGlobal(globalThis.__STATSIG__); } catch (_) {}
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
    status: 'ok',
    configId: configId,
    enable_i18n: true,
    locale_source: 'SYSTEM',
    patchedClients: patchedClients,
    patchedConfigs: patchedConfigs
  });
})()

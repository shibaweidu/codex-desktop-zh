(function () {
  // Reuse the running renderer's exported module runtime; never load new code
  // from a server or change Codex files.
  globalThis.__codexZhGetAppServices = async function () {
    var scripts = Array.from(document.querySelectorAll('script[type="module"][src]'));
    for (var script of scripts) {
      var source;
      try { source = new URL(script.src, location.href); } catch (_) { continue; }
      if (source.origin !== location.origin || !/\/assets\/[^/]+\.js$/.test(source.pathname)) continue;
      try {
        var entry = await import(source.href);
        var runtime = entry.__webpack_require__;
        if (typeof runtime !== 'function' || !runtime.m) continue;
        var modules = Object.keys(runtime.m);
        for (var id of modules) {
          var factory = runtime.m[id];
          if (typeof factory !== 'function') continue;
          var code = Function.prototype.toString.call(factory);
          if (code.indexOf('setAppServices') < 0 || code.indexOf('appServices') < 0 ||
              code.indexOf('setAppHost') < 0) continue;
          var owner = runtime(id);
          var previousPatch = globalThis.__codexZhMenuServicePatch;
          if (previousPatch && previousPatch.owner === owner && previousPatch.version !== 2) {
            owner.setAppServices(previousPatch.original);
            delete globalThis.__codexZhMenuServicePatch;
          }
          var services = owner.appServices;
          if (services && services.settings && typeof services.settings.read === 'function') {
            globalThis.__codexZhAppServicesModule = owner;
            return services;
          }
        }
      } catch (_) {}
    }
    return null;
  };
})()

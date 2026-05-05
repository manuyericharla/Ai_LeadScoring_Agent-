/**
 * Posts anonymous activity to Lead Scoring (uses /api/ingest/site-activity — avoids blocklists that target /track/event).
 * - On localhost / 127.0.0.1, defaults API to http://localhost:5221 (avoids blocked cross-origin calls to production).
 * - Override with window.__LEAD_SCORING__ = { apiBase: 'https://...' } before this script, or data-api-base on the script tag.
 */
(function () {
  'use strict';

  var STORAGE_KEY = 'leadScoring.scriptVisitorId';
  var LAST_VISITOR_KEY = 'leadScoring.lastVisitorId';

  function trim(s) {
    return s ? String(s).trim() : '';
  }

  function resolveApiBase() {
    var cfg = typeof window !== 'undefined' ? window.__LEAD_SCORING__ : null;
    if (cfg && trim(cfg.apiBase)) {
      return trim(cfg.apiBase).replace(/\/$/, '');
    }
    var scripts = document.getElementsByTagName('script');
    for (var i = scripts.length - 1; i >= 0; i--) {
      var s = scripts[i];
      if (!s.src || s.src.indexOf('lead-scoring-analytics') === -1) {
        continue;
      }
      var d = trim(s.getAttribute('data-api-base'));
      if (d) {
        return d.replace(/\/$/, '');
      }
      break;
    }
    var h = location.hostname;
    if (h === 'localhost' || h === '127.0.0.1') {
      return 'http://localhost:5221';
    }
    return 'https://leadscoring.hiperbrains.com';
  }

  function readVisitorIdFromQuery() {
    try {
      return trim(new URLSearchParams(location.search).get('visitorId'));
    } catch (e) {
      return '';
    }
  }

  function readStoredVisitorId() {
    try {
      return trim(localStorage.getItem(LAST_VISITOR_KEY));
    } catch (e) {
      return '';
    }
  }

  function readCookieVisitorId() {
    var m = document.cookie.match(/(?:^|;\s*)visitorId=([^;]*)/);
    return m ? trim(decodeURIComponent(m[1])) : '';
  }

  function ensureScriptVisitorId() {
    try {
      var existing = trim(localStorage.getItem(STORAGE_KEY));
      if (existing) {
        return existing;
      }
      var gen =
        typeof crypto !== 'undefined' && crypto.randomUUID
          ? crypto.randomUUID().replace(/-/g, '')
          : 'anon' + String(Date.now()) + Math.random().toString(16).slice(2);
      localStorage.setItem(STORAGE_KEY, gen);
      return gen;
    } catch (e) {
      return 'anon' + String(Date.now());
    }
  }

  function resolveVisitorId() {
    return (
      readVisitorIdFromQuery() ||
      readStoredVisitorId() ||
      readCookieVisitorId() ||
      ensureScriptVisitorId()
    );
  }

  function paramFromSearch(names) {
    try {
      var sp = new URLSearchParams(location.search);
      for (var i = 0; i < names.length; i++) {
        var v = trim(sp.get(names[i]));
        if (v) {
          return v;
        }
      }
    } catch (e) {}
    return '';
  }

  function paramSource() {
    return paramFromSearch(['src', 'source', 'utm_source']).toLowerCase();
  }

  function paramCampaign() {
    return paramFromSearch(['cmp', 'campaign', 'utm_campaign']);
  }

  function postPageView() {
    var api = resolveApiBase();
    var visitorId = resolveVisitorId();
    if (!visitorId) {
      return;
    }

    var path = location.pathname + location.search;
    var body = JSON.stringify({
      visitorId: visitorId,
      source: paramSource() || null,
      eventType: 'WebsiteActivity',
      campaign: paramCampaign() || null,
      metadataJson: JSON.stringify({
        eventName: 'PageView',
        path: path,
        referrer: typeof document !== 'undefined' ? document.referrer || null : null
      }),
      leadId: null
    });

    var url = api + '/api/ingest/site-activity';
    if (typeof fetch !== 'function') {
      return;
    }

    fetch(url, {
      method: 'POST',
      credentials: 'omit',
      mode: 'cors',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: body
    }).catch(function () {});
  }

  if (document.readyState === 'complete') {
    postPageView();
  } else {
    window.addEventListener('load', postPageView);
  }
})();

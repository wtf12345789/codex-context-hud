(() => {
  'use strict';

  const INSTANCE = Symbol.for('codex-context-hud.renderer.v1');
  const previous = window[INSTANCE];
  if (previous && typeof previous.dispose === 'function') previous.dispose();
  for (const element of document.querySelectorAll('[data-codex-context-hud]')) element.remove();

  const state = {
    threadId: '',
    compressions: 0,
    quotaPercent: -1,
    compactionKeys: new Set(),
    notificationSequence: 0,
    nativeReadMs: -1,
    loadedThreadId: '',
    loadingThreadId: '',
    threadReadSequence: 0
  };
  let host = null;
  let tooltip = null;
  let tooltipShowTimer = 0;
  let mountTimer = 0;
  let quotaRequestTimer = 0;
  let quotaRequestAttempts = 0;
  let sessionMotionTimer = 0;
  let sessionMotionFrame = 0;
  let pendingMotionThreadId = '';

  const clamp = value => Math.max(0, Math.min(100, value));
  const number = value => {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  };
  const first = (object, names) => {
    if (!object || typeof object !== 'object') return undefined;
    for (const name of names) {
      if (Object.prototype.hasOwnProperty.call(object, name)) return object[name];
    }
    return undefined;
  };

  function setThread(value) {
    if (typeof value !== 'string' || !value || value === state.threadId) return;
    if (sessionMotionTimer) window.clearTimeout(sessionMotionTimer);
    if (sessionMotionFrame) window.cancelAnimationFrame(sessionMotionFrame);
    sessionMotionTimer = 0;
    sessionMotionFrame = 0;
    pendingMotionThreadId = value;
    state.threadId = value;
    state.compressions = 0;
    state.compactionKeys.clear();
    state.notificationSequence = 0;
    state.loadedThreadId = '';
    state.loadingThreadId = '';
    state.threadReadSequence += 1;
  }

  function refreshActiveThread() {
    const active = Array.from(document.querySelectorAll('[data-app-action-sidebar-thread-id]'))
      .find(element => element.closest('[aria-current="page"]'));
    if (active) {
      const raw = active.getAttribute('data-app-action-sidebar-thread-id') || '';
      const separator = raw.indexOf(':');
      setThread(separator >= 0 ? raw.slice(separator + 1) : raw);
      return;
    }

    let anchor = editableCandidates()[0];
    if (!anchor) return;
    let fiberKey = null;
    while (anchor && anchor !== document.body && !fiberKey) {
      fiberKey = Object.getOwnPropertyNames(anchor).find(key => key.startsWith('__reactFiber'));
      if (!fiberKey) anchor = anchor.parentElement;
    }
    let fiber = anchor && fiberKey ? anchor[fiberKey] : null;
    for (let depth = 0; fiber && depth < 40; depth += 1, fiber = fiber.return) {
      for (const props of [fiber.memoizedProps, fiber.pendingProps]) {
        if (props && typeof props.conversationId === 'string') {
          setThread(props.conversationId);
          return;
        }
      }
    }
  }

  function findConversationManager() {
    let anchor = editableCandidates()[0];
    if (!anchor) return null;
    let fiberKey = null;
    while (anchor && anchor !== document.body && !fiberKey) {
      fiberKey = Object.getOwnPropertyNames(anchor).find(key => key.startsWith('__reactFiber'));
      if (!fiberKey) anchor = anchor.parentElement;
    }
    if (!anchor || !fiberKey) return null;

    let fiber = anchor[fiberKey];
    for (let depth = 0; fiber && depth < 40; depth += 1, fiber = fiber.return) {
      let hook = fiber.memoizedState;
      for (let index = 0; hook && index < 200; index += 1, hook = hook.next) {
        const candidate = hook.memoizedState;
        if (candidate && typeof candidate.getConversation === 'function' &&
          typeof candidate.getHostId === 'function') return candidate;
      }
    }
    return null;
  }

  function countCompactions(value) {
    let count = 0;
    const seen = new WeakSet();
    const stableIds = new Set();
    const scan = item => {
      if (!item || typeof item !== 'object' || seen.has(item)) return;
      seen.add(item);
      if ((item.type === 'context-compaction' || item.type === 'contextCompaction') &&
        item.completed !== false) {
        const stableId = first(item, ['id', 'itemId', 'item_id']);
        if (stableId === undefined || stableId === null || !stableIds.has(String(stableId))) {
          count += 1;
          if (stableId !== undefined && stableId !== null) stableIds.add(String(stableId));
        }
      }
      for (const child of Object.values(item)) scan(child);
    };
    scan(value);
    return { count, stableIds };
  }

  function applyCompactionSnapshot(value) {
    const snapshot = countCompactions(value);
    state.compressions = snapshot.count;
    state.compactionKeys.clear();
    for (const id of snapshot.stableIds)
      state.compactionKeys.add(`${state.threadId}:${id}`);
  }

  function refreshCompressionFromNative() {
    if (!state.threadId) return;
    const started = performance.now();
    const manager = findConversationManager();
    if (!manager) return;
    const conversation = manager.getConversation(state.threadId);
    const entities = conversation && conversation.turnHistory && conversation.turnHistory.history &&
      conversation.turnHistory.history.entitiesByKey;
    const hasCompleteHistory = conversation && conversation.turnsPagination &&
      conversation.turnsPagination.hasLoadedOldest === true && entities && typeof entities === 'object';
    if (conversation && ((Array.isArray(conversation.turns) && conversation.turns.length) || hasCompleteHistory)) {
      const firstLoadForThread = state.loadedThreadId !== state.threadId;
      applyCompactionSnapshot({
        turns: conversation.turns,
        history: hasCompleteHistory ? Object.values(entities) : []
      });
      state.nativeReadMs = performance.now() - started;
      state.loadedThreadId = state.threadId;
      if (firstLoadForThread) scheduleSessionMotion();
      return;
    }
    if (state.loadedThreadId === state.threadId || state.loadingThreadId === state.threadId ||
      typeof manager.readThread !== 'function') return;

    const requestedThread = state.threadId;
    const sequence = ++state.threadReadSequence;
    state.loadingThreadId = requestedThread;
    Promise.resolve(manager.readThread(requestedThread, { includeTurns: true })).then(result => {
      if (sequence !== state.threadReadSequence || requestedThread !== state.threadId) return;
      applyCompactionSnapshot(result);
      state.nativeReadMs = performance.now() - started;
      state.loadedThreadId = requestedThread;
      state.loadingThreadId = '';
      render();
      scheduleSessionMotion();
    }).catch(() => {
      if (sequence === state.threadReadSequence) state.loadingThreadId = '';
    });
  }

  function applyRateLimits(value) {
    if (!value || typeof value !== 'object') return;
    let used = number(first(value, ['usedPercent', 'used_percent']));
    if (used === null) {
      const primary = first(value, ['primary', 'primaryLimit', 'primary_limit']);
      used = number(first(primary, ['usedPercent', 'used_percent']));
    }
    if (used === null) {
      const limits = first(value, ['rateLimits', 'rate_limits', 'limits']);
      if (limits !== value) applyRateLimits(limits);
      return;
    }
    state.quotaPercent = clamp(100 - used);
  }

  function registerCompaction(value, source) {
    if (!value || typeof value !== 'object') return;
    if (value.completed === false) return;
    const id = first(value, ['id', 'itemId', 'item_id']);
    const turn = first(value, ['turnId', 'turn_id']);
    const thread = first(value, ['threadId', 'thread_id']) || state.threadId;
    const stable = id || turn;
    const key = stable ? `${thread || ''}:${stable}` :
      source === 'notification' ? `notification:${++state.notificationSequence}` :
      `${source}:${thread || ''}:${JSON.stringify(value)}`;
    if (state.compactionKeys.has(key)) return;
    state.compactionKeys.add(key);
    state.compressions += 1;
  }

  function applyNotification(method, params) {
    if (!params || typeof params !== 'object') return;
    if (method === 'account/rateLimits/updated') {
      applyRateLimits(params);
    } else if (method === 'thread/compacted') {
      const threadId = first(params, ['threadId', 'thread_id']);
      if (state.threadId && threadId !== state.threadId) return;
      registerCompaction(params, 'notification');
    }
  }

  function visit(value, parentKey, seen) {
    if (!value || typeof value !== 'object' || seen.has(value)) return;
    seen.add(value);
    if (typeof value.method === 'string' && value.params && typeof value.params === 'object') {
      applyNotification(value.method, value.params);
    }
    const rateLimitParent = /^(?:rateLimits|rate_limits|limits|primary|primaryLimit|primary_limit)$/
      .test(parentKey || '');
    const hasRateLimitContainer = first(value,
      ['rateLimits', 'rate_limits', 'limits', 'primary', 'primaryLimit', 'primary_limit']) !== undefined;
    if (rateLimitParent || hasRateLimitContainer) applyRateLimits(value);
    for (const [key, child] of Object.entries(value)) visit(child, key, seen);
  }

  function consume(raw) {
    let value = raw;
    if (typeof value === 'string') {
      const trimmed = value.trim();
      if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return;
      try { value = JSON.parse(trimmed); } catch (_) { return; }
    }
    if (!value || typeof value !== 'object') return;
    refreshActiveThread();
    visit(value, '', new WeakSet());
    render();
  }

  function severity(kind, value) {
    if (value < 0) return 'muted';
    if (kind === 'context') return value >= 85 ? 'danger' : value >= 65 ? 'warn' : 'normal';
    return value <= 15 ? 'danger' : value <= 35 ? 'warn' : 'normal';
  }

  function percent(value) {
    return value < 0 ? '--' : `${Math.round(value)}%`;
  }

  function motionAllowed() {
    return typeof Element.prototype.animate === 'function' &&
      !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  function animateCompressionBars(bars, activeCount) {
    if (!motionAllowed() || activeCount <= 0) return;
    bars.forEach((bar, index) => {
      for (const animation of bar.getAnimations()) animation.cancel();
      if (index >= activeCount) return;
      bar.animate([
        { transform: 'scaleY(.38)', opacity: .28 },
        { transform: 'scaleY(1.10)', opacity: 1, offset: .72 },
        { transform: 'scaleY(1)', opacity: 1 }
      ], {
        duration: 720,
        delay: index * 110,
        easing: 'cubic-bezier(.2,.75,.25,1)',
        fill: 'none'
      });
    });
  }

  function animateQuotaFill(fill, track, previousWidth, nextWidth) {
    if (!motionAllowed() || nextWidth <= 0) return;
    for (const animation of fill.getAnimations()) animation.cancel();
    const startScale = Math.max(.12, Math.min(1, previousWidth / nextWidth));
    fill.animate([
      { transform: `scaleX(${startScale}) scaleY(1)`, opacity: .72 },
      { transform: 'scaleX(1.025) scaleY(1.45)', opacity: 1, offset: .76 },
      { transform: 'scaleX(1) scaleY(1)', opacity: 1 }
    ], {
      duration: 900,
      easing: 'cubic-bezier(.2,.72,.2,1)',
      fill: 'none'
    });
    if (track) {
      for (const animation of track.getAnimations()) animation.cancel();
      track.animate([
        { opacity: .62 },
        { opacity: .84, offset: .58 },
        { opacity: .62 }
      ], { duration: 900, easing: 'ease-out', fill: 'none' });
    }
  }

  function activeCompressionBarCount(length) {
    return state.compressions <= 0 ? 0 :
      state.compressions >= 10 ? length :
      ((state.compressions - 1) % length) + 1;
  }

  function scheduleSessionMotion() {
    if (!state.threadId || pendingMotionThreadId !== state.threadId) return;
    if (sessionMotionTimer) window.clearTimeout(sessionMotionTimer);
    if (sessionMotionFrame) window.cancelAnimationFrame(sessionMotionFrame);
    const requestedThread = state.threadId;
    sessionMotionTimer = window.setTimeout(() => {
      sessionMotionTimer = 0;
      sessionMotionFrame = window.requestAnimationFrame(() => {
        sessionMotionFrame = window.requestAnimationFrame(() => {
          sessionMotionFrame = 0;
          if (!host || !host.isConnected || requestedThread !== state.threadId ||
            pendingMotionThreadId !== requestedThread) return;
          const bars = Array.from(host.shadowRoot.querySelectorAll('[data-compression-bar]'));
          const fill = host.shadowRoot.querySelector('[data-quota-fill]');
          const track = host.shadowRoot.querySelector('[data-quota-track]');
          animateCompressionBars(bars, activeCompressionBarCount(bars.length));
          if (fill) animateQuotaFill(fill, track, 0, Number(fill.getAttribute('width')) || 0);
          pendingMotionThreadId = '';
        });
      });
    }, 720);
  }

  function render() {
    if (!host || !host.shadowRoot) return;
    const compression = host.shadowRoot.querySelector('[data-value="compression"]');
    const quota = host.shadowRoot.querySelector('[data-value="quota"]');
    const compressionStat = host.shadowRoot.querySelector('[data-stat="compression"]');
    const quotaStat = host.shadowRoot.querySelector('[data-stat="quota"]');
    const quotaFill = host.shadowRoot.querySelector('[data-quota-fill]');
    const compressionTracks = Array.from(host.shadowRoot.querySelectorAll('[data-compression-track]'));
    const compressionBars = Array.from(host.shadowRoot.querySelectorAll('[data-compression-bar]'));
    const popoverCompression = tooltip && tooltip.querySelector('[data-popover="compression"]');
    const popoverQuota = tooltip && tooltip.querySelector('[data-popover="quota"]');
    if (!compression || !quota) return;
    const compressionText = String(state.compressions);
    const quotaText = percent(state.quotaPercent);
    if (compression.textContent !== compressionText) compression.textContent = compressionText;
    if (quota.textContent !== quotaText) quota.textContent = quotaText;
    if (popoverCompression) popoverCompression.textContent = `${compressionText} 次`;
    if (popoverQuota) popoverQuota.textContent = quotaText;
    if (quotaStat) quotaStat.dataset.tone = severity('quota', state.quotaPercent);
    if (quotaStat) quotaStat.style.display = state.quotaPercent < 0 ? 'none' : 'inline-flex';
    if (quotaFill) {
      const quotaWidth = state.quotaPercent <= 0 ? 0 :
        Math.max(1.5, clamp(state.quotaPercent) * 22 / 100);
      quotaFill.setAttribute('width', quotaWidth.toFixed(2));
      quotaFill.style.fill = state.quotaPercent <= 15 ? '#C96B6B' :
        state.quotaPercent <= 35 ? '#D4BB6F' : '#86A58E';
    }
    const activeCompressionBars = activeCompressionBarCount(compressionBars.length);
    let compressionColor = '#AEB2B7';
    let previousTierColor = '#62666C';
    let compressionStroke = 'none';
    let compressionTier = 'neutral';
    if (state.compressions >= 10) {
      compressionColor = '#AF8CE0';
      previousTierColor = '#C96B6B';
      compressionStroke = 'rgba(236,224,255,.58)';
      compressionTier = 'critical';
    } else if (state.compressions >= 7) {
      compressionColor = '#C96B6B';
      previousTierColor = '#D4BB6F';
      compressionTier = 'alert';
    } else if (state.compressions >= 4) {
      compressionColor = '#D4BB6F';
      compressionTier = 'warning';
    }
    compressionTracks.forEach(track => {
      track.style.fill = previousTierColor;
      track.style.opacity = state.compressions >= 4 ? '.78' : '.62';
    });
    compressionBars.forEach((bar, index) => {
      const active = index < activeCompressionBars;
      bar.style.opacity = active ? '1' : '0';
      bar.style.fill = compressionColor;
      bar.style.stroke = compressionStroke;
    });
    if (compressionStat) compressionStat.dataset.tier = compressionTier;
    if (compressionStat) {
      compressionStat.setAttribute('aria-label', `压缩次数：${compressionText}`);
    }
    if (quotaStat) {
      quotaStat.setAttribute('aria-label', `账户剩余额度：${quotaText}`);
    }
  }

  function visible(element) {
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 12 && rect.height > 12 && rect.bottom > innerHeight * .45 &&
      style.display !== 'none' && style.visibility !== 'hidden';
  }

  function editableCandidates() {
    return Array.from(document.querySelectorAll(
      'textarea, [contenteditable="true"], [role="textbox"]'
    )).filter(visible).sort((a, b) =>
      b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom
    );
  }

  function findToolbar() {
    const editor = editableCandidates()[0];
    if (!editor) return null;
    let footer = editor;
    while (footer && footer !== document.body &&
      !String(footer.className).includes('ComposerLayoutFooter')) {
      footer = footer.parentElement;
    }
    if (!footer || footer === document.body) return null;

    const flexes = Array.from(footer.querySelectorAll('div')).filter(element => {
      const style = getComputedStyle(element);
      return style.display === 'flex' && visible(element) &&
        element.querySelectorAll('button').length >= 3;
    });
    flexes.sort((a, b) => {
      const aRect = a.getBoundingClientRect();
      const bRect = b.getBoundingClientRect();
      return aRect.width * aRect.height - bRect.width * bRect.height;
    });
    return flexes[0] || null;
  }

  function positionTooltip() {
    if (!host || !tooltip || tooltip.style.display === 'none') return;
    const anchor = host.getBoundingClientRect();
    const width = tooltip.offsetWidth;
    const height = tooltip.offsetHeight;
    const left = Math.max(8, Math.min(innerWidth - width - 8, anchor.left - 3));
    const above = anchor.top - height - 9;
    const top = above >= 8 ? above : Math.min(innerHeight - height - 8, anchor.bottom + 9);
    tooltip.style.left = `${Math.round(left)}px`;
    tooltip.style.top = `${Math.round(top)}px`;
  }

  function showTooltip() {
    if (!host || tooltipShowTimer) return;
    tooltipShowTimer = window.setTimeout(() => {
      tooltipShowTimer = 0;
      if (!host || !host.isConnected) return;
      if (!tooltip) {
        tooltip = document.createElement('div');
        tooltip.dataset.codexContextHudTooltip = 'renderer-v1';
        tooltip.setAttribute('role', 'tooltip');
        tooltip.style.cssText = 'position:fixed;z-index:2147483647;display:none;width:184px;box-sizing:border-box;padding:10px 12px;border:1px solid rgba(255,255,255,.12);border-radius:12px;background:#303030;box-shadow:0 12px 28px rgba(0,0,0,.34);color:#dadada;font:500 12px/1.35 system-ui,sans-serif;letter-spacing:0;pointer-events:none;';
        tooltip.innerHTML = '<div style="display:grid;gap:5px"><span style="color:#9c9c9c;font-weight:600">会话统计：</span><span style="display:flex;align-items:center;justify-content:space-between;gap:18px"><span style="color:#ababab">账户额度</span><span data-popover="quota" style="color:#f0f0f0;font-variant-numeric:tabular-nums">--</span></span><span style="display:flex;align-items:center;justify-content:space-between;gap:18px"><span style="color:#ababab">压缩次数</span><span data-popover="compression" style="color:#f0f0f0;font-variant-numeric:tabular-nums">0 次</span></span></div>';
        document.body.appendChild(tooltip);
        render();
      }
      tooltip.style.display = 'block';
      positionTooltip();
    }, 520);
  }

  function hideTooltip() {
    if (tooltipShowTimer) window.clearTimeout(tooltipShowTimer);
    tooltipShowTimer = 0;
    if (tooltip) tooltip.style.display = 'none';
  }

  function removeTooltip() {
    if (tooltipShowTimer) window.clearTimeout(tooltipShowTimer);
    tooltipShowTimer = 0;
    if (tooltip) tooltip.remove();
    tooltip = null;
  }

  function createHost() {
    const element = document.createElement('span');
    element.dataset.codexContextHud = 'renderer-v1';
    element.style.cssText = 'display:inline-flex;align-items:center;flex:0 0 auto;min-width:0;height:28px;';
    const root = element.attachShadow({ mode: 'open' });
    root.innerHTML = `
      <style>
        :host { display:inline-flex; align-items:center; flex:0 0 auto; font:inherit; }
        .shell { position:relative; display:inline-flex; align-items:center; height:28px; }
        .hud { display:inline-flex; align-items:center; gap:6px; height:28px; padding:0 2px;
          color:inherit; white-space:nowrap;
          font-family:inherit; font-size:12px; font-weight:500; line-height:1; letter-spacing:0; }
        .stat { display:inline-flex; align-items:center; opacity:1; color:inherit; }
        [data-stat="compression"], [data-stat="quota"] { color:inherit; }
        .icon { width:16px; height:16px; display:block; fill:none; stroke:currentColor;
          stroke-width:1.4; stroke-linecap:round; stroke-linejoin:round; }
        .quota-pulse-icon { width:24px; height:12px; }
        .compression-bars-icon { width:15px; height:12px; }
        .inline-value { display:none; }
        .quota-track { fill:#62666C; stroke:none; opacity:.62; }
        .quota-fill { fill:#86A58E; stroke:none; shape-rendering:geometricPrecision;
          transform-box:fill-box; transform-origin:left center; }
        .compression-track { fill:#62666C; stroke:none; opacity:.62; }
        .compression-bar { stroke-width:.5; transition:opacity .12s ease, fill .12s ease, stroke .12s ease;
          shape-rendering:geometricPrecision;
          transform-box:fill-box; transform-origin:center bottom; }
        [data-tone="muted"] { opacity:.55; }
        [data-tone="warn"], [data-tone="danger"] { color:inherit; }
        @media (max-width:760px) { .hud { gap:6px; padding:0 1px; font-size:11px; } }
      </style>
      <span class="shell">
        <span class="hud" aria-label="会话统计；上下文用量由相邻的 Codex 原生圆环表示">
          <span class="stat" data-stat="quota" data-tone="muted">
            <svg class="icon quota-pulse-icon" viewBox="0 0 24 12" aria-hidden="true">
              <rect class="quota-track" data-quota-track x="1" y="4.5" width="22" height="3" rx="1.5"/>
              <rect class="quota-fill" data-quota-fill x="1" y="4.5" width="0" height="3" rx="1.5"/>
            </svg>
            <span class="inline-value" data-value="quota">--</span>
          </span>
          <span class="stat" data-stat="compression">
            <svg class="icon compression-bars-icon" viewBox="0 0 15 12" aria-hidden="true">
              <rect class="compression-track" data-compression-track x="1" y="1" width="2.6" height="10" rx="1.3"/>
              <rect class="compression-track" data-compression-track x="6.2" y="1" width="2.6" height="10" rx="1.3"/>
              <rect class="compression-track" data-compression-track x="11.4" y="1" width="2.6" height="10" rx="1.3"/>
              <rect class="compression-bar" data-compression-bar x="1" y="1" width="2.6" height="10" rx="1.3"/>
              <rect class="compression-bar" data-compression-bar x="6.2" y="1" width="2.6" height="10" rx="1.3"/>
              <rect class="compression-bar" data-compression-bar x="11.4" y="1" width="2.6" height="10" rx="1.3"/>
            </svg>
            <span class="inline-value" data-value="compression">0</span>
          </span>
        </span>
      </span>`;
    element.addEventListener('mouseenter', showTooltip);
    element.addEventListener('mouseleave', hideTooltip);
    return element;
  }

  function syncNativeTone(nativeContext) {
    if (!host || !nativeContext) return;
    const nativeStyle = getComputedStyle(nativeContext);
    const nativeColor = nativeStyle.stroke && nativeStyle.stroke !== 'none' ?
      nativeStyle.stroke : nativeStyle.color;
    const circleOpacities = Array.from(nativeContext.querySelectorAll('circle')).map(circle =>
      Number(getComputedStyle(circle).opacity)).filter(value => Number.isFinite(value));
    const activeOpacity = circleOpacities.length ? Math.max(...circleOpacities) : 1;
    host.style.color = nativeColor;
    host.style.opacity = activeOpacity.toFixed(3);
  }

  function mount() {
    refreshActiveThread();
    refreshCompressionFromNative();
    const toolbar = findToolbar();
    if (!toolbar) {
      if (host) host.remove();
      hideTooltip();
      host = null;
      return;
    }
    const nativeContext = Array.from(toolbar.querySelectorAll('[role="img"][aria-label]'))
      .find(element => /上下文|context/i.test(element.getAttribute('aria-label') || ''));
    const modelButton = Array.from(toolbar.querySelectorAll('button[aria-haspopup="menu"]'))
      .find(button => button.hasAttribute('data-codex-intelligence-trigger')) ||
      Array.from(toolbar.querySelectorAll('button[aria-haspopup="menu"]')).find(visible);
    let anchorSlot = nativeContext && nativeContext.parentElement;
    let anchorGroup = anchorSlot && anchorSlot.parentElement;
    let toneSource = nativeContext;
    if (!anchorSlot || !anchorGroup) {
      anchorGroup = modelButton && modelButton.parentElement;
      while (anchorGroup && anchorGroup !== toolbar &&
        !(anchorGroup.tagName === 'DIV' && getComputedStyle(anchorGroup).display === 'flex')) {
        anchorGroup = anchorGroup.parentElement;
      }
      anchorSlot = modelButton;
      while (anchorSlot && anchorSlot.parentElement !== anchorGroup) anchorSlot = anchorSlot.parentElement;
      toneSource = modelButton;
    }
    if (!anchorSlot || !anchorGroup || !toneSource) {
      if (host) host.remove();
      hideTooltip();
      host = null;
      return;
    }
    if (host && host.isConnected && host.parentElement === anchorGroup &&
      host.nextElementSibling === anchorSlot) {
      syncNativeTone(toneSource);
      render();
      return;
    }
    if (host) host.remove();
    hideTooltip();
    host = createHost();
    syncNativeTone(toneSource);
    anchorGroup.insertBefore(host, anchorSlot);
    render();
  }

  function scheduleMount(delay = 80) {
    if (mountTimer) return;
    mountTimer = window.setTimeout(() => {
      mountTimer = 0;
      try { mount(); } catch (_) { scheduleMount(180); }
    }, delay);
  }

  function requestRateLimits() {
    quotaRequestTimer = 0;
    quotaRequestAttempts += 1;
    const retry = () => {
      if (state.quotaPercent >= 0 || quotaRequestAttempts >= 6 || quotaRequestTimer) return;
      quotaRequestTimer = window.setTimeout(requestRateLimits, 650);
    };
    const manager = findConversationManager();
    if (manager && typeof manager.sendRequest === 'function') {
      Promise.resolve(manager.sendRequest('account/rateLimits/read', {})).then(result => {
        applyRateLimits(result);
        render();
        retry();
      }).catch(retry);
      return;
    }
    const bridge = window.electronBridge;
    if (!bridge || typeof bridge.sendMessageFromView !== 'function') {
      retry();
      return;
    }
    try {
      bridge.sendMessageFromView({
        type: 'send-cli-request-for-host',
        payload: {
          hostId: 'local',
          method: 'account/rateLimits/read',
          params: {}
        }
      });
    } catch (_) { }
    retry();
  }

  const onMessage = event => consume(event.data);
  const onViewportChange = () => positionTooltip();
  window.addEventListener('message', onMessage, true);
  window.addEventListener('resize', onViewportChange, true);
  window.addEventListener('scroll', onViewportChange, true);
  const observer = new MutationObserver(scheduleMount);
  observer.observe(document.documentElement, { childList: true, subtree: true });
  const dispose = () => {
    observer.disconnect();
    window.removeEventListener('message', onMessage, true);
    window.removeEventListener('resize', onViewportChange, true);
    window.removeEventListener('scroll', onViewportChange, true);
    if (mountTimer) window.clearTimeout(mountTimer);
    mountTimer = 0;
    if (quotaRequestTimer) window.clearTimeout(quotaRequestTimer);
    quotaRequestTimer = 0;
    if (sessionMotionTimer) window.clearTimeout(sessionMotionTimer);
    sessionMotionTimer = 0;
    if (sessionMotionFrame) window.cancelAnimationFrame(sessionMotionFrame);
    sessionMotionFrame = 0;
    if (host) host.remove();
    host = null;
    removeTooltip();
    if (window[INSTANCE] && window[INSTANCE].dispose === dispose) delete window[INSTANCE];
  };
  window[INSTANCE] = {
    version: 34,
    remount: scheduleMount,
    dispose,
    snapshot: () => ({
      threadId: state.threadId,
      compressions: state.compressions,
      quotaPercent: state.quotaPercent,
      nativeReadMs: state.nativeReadMs
    })
  };
  scheduleMount();
  window.setTimeout(requestRateLimits, 120);
})();

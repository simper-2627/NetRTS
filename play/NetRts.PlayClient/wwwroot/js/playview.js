// The play client's map: the spectator site's MapView (linked in from src/NetRts.Server/wwwroot), plus
// what a player needs on a second canvas stacked over it (selection rings, the drag box, order markers).
// MapView itself is used as is: this file reads its camera (tileSize, offset) and its interpolated unit positions.
//
// Input is reported to Blazor in tile coordinates; Blazor decides what a click means.
import { MapView } from '../mapview.js';
import { cssVar, playerColours } from '../util.js';
import * as lore from '../lore.js';

/** The active brand's vocabulary from lore.js, so the HUD names things exactly as the map and the spectator site do. */
export function loreTables() {
  return {
    snow: lore.SNOW,
    game: lore.GAME,
    ore: lore.ORE,
    buildingNames: lore.SNOW ? lore.BUILDING_NAME : {},
    upgradeNames: lore.SNOW ? lore.UPGRADE_NAME : {},
    hqNames: lore.HQ_NAME,
    housingNames: lore.HOUSING_NAME,
    botNicknames: lore.SNOW ? lore.BOT_NICKNAME : {},
  };
}

/** Engine messages ("Barracks 80 completed") in the active brand's words, exactly as the spectator site tells them. */
export function retell(messages) {
  return messages.map((m) => lore.badgerize(m));
}

const DRAG = 5;           // CSS pixels before a press becomes a drag box
const EDGE = 12;          // CSS pixels from the map's edge where the pointer scrolls the view
const FLASH_MS = 700;     // how long an order marker shows
const PAN_KEYS = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export function createView(frame, canvas, overlay, minimap, host) {
  const view = new MapView(canvas);
  overlay.mapView = view;   // handy from the dev tools console
  const g = overlay.getContext('2d');
  let alive = true;
  let selection = { units: new Set(), buildings: new Set(), inspected: null };
  let mode = null;          // null | 'attack' | 'build'
  let drag = null;          // { start, cur, shift, box }
  let pan = null;           // last pointer position while middle-dragging
  let flashes = [];         // { x, y, kind, t0 }
  let ghost = null;         // { type, owner, mask } while placing a building
  let hover = null;         // { x, y, css } the tile under the pointer, and the pointer in CSS pixels
  let pointerIn = false;
  let theme = readTheme();
  const keysDown = new Set();

  function readTheme() {
    return {
      ok: cssVar('--ok'), danger: cssVar('--danger'), focus: cssVar('--focus'), ink: cssVar('--ink'), signal: cssVar('--signal-bg'),
      open: cssVar('--terrain-open'), rock: cssVar('--terrain-rock'), ore: cssVar('--ore'), fog: cssVar('--fog'),
      players: playerColours(),
    };
  }

  // ---------- layout: the overlay always matches the map canvas
  const fit = () => {
    view.fit(frame.clientWidth, frame.clientHeight);
    overlay.width = canvas.width;
    overlay.height = canvas.height;
    overlay.style.width = canvas.style.width;
    overlay.style.height = canvas.style.height;
  };
  const ro = new ResizeObserver(fit);
  ro.observe(frame);
  const scheme = window.matchMedia('(prefers-color-scheme: dark)');
  const onTheme = () => { view.readTheme(); theme = readTheme(); mini.terrain = null; };
  scheme.addEventListener('change', onTheme);
  window.addEventListener('netrts-theme', onTheme);

  // ---------- geometry
  const scale = () => canvas.width / canvas.getBoundingClientRect().width;
  const local = (e) => { const r = overlay.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  const toWorld = (p) => view.screenToWorld(p.x * scale(), p.y * scale());
  const now = () => performance.now();
  /** Where MapView draws a unit right now (tile units, centre of the glyph). */
  const unitPos = (u, t) => {
    const a = view.anim.get(u.id);
    return a ? view.interp(a, t) : { x: u.x + 0.5, y: u.y + 0.5 };
  };

  /** The tile under the pointer and the unit drawn nearest to it (within about half a tile). */
  function pick(p) {
    if (!view.map) return null;
    const w = toWorld(p);
    const x = Math.floor(w.x), y = Math.floor(w.y);
    if (x < 0 || y < 0 || x >= view.map.width || y >= view.map.height) return null;
    let best = null, bestD = 0.55;
    const t = now();
    for (const u of view.state?.units || []) {
      const q = unitPos(u, t);
      const d = Math.hypot(q.x - w.x, q.y - w.y);
      if (d < bestD) { best = u; bestD = d; }
    }
    return { x, y, unitId: best ? best.id : null };
  }

  function unitsInBox(a, b) {
    const w0 = toWorld(a), w1 = toWorld(b);
    const x0 = Math.min(w0.x, w1.x), x1 = Math.max(w0.x, w1.x), y0 = Math.min(w0.y, w1.y), y1 = Math.max(w0.y, w1.y);
    const t = now();
    return (view.state?.units || []).filter((u) => {
      const q = unitPos(u, t);
      return q.x >= x0 && q.x <= x1 && q.y >= y0 && q.y <= y1;
    }).map((u) => u.id);
  }

  const capture = (e) => { try { overlay.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ } };
  const call = (method, ...args) => { if (alive) host.invokeMethodAsync(method, ...args).catch(() => {}); };

  // ---------- pointer
  overlay.addEventListener('contextmenu', (e) => e.preventDefault());
  overlay.addEventListener('pointerdown', (e) => {
    overlay.focus({ preventScroll: true });
    const p = local(e);
    if (e.button === 0) {
      drag = { start: p, cur: p, shift: e.shiftKey, box: false };
      capture(e);
    } else if (e.button === 1) {
      e.preventDefault();
      pan = p;
      capture(e);
    } else if (e.button === 2) {
      const at = pick(p);
      if (at) call('OnRightClick', at.x, at.y, at.unitId, e.shiftKey);
    }
  });
  overlay.addEventListener('pointermove', (e) => {
    const p = local(e);
    pointerIn = true;
    const at = pick(p);
    const moved = at && (!hover || hover.x !== at.x || hover.y !== at.y);
    hover = at ? { x: at.x, y: at.y, css: p } : null;
    if (moved && mode === 'build') call('OnHover', at.x, at.y);
    if (pan) {
      view.panBy(p.x - pan.x, p.y - pan.y);
      pan = p;
      return;
    }
    if (!drag) return;
    drag.cur = p;
    if (!drag.box && Math.hypot(p.x - drag.start.x, p.y - drag.start.y) > DRAG && mode == null) drag.box = true;
  });
  const release = (e) => {
    if (pan) { pan = null; return; }
    if (!drag) return;
    const d = drag;
    drag = null;
    if (e.type === 'pointercancel') return;
    if (d.box) {
      call('OnBox', unitsInBox(d.start, d.cur), d.shift);
    } else {
      const at = pick(d.start);
      if (at) call('OnClick', at.x, at.y, at.unitId, e.shiftKey, e.ctrlKey || e.metaKey);
    }
  };
  overlay.addEventListener('pointerleave', () => { pointerIn = false; hover = null; });
  overlay.addEventListener('pointerup', release);
  overlay.addEventListener('pointercancel', release);
  overlay.addEventListener('dblclick', (e) => {
    const at = pick(local(e));
    if (at && mode == null) call('OnDoubleClick', at.x, at.y, at.unitId, e.shiftKey);
  });
  overlay.addEventListener('wheel', (e) => {
    e.preventDefault();
    const p = local(e);
    view.zoomAt(Math.exp(-e.deltaY * (e.deltaMode === 1 ? 0.05 : 0.0018)), p.x, p.y);
  }, { passive: false });

  // ---------- keyboard: arrows pan here; everything else goes to Blazor (hotkeys, control groups)
  const typing = (e) => e.target instanceof HTMLElement && (e.target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName));
  const onKeyDown = (e) => {
    if (typing(e) || e.altKey) return;
    if (PAN_KEYS[e.key]) { keysDown.add(e.key); e.preventDefault(); return; }
    if (e.key === '+' || e.key === '=') { view.zoomCentre(1.25); return; }
    if (e.key === '-' || e.key === '_') { view.zoomCentre(0.8); return; }
    // Digits by key position, so Shift+1 is "1" (not "!") on every layout.
    const digit = /^(?:Digit|Numpad)([0-9])$/.exec(e.code)?.[1];
    if (digit) {
      e.preventDefault();   // Ctrl+1-9 are control groups here, not tab switching
      call('OnKey', digit, e.ctrlKey || e.metaKey, e.shiftKey);
      return;
    }
    if (/^[a-zA-Z]$/.test(e.key) || e.key === 'Escape' || e.key === ' ') {
      if (e.key === ' ') e.preventDefault();
      call('OnKey', e.key, e.ctrlKey || e.metaKey, e.shiftKey);
    }
  };
  const onKeyUp = (e) => keysDown.delete(e.key);
  const onBlur = () => keysDown.clear();
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('blur', onBlur);

  // ---------- overlay drawing, every frame
  let raf = 0;
  let last = now();
  const loop = (t) => {
    raf = requestAnimationFrame(loop);
    const dt = Math.min(0.1, (t - last) / 1000);
    last = t;
    let dx = 0, dy = 0;
    for (const k of keysDown) { dx += PAN_KEYS[k][0]; dy += PAN_KEYS[k][1]; }
    // The pointer at the edge of the map scrolls it, as in most RTS games (not while dragging).
    if (pointerIn && hover && !drag && !pan && document.hasFocus()) {
      const r = overlay.getBoundingClientRect();
      if (hover.css.x < EDGE) dx -= 1; else if (hover.css.x > r.width - EDGE) dx += 1;
      if (hover.css.y < EDGE) dy -= 1; else if (hover.css.y > r.height - EDGE) dy += 1;
    }
    if (dx || dy) {
      const r = canvas.getBoundingClientRect();
      view.panBy(-dx * r.width * 0.9 * dt, -dy * r.height * 0.9 * dt);
    }
    draw(t);
    if (t - mini.drawn > 100) drawMinimap(t);
  };
  raf = requestAnimationFrame(loop);

  function draw(t) {
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, overlay.width, overlay.height);
    if (!view.map || !view.cam) return;
    const Te = view.tileSize();
    const { ox, oy } = view.offset(Te);
    g.translate(-ox, -oy);
    const s = view.state;
    const lw = Math.max(1.5, Math.min(3, Te / 12));

    if (s) {
      // Selected units: a ring under the pointer's colour, green for yours.
      g.lineWidth = lw;
      for (const u of s.units) {
        const mine = selection.units.has(u.id), looked = selection.inspected === u.id;
        if (!mine && !looked) continue;
        const q = unitPos(u, t);
        g.strokeStyle = mine ? theme.ok : theme.danger;
        g.beginPath();
        g.arc(q.x * Te, q.y * Te, Math.max(3, Te * 0.46 * (u._scale || 1)), 0, Math.PI * 2);
        g.stroke();
      }
      for (const b of s.buildings) {
        const mine = selection.buildings.has(b.id), looked = selection.inspected === b.id;
        if (!mine && !looked) continue;
        g.strokeStyle = mine ? theme.ok : theme.danger;
        g.strokeRect(b.x * Te - lw, b.y * Te - lw, Te + 2 * lw, Te + 2 * lw);
        if (mine && b.rally) {
          // Rally point: a dashed line to a small flag.
          g.setLineDash([Te / 5, Te / 6]);
          g.beginPath();
          g.moveTo((b.x + 0.5) * Te, (b.y + 0.5) * Te);
          g.lineTo((b.rally.x + 0.5) * Te, (b.rally.y + 0.5) * Te);
          g.stroke();
          g.setLineDash([]);
          g.fillStyle = theme.ok;
          g.fillRect((b.rally.x + 0.5) * Te - lw, (b.rally.y + 0.15) * Te, lw * 2, Te * 0.7);
        }
      }
      for (const r of s.resources) {
        if (selection.inspected !== r.id) continue;
        g.strokeStyle = theme.focus;
        g.strokeRect(r.x * Te - lw, r.y * Te - lw, Te + 2 * lw, Te + 2 * lw);
      }
    }

    // Order markers: a shrinking ring where an order was given.
    flashes = flashes.filter((f) => t - f.t0 < FLASH_MS);
    for (const f of flashes) {
      const k = (t - f.t0) / FLASH_MS;
      g.globalAlpha = 1 - k;
      g.strokeStyle = f.kind === 'attack' ? theme.danger : f.kind === 'rally' ? theme.signal : theme.ok;
      g.lineWidth = lw;
      g.beginPath();
      g.arc((f.x + 0.5) * Te, (f.y + 0.5) * Te, Te * (0.6 - 0.35 * (reducedMotion() ? 0 : k)), 0, Math.PI * 2);
      g.stroke();
      g.globalAlpha = 1;
    }

    // Build ghost: the building's square on the tile under the pointer, green where it may go.
    if (ghost && hover) {
      const ok = ghost.mask?.[hover.y]?.[hover.x] === '1';
      const x = hover.x * Te, y = hover.y * Te;
      g.globalAlpha = 0.45;
      g.fillStyle = ok ? theme.players[ghost.owner % theme.players.length] : theme.danger;
      g.fillRect(x, y, Te, Te);
      g.globalAlpha = 1;
      g.strokeStyle = ok ? theme.ok : theme.danger;
      g.lineWidth = Math.max(2, lw);
      g.strokeRect(x + g.lineWidth / 2, y + g.lineWidth / 2, Te - g.lineWidth, Te - g.lineWidth);
      if (Te >= 14) {
        g.fillStyle = theme.ink;
        g.font = `700 ${Math.round(Te * 0.5)}px Bahnschrift, "Segoe UI", sans-serif`;
        g.textAlign = 'center';
        g.textBaseline = 'middle';
        g.fillText(ok ? ghost.type[0] : '\u00d7', x + Te / 2, y + Te / 2);
      }
    }

    g.setTransform(1, 0, 0, 1, 0, 0);
    if (drag?.box) {
      const sc = scale();
      const x0 = Math.min(drag.start.x, drag.cur.x) * sc, y0 = Math.min(drag.start.y, drag.cur.y) * sc;
      const w = Math.abs(drag.cur.x - drag.start.x) * sc, h = Math.abs(drag.cur.y - drag.start.y) * sc;
      g.fillStyle = theme.ok;
      g.globalAlpha = 0.12;
      g.fillRect(x0, y0, w, h);
      g.globalAlpha = 1;
      g.strokeStyle = theme.ok;
      g.lineWidth = 1.5;
      g.strokeRect(x0 + 0.5, y0 + 0.5, w, h);
    }
  }

  // ---------- minimap: terrain, what you can see, and the camera; click to look, right-click to order
  const mg = minimap.getContext('2d');
  const mini = { terrain: null, drawn: 0, dragging: false };
  const K = 3;   // minimap pixels per tile
  function minimapTile(e) {
    if (!view.map) return null;
    const r = minimap.getBoundingClientRect();
    const x = Math.floor(((e.clientX - r.left) / r.width) * view.map.width);
    const y = Math.floor(((e.clientY - r.top) / r.height) * view.map.height);
    return { x: Math.max(0, Math.min(view.map.width - 1, x)), y: Math.max(0, Math.min(view.map.height - 1, y)) };
  }
  const lookAtTile = (t) => {
    if (view.zoom <= 1) view.zoom = Math.min(view.maxZoom, 2.5);
    view.cam = { x: t.x + 0.5, y: t.y + 0.5 };
    view.clampCamera();
    view.dirty = true;
  };
  minimap.addEventListener('contextmenu', (e) => e.preventDefault());
  minimap.addEventListener('pointerdown', (e) => {
    const t = minimapTile(e);
    if (!t) return;
    if (e.button === 2) { call('OnRightClick', t.x, t.y, null, e.shiftKey); return; }
    if (e.button !== 0) return;
    mini.dragging = true;
    try { minimap.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ }
    lookAtTile(t);
  });
  minimap.addEventListener('pointermove', (e) => { if (mini.dragging) { const t = minimapTile(e); if (t) lookAtTile(t); } });
  const miniUp = () => { mini.dragging = false; };
  minimap.addEventListener('pointerup', miniUp);
  minimap.addEventListener('pointercancel', miniUp);

  function drawMinimap(t) {
    mini.drawn = t;
    const map = view.map;
    if (!map) return;
    const W = map.width, H = map.height;
    if (minimap.width !== W * K || minimap.height !== H * K) { minimap.width = W * K; minimap.height = H * K; mini.terrain = null; }
    if (!mini.terrain) {
      const c = document.createElement('canvas');
      c.width = W * K; c.height = H * K;
      const tg = c.getContext('2d');
      tg.fillStyle = theme.open;
      tg.fillRect(0, 0, c.width, c.height);
      tg.fillStyle = theme.rock;
      map.terrain.forEach((row, y) => { for (let x = 0; x < W; x++) if (row[x] === '#') tg.fillRect(x * K, y * K, K, K); });
      mini.terrain = c;
    }
    mg.setTransform(1, 0, 0, 1, 0, 0);
    mg.drawImage(mini.terrain, 0, 0);
    const s = view.state;
    if (s) {
      mg.fillStyle = theme.ore;
      for (const r of s.resources) mg.fillRect(r.x * K, r.y * K, K, K);
      for (const b of s.buildings) {
        mg.fillStyle = theme.players[b.owner % theme.players.length];
        mg.globalAlpha = b.remembered ? 0.55 : 1;
        mg.fillRect(b.x * K - 1, b.y * K - 1, K + 2, K + 2);
      }
      mg.globalAlpha = 1;
      for (const u of s.units) {
        mg.fillStyle = selection.units.has(u.id) ? theme.ok : theme.players[u.owner % theme.players.length];
        mg.fillRect(u.x * K, u.y * K, K - 1, K - 1);
      }
      const vis = s.players?.[0]?.visibility;
      if (vis?.length) {
        mg.fillStyle = theme.fog;
        for (let y = 0; y < H; y++) {
          const row = vis[y] || '';
          let start = -1;
          for (let x = 0; x <= W; x++) {
            const fogged = x < W && row[x] === '0';
            if (fogged && start < 0) start = x;
            if (!fogged && start >= 0) { mg.fillRect(start * K, y * K, (x - start) * K, K); start = -1; }
          }
        }
      }
    }
    // The camera: what the big map shows.
    const Te = view.tileSize();
    const { ox, oy } = view.offset(Te);
    const x0 = Math.max(0, ox / Te), y0 = Math.max(0, oy / Te);
    const x1 = Math.min(W, (ox + canvas.width) / Te), y1 = Math.min(H, (oy + canvas.height) / Te);
    mg.strokeStyle = theme.ink;
    mg.lineWidth = 1.5;
    mg.strokeRect(x0 * K + 0.75, y0 * K + 0.75, (x1 - x0) * K - 1.5, (y1 - y0) * K - 1.5);
  }

  return {
    setMap(map) { view.setMap(map); mini.terrain = null; fit(); },
    setState(state) { view.setState(state); },
    setVision(slot) { view.setVision(slot); },
    setSelection(units, buildings, inspected) {
      selection = { units: new Set(units), buildings: new Set(buildings), inspected: inspected ?? null };
    },
    setMode(m) {
      mode = m || null;
      if (mode !== 'build') ghost = null;
      overlay.style.cursor = mode ? 'crosshair' : 'default';
    },
    setGhost(type, owner, mask) { ghost = { type, owner, mask }; },
    flash(x, y, kind) { flashes.push({ x, y, kind, t0: now() }); },
    /** Centre the camera on a tile (control groups, the minimap). */
    lookAt(x, y) {
      if (!view.map) return;
      if (view.zoom < 2) view.zoom = Math.min(view.maxZoom, 2.5);
      view.cam = { x: x + 0.5, y: y + 0.5 };
      view.clampCamera();
      view.dirty = true;
    },
    dispose() {
      alive = false;
      cancelAnimationFrame(raf);
      ro.disconnect();
      scheme.removeEventListener('change', onTheme);
      window.removeEventListener('netrts-theme', onTheme);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('blur', onBlur);
      view.destroy();
    },
  };
}

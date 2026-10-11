(function () {
  "use strict";
  const margin = 12;
  const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max));

  function selectHeaderAnchor(rectangles, viewportWidth) {
    const candidates = rectangles.filter(r =>
      [r.left, r.top, r.right, r.bottom, r.width, r.height].every(Number.isFinite) &&
      r.width > 0 && r.height >= 20 && r.height <= 80 && r.top >= 0 && r.top < 100 &&
      r.right <= viewportWidth + 1);
    if (!candidates.length) return null;
    candidates.sort((a, b) => b.right - a.right);
    const edge = candidates[0];
    if (edge.right <= viewportWidth - Math.min(340, viewportWidth / 2)) return null;
    let left = edge.left;
    let bottom = edge.bottom;
    let top = edge.top;
    // Seed from the right edge, but retain adjacent actions that cross the viewport midpoint.
    for (const r of candidates.slice(1)) {
      if (r.right < left - 40 || r.bottom <= edge.top || r.top >= edge.bottom) continue;
      left = Math.min(left, r.left);
      top = Math.min(top, r.top);
      bottom = Math.max(bottom, r.bottom);
    }
    return { left, top, right: edge.right, bottom };
  }

  function resolveLauncher(anchor, width, height) {
    const size = 40;
    let x = anchor ? anchor.left - size - 10 : width - size - margin;
    let y = anchor ? anchor.top + (anchor.bottom - anchor.top - size) / 2 : margin;
    // A crowded header gets a second row, never an orb covering an account button.
    if (anchor && x < margin) { x = width - size - margin; y = anchor.bottom + 8; }
    return { x: clamp(x, margin, width - size - margin), y: clamp(y, 4, height - size - 4), w: size, h: size };
  }

  function resolvePanel(preferred, custom, anchor, width, height) {
    const launcher = resolveLauncher(anchor, width, height);
    const w = clamp(custom ? preferred.w : 560, Math.min(360, width - margin * 2), width - margin * 2);
    const top = Math.max(anchor ? anchor.bottom : 52, launcher.y + launcher.h) + 8;
    const h = clamp(custom ? preferred.h : Math.min(720, height - top - margin),
      Math.min(180, height - margin * 2), height - margin * 2);
    return {
      x: clamp(custom ? preferred.x : (anchor ? anchor.right : width - margin) - w, margin, width - w - margin),
      y: clamp(custom ? preferred.y : top, margin, height - h - margin), w, h
    };
  }

  window.VAL_DockLayout = Object.freeze({ selectHeaderAnchor, resolveLauncher, resolvePanel });
})();

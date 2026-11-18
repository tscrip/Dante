// Draws DANTE simulator frames ({"t":"sim.frame",...}) onto <canvas id="sim-screen">.
(() => {
  const bytes = (b64) => Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));

  // OLED: SSD1306 buffer, 4 pages x 128 columns, one byte per column per page, LSB = top pixel.
  function drawOled(canvas, f) {
    const buf = bytes(f.pixels);
    const ctx = canvas.getContext('2d');
    const img = ctx.createImageData(f.w, f.h);
    for (let y = 0; y < f.h; y++) {
      for (let x = 0; x < f.w; x++) {
        const on = (buf[(y >> 3) * f.w + x] >> (y & 7)) & 1;
        const i = (y * f.w + x) * 4;
        img.data[i] = on ? 0x7e : 0x05;
        img.data[i + 1] = on ? 0xf3 : 0x07;
        img.data[i + 2] = on ? 0xe0 : 0x0c;
        img.data[i + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
  }

  // TFT: run-length encoded RGB565, little-endian uint16 pairs (run length, colour).
  function drawTft(canvas, f) {
    const rle = bytes(f.rle);
    const ctx = canvas.getContext('2d');
    const img = ctx.createImageData(f.w, f.h);
    const d = img.data;
    let p = 0;
    for (let i = 0; i + 3 < rle.length; i += 4) {
      const n = rle[i] | (rle[i + 1] << 8);
      const c = rle[i + 2] | (rle[i + 3] << 8);
      const r = ((c >> 11) & 31) * 255 / 31, g = ((c >> 5) & 63) * 255 / 63, b = (c & 31) * 255 / 31;
      for (let k = 0; k < n; k++, p += 4) { d[p] = r; d[p + 1] = g; d[p + 2] = b; d[p + 3] = 255; }
    }
    ctx.putImageData(img, 0, 0);
    canvas.style.filter = `brightness(${Math.max(0.08, (f.light ?? 255) / 255)})`;   // the PWM backlight
  }

  // Character LCD: 5x8 cells with a 1 px gap. Codes 0-7 are the custom glyphs, 255 a full block,
  // the rest ASCII (drawn with a small font as a stand-in for the HD44780 ROM).
  const fontCache = new Map();
  const fontCanvas = document.createElement('canvas');
  fontCanvas.width = 8; fontCanvas.height = 10;
  const fontCtx = fontCanvas.getContext('2d', { willReadFrequently: true });
  function charRows(code) {
    if (fontCache.has(code)) return fontCache.get(code);
    fontCtx.clearRect(0, 0, 8, 10);
    fontCtx.fillStyle = '#000';
    fontCtx.font = 'bold 8px monospace';
    fontCtx.textBaseline = 'top';
    fontCtx.fillText(String.fromCharCode(code), 0, 0);
    const d = fontCtx.getImageData(0, 0, 5, 8).data;
    const rows = [];
    for (let y = 0; y < 8; y++) {
      let b = 0;
      for (let x = 0; x < 5; x++) if (d[(y * 5 + x) * 4 + 3] > 110) b |= 0x10 >> x;
      rows.push(b);
    }
    fontCache.set(code, rows);
    return rows;
  }

  function drawLcd(canvas, f) {
    const cells = bytes(f.cells), glyphs = bytes(f.glyphs);
    const ctx = canvas.getContext('2d');
    const lit = f.light;
    ctx.fillStyle = lit ? '#2e5bff' : '#0d1a4a';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    for (let r = 0; r < f.rows; r++) {
      for (let c = 0; c < f.cols; c++) {
        const code = cells[r * f.cols + c];
        const bits = code < 8 ? Array.from(glyphs.slice(code * 8, code * 8 + 8))
          : code === 255 ? [31, 31, 31, 31, 31, 31, 31, 31] : charRows(code);
        for (let y = 0; y < 8; y++) {
          for (let x = 0; x < 5; x++) {
            const on = bits[y] & (0x10 >> x);
            ctx.fillStyle = on ? (lit ? '#f2f6ff' : '#6a7bb0') : (lit ? '#3d68ff' : '#122259');
            ctx.fillRect(1 + c * 6 + x, 1 + r * 9 + y, 1, 1);
          }
        }
      }
    }
  }

  window.danteSim = {
    draw(frameJson) {
      const f = JSON.parse(frameJson);
      const canvas = document.getElementById('sim-screen');
      if (!canvas) return;
      if (f.kind === 'oled') drawOled(canvas, f);
      else if (f.kind === 'tft') drawTft(canvas, f);
      else drawLcd(canvas, f);
    },
  };
})();

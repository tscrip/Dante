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

  window.danteSim = {
    draw(frameJson) {
      const f = JSON.parse(frameJson);
      const canvas = document.getElementById('sim-screen');
      if (!canvas) return;
      if (f.kind === 'tft') drawTft(canvas, f);
      else drawOled(canvas, f);
    },
  };
})();

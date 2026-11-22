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

  // Yellow board: mouse (or finger) on the canvas = a finger on DANTE's touch screen. Reports
  // (fingers, x, y) in screen pixels to the page's .NET object. Right-click or Shift+click = two fingers.
  function attachTouch(dotnet) {
    const canvas = document.getElementById('sim-screen');
    if (!canvas || canvas.danteTouch) return;   // already attached to this canvas
    canvas.danteTouch = true;
    canvas.style.cursor = 'pointer';
    canvas.style.touchAction = 'none';
    let fingers = 0, lastMove = 0;
    const at = (e) => {
      const r = canvas.getBoundingClientRect();
      // The canvas has a border and is scaled by CSS; map to the 480x320 screen.
      const bx = (canvas.offsetWidth - canvas.clientWidth) / 2, by = (canvas.offsetHeight - canvas.clientHeight) / 2;
      const x = (e.clientX - r.left - bx) * canvas.width / canvas.clientWidth;
      const y = (e.clientY - r.top - by) * canvas.height / canvas.clientHeight;
      return [Math.round(x), Math.round(y)];
    };
    const send = (n, e) => dotnet.invokeMethodAsync('Touch', n, ...at(e)).catch(() => {});
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    canvas.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      canvas.setPointerCapture(e.pointerId);
      fingers = e.button === 2 || e.shiftKey ? 2 : 1;
      send(fingers, e);
    });
    canvas.addEventListener('pointermove', (e) => {
      if (!fingers || e.timeStamp - lastMove < 30) return;   // only while pressed, ~30 updates a second
      lastMove = e.timeStamp;
      send(fingers, e);
    });
    const up = (e) => {
      if (!fingers) return;
      fingers = 0;
      send(0, e);
    };
    canvas.addEventListener('pointerup', up);
    canvas.addEventListener('pointercancel', up);
  }

  window.danteSim = {
    attachTouch,
    draw(frameJson) {
      const f = JSON.parse(frameJson);
      const canvas = document.getElementById('sim-screen');
      if (!canvas) return;
      if (f.kind === 'tft') drawTft(canvas, f);
      else drawOled(canvas, f);
    },
  };
})();

#include "Link.h"
#include "config.h"
#include <BluetoothSerial.h>

namespace {
  BluetoothSerial bt;
  Link::Handler onMsg = nullptr;

  struct LineReader {
    char buf[RX_LINE_MAX];
    size_t len = 0;
    bool overflow = false;
  };
  LineReader usbRx, btRx;

  enum Source { SRC_USB, SRC_BT };
  Source active = SRC_BT;
  uint32_t lastHeard = 0;
  bool everHeard = false;

  void dispatch(LineReader& r, Source src) {
    r.buf[r.len] = 0;
    if (r.len == 0 || r.buf[0] != '{') return;   // ignore noise / debug text
    JsonDocument doc;
    if (deserializeJson(doc, r.buf, r.len)) return;
    if (!doc["t"].is<const char*>()) return;
    active = src;
    lastHeard = millis();
    everHeard = true;
    if (onMsg) onMsg(doc);
  }

  void pump(Stream& s, LineReader& r, Source src) {
    int budget = 512;   // keep the frame loop responsive
    while (s.available() && budget-- > 0) {
      char c = (char)s.read();
      if (c == '\r') continue;
      if (c == '\n') {
        if (!r.overflow) dispatch(r, src);
        r.len = 0;
        r.overflow = false;
        continue;
      }
      if (r.len < RX_LINE_MAX - 1) r.buf[r.len++] = c;
      else r.overflow = true;   // drop the rest of an oversize line
    }
  }
}

namespace Link {
  void begin(Handler handler) {
    onMsg = handler;
    Serial.begin(USB_BAUD);
    bt.begin(DEVICE_NAME);
  }

  void poll() {
    pump(Serial, usbRx, SRC_USB);
    pump(bt, btRx, SRC_BT);
  }

  void send(JsonDocument& msg) {
    char out[384];
    size_t n = serializeJson(msg, out, sizeof(out) - 2);
    out[n++] = '\n';
    if (active == SRC_BT) {
      if (bt.hasClient()) bt.write((const uint8_t*)out, n);
    } else {
      Serial.write((const uint8_t*)out, n);
    }
  }

  bool pcAlive() { return everHeard && (millis() - lastHeard) < PC_TIMEOUT_MS; }
  bool btConnected() { return bt.hasClient(); }
  uint32_t lastHeardMs() { return lastHeard; }
}

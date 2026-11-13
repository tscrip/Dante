#include "Link.h"
#include "config.h"
#include "BleUart.h"

// Links, newest message wins the reply:
//   BLE   - Nordic UART Service (the normal way: wireless)
//   USB   - `Serial`: on the ESP32-S3 with USB CDC on boot this is the native USB port
//   UART  - `Serial0`: the S3 DevKit's second ("UART"/CP210x) USB port, handy while developing
namespace {
  Link::Handler onMsg = nullptr;
  Link::TextHandler onTxt = nullptr;

  struct LineReader {
    char buf[RX_LINE_MAX];
    size_t len = 0;
    bool overflow = false;
  };
  LineReader usbRx, bleRx, uartRx;

  enum Source { SRC_USB, SRC_BLE, SRC_UART };
  Source active = SRC_BLE;
  uint32_t lastHeard = 0;
  bool everHeard = false;

  void dispatch(LineReader& r, Source src) {
    r.buf[r.len] = 0;
    if (r.len == 0) return;
    if (r.buf[0] != '{') {                        // a human typing in a serial monitor
      if (onTxt) { active = src; onTxt(r.buf); }
      return;
    }
    JsonDocument doc;
    if (deserializeJson(doc, r.buf, r.len)) return;
    if (!doc["t"].is<const char*>()) return;
    active = src;
    lastHeard = millis();
    everHeard = true;
    if (onMsg) onMsg(doc);
  }

  void feed(char c, LineReader& r, Source src) {
    if (c == '\r') return;
    if (c == '\n') {
      if (!r.overflow) dispatch(r, src);
      r.len = 0;
      r.overflow = false;
      return;
    }
    if (r.len < RX_LINE_MAX - 1) r.buf[r.len++] = c;
    else r.overflow = true;   // drop the rest of an oversize line
  }

  void pump(Stream& s, LineReader& r, Source src) {
    int budget = 512;   // keep the frame loop responsive
    while (s.available() && budget-- > 0) feed((char)s.read(), r, src);
  }

  void pumpBle() {
    int budget = 1024;
    int c;
    while (budget-- > 0 && (c = BleUart::read()) >= 0) feed((char)c, bleRx, SRC_BLE);
  }
}

namespace Link {
  void onText(TextHandler handler) { onTxt = handler; }

  void sendText(const char* line) {
    size_t n = strlen(line);
    switch (active) {
      case SRC_BLE: BleUart::write((const uint8_t*)line, n); BleUart::write((const uint8_t*)"\r\n", 2); break;
#if ARDUINO_USB_CDC_ON_BOOT
      case SRC_UART: Serial0.write((const uint8_t*)line, n); Serial0.write((const uint8_t*)"\r\n", 2); break;
#endif
      default: Serial.write((const uint8_t*)line, n); Serial.write((const uint8_t*)"\r\n", 2); break;
    }
  }

  void begin(Handler handler) {
    onMsg = handler;
    Serial.begin(USB_BAUD);
#if ARDUINO_USB_CDC_ON_BOOT
    Serial0.begin(USB_BAUD);
#endif
    BleUart::begin(DEVICE_NAME);
  }

  void poll() {
    pump(Serial, usbRx, SRC_USB);
#if ARDUINO_USB_CDC_ON_BOOT
    pump(Serial0, uartRx, SRC_UART);
#endif
    pumpBle();
  }

  void send(JsonDocument& msg) {
    char out[768];   // big enough for a `diag` reply
    size_t n = serializeJson(msg, out, sizeof(out) - 2);
    out[n++] = '\n';
    switch (active) {
      case SRC_BLE: BleUart::write((const uint8_t*)out, n); break;
#if ARDUINO_USB_CDC_ON_BOOT
      case SRC_UART: Serial0.write((const uint8_t*)out, n); break;
#endif
      default: Serial.write((const uint8_t*)out, n); break;
    }
  }

  bool pcAlive() { return everHeard && (millis() - lastHeard) < PC_TIMEOUT_MS; }
  bool btConnected() { return BleUart::connected(); }
  uint32_t lastHeardMs() { return lastHeard; }
}

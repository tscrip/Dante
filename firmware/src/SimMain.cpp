// DANTE simulator: runs this firmware on a PC (`pio run -e simulator`). Compiled only for the
// simulator build; the ESP32 build sees an empty file.
//
// The PC app starts the program and talks to it over stdin/stdout:
//   * every protocol line (docs/protocol.md) goes in and out exactly as over USB serial;
//   * extra lines for the simulator itself:
//       PC  -> sim  {"t":"sim.btn","b":"left|right|sel|magic","down":true|false}
//       PC  -> sim  {"t":"sim.touch","n":0|1|2,"x":0-479,"y":0-319}   fingers on the screen (cyd only)
//       sim -> PC   {"t":"sim.frame", ...}   what the screens show, whenever it changes.
//
// Usage: program [--hw oled|tft|cyd]
//   oled = 0.91" 128x32 OLED
//   tft  = 3.5" 480x320 ST7796 TFT (drawn by the real LovyanGFX into a memory "panel")
//   cyd  = the ESP32-3248S035C "yellow board": the same TFT with a touch panel (mouse clicks = taps)
#ifdef DANTE_SIMULATOR

#include <Arduino.h>
#include <ArduinoJson.h>
#include <SPI.h>
#include <Wire.h>
#include <atomic>
#include <chrono>
#include <iostream>
#include <mutex>
#include <random>
#include <string>
#include <thread>

#include "config.h"
#include "Displays.h"
#include "Tft.h"
#include "Touch.h"

void setup();
void loop();

// ---- the stand-ins declared in simulator/host ----------------------------------------------
int g_simPins[64];
bool g_simPsram = true;
HardwareSerial Serial(true);
HardwareSerial Serial0(false);
TwoWire Wire;
TwoWire Wire1;
SPIClass SPI;

namespace {
const auto t0 = std::chrono::steady_clock::now();

std::mutex inboxGate;
std::deque<std::string> inbox;        // lines from stdin, filled by the reader thread
std::atomic<bool> stdinClosed{false};
std::deque<uint8_t> serialRx;         // bytes the firmware reads from `Serial`

// Button changes from the PC app, applied at least BTN_GAP_MS apart. A quick click can arrive
// as "down" and "up" in the same batch; applied together the firmware's debounce would never
// see the button pressed.
const uint32_t BTN_GAP_MS = BTN_DEBOUNCE_MS + 15;
std::deque<std::pair<int, int>> buttonQueue;   // (pin, level)
uint32_t lastButtonChange = 0;

// Touches from the PC app, spaced out the same way so a quick click is seen as down, then up. Moves
// while the finger count stays the same just update the latest position.
struct SimTouch { int n, x, y; };
const uint32_t TOUCH_GAP_MS = 30;
std::deque<SimTouch> touchQueue;
uint32_t lastTouchChange = 0;

void queueTouch(SimTouch t) {
  if (!touchQueue.empty() && touchQueue.back().n == t.n) touchQueue.back() = t;
  else touchQueue.push_back(t);
}

void applyTouches() {
  if (touchQueue.empty() || millis() - lastTouchChange < TOUCH_GAP_MS) return;
  const SimTouch& t = touchQueue.front();
  Touch::simSet((uint8_t)t.n, t.x, t.y);
  touchQueue.pop_front();
  lastTouchChange = millis();
}

void applyButtons() {
  if (buttonQueue.empty() || millis() - lastButtonChange < BTN_GAP_MS) return;
  g_simPins[buttonQueue.front().first] = buttonQueue.front().second;
  buttonQueue.pop_front();
  lastButtonChange = millis();
}

void readStdin() {
  std::string line;
  while (std::getline(std::cin, line)) {
    if (!line.empty() && line.back() == '\r') line.pop_back();
    std::lock_guard<std::mutex> lock(inboxGate);
    inbox.push_back(line);
  }
  stdinClosed = true;                  // the PC app went away: so do we
}

int pinFor(const char* b) {
  if (!strcmp(b, "left")) return PIN_BTN_LEFT;
  if (!strcmp(b, "right")) return PIN_BTN_RIGHT;
  if (!strcmp(b, "sel")) return PIN_BTN_SELECT;
  if (!strcmp(b, "magic")) return PIN_BTN_MAGIC;
  return -1;
}

// Lines for the simulator itself are handled here; everything else goes to the firmware.
void pumpInbox() {
  std::deque<std::string> lines;
  {
    std::lock_guard<std::mutex> lock(inboxGate);
    lines.swap(inbox);
  }
  for (auto& line : lines) {
    if (line.rfind("{\"t\":\"sim.", 0) == 0) {
      JsonDocument d;
      if (deserializeJson(d, line)) continue;
      const char* t = d["t"] | "";
      if (!strcmp(t, "sim.btn")) {
        int pin = pinFor(d["b"] | "");
        if (pin >= 0) buttonQueue.emplace_back(pin, (d["down"] | false) ? LOW : HIGH);   // pressed = pulled to GND
      } else if (!strcmp(t, "sim.touch")) {
        int n = d["n"] | 0;
        queueTouch({ constrain(n, 0, 5), d["x"] | 0, d["y"] | 0 });
      }
      continue;
    }
    for (char c : line) serialRx.push_back((uint8_t)c);
    serialRx.push_back('\n');
  }
}

std::string base64(const uint8_t* data, size_t n) {
  static const char* abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  std::string out;
  out.reserve((n + 2) / 3 * 4);
  for (size_t i = 0; i < n; i += 3) {
    uint32_t v = (uint32_t)data[i] << 16;
    if (i + 1 < n) v |= (uint32_t)data[i + 1] << 8;
    if (i + 2 < n) v |= data[i + 2];
    out += abc[(v >> 18) & 63];
    out += abc[(v >> 12) & 63];
    out += i + 1 < n ? abc[(v >> 6) & 63] : '=';
    out += i + 2 < n ? abc[v & 63] : '=';
  }
  return out;
}

// Run-length encoding of RGB565 pixels: little-endian uint16 pairs (run length, colour). DANTE's
// screens are mostly flat colour, so a 300 KB frame usually shrinks to a few tens of KB.
std::string rle565(const uint16_t* px, size_t n) {
  std::string out;
  out.reserve(8192);
  size_t i = 0;
  while (i < n) {
    uint16_t c = px[i];
    size_t run = 1;
    while (i + run < n && px[i + run] == c && run < 0xFFFF) run++;
    uint8_t rec[4] = { (uint8_t)(run & 0xFF), (uint8_t)(run >> 8), (uint8_t)(c & 0xFF), (uint8_t)(c >> 8) };
    out.append((const char*)rec, 4);
    i += run;
  }
  return out;
}

std::string frameJson() {
  std::string j = "{\"t\":\"sim.frame\",\"fw\":\"" FW_VERSION "\"";
  // The yellow board's notification light (P3), so the PC app can show it blinking.
  if (PIN_LIGHT >= 0 && Touch::available()) j += g_simPins[PIN_LIGHT] == LIGHT_ON ? ",\"led\":true" : ",\"led\":false";
  switch (Displays::active()) {
    case Displays::KIND_TFT: {
      static uint16_t px[TFT_W * TFT_H];
      uint8_t light = 0;
      if (!Tft::simSnapshot(px, light)) memset(px, 0, sizeof(px));
      std::string rle = rle565(px, TFT_W * TFT_H);
      j += ",\"kind\":\"tft\",\"w\":" + std::to_string(TFT_W) + ",\"h\":" + std::to_string(TFT_H) +
           ",\"light\":" + std::to_string(light) + ",\"rle\":\"" + base64((const uint8_t*)rle.data(), rle.size()) + "\"";
      break;
    }
    default:   // OLED. SSD1306 layout: 4 pages x 128 columns, one byte per column per page, LSB on top.
      j += ",\"kind\":\"oled\",\"w\":128,\"h\":32,\"pixels\":\"" + base64(Displays::oled().getBufferPtr(), 512) + "\"";
      break;
  }
  j += "}\n";
  return j;
}

void emitFrame() {
  static std::string last;
  static uint32_t lastAt = 0;
  uint32_t now = millis();
  // At most ~25 frames per second (20 for the big TFT frames).
  if (now - lastAt < (Displays::isTft() ? 50u : 40u)) return;
  std::string j = frameJson();
  if (j == last) return;
  last = j;
  lastAt = now;
  fwrite(j.data(), 1, j.size(), stdout);
  fflush(stdout);
}

void simulateHardware(const std::string& hw) {
  // What answers on the I2C bus: the firmware's own detection then picks the screen. Nothing
  // answering means the TFT (on SPI, which can't be detected), exactly as on the board.
  const uint8_t OLED = 0x3C;
  if (hw == "tft" || hw == "cyd") Wire.present = {};
  else                  Wire.present = { OLED };
  Touch::simEnable(hw == "cyd");   // the yellow board has a touch panel; the others have buttons
  g_simPsram = hw != "cyd";        // ...and no PSRAM, so its TFT draws in strips, like the real board
}
} // namespace

// ---- Arduino core stand-ins ---------------------------------------------------------------
uint32_t millis() {
  return (uint32_t)std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();
}
uint32_t micros() {
  return (uint32_t)std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - t0).count();
}
void delay(uint32_t ms) { std::this_thread::sleep_for(std::chrono::milliseconds(ms)); }
void delayMicroseconds(uint32_t us) { std::this_thread::sleep_for(std::chrono::microseconds(us)); }
uint32_t esp_random() {
  static std::mt19937 rng{std::random_device{}()};
  return (uint32_t)rng();
}

int HardwareSerial::available() { return stdio_ ? (int)serialRx.size() : 0; }
int HardwareSerial::read() {
  if (!stdio_ || serialRx.empty()) return -1;
  int c = serialRx.front();
  serialRx.pop_front();
  return c;
}
size_t HardwareSerial::write(uint8_t c) { return write(&c, 1); }
size_t HardwareSerial::write(const uint8_t* b, size_t n) {
  if (!stdio_) return n;
  fwrite(b, 1, n, stdout);
  if (memchr(b, '\n', n)) fflush(stdout);
  return n;
}

int main(int argc, char** argv) {
  std::string hw = "oled";
  for (int i = 1; i + 1 < argc; i++)
    if (!strcmp(argv[i], "--hw")) hw = argv[i + 1];
  for (int& p : g_simPins) p = HIGH;
  simulateHardware(hw);
  fprintf(stderr, "DANTE simulator %s: hardware '%s'. Talking JSON lines on stdin/stdout.\n", FW_VERSION, hw.c_str());

  std::thread(readStdin).detach();
  setup();
  for (;;) {
    pumpInbox();
    applyButtons();
    applyTouches();
    loop();
    emitFrame();
    if (stdinClosed) {
      std::lock_guard<std::mutex> lock(inboxGate);
      if (inbox.empty()) return 0;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(1));
  }
}

#endif // DANTE_SIMULATOR

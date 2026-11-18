#include "Displays.h"
#include "Tft.h"
#include "config.h"
#include <Wire.h>
#include <Preferences.h>
#include <esp_system.h>

// Symbol name kept short: the host simulator dumps this buffer.
U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C panel1(U8G2_R0, U8X8_PIN_NONE, PIN_SCL, PIN_SDA);

namespace {
Displays::Settings cfg;
bool oledFound = false;
uint8_t oledAddr = OLED_I2C_ADDR;   // SSD1306 modules answer at 0x3C, or 0x3D with the address jumper moved
Displays::Kind current = Displays::KIND_OLED;   // cfg.kind with KIND_AUTO resolved
bool tftFailed = false;
Preferences prefs;

bool probe(uint8_t addr) {
  Wire.beginTransmission(addr);
  return Wire.endTransmission() == 0;
}

void load() {
  // Note: opening a namespace read-only fails on a freshly flashed chip (it doesn't exist
  // yet). The defaults in Settings (KIND_AUTO etc.) then stand, which is what we want.
  // Settings live in the "dante" namespace; earlier firmware used "hank", and before that "deskbuddy".
  if (!prefs.begin("dante", true) && !prefs.begin("hank", true) && !prefs.begin("deskbuddy", true)) return;
  uint8_t kind = prefs.getUChar("display", Displays::KIND_AUTO);
  // Anything else (including 1, the character LCD of firmware 3.1 and older) means automatic.
  cfg.kind = kind == Displays::KIND_OLED ? Displays::KIND_OLED
           : kind == Displays::KIND_TFT ? Displays::KIND_TFT : Displays::KIND_AUTO;
  cfg.flip = prefs.isKey("flip") ? prefs.getBool("flip", false) : prefs.getBool("flip1", false);   // 2.x: one flag per screen
  cfg.bright = prefs.getUChar("bright", 200);
  prefs.end();
}

void save() {
  if (!prefs.begin("dante", false)) return;
  prefs.putUChar("display", cfg.kind);
  prefs.putBool("flip", cfg.flip);
  prefs.putUChar("bright", cfg.bright);
  // Settings of removed features: two screens (2.x) and the character LCD (3.1 and older).
  for (const char* old : { "screens", "layout", "content", "swap", "flip1", "flip2", "lcdAddr", "lcdSize" })
    if (prefs.isKey(old)) prefs.remove(old);
  prefs.end();
}

Displays::Kind resolve() {
  if (cfg.kind != Displays::KIND_AUTO) return cfg.kind;
  return oledFound ? Displays::KIND_OLED : Displays::KIND_TFT;
}

// Push the settings to the hardware: wake the screen in use, put the other one to sleep.
void apply() {
  current = resolve();

  panel1.setDisplayRotation(cfg.flip ? U8G2_R2 : U8G2_R0);
  panel1.setContrast(cfg.bright);
  panel1.setPowerSave(current == Displays::KIND_OLED ? 0 : 1);

  if (current == Displays::KIND_TFT) {
    Tft::setFlip(cfg.flip);
    Tft::setBrightness(cfg.bright);
    if (!Tft::active()) tftFailed = !Tft::begin();
  } else {
    Tft::end();
  }
}
} // namespace

namespace Displays {

void begin() {
  load();
  Wire.begin(PIN_SDA, PIN_SCL, 400000);
  oledFound = probe(OLED_I2C_ADDR);
  if (!oledFound && probe(OLED_I2C_ADDR ^ 0x01)) { oledFound = true; oledAddr = OLED_I2C_ADDR ^ 0x01; }

  panel1.setI2CAddress(oledAddr << 1);
  panel1.setBusClock(400000);
  panel1.begin();               // harmless if nothing is attached
  apply();
}

const Settings& settings() { return cfg; }
bool oledDetected() { return oledFound; }
Kind active() { return current; }
bool isTft() { return current == KIND_TFT; }
U8G2& oled() { return panel1; }
const char* kindName() { return current == KIND_TFT ? "tft" : "oled"; }

bool applyConfig(JsonDocument& m) {
  Settings before = cfg;
  if (m["bright"].is<int>()) cfg.bright = (uint8_t)constrain(m["bright"].as<int>(), 0, 255);
  if (m["display"].is<const char*>()) {
    const char* d = m["display"];
    cfg.kind = !strcmp(d, "oled") ? KIND_OLED : !strcmp(d, "tft") ? KIND_TFT : KIND_AUTO;
  }
  if (m["flip"].is<bool>()) cfg.flip = m["flip"];
  else if (m["flip1"].is<bool>()) cfg.flip = m["flip1"];   // a PC app from before 3.0

  bool changed = before.kind != cfg.kind || before.flip != cfg.flip || before.bright != cfg.bright;
  if (changed) { apply(); save(); }
  return changed;
}

// Why the chip last started. A brownout means the supply voltage dipped (power problem).
const char* resetReason() {
  switch (esp_reset_reason()) {
    case ESP_RST_POWERON:  return "power on";
    case ESP_RST_EXT:      return "reset pin";
    case ESP_RST_SW:       return "software";
    case ESP_RST_PANIC:    return "crash";
    case ESP_RST_INT_WDT:
    case ESP_RST_TASK_WDT:
    case ESP_RST_WDT:      return "watchdog";
    case ESP_RST_BROWNOUT: return "BROWNOUT (power dipped)";
    case ESP_RST_DEEPSLEEP: return "deep sleep";
    default:               return "other";
  }
}

void describe(char* out, size_t n) {
  char oled[8] = "none";
  if (oledFound) snprintf(oled, sizeof(oled), "0x%02X", oledAddr);
  snprintf(out, n, "DANTE " FW_VERSION ": I2C (SDA=%d SCL=%d): OLED %s | TFT (SPI, SCK=%d MOSI=%d CS=%d DC=%d RST=%d BL=%d)%s"
                   " | screen: %s%s | last reset: %s",
           PIN_SDA, PIN_SCL, oled,
           TFT_PIN_SCK, TFT_PIN_MOSI, TFT_PIN_CS, TFT_PIN_DC, TFT_PIN_RST, TFT_PIN_BL, tftFailed ? ": NO MEMORY FOR THE FRAME BUFFER" : "",
           kindName(), cfg.kind == KIND_AUTO ? " (auto)" : " (set by PC app)", resetReason());
  if (current == KIND_TFT) {   // how smoothly it runs
    size_t used = strlen(out);
    if (used + 3 < n) { strcat(out, " | "); Tft::describe(out + used + 3, n - used - 3); }
  }
}

void scanNow(char* out, size_t n) {
  size_t used = snprintf(out, n, "I2C scan now: bus 1 (SDA=%d SCL=%d):", PIN_SDA, PIN_SCL);
  int found = 0;
  for (uint8_t a = 0x08; a <= 0x77 && used < n; a++) {
    if (!probe(a)) continue;
    used += snprintf(out + used, n - used, " 0x%02X%s", a, a == 0x3C || a == 0x3D ? " (OLED)" : "");
    found++;
  }
  if (!found && used < n) snprintf(out + used, n - used, " nothing");
}

} // namespace Displays

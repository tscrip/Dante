#include "Displays.h"
#include "Lcd.h"
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
uint8_t lcdFound = 0;
Displays::Kind current = Displays::KIND_OLED;   // cfg.kind with KIND_AUTO resolved
bool tftFailed = false;
Preferences prefs;

void load() {
  // Note: opening a namespace read-only fails on a freshly flashed chip (it doesn't exist
  // yet). The defaults in Settings (KIND_AUTO etc.) then stand, which is what we want.
  // Settings live in the "dante" namespace; earlier firmware used "hank", and before that "deskbuddy".
  if (!prefs.begin("dante", true) && !prefs.begin("hank", true) && !prefs.begin("deskbuddy", true)) return;
  uint8_t kind = prefs.getUChar("display", Displays::KIND_AUTO);
  cfg.kind = kind == Displays::KIND_LCD ? Displays::KIND_LCD
           : kind == Displays::KIND_OLED ? Displays::KIND_OLED
           : kind == Displays::KIND_TFT ? Displays::KIND_TFT : Displays::KIND_AUTO;
  cfg.lcdAddr = prefs.getUChar("lcdAddr", LCD_DEFAULT_ADDR);
  cfg.lcdSize = prefs.getUChar("lcdSize", Displays::LCD_1602) == Displays::LCD_2004 ? Displays::LCD_2004 : Displays::LCD_1602;
  cfg.flip = prefs.isKey("flip") ? prefs.getBool("flip", false) : prefs.getBool("flip1", false);   // 2.x: one flag per screen
  cfg.bright = prefs.getUChar("bright", 200);
  prefs.end();
}

void save() {
  if (!prefs.begin("dante", false)) return;
  prefs.putUChar("display", cfg.kind);
  prefs.putUChar("lcdAddr", cfg.lcdAddr);
  prefs.putUChar("lcdSize", cfg.lcdSize);
  prefs.putBool("flip", cfg.flip);
  prefs.putUChar("bright", cfg.bright);
  // Two-screen settings from 2.x firmware.
  for (const char* old : { "screens", "layout", "content", "swap", "flip1", "flip2" })
    if (prefs.isKey(old)) prefs.remove(old);
  prefs.end();
}

Displays::Kind resolve() {
  if (cfg.kind != Displays::KIND_AUTO) return cfg.kind;
  if (oledFound) return Displays::KIND_OLED;
  if (lcdFound) return Displays::KIND_LCD;
  return Displays::KIND_TFT;
}

// Push the settings to the hardware: wake the screen in use, put the others to sleep.
void apply() {
  current = resolve();

  panel1.setDisplayRotation(cfg.flip ? U8G2_R2 : U8G2_R0);
  panel1.setContrast(cfg.bright);
  panel1.setPowerSave(current == Displays::KIND_OLED ? 0 : 1);

  if (current == Displays::KIND_LCD) {
    uint8_t cols = cfg.lcdSize == Displays::LCD_2004 ? 20 : 16;
    uint8_t rows = cfg.lcdSize == Displays::LCD_2004 ? 4 : 2;
    // In automatic mode, talk to the backpack we actually found.
    uint8_t addr = (cfg.kind == Displays::KIND_AUTO && lcdFound) ? lcdFound : cfg.lcdAddr;
    static uint8_t startedAt = 0;
    if (!Lcd::active() || startedAt != addr || Lcd::cols() != cols || Lcd::rows() != rows) {
      Lcd::begin(addr, cols, rows);
      startedAt = addr;
    }
    Lcd::backlight(cfg.bright > 0);
  } else if (Lcd::active()) {
    Lcd::end();
  }

  if (current == Displays::KIND_TFT) {
    Tft::setFlip(cfg.flip);
    Tft::setBrightness(cfg.bright);
    if (!Tft::active()) tftFailed = !Tft::begin();
  } else {
    Tft::end();
  }
}

// PCF8574 backpacks live at 0x20-0x27, PCF8574A at 0x38-0x3F (A0-A2 jumpers). Try the
// preferred one first, then the usual 0x27 / 0x3F, then the rest.
uint8_t findLcd(uint8_t preferred) {
  uint8_t candidates[18] = { preferred, 0x27, 0x3F };
  int n = 3;
  for (uint8_t a = 0x20; a <= 0x26; a++) candidates[n++] = a;
  for (uint8_t a = 0x38; a <= 0x3E; a++) candidates[n++] = a;
  for (int i = 0; i < n; i++)
    if (candidates[i] != 0x3C && candidates[i] != 0x3D && Lcd::probe(candidates[i])) return candidates[i];
  return 0;
}
} // namespace

namespace Displays {

void begin() {
  load();
  Wire.begin(PIN_SDA, PIN_SCL, 400000);
  oledFound = Lcd::probe(OLED_I2C_ADDR);
  if (!oledFound && Lcd::probe(OLED_I2C_ADDR ^ 0x01)) { oledFound = true; oledAddr = OLED_I2C_ADDR ^ 0x01; }
  lcdFound = findLcd(cfg.lcdAddr);

  panel1.setI2CAddress(oledAddr << 1);
  panel1.setBusClock(400000);
  panel1.begin();               // harmless if nothing is attached
  apply();
}

const Settings& settings() { return cfg; }
bool oledDetected() { return oledFound; }
uint8_t lcdDetected() { return lcdFound; }
Kind active() { return current; }
bool isLcd() { return current == KIND_LCD; }
bool isTft() { return current == KIND_TFT; }
U8G2& oled() { return panel1; }

const char* kindName() {
  switch (current) {
    case KIND_LCD: return cfg.lcdSize == LCD_2004 ? "lcd2004" : "lcd1602";
    case KIND_TFT: return "tft";
    default:       return "oled";
  }
}

bool applyConfig(JsonDocument& m) {
  Settings before = cfg;
  if (m["bright"].is<int>()) cfg.bright = (uint8_t)constrain(m["bright"].as<int>(), 0, 255);
  if (m["display"].is<const char*>()) {
    const char* d = m["display"];
    bool is2004 = !strcmp(d, "lcd2004");
    cfg.kind = (is2004 || !strcmp(d, "lcd1602")) ? KIND_LCD
             : !strcmp(d, "oled") ? KIND_OLED
             : !strcmp(d, "tft") ? KIND_TFT : KIND_AUTO;
    if (cfg.kind == KIND_LCD) cfg.lcdSize = is2004 ? LCD_2004 : LCD_1602;
  }
  if (m["lcdSize"].is<const char*>()) {   // lets "auto" know which LCD to expect
    cfg.lcdSize = strcmp(m["lcdSize"], "2004") == 0 ? LCD_2004 : LCD_1602;
  }
  if (m["lcdAddr"].is<int>()) cfg.lcdAddr = (uint8_t)constrain(m["lcdAddr"].as<int>(), 0x08, 0x77);
  if (m["flip"].is<bool>()) cfg.flip = m["flip"];
  else if (m["flip1"].is<bool>()) cfg.flip = m["flip1"];   // a PC app from before 3.0

  bool changed = before.kind != cfg.kind || before.lcdAddr != cfg.lcdAddr || before.lcdSize != cfg.lcdSize ||
                 before.flip != cfg.flip || before.bright != cfg.bright;
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
  char lcd[8] = "none", oled[8] = "none";
  if (lcdFound) snprintf(lcd, sizeof(lcd), "0x%02X", lcdFound);
  if (oledFound) snprintf(oled, sizeof(oled), "0x%02X", oledAddr);
  snprintf(out, n, "DANTE " FW_VERSION ": I2C (SDA=%d SCL=%d): OLED %s, LCD %s | TFT (SPI, SCK=%d MOSI=%d CS=%d DC=%d RST=%d BL=%d)%s"
                   " | screen: %s%s | last reset: %s",
           PIN_SDA, PIN_SCL, oled, lcd,
           TFT_PIN_SCK, TFT_PIN_MOSI, TFT_PIN_CS, TFT_PIN_DC, TFT_PIN_RST, TFT_PIN_BL, tftFailed ? ": NO MEMORY FOR THE FRAME BUFFER" : "",
           kindName(), cfg.kind == KIND_AUTO ? " (auto)" : " (set by PC app)", resetReason());
}

void scanNow(char* out, size_t n) {
  size_t used = snprintf(out, n, "I2C scan now: bus 1 (SDA=%d SCL=%d):", PIN_SDA, PIN_SCL);
  int found = 0;
  for (uint8_t a = 0x08; a <= 0x77 && used < n; a++) {
    Wire.beginTransmission(a);
    if (Wire.endTransmission() != 0) continue;
    const char* what = a == 0x3C || a == 0x3D ? " (OLED)" : (a >= 0x20 && a <= 0x27) || (a >= 0x38 && a <= 0x3F) ? " (LCD)" : "";
    used += snprintf(out + used, n - used, " 0x%02X%s", a, what);
    found++;
  }
  if (!found && used < n) snprintf(out + used, n - used, " nothing");
}

} // namespace Displays

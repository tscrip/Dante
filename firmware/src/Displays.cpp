#include "Displays.h"
#include "Lcd.h"
#include "config.h"
#include <Wire.h>
#include <Preferences.h>

// ---- second panel driver ------------------------------------------------------------
// U8g2's own "2ND_HW_I2C" driver is compiled out on ESP32 (it keys off WIRE_INTERFACES_COUNT,
// which the ESP32 core doesn't define), so we drive the bus ourselves.
#if DISP2_SAME_BUS
#define BUS2 Wire
#define BUS2_SDA PIN_SDA
#define BUS2_SCL PIN_SCL
#else
#define BUS2 Wire1
#define BUS2_SDA PIN_SDA2
#define BUS2_SCL PIN_SCL2
#endif

extern "C" uint8_t deskbuddy_bus2_byte(u8x8_t* u8x8, uint8_t msg, uint8_t argInt, void* argPtr) {
  switch (msg) {
    case U8X8_MSG_BYTE_SEND:
      BUS2.write((uint8_t*)argPtr, (size_t)argInt);
      break;
    case U8X8_MSG_BYTE_INIT:
      BUS2.begin(BUS2_SDA, BUS2_SCL, 400000);
      break;
    case U8X8_MSG_BYTE_SET_DC:
      break;
    case U8X8_MSG_BYTE_START_TRANSFER:
      BUS2.beginTransmission(u8x8_GetI2CAddress(u8x8) >> 1);
      break;
    case U8X8_MSG_BYTE_END_TRANSFER:
      BUS2.endTransmission();
      break;
    default:
      return 0;
  }
  return 1;
}

// Same as u8g2_Setup_ssd1306_i2c_128x32_univision_f, but with a frame buffer of its own: the
// stock setup hands every panel of the same model one shared static buffer, so two panels
// would always show the same picture.
class SecondPanel : public U8G2 {
  uint8_t frame[128 * 32 / 8];
 public:
  SecondPanel() : U8G2() {
    u8g2_SetupDisplay(&u8g2, u8x8_d_ssd1306_128x32_univision, u8x8_cad_ssd13xx_fast_i2c,
                      deskbuddy_bus2_byte, u8x8_gpio_and_delay_arduino);
    u8g2_SetupBuffer(&u8g2, frame, 4, u8g2_ll_hvline_vertical_top_lsb, U8G2_R0);
    u8x8_SetPin_HW_I2C(getU8x8(), U8X8_PIN_NONE);
  }
};

// Symbol names kept short: the host simulator dumps these buffers.
U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C panel1(U8G2_R0, U8X8_PIN_NONE, PIN_SCL, PIN_SDA);
SecondPanel panel2;

namespace {
Displays::Settings cfg;
bool detected2 = false;
bool detected1 = false;
uint8_t lcdFound = 0;
Displays::Kind active = Displays::KIND_OLED;   // cfg.kind with KIND_AUTO resolved
bool panel2Awake = false;
Preferences prefs;

U8G2* physical(int i) { return i == 0 ? (U8G2*)&panel1 : (U8G2*)&panel2; }

void load() {
  // Note: opening a namespace read-only fails on a freshly flashed chip (it doesn't exist
  // yet). The defaults in Settings (KIND_AUTO etc.) then stand, which is what we want.
  if (!prefs.begin("deskbuddy", true)) return;
  uint8_t kind = prefs.getUChar("display", Displays::KIND_AUTO);
  cfg.kind = kind == Displays::KIND_LCD ? Displays::KIND_LCD
           : kind == Displays::KIND_OLED ? Displays::KIND_OLED : Displays::KIND_AUTO;
  cfg.lcdAddr = prefs.getUChar("lcdAddr", LCD_DEFAULT_ADDR);
  cfg.lcdSize = prefs.getUChar("lcdSize", Displays::LCD_1602) == Displays::LCD_2004 ? Displays::LCD_2004 : Displays::LCD_1602;
  cfg.screens = prefs.getUChar("screens", 1);
  cfg.layout = (Displays::Layout)prefs.getUChar("layout", Displays::LAYOUT_WATCH);
  cfg.contentSide = prefs.getUChar("content", 1);
  cfg.swap = prefs.getBool("swap", false);
  cfg.flip[0] = prefs.getBool("flip1", false);
  cfg.flip[1] = prefs.getBool("flip2", false);
  cfg.bright = prefs.getUChar("bright", 200);
  prefs.end();
}

void save() {
  if (!prefs.begin("deskbuddy", false)) return;
  prefs.putUChar("display", cfg.kind);
  prefs.putUChar("lcdAddr", cfg.lcdAddr);
  prefs.putUChar("lcdSize", cfg.lcdSize);
  prefs.putUChar("screens", cfg.screens);
  prefs.putUChar("layout", cfg.layout);
  prefs.putUChar("content", cfg.contentSide);
  prefs.putBool("swap", cfg.swap);
  prefs.putBool("flip1", cfg.flip[0]);
  prefs.putBool("flip2", cfg.flip[1]);
  prefs.putUChar("bright", cfg.bright);
  prefs.end();
}

Displays::Kind resolve() {
  if (cfg.kind != Displays::KIND_AUTO) return cfg.kind;
  return (!detected1 && lcdFound) ? Displays::KIND_LCD : Displays::KIND_OLED;
}

// Push the settings to the hardware.
void apply() {
  active = resolve();
  bool lcd = active == Displays::KIND_LCD;
  uint8_t cols = cfg.lcdSize == Displays::LCD_2004 ? 20 : 16;
  uint8_t rows = cfg.lcdSize == Displays::LCD_2004 ? 4 : 2;
  // In automatic mode, talk to the backpack we actually found.
  uint8_t lcdAddr = (cfg.kind == Displays::KIND_AUTO && lcdFound) ? lcdFound : cfg.lcdAddr;
  for (int i = 0; i < 2; i++) {
    physical(i)->setDisplayRotation(cfg.flip[i] ? U8G2_R2 : U8G2_R0);
    physical(i)->setContrast(cfg.bright);
  }
  panel1.setPowerSave(lcd ? 1 : 0);
  bool want2 = !lcd && cfg.screens == 2;
  if (want2 != panel2Awake) {
    if (!want2) { panel2.clearBuffer(); panel2.sendBuffer(); }
    panel2.setPowerSave(want2 ? 0 : 1);
    panel2Awake = want2;
  }
  if (lcd) {
    static uint8_t startedAt = 0;
    if (!Lcd::active() || startedAt != lcdAddr || Lcd::cols() != cols || Lcd::rows() != rows) {
      Lcd::begin(lcdAddr, cols, rows);
      startedAt = lcdAddr;
    }
    Lcd::backlight(cfg.bright > 0);
  } else {
    Lcd::end();
  }
}
} // namespace

namespace Displays {

void begin() {
  load();
  Wire.begin(PIN_SDA, PIN_SCL, 400000);
  detected1 = Lcd::probe(DISP1_I2C_ADDR);
  // PCF8574 backpacks live at 0x20-0x27, PCF8574A at 0x38-0x3F (A0-A2 jumpers). Try the
  // configured one first, then the usual 0x27 / 0x3F, then the rest.
  uint8_t candidates[18] = { cfg.lcdAddr, 0x27, 0x3F };
  int n = 3;
  for (uint8_t a = 0x20; a <= 0x26; a++) candidates[n++] = a;
  for (uint8_t a = 0x38; a <= 0x3E; a++) candidates[n++] = a;
  for (int i = 0; i < n && !lcdFound; i++)
    if (candidates[i] != DISP1_I2C_ADDR && Lcd::probe(candidates[i])) lcdFound = candidates[i];

  panel1.setI2CAddress(DISP1_I2C_ADDR << 1);
  panel1.setBusClock(400000);
  panel1.begin();

  panel2.setI2CAddress(DISP2_I2C_ADDR << 1);
  panel2.begin();               // inits the bus; harmless if nothing is attached
  BUS2.beginTransmission(DISP2_I2C_ADDR);
  detected2 = BUS2.endTransmission() == 0;
  panel2Awake = true;           // begin() wakes it; apply() puts it back to sleep if unused
  apply();
}

const Settings& settings() { return cfg; }
bool secondDetected() { return detected2; }
bool oledDetected() { return detected1; }
uint8_t lcdDetected() { return lcdFound; }
bool isLcd() { return active == KIND_LCD; }
const char* kindName() { return !isLcd() ? "oled" : cfg.lcdSize == LCD_2004 ? "lcd2004" : "lcd1602"; }
int count() { return !isLcd() && cfg.screens == 2 ? 2 : 1; }

U8G2& side(int s) {
  if (count() != 2) return panel1;
  int phys = (s ? 1 : 0) ^ (cfg.swap ? 1 : 0);
  return *physical(phys);
}

bool applyConfig(JsonDocument& m) {
  Settings before = cfg;
  if (m["bright"].is<int>()) cfg.bright = (uint8_t)constrain(m["bright"].as<int>(), 0, 255);
  if (m["display"].is<const char*>()) {
    const char* d = m["display"];
    bool is2004 = !strcmp(d, "lcd2004");
    cfg.kind = (is2004 || !strcmp(d, "lcd1602")) ? KIND_LCD : !strcmp(d, "oled") ? KIND_OLED : KIND_AUTO;
    if (cfg.kind == KIND_LCD) cfg.lcdSize = is2004 ? LCD_2004 : LCD_1602;
  }
  if (m["lcdSize"].is<const char*>()) {   // lets "auto" know which LCD to expect
    cfg.lcdSize = strcmp(m["lcdSize"], "2004") == 0 ? LCD_2004 : LCD_1602;
  }
  if (m["lcdAddr"].is<int>()) cfg.lcdAddr = (uint8_t)constrain(m["lcdAddr"].as<int>(), 0x08, 0x77);
  if (m["screens"].is<int>()) cfg.screens = m["screens"].as<int>() == 2 ? 2 : 1;
  if (m["layout"].is<const char*>()) cfg.layout = strcmp(m["layout"], "mirror") == 0 ? LAYOUT_MIRROR : LAYOUT_WATCH;
  if (m["content"].is<const char*>()) cfg.contentSide = strcmp(m["content"], "left") == 0 ? 0 : 1;
  if (m["swap"].is<bool>()) cfg.swap = m["swap"];
  if (m["flip1"].is<bool>()) cfg.flip[0] = m["flip1"];
  if (m["flip2"].is<bool>()) cfg.flip[1] = m["flip2"];

  bool changed = before.kind != cfg.kind || before.lcdAddr != cfg.lcdAddr || before.lcdSize != cfg.lcdSize || before.screens != cfg.screens || before.layout != cfg.layout || before.contentSide != cfg.contentSide ||
                 before.swap != cfg.swap || before.flip[0] != cfg.flip[0] || before.flip[1] != cfg.flip[1] ||
                 before.bright != cfg.bright;
  if (changed) { apply(); save(); }
  return changed;
}

void describe(char* out, size_t n) {
  char lcd[8] = "none";
  if (lcdFound) snprintf(lcd, sizeof(lcd), "0x%02X", lcdFound);
  snprintf(out, n, "DeskBuddy " FW_VERSION ": I2C SDA=%d SCL=%d | OLED@0x%02X %s | LCD %s | screen: %s%s",
           PIN_SDA, PIN_SCL, DISP1_I2C_ADDR, detected1 ? "found" : "not found", lcd,
           kindName(), cfg.kind == KIND_AUTO ? " (auto)" : " (set by PC app)");
}

} // namespace Displays

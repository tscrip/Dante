#include "Displays.h"
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
bool panel2Awake = false;
Preferences prefs;

U8G2* physical(int i) { return i == 0 ? (U8G2*)&panel1 : (U8G2*)&panel2; }

void load() {
  if (!prefs.begin("deskbuddy", true)) return;
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
  prefs.putUChar("screens", cfg.screens);
  prefs.putUChar("layout", cfg.layout);
  prefs.putUChar("content", cfg.contentSide);
  prefs.putBool("swap", cfg.swap);
  prefs.putBool("flip1", cfg.flip[0]);
  prefs.putBool("flip2", cfg.flip[1]);
  prefs.putUChar("bright", cfg.bright);
  prefs.end();
}

// Push the settings to the hardware.
void apply() {
  for (int i = 0; i < 2; i++) {
    physical(i)->setDisplayRotation(cfg.flip[i] ? U8G2_R2 : U8G2_R0);
    physical(i)->setContrast(cfg.bright);
  }
  bool want2 = cfg.screens == 2;
  if (want2 != panel2Awake) {
    if (!want2) { panel2.clearBuffer(); panel2.sendBuffer(); }
    panel2.setPowerSave(want2 ? 0 : 1);
    panel2Awake = want2;
  }
}
} // namespace

namespace Displays {

void begin() {
  load();
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
int count() { return cfg.screens == 2 ? 2 : 1; }

U8G2& side(int s) {
  if (cfg.screens != 2) return panel1;
  int phys = (s ? 1 : 0) ^ (cfg.swap ? 1 : 0);
  return *physical(phys);
}

bool applyConfig(JsonDocument& m) {
  Settings before = cfg;
  if (m["bright"].is<int>()) cfg.bright = (uint8_t)constrain(m["bright"].as<int>(), 0, 255);
  if (m["screens"].is<int>()) cfg.screens = m["screens"].as<int>() == 2 ? 2 : 1;
  if (m["layout"].is<const char*>()) cfg.layout = strcmp(m["layout"], "mirror") == 0 ? LAYOUT_MIRROR : LAYOUT_WATCH;
  if (m["content"].is<const char*>()) cfg.contentSide = strcmp(m["content"], "left") == 0 ? 0 : 1;
  if (m["swap"].is<bool>()) cfg.swap = m["swap"];
  if (m["flip1"].is<bool>()) cfg.flip[0] = m["flip1"];
  if (m["flip2"].is<bool>()) cfg.flip[1] = m["flip2"];

  bool changed = before.screens != cfg.screens || before.layout != cfg.layout || before.contentSide != cfg.contentSide ||
                 before.swap != cfg.swap || before.flip[0] != cfg.flip[0] || before.flip[1] != cfg.flip[1] ||
                 before.bright != cfg.bright;
  if (changed) { apply(); save(); }
  return changed;
}

} // namespace Displays

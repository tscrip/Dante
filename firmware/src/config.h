#pragma once
#include <Arduino.h>

// ---- Identity ---------------------------------------------------------------
#define FW_VERSION      "1.2.0"
#define DEVICE_NAME     "DeskBuddy"

// ---- Display (SSD1306 128x32, I2C) -----------------------------------------
#define PIN_SDA         21
#define PIN_SCL         22
#define SCREEN_W        128
#define SCREEN_H        32

// ---- Optional second display: one screen per eye ---------------------------
// Most 0.91" modules are hard-wired to I2C address 0x3C, so by default the second
// screen sits on the ESP32's second I2C bus (Wire1) with its own pins. If your second
// module has an address jumper set to 0x3D you can share the first bus instead:
// set DISP2_SAME_BUS to 1 and DISP2_I2C_ADDR to 0x3D.
// Whether one or two screens are used is chosen in the PC app (and remembered).
#define DISP2_SAME_BUS  0
#define PIN_SDA2        18
#define PIN_SCL2        19
#define DISP2_I2C_ADDR  0x3C      // 7-bit address
#define DISP1_I2C_ADDR  0x3C

// ---- Alternative display: 1602 character LCD with a PCF8574 I2C backpack ---
// Shares the first I2C bus (GPIO 21/22). Choose OLED or LCD in the PC app; with nothing
// chosen yet the buddy picks whichever it finds at boot. Backpacks are usually at 0x27
// (PCF8574) or 0x3F (PCF8574A); the address is also set from the PC app.
#define LCD_COLS        16
#define LCD_ROWS        2
#define LCD_DEFAULT_ADDR 0x27
#define LCD_FRAME_MS    50        // the LCD is slow to update (and to change visibly)

// ---- Buttons (wired to GND, internal pull-ups) -----------------------------
#define PIN_BTN_LEFT    25
#define PIN_BTN_RIGHT   26
#define PIN_BTN_SELECT  27   // the "top" button

#define BTN_DEBOUNCE_MS 25
#define BTN_LONG_MS     800

// ---- Link ---------------------------------------------------------------------
#define USB_BAUD        115200
#define RX_LINE_MAX        1024
#define PC_TIMEOUT_MS   30000UL   // no message for this long => buddy feels alone

// ---- Timing -------------------------------------------------------------------
#define FRAME_MS        33        // ~30 FPS
#define SLIDE_MS        180       // screen slide transition
#define TOAST_MS        4500      // speech-bubble duration

// ---- Limits (must match docs/protocol.md) ------------------------------------
#define MAX_APPS        12
#define ID_LEN          16        // 15 chars + NUL
#define NAME_LEN        20
#define UNIT_LEN        6
#define TXT_LEN         48
#define QUIP_LEN        64
#define HIST_LEN        32
#define MAX_ALERTS      4

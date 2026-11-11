#pragma once
#include <Arduino.h>

// ---- Identity ---------------------------------------------------------------
#define FW_VERSION      "2.1.0"
#define DEVICE_NAME     "DeskBuddy"

// ---- Board: ESP32-S3 (e.g. ESP32-S3-DevKitC-1) --------------------------------
// Pins avoid the S3's strapping pins (0, 3, 45, 46), native USB (19, 20), the UART0
// pins (43, 44) and 26-37, which the flash / octal PSRAM use on most S3 modules.

// ---- Display (SSD1306 128x32, I2C) -----------------------------------------
#define PIN_SDA         8     // ESP32-S3 Arduino default I2C pins
#define PIN_SCL         9
#define SCREEN_W        128
#define SCREEN_H        32

// ---- Optional second display: one screen per eye ---------------------------
// Most 0.91" modules are hard-wired to I2C address 0x3C, so by default the second
// screen sits on the ESP32's second I2C bus (Wire1) with its own pins. If your second
// module has an address jumper set to 0x3D you can share the first bus instead:
// set DISP2_SAME_BUS to 1 and DISP2_I2C_ADDR to 0x3D.
// Whether one or two screens are used is chosen in the PC app (and remembered).
#define DISP2_SAME_BUS  0
#define PIN_SDA2        17
#define PIN_SCL2        18
#define DISP2_I2C_ADDR  0x3C      // 7-bit address
#define DISP1_I2C_ADDR  0x3C

// ---- Alternative display: 1602 / 2004 character LCD with a PCF8574 I2C backpack
// Shares the first I2C bus (GPIO 8/9). Choose OLED or LCD in the PC app; with nothing
// chosen yet the buddy picks whichever it finds at boot. Backpacks are usually at 0x27
// (PCF8574) or 0x3F (PCF8574A); the address is also set from the PC app.
// Sizes supported: 16x2 ("1602") and 20x4 ("2004"); the size is set from the PC app.
#define LCD_MAX_COLS    20
#define LCD_MAX_ROWS    4
#define LCD_DEFAULT_ADDR 0x27
#define LCD_FRAME_MS    50        // the LCD is slow to update (and to change visibly)

// ---- Buttons (wired to GND, internal pull-ups) -----------------------------
#define PIN_BTN_LEFT    4
#define PIN_BTN_RIGHT   5
#define PIN_BTN_SELECT  6    // the "top" button

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

#pragma once
#include <Arduino.h>

// ---- Identity ---------------------------------------------------------------
#define FW_VERSION      "3.2.0"
#define DEVICE_NAME     "DANTE"

// ---- Board: ESP32-S3 (e.g. ESP32-S3-DevKitC-1) --------------------------------
// Pins avoid the S3's strapping pins (0, 3, 45, 46), native USB (19, 20), the UART0
// pins (43, 44) and 26-37, which the flash / octal PSRAM use on most S3 modules.

// ---- Screen ------------------------------------------------------------------
// DANTE drives exactly one screen: a 0.91" OLED, a 1602/2004 character LCD (both I2C), or the
// 3.5" colour TFT (SPI). Which one is chosen in the PC app and remembered; until then DANTE uses
// whichever I2C screen answers, and the TFT when none does (an SPI screen can't be detected).

// ---- OLED (SSD1306 128x32, I2C) ----------------------------------------------
#define PIN_SDA         8     // ESP32-S3 Arduino default I2C pins
#define PIN_SCL         9
#define SCREEN_W        128
#define SCREEN_H        32

#define OLED_I2C_ADDR   0x3C      // 7-bit; 0x3D (address jumper moved) is found too

// ---- Alternative display: 1602 / 2004 character LCD with a PCF8574 I2C backpack
// Shares the first I2C bus (GPIO 8/9). Choose OLED or LCD in the PC app; with nothing
// chosen yet DANTE picks whichever it finds at boot. Backpacks are usually at 0x27
// (PCF8574) or 0x3F (PCF8574A); the address is also set from the PC app.
// Sizes supported: 16x2 ("1602") and 20x4 ("2004"); the size is set from the PC app.
#define LCD_MAX_COLS    20
#define LCD_MAX_ROWS    4
#define LCD_DEFAULT_ADDR 0x27
#define LCD_FRAME_MS    50        // the LCD is slow to update (and to change visibly)

// ---- 3.5" TFT, 480x320, ST7796 driver, SPI ---------------------------------------
// The 8-pin modules label the SPI pins SCL (= SCK) and SDA (= MOSI). There is no MISO: DANTE
// never reads from the panel. 10-13 are the S3's native FSPI pins (fastest routing).
#define TFT_PIN_SCK     12
#define TFT_PIN_MOSI    11
#define TFT_PIN_CS      10
#define TFT_PIN_DC      13
#define TFT_PIN_RST     14
#define TFT_PIN_BL      21        // backlight, PWM-dimmed (brightness from the PC app)
#define TFT_SPI_HZ      40000000  // 40 MHz is safe with short wires; many panels take 80 MHz
// Panels differ: if black shows as white, flip TFT_INVERT; if red and blue are swapped, flip TFT_BGR.
#define TFT_INVERT      false
#define TFT_BGR         false
#define TFT_W           480
#define TFT_H           320
#define TFT_STRIP_H     64        // without PSRAM: drawn in strips of this many rows (61 KB of RAM)
#define TFT_FRAME_MS    33        // ~30 fps; with PSRAM only the changed parts of the screen are sent

// ---- Buttons (wired to GND, internal pull-ups) -----------------------------
#define PIN_BTN_LEFT    4
#define PIN_BTN_RIGHT   5
#define PIN_BTN_SELECT  6    // the "top" button
#define PIN_BTN_MAGIC   7    // also on top: does whatever the PC app says (mute, play/pause, ...)

#define BTN_DEBOUNCE_MS 25
#define BTN_LONG_MS     800

// ---- Link ---------------------------------------------------------------------
#define USB_BAUD        115200
#define RX_LINE_MAX        1024
#define PC_TIMEOUT_MS   30000UL   // no message for this long => DANTE feels alone

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

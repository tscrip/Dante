#pragma once
#include <Arduino.h>

// ---- Identity ---------------------------------------------------------------
#define FW_VERSION      "3.11.0"
#define DEVICE_NAME     "DANTE"

// ---- Board ------------------------------------------------------------------------
// Two boards are supported; platformio.ini picks one per build environment:
//   * esp32-s3          an ESP32-S3 dev board, with a screen and 4 buttons wired by hand (below).
//   * esp32-3248s035c   the "yellow board" (ESP32-3248S035C): a plain ESP32 with a 3.5" ST7796 TFT
//                       and a capacitive GT911 touch panel built in. Taps on the screen stand in for
//                       the buttons (Touch.h). Built with -DDANTE_BOARD_CYD35C.
// The simulator compiles the S3 settings; it gets the touch layer too, for the yellow board's sake.

#if defined(DANTE_BOARD_CYD35C)
// ==== Yellow board: ESP32-3248S035C (ESP32-WROOM-32, 4 MB flash, no PSRAM) ========================
#define BOARD_ID        "esp32-3248s035c"
#define HAS_OLED        0         // the screen is built in: always the TFT
#define HAS_BUTTONS     0         // no buttons of its own: the touch panel stands in (+ optional MAGIC on P3)
#define HAS_TOUCH       1

// The OLED symbols below only keep the shared code compiling; nothing uses them on this board.
#define PIN_SDA         33
#define PIN_SCL         32
#define SCREEN_W        128
#define SCREEN_H        32
#define OLED_I2C_ADDR   0x3C

// 3.5" TFT, 480x320, ST7796, wired on the board to the classic ESP32's HSPI pins.
#define TFT_SPI_HOST    SPI2_HOST // = HSPI on the classic ESP32
#define TFT_PIN_SCK     14
#define TFT_PIN_MOSI    13
#define TFT_PIN_MISO    12
#define TFT_PIN_CS      15
#define TFT_PIN_DC      2
#define TFT_PIN_RST     -1        // tied to the ESP32's own reset
#define TFT_PIN_BL      27        // backlight, PWM-dimmed (brightness from the PC app)
#define TFT_SPI_HZ      80000000  // the panel is wired to the ESP32's fast (IOMUX) HSPI pins on the board; if the
                                  // picture shows noise or wrong pixels, go back to 40000000
// Panels differ: if black shows as white, flip TFT_INVERT; if red and blue are swapped, flip TFT_BGR.
#define TFT_INVERT      false
#define TFT_BGR         false
#define TFT_W           480
#define TFT_H           320
#define TFT_STRIP_H     40        // no PSRAM: drawn in strips of 40 rows (38 KB), leaving room for Bluetooth
#define TFT_FRAME_MS    33

// GT911 capacitive touch on its own I2C bus.
#define TOUCH_PIN_SDA   33
#define TOUCH_PIN_SCL   32
#define TOUCH_PIN_INT   21
#define TOUCH_PIN_RST   25
#define TOUCH_I2C_PORT  1
// If taps land in the wrong place (left and right swapped, or up and down), change this 0-7: it turns
// and mirrors the touch panel's coordinates relative to the screen. `?` in the serial monitor shows
// where the last touch landed.
#define TOUCH_OFFSET_ROTATION 0

// No buttons on the board itself (-1 = not there). One can be added on the P3 connector, see below.
#define PIN_BTN_LEFT    -1
#define PIN_BTN_RIGHT   -1
#define PIN_BTN_SELECT  -1
#define PIN_BTN_MAGIC   -1

// ---- An extra MAGIC button on P3 (GND, IO35, IO22, IO21) or CN1 (GND, IO22, IO21, 3.3V) -----------
// Between IO22 and GND (a push button's NO and C terminals). IO22 has a built-in pull-up, so nothing
// else is needed, and a board without the button just reads "released". (Not IO35: it has no pull-up
// and floats without an extra resistor. Not IO21: it is the touch chip's "touched" line here.)
#define PIN_EXTRA_MAGIC 22

#else
// ==== ESP32-S3 (e.g. ESP32-S3-DevKitC-1) =========================================================
// Pins avoid the S3's strapping pins (0, 3, 45, 46), native USB (19, 20), the UART0
// pins (43, 44) and 26-37, which the flash / octal PSRAM use on most S3 modules.
#define BOARD_ID        "esp32-s3"
#define HAS_OLED        1
#define HAS_BUTTONS     1
#ifdef DANTE_SIMULATOR
#define HAS_TOUCH       1         // the simulator's "yellow board" mode: mouse clicks on the screen are taps
#else
#define HAS_TOUCH       0
#endif

// ---- Screen ------------------------------------------------------------------
// DANTE drives exactly one screen: the 0.91" OLED (I2C) or the 3.5" colour TFT (SPI). Which one is
// chosen in the PC app and remembered; until then DANTE uses the OLED if it answers, else the TFT
// (an SPI screen can't be detected).

// ---- OLED (SSD1306 128x32, I2C) ----------------------------------------------
#define PIN_SDA         8     // ESP32-S3 Arduino default I2C pins
#define PIN_SCL         9
#define SCREEN_W        128
#define SCREEN_H        32

#define OLED_I2C_ADDR   0x3C      // 7-bit; 0x3D (address jumper moved) is found too

// ---- 3.5" TFT, 480x320, ST7796 driver, SPI ---------------------------------------
// The 8-pin modules label the SPI pins SCL (= SCK) and SDA (= MOSI). There is no MISO: DANTE
// never reads from the panel. 10-13 are the S3's native FSPI pins (fastest routing).
#define TFT_SPI_HOST    SPI2_HOST // = FSPI on the S3
#define TFT_PIN_SCK     12
#define TFT_PIN_MOSI    11
#define TFT_PIN_MISO    -1
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

#define PIN_EXTRA_MAGIC -1   // the S3 has MAGIC on its own pin already
#endif

#define BTN_DEBOUNCE_MS 25
#define BTN_LONG_MS     800

// ---- Touch (yellow board, and the simulator) ---------------------------------------
// A tap anywhere = SELECT (hold = long SELECT), swipe left / right = next / previous screen, swipe
// down = home to the face, a two-finger tap = MAGIC. See Touch.h.
#define TOUCH_SWIPE_PX  60        // a finger that moves this far (screen pixels) is a swipe, not a tap

// ---- Link ---------------------------------------------------------------------
#define USB_BAUD        115200
#define USB_RX_BUFFER   4096      // bytes the serial port holds while DANTE is busy drawing a frame
#define RX_LINE_MAX     2048      // a full list of 12 widgets is ~1.5 KB; album art arrives in chunks below this
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

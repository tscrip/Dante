#pragma once
#include <Arduino.h>
#include "Buttons.h"

// The touch screen as DANTE's buttons, for boards that have a touch panel instead of buttons (the
// ESP32-3248S035C "yellow board"). Taps turn into the same ButtonEvents the buttons make, so
// everything else (screens, nudges, the PC's button handling) works unchanged:
//
//   tap the left third of the screen   = LEFT     (hold = long LEFT: jump home to the face)
//   tap the middle                     = SELECT   (hold = long SELECT)
//   tap the right third                = RIGHT    (hold = long RIGHT)
//   swipe left / right                 = RIGHT / LEFT (next / previous screen, like a phone)
//   tap with two fingers               = MAGIC    (hold = long MAGIC)
namespace Touch {

  // The gesture logic on its own: fed what the panel reports, it says which "button" was pressed.
  // Kept free of hardware so the simulator runs exactly the same code.
  class Gestures {
   public:
    // `fingers` = how many fingers touch the screen now (0 = none); x, y = the first one, in screen
    // pixels (0..TFT_W-1, 0..TFT_H-1). Returns true and fills `ev` when a press is complete.
    bool feed(uint32_t now, uint8_t fingers, int x, int y, ButtonEvent& ev);

   private:
    bool down = false;          // a finger is on the screen (a gesture is under way)
    bool done = false;          // this gesture already produced its press (long press or swipe)
    uint32_t downAt = 0, zeroSince = 0;
    int startX = 0, startY = 0;
    uint8_t mostFingers = 0;    // two fingers at any point = MAGIC

    ButtonId zone() const;
  };

  void begin();                 // starts reading the panel, if this board has one
  bool available();             // is there a touch panel?
  // Returns true and fills `ev` when a tap or swipe has turned into a press.
  bool poll(ButtonEvent& ev);
  // Where the last touch landed and how long ago, for the `?` status line ("touch 240,160 3s ago").
  void describe(char* out, size_t n);

#ifdef DANTE_SIMULATOR
  // Simulator: whether it pretends to have a touch panel, and what the mouse is doing.
  void simEnable(bool on);
  void simSet(uint8_t fingers, int x, int y);
#endif
}

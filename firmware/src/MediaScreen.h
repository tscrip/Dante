#pragma once
#include <Arduino.h>
#include "Apps.h"

class TftGfx;

// The media player screen on the 3.5" TFT: album art, title, artist, play state and volume, with a row
// of buttons (previous, play/pause, next, volume down, mute, volume up). On a touch screen the buttons
// are tapped; with real buttons, SELECT = play/pause and a long SELECT = next (the PC decides).
// The OLED shows a small version instead (drawMedia in Apps.cpp).
namespace MediaScreen {
  // Draws the screen under the status bar, in screen pixels.
  void draw(TftGfx& g, const App& a, uint32_t accent, uint32_t now);
  // Which control is at screen pixel (x, y): "prev", "play", "next", "vold", "mute", "volu", or nullptr.
  const char* hit(int x, int y);
  // Lights a control up for a moment, so a tap or press is seen straight away.
  void pressed(const char* ctl, uint32_t now);
}

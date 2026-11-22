#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>

class TftGfx;

// Album art for the media screen, sent by the PC as a small JPEG in `art` messages (base64 chunks, see
// docs/protocol.md). The chunks are put together, the JPEG is decoded once into a 120x120 picture, and
// the media screen copies that picture onto the TFT every frame. One picture at a time: DANTE has
// little memory, and there is only one media player widget.
namespace Art {
  constexpr int SIZE = 120;            // pixels per side, as the PC makes them

  // Handles one `art` message.
  void chunk(JsonDocument& msg);
  // Is the picture with this fingerprint ready to show?
  bool ready(const char* hash);
  // Draws the picture with its top-left corner at screen pixel (x, y).
  void draw(TftGfx& g, int x, int y);

  // Decodes base64 `in` into `out` (at most `max` bytes). Returns the number of bytes, or -1 on bad input.
  int decodeBase64(const char* in, uint8_t* out, size_t max);
}
